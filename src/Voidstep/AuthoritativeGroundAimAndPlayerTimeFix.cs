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

        private static Type _missionScreenType;
        private static PropertyInfo _topScreenProperty;
        private static PropertyInfo _missionProperty;
        private static MethodInfo _projectGroundMethod;
        private static bool _successLogged;
        private static bool _failureLogged;
        private static readonly VoidstepLogger Logger = new VoidstepLogger();

        internal static bool TryResolve(Mission mission, Agent actor, float range, out Vec3 position)
        {
            position = Vec3.Invalid;
            if (mission?.Scene == null || actor == null || !actor.IsActive() || !EnsureResolved())
            {
                LogFailureOnce("Mission-screen ground projection API was unavailable.");
                return false;
            }

            try
            {
                var screen = _topScreenProperty.GetValue(null, null);
                if (screen == null || !_missionScreenType.IsInstanceOfType(screen) ||
                    !ReferenceEquals(_missionProperty.GetValue(screen, null), mission))
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
                var projected = _projectGroundMethod.Invoke(screen, arguments);
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

        private static bool EnsureResolved()
        {
            if (_missionScreenType != null && _topScreenProperty != null &&
                _missionProperty != null && _projectGroundMethod != null)
            {
                return true;
            }

            var screenManagerType = AccessTools.TypeByName(ScreenManagerTypeName);
            var missionScreenType = AccessTools.TypeByName(MissionScreenTypeName);
            if (screenManagerType == null || missionScreenType == null)
                return false;

            var topScreen = AccessTools.Property(screenManagerType, "TopScreen");
            var missionProperty = AccessTools.Property(missionScreenType, "Mission");
            var projectedGround = AccessTools.Method(
                missionScreenType,
                "GetProjectedMousePositionOnGround",
                new[]
                {
                    typeof(Vec3).MakeByRefType(),
                    typeof(Vec3).MakeByRefType(),
                    typeof(BodyFlags),
                    typeof(bool)
                });
            if (topScreen == null || missionProperty == null || projectedGround == null)
                return false;

            _missionScreenType = missionScreenType;
            _topScreenProperty = topScreen;
            _missionProperty = missionProperty;
            _projectGroundMethod = projectedGround;
            return true;
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
        private static readonly FieldInfo PlayerField =
            AccessTools.Field(typeof(TimeControlService), "_player");
        private static readonly FieldInfo MountField =
            AccessTools.Field(typeof(TimeControlService), "_mount");
        private static readonly FieldInfo FactorField =
            AccessTools.Field(typeof(TimeControlService), "_factor");
        private static readonly FieldInfo ManagerField =
            AccessTools.Field(typeof(VoidstepMissionBehavior), "_manager");
        private static readonly FieldInfo TimeField =
            AccessTools.Field(typeof(AbilityManager), "_time");

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

            var player = GetAgent(PlayerField, service);
            var mount = GetAgent(MountField, service);
            if (!ReferenceEquals(agent, player) && !ReferenceEquals(agent, mount))
                return false;

            var factor = GetFactor(service);
            if (factor <= 0.001f || factor >= 0.999f)
                return false;
            compensation = Math.Min(8f, 1f / factor);
            return compensation > 1.001f;
        }

        internal static void Enforce(TimeControlService service, VoidstepLogger logger)
        {
            if (service == null || !service.Active)
                return;

            var factor = GetFactor(service);
            if (factor <= 0.001f || factor >= 0.999f)
                return;
            var compensation = Math.Min(8f, 1f / factor);
            var player = GetAgent(PlayerField, service);
            var mount = GetAgent(MountField, service);

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

        private static Agent GetAgent(FieldInfo field, TimeControlService service)
        {
            try { return field?.GetValue(service) as Agent; }
            catch { return null; }
        }

        private static float GetFactor(TimeControlService service)
        {
            try
            {
                var value = FactorField?.GetValue(service);
                return value is float factor ? factor : 1f;
            }
            catch
            {
                return 1f;
            }
        }

        private static TimeControlService ResolveService()
        {
            try
            {
                var behavior = Mission.Current?.GetMissionBehavior<VoidstepMissionBehavior>();
                var manager = ManagerField?.GetValue(behavior) as AbilityManager;
                return TimeField?.GetValue(manager) as TimeControlService;
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

    /// <summary>
    /// The earlier controller-delta experiment affected only camera look interpolation in
    /// Bannerlord's MissionMainAgentController. It cannot change native movement simulation.
    /// Suppress it so it cannot distort camera response while the real native compensation runs.
    /// </summary>
    [HarmonyPatch(typeof(BendTimeMainAgentTickRuntime), nameof(BendTimeMainAgentTickRuntime.Scale))]
    internal static class DisableIneffectiveMainAgentControllerDeltaPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix()
        {
            return false;
        }
    }
}
