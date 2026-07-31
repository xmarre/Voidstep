using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace Voidstep
{
    /// <summary>
    /// Uses Bannerlord's actual mission-screen ground projection. When the mouse is hidden,
    /// MissionScreen projects the centre reticle; when it is visible, it projects the cursor.
    /// This is the same screen-space contract used by native ground-order placement and allows
    /// every point inside the circular range instead of deriving distance from a camera vector
    /// that can remain effectively horizontal in third-person combat.
    /// </summary>
    internal static class MissionScreenGroundAimRuntime
    {
        private const string ScreenManagerTypeName = "TaleWorlds.ScreenSystem.ScreenManager";
        private const string MissionScreenTypeName = "TaleWorlds.MountAndBlade.View.Screens.MissionScreen";

        private static readonly Type ScreenManagerType = AccessTools.TypeByName(ScreenManagerTypeName);
        private static readonly Type MissionScreenType = AccessTools.TypeByName(MissionScreenTypeName);
        private static readonly PropertyInfo TopScreenProperty =
            ScreenManagerType == null ? null : AccessTools.Property(ScreenManagerType, "TopScreen");
        private static readonly PropertyInfo MissionProperty =
            MissionScreenType == null ? null : AccessTools.Property(MissionScreenType, "Mission");
        private static readonly MethodInfo ProjectGroundMethod = ResolveProjectGroundMethod();
        private static bool _successLogged;
        private static bool _failureLogged;
        private static readonly VoidstepLogger Logger = new VoidstepLogger();

        internal static bool TryResolve(Mission mission, Agent actor, float range, out Vec3 position)
        {
            position = Vec3.Invalid;
            if (mission?.Scene == null || actor == null || !actor.IsActive() ||
                TopScreenProperty == null || MissionProperty == null || ProjectGroundMethod == null)
            {
                LogFailureOnce("Mission-screen ground projection API was unavailable.");
                return false;
            }

            try
            {
                var screen = TopScreenProperty.GetValue(null, null);
                if (screen == null || !MissionScreenType.IsInstanceOfType(screen) ||
                    !ReferenceEquals(MissionProperty.GetValue(screen, null), mission))
                {
                    LogFailureOnce("The active top screen was not the current mission screen.");
                    return false;
                }

                var arguments = new object[]
                {
                    Vec3.Invalid,
                    Vec3.Invalid,
                    BodyFlags.AgentOnly | BodyFlags.MissileOnly | BodyFlags.DroppedItem,
                    false
                };
                var projected = ProjectGroundMethod.Invoke(screen, arguments);
                if (!(projected is bool success) || !success || !(arguments[0] is Vec3 ground) || !ground.IsValid)
                {
                    LogFailureOnce("Bannerlord mission-screen projection returned no ground point.");
                    return false;
                }

                position = ClampToCastCircle(mission, actor.Position, ground, Math.Max(0f, range));
                if (!position.IsValid)
                    return false;

                if (!_successLogged)
                {
                    _successLogged = true;
                    Logger.Debug("Teleport targeting now uses MissionScreen projected reticle ground and supports the complete cast circle.");
                }
                return true;
            }
            catch (Exception ex)
            {
                LogFailureOnce("Mission-screen ground projection failed safely: " + Unwrap(ex).Message);
                return false;
            }
        }

        private static Vec3 ClampToCastCircle(Mission mission, Vec3 origin, Vec3 ground, float range)
        {
            var planar = ground - origin;
            planar.z = 0f;
            var distance = planar.Length;
            Vec3 result;
            if (distance > range && distance > 0.001f)
                result = origin + planar * (range / distance);
            else
                result = ground;

            var height = mission.Scene.GetGroundHeightAtPosition(
                result,
                BodyFlags.CommonCollisionExcludeFlagsForAgent);
            if (float.IsNaN(height) || float.IsInfinity(height))
                return Vec3.Invalid;
            result.z = height;
            return result;
        }

        private static MethodInfo ResolveProjectGroundMethod()
        {
            if (MissionScreenType == null)
                return null;
            return AccessTools.Method(
                MissionScreenType,
                "GetProjectedMousePositionOnGround",
                new[]
                {
                    typeof(Vec3).MakeByRefType(),
                    typeof(Vec3).MakeByRefType(),
                    typeof(BodyFlags),
                    typeof(bool)
                });
        }

        private static Exception Unwrap(Exception exception)
        {
            while (exception is TargetInvocationException invocation && invocation.InnerException != null)
                exception = invocation.InnerException;
            return exception;
        }

        private static void LogFailureOnce(string message)
        {
            if (_failureLogged)
                return;
            _failureLogged = true;
            Logger.Debug(message);
        }
    }

    [HarmonyPatch(typeof(VariableDistanceCameraAimRuntime), nameof(VariableDistanceCameraAimRuntime.TryResolve))]
    internal static class MissionScreenGroundAimPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(
            Mission mission,
            Agent actor,
            float range,
            ref Vec3 position,
            ref bool __result)
        {
            if (!MissionScreenGroundAimRuntime.TryResolve(mission, actor, range, out position))
                return true;
            __result = true;
            return false;
        }
    }

    /// <summary>
    /// Bend Time's player exemption is part of the ability contract, not an optional path.
    /// Enforce it after every TimeControlService tick and again whenever Bannerlord starts or
    /// resets a native action. This prevents persisted MCM values and late native action changes
    /// from reducing the player to the same timescale as the world.
    /// </summary>
    internal static class MandatoryBendTimePlayerExemption
    {
        private static readonly ConditionalWeakTable<TimeControlService, State> States =
            new ConditionalWeakTable<TimeControlService, State>();
        private static readonly AccessTools.FieldRef<TimeControlService, Agent> Player =
            AccessTools.FieldRefAccess<TimeControlService, Agent>("_player");
        private static readonly AccessTools.FieldRef<TimeControlService, Agent> Mount =
            AccessTools.FieldRefAccess<TimeControlService, Agent>("_mount");
        private static readonly AccessTools.FieldRef<TimeControlService, float> Factor =
            AccessTools.FieldRefAccess<TimeControlService, float>("_factor");

        private sealed class State
        {
            internal bool Logged;
        }

        internal static bool TryGetCompensation(Agent agent, out float compensation)
        {
            compensation = 1f;
            if (agent == null || !agent.IsActive())
                return false;

            var service = ResolveService();
            if (service == null || !service.Active)
                return false;

            var player = Player(service);
            var mount = Mount(service);
            if (!ReferenceEquals(agent, player) && !ReferenceEquals(agent, mount))
                return false;

            var factor = Factor(service);
            if (factor <= 0.001f || factor >= 0.999f)
                return false;
            compensation = Math.Min(8f, 1f / factor);
            return compensation > 1.001f;
        }

        internal static void Enforce(TimeControlService service, VoidstepLogger logger)
        {
            if (service == null || !service.Active)
                return;

            var factor = Factor(service);
            if (factor <= 0.001f || factor >= 0.999f)
                return;
            var compensation = Math.Min(8f, 1f / factor);
            var player = Player(service);
            var mount = Mount(service);

            EnforceAgent(player, compensation);
            if (!ReferenceEquals(mount, player))
                EnforceAgent(mount, compensation);

            var state = States.GetOrCreateValue(service);
            if (!state.Logged)
            {
                state.Logged = true;
                logger?.Debug(
                    "Bend Time mandatory player exemption enforced at native property/action boundaries=" +
                    compensation.ToString("0.00") + "x.");
            }
        }

        private static void EnforceAgent(Agent agent, float compensation)
        {
            if (agent == null || !agent.IsActive())
                return;

            try
            {
                agent.UpdateAgentProperties();
                agent.SetMaximumSpeedLimit(compensation, true);
                agent.SetCurrentActionSpeed(0, compensation);
                agent.SetCurrentActionSpeed(1, compensation);
            }
            catch
            {
            }
        }

        private static TimeControlService ResolveService()
        {
            try
            {
                var mission = Mission.Current;
                var behavior = mission?.GetMissionBehavior<VoidstepMissionBehavior>();
                if (behavior == null)
                    return null;
                var managerField = AccessTools.Field(typeof(VoidstepMissionBehavior), "_manager");
                var manager = managerField?.GetValue(behavior) as AbilityManager;
                if (manager == null)
                    return null;
                var timeField = AccessTools.Field(typeof(AbilityManager), "_time");
                return timeField?.GetValue(manager) as TimeControlService;
            }
            catch
            {
                return null;
            }
        }
    }

    [HarmonyPatch(typeof(VoidstepSettings), "get_PreservePlayerSpeed")]
    internal static class BendTimePlayerExemptionIsMandatoryPatch
    {
        private static void Postfix(ref bool __result)
        {
            __result = true;
        }
    }

    [HarmonyPatch(typeof(TimeControlService), nameof(TimeControlService.Tick))]
    internal static class MandatoryBendTimePlayerExemptionTickPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(TimeControlService __instance, VoidstepLogger ____logger)
        {
            MandatoryBendTimePlayerExemption.Enforce(__instance, ____logger);
        }
    }

    [HarmonyPatch(typeof(Agent), nameof(Agent.SetCurrentActionSpeed))]
    internal static class BendTimePlayerActionSpeedBoundaryPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(Agent __instance, ref float speed)
        {
            if (MandatoryBendTimePlayerExemption.TryGetCompensation(__instance, out var compensation))
                speed = Math.Max(speed, compensation);
        }
    }

    [HarmonyPatch(typeof(Agent), nameof(Agent.SetActionChannel))]
    internal static class BendTimePlayerActionStartBoundaryPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(Agent __instance, ref float actionSpeed)
        {
            if (MandatoryBendTimePlayerExemption.TryGetCompensation(__instance, out var compensation))
                actionSpeed = Math.Max(actionSpeed, compensation);
        }
    }
}
