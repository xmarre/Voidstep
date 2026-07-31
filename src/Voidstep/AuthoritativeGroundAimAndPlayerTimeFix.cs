using System;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace Voidstep
{
    /// <summary>
    /// Uses Bannerlord's active mission-screen projection. With the mouse hidden this projects
    /// the centre reticle; with the mouse visible it projects the cursor. Only results outside
    /// the circular cast radius are clamped.
    /// </summary>
    internal static class MissionScreenGroundAimRuntime
    {
        private const string ScreenManagerTypeName = "TaleWorlds.ScreenSystem.ScreenManager";
        private const string MissionScreenTypeName =
            "TaleWorlds.MountAndBlade.View.Screens.MissionScreen";

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
                if (!(projected is bool success) || !success ||
                    !(arguments[0] is Vec3 ground) || !ground.IsValid)
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
                    Logger.Debug(
                        "Teleport targeting now uses MissionScreen projected reticle ground and supports the complete cast circle.");
                }
                return true;
            }
            catch (Exception ex)
            {
                LogFailureOnce(
                    "Mission-screen ground projection failed safely: " + Unwrap(ex).Message);
                return false;
            }
        }

        private static bool EnsureResolved()
        {
            if (_missionScreenType != null && _topScreenProperty != null &&
                _missionProperty != null && _projectGroundMethod != null)
                return true;

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

        private static Vec3 ClampToCastCircle(
            Mission mission,
            Vec3 origin,
            Vec3 ground,
            float range)
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
            while (exception is TargetInvocationException invocation &&
                   invocation.InnerException != null)
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

    [HarmonyPatch(
        typeof(VariableDistanceCameraAimRuntime),
        nameof(VariableDistanceCameraAimRuntime.TryResolve))]
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
            if (!MissionScreenGroundAimRuntime.TryResolve(
                    mission,
                    actor,
                    range,
                    out position))
                return true;
            __result = true;
            return false;
        }
    }
}
