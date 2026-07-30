using System;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace Voidstep
{
    /// <summary>
    /// Bannerlord recalculates AgentDrivenProperties after mission behaviours tick and only
    /// then submits the resulting array to native agent simulation. Bend Time compensation
    /// therefore runs after that model calculation rather than only mutating an earlier snapshot.
    /// </summary>
    [HarmonyPatch]
    internal static class BendTimePostCalculatedDrivenPropertiesPatch
    {
        private static readonly AccessTools.FieldRef<VoidstepMissionBehavior, AbilityManager> Manager =
            AccessTools.FieldRefAccess<VoidstepMissionBehavior, AbilityManager>("_manager");
        private static readonly AccessTools.FieldRef<AbilityManager, TimeControlService> Time =
            AccessTools.FieldRefAccess<AbilityManager, TimeControlService>("_time");

        private static WeakReference<Mission> _cachedMission;
        private static WeakReference<TimeControlService> _cachedTime;

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(AgentDrivenProperties),
                "UpdateDrivenProperties",
                new[] { typeof(Agent) });
        }

        private static bool Prepare()
        {
            return TargetMethod() != null;
        }

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(AgentDrivenProperties __instance, Agent __0)
        {
            var agent = __0;
            if (__instance == null || agent == null)
                return;

            var mission = Mission.Current;
            var mainAgent = mission?.MainAgent;
            if (mission == null || mainAgent == null)
                return;

            var controlledMount = mainAgent.MountAgent;
            if (!ReferenceEquals(agent, mainAgent) &&
                (controlledMount == null || !ReferenceEquals(agent, controlledMount)))
            {
                return;
            }

            var time = ResolveTime(mission);
            if (time == null)
                return;

            BendTimeDrivenPropertyCompensation.Apply(time, agent, __instance);
        }

        internal static void ResetCache()
        {
            _cachedMission = null;
            _cachedTime = null;
        }

        private static TimeControlService ResolveTime(Mission mission)
        {
            Mission cachedMission;
            TimeControlService cachedTime;
            if (_cachedMission != null &&
                _cachedMission.TryGetTarget(out cachedMission) &&
                ReferenceEquals(cachedMission, mission) &&
                _cachedTime != null &&
                _cachedTime.TryGetTarget(out cachedTime))
            {
                return cachedTime;
            }

            var behavior = mission.GetMissionBehavior<VoidstepMissionBehavior>();
            var manager = behavior == null ? null : Manager(behavior);
            var resolved = manager == null ? null : Time(manager);
            _cachedMission = new WeakReference<Mission>(mission);
            _cachedTime = resolved == null ? null : new WeakReference<TimeControlService>(resolved);
            return resolved;
        }
    }

    /// <summary>
    /// Forces the post-calculation path to run while Bend Time is active. The operation is
    /// limited to the controlled player and mount and exists only for the duration of the effect.
    /// </summary>
    [HarmonyPatch(typeof(TimeControlService), "ApplyPlayerCompensation")]
    internal static class BendTimeNativeDrivenPropertyRefreshPatch
    {
        private static void Postfix(TimeControlService __instance)
        {
            var player = BendTimeDrivenPropertyCompensation.GetPlayer(__instance);
            BendTimeDrivenPropertyCompensation.RefreshNative(player);
            var mount = BendTimeDrivenPropertyCompensation.GetMount(__instance);
            if (!ReferenceEquals(mount, player))
                BendTimeDrivenPropertyCompensation.RefreshNative(mount);
        }
    }

    /// <summary>
    /// Pushes normal driven properties back to native simulation immediately after Bend Time
    /// releases instead of waiting for an unrelated equipment or stat refresh.
    /// </summary>
    [HarmonyPatch(typeof(TimeControlService), "CompleteLocalState")]
    internal static class BendTimeNativeDrivenPropertyCleanupPatch
    {
        private struct RefreshState
        {
            internal Agent Player;
            internal Agent Mount;
        }

        private static void Prefix(TimeControlService __instance, out RefreshState __state)
        {
            __state = new RefreshState
            {
                Player = BendTimeDrivenPropertyCompensation.GetPlayer(__instance),
                Mount = BendTimeDrivenPropertyCompensation.GetMount(__instance)
            };
        }

        private static void Postfix(RefreshState __state)
        {
            BendTimeDrivenPropertyCompensation.RefreshNative(__state.Player);
            if (!ReferenceEquals(__state.Mount, __state.Player))
                BendTimeDrivenPropertyCompensation.RefreshNative(__state.Mount);
            BendTimeDrivenPropertyCompensation.ResetDiagnostics();
            BendTimePostCalculatedDrivenPropertiesPatch.ResetCache();
        }
    }

    internal static class BendTimeDrivenPropertyCompensation
    {
        private static readonly VoidstepLogger Logger = new VoidstepLogger();

        // Centralized cached field-access delegates. They are delegates, not stored Agent roots.
        private static AccessTools.FieldRef<TimeControlService, Agent> Player =
            AccessTools.FieldRefAccess<TimeControlService, Agent>("_player");
        private static AccessTools.FieldRef<TimeControlService, Agent> Mount =
            AccessTools.FieldRefAccess<TimeControlService, Agent>("_mount");
        private static readonly AccessTools.FieldRef<TimeControlService, float> Factor =
            AccessTools.FieldRefAccess<TimeControlService, float>("_factor");

        private static readonly AccessTools.FieldRef<TimeControlService, bool> PlayerSnapshotCaptured =
            AccessTools.FieldRefAccess<TimeControlService, bool>("_playerDrivenSnapshotCaptured");
        private static readonly AccessTools.FieldRef<TimeControlService, bool> MountSnapshotCaptured =
            AccessTools.FieldRefAccess<TimeControlService, bool>("_mountDrivenSnapshotCaptured");
        private static readonly AccessTools.FieldRef<TimeControlService, bool> PlayerPropertiesApplied =
            AccessTools.FieldRefAccess<TimeControlService, bool>("_playerPropertiesApplied");
        private static readonly AccessTools.FieldRef<TimeControlService, bool> MountPropertiesApplied =
            AccessTools.FieldRefAccess<TimeControlService, bool>("_mountPropertiesApplied");

        private static readonly AccessTools.FieldRef<TimeControlService, float> OriginalMaxSpeed =
            AccessTools.FieldRefAccess<TimeControlService, float>("_originalMaxSpeedMultiplier");
        private static readonly AccessTools.FieldRef<TimeControlService, float> OriginalCombatMaxSpeed =
            AccessTools.FieldRefAccess<TimeControlService, float>("_originalCombatMaxSpeedMultiplier");
        private static readonly AccessTools.FieldRef<TimeControlService, float> OriginalTopSpeedReachDuration =
            AccessTools.FieldRefAccess<TimeControlService, float>("_originalTopSpeedReachDuration");
        private static readonly AccessTools.FieldRef<TimeControlService, float> OriginalSwingSpeed =
            AccessTools.FieldRefAccess<TimeControlService, float>("_originalSwingSpeedMultiplier");
        private static readonly AccessTools.FieldRef<TimeControlService, float> OriginalReadySpeed =
            AccessTools.FieldRefAccess<TimeControlService, float>("_originalReadySpeedMultiplier");
        private static readonly AccessTools.FieldRef<TimeControlService, float> OriginalReloadSpeed =
            AccessTools.FieldRefAccess<TimeControlService, float>("_originalReloadSpeed");
        private static readonly AccessTools.FieldRef<TimeControlService, float> OriginalRangedReadySpeed =
            AccessTools.FieldRefAccess<TimeControlService, float>("_originalRangedReadySpeedMultiplier");
        private static readonly AccessTools.FieldRef<TimeControlService, float> OriginalRangedReloadSpeed =
            AccessTools.FieldRefAccess<TimeControlService, float>("_originalRangedReloadSpeedMultiplier");

        private static readonly AccessTools.FieldRef<TimeControlService, float> AppliedMaxSpeed =
            AccessTools.FieldRefAccess<TimeControlService, float>("_appliedMaxSpeedMultiplier");
        private static readonly AccessTools.FieldRef<TimeControlService, float> AppliedCombatMaxSpeed =
            AccessTools.FieldRefAccess<TimeControlService, float>("_appliedCombatMaxSpeedMultiplier");
        private static readonly AccessTools.FieldRef<TimeControlService, float> AppliedTopSpeedReachDuration =
            AccessTools.FieldRefAccess<TimeControlService, float>("_appliedTopSpeedReachDuration");
        private static readonly AccessTools.FieldRef<TimeControlService, float> AppliedSwingSpeed =
            AccessTools.FieldRefAccess<TimeControlService, float>("_appliedSwingSpeedMultiplier");
        private static readonly AccessTools.FieldRef<TimeControlService, float> AppliedReadySpeed =
            AccessTools.FieldRefAccess<TimeControlService, float>("_appliedReadySpeedMultiplier");
        private static readonly AccessTools.FieldRef<TimeControlService, float> AppliedReloadSpeed =
            AccessTools.FieldRefAccess<TimeControlService, float>("_appliedReloadSpeed");
        private static readonly AccessTools.FieldRef<TimeControlService, float> AppliedRangedReadySpeed =
            AccessTools.FieldRefAccess<TimeControlService, float>("_appliedRangedReadySpeedMultiplier");
        private static readonly AccessTools.FieldRef<TimeControlService, float> AppliedRangedReloadSpeed =
            AccessTools.FieldRefAccess<TimeControlService, float>("_appliedRangedReloadSpeedMultiplier");

        private static readonly AccessTools.FieldRef<TimeControlService, float> OriginalMountSpeed =
            AccessTools.FieldRefAccess<TimeControlService, float>("_originalMountSpeed");
        private static readonly AccessTools.FieldRef<TimeControlService, float> OriginalMountManeuver =
            AccessTools.FieldRefAccess<TimeControlService, float>("_originalMountManeuver");
        private static readonly AccessTools.FieldRef<TimeControlService, float> OriginalMountDashAcceleration =
            AccessTools.FieldRefAccess<TimeControlService, float>("_originalMountDashAcceleration");
        private static readonly AccessTools.FieldRef<TimeControlService, float> AppliedMountSpeed =
            AccessTools.FieldRefAccess<TimeControlService, float>("_appliedMountSpeed");
        private static readonly AccessTools.FieldRef<TimeControlService, float> AppliedMountManeuver =
            AccessTools.FieldRefAccess<TimeControlService, float>("_appliedMountManeuver");
        private static readonly AccessTools.FieldRef<TimeControlService, float> AppliedMountDashAcceleration =
            AccessTools.FieldRefAccess<TimeControlService, float>("_appliedMountDashAcceleration");

        private static bool _refreshFailureLogged;

        internal static Agent GetPlayer(TimeControlService time)
        {
            return time == null ? null : Player(time);
        }

        internal static Agent GetMount(TimeControlService time)
        {
            return time == null ? null : Mount(time);
        }

        internal static void Apply(TimeControlService time, Agent agent, AgentDrivenProperties driven)
        {
            if (time == null || agent == null || driven == null || !time.Active ||
                !VoidstepSettings.Current.PreservePlayerSpeed)
            {
                return;
            }

            var factor = Factor(time);
            if (factor <= 0.001f || factor >= 0.999f)
                return;

            var compensation = Math.Min(8f, 1f / factor);
            if (ReferenceEquals(agent, GetPlayer(time)))
            {
                ApplyPlayer(time, driven, compensation);
                return;
            }

            if (ReferenceEquals(agent, GetMount(time)))
                ApplyMount(time, driven, compensation);
        }

        internal static void RefreshNative(Agent agent)
        {
            if (agent == null || !agent.IsActive())
                return;

            try
            {
                agent.UpdateAgentProperties();
                _refreshFailureLogged = false;
            }
            catch (Exception ex)
            {
                if (_refreshFailureLogged)
                    return;
                _refreshFailureLogged = true;
                Logger.Debug("Bend Time could not refresh compensated native driven properties: " + ex.Message);
            }
        }

        internal static void ResetDiagnostics()
        {
            _refreshFailureLogged = false;
        }

        private static void ApplyPlayer(TimeControlService time, AgentDrivenProperties driven, float compensation)
        {
            OriginalMaxSpeed(time) = driven.MaxSpeedMultiplier;
            OriginalCombatMaxSpeed(time) = driven.CombatMaxSpeedMultiplier;
            OriginalTopSpeedReachDuration(time) = driven.TopSpeedReachDuration;
            OriginalSwingSpeed(time) = driven.SwingSpeedMultiplier;
            OriginalReadySpeed(time) = driven.ThrustOrRangedReadySpeedMultiplier;
            OriginalReloadSpeed(time) = driven.ReloadSpeed;
            OriginalRangedReadySpeed(time) = driven.BipedalRangedReadySpeedMultiplier;
            OriginalRangedReloadSpeed(time) = driven.BipedalRangedReloadSpeedMultiplier;

            AppliedMaxSpeed(time) = OriginalMaxSpeed(time) * compensation;
            AppliedCombatMaxSpeed(time) = OriginalCombatMaxSpeed(time) * compensation;
            AppliedTopSpeedReachDuration(time) = Math.Max(0.01f, OriginalTopSpeedReachDuration(time) / compensation);
            AppliedSwingSpeed(time) = OriginalSwingSpeed(time) * compensation;
            AppliedReadySpeed(time) = OriginalReadySpeed(time) * compensation;
            AppliedReloadSpeed(time) = OriginalReloadSpeed(time) * compensation;
            AppliedRangedReadySpeed(time) = OriginalRangedReadySpeed(time) * compensation;
            AppliedRangedReloadSpeed(time) = OriginalRangedReloadSpeed(time) * compensation;

            driven.MaxSpeedMultiplier = AppliedMaxSpeed(time);
            driven.CombatMaxSpeedMultiplier = AppliedCombatMaxSpeed(time);
            driven.TopSpeedReachDuration = AppliedTopSpeedReachDuration(time);
            driven.SwingSpeedMultiplier = AppliedSwingSpeed(time);
            driven.ThrustOrRangedReadySpeedMultiplier = AppliedReadySpeed(time);
            driven.ReloadSpeed = AppliedReloadSpeed(time);
            driven.BipedalRangedReadySpeedMultiplier = AppliedRangedReadySpeed(time);
            driven.BipedalRangedReloadSpeedMultiplier = AppliedRangedReloadSpeed(time);

            PlayerSnapshotCaptured(time) = true;
            PlayerPropertiesApplied(time) = true;
        }

        private static void ApplyMount(TimeControlService time, AgentDrivenProperties driven, float compensation)
        {
            OriginalMountSpeed(time) = driven.MountSpeed;
            OriginalMountManeuver(time) = driven.MountManeuver;
            OriginalMountDashAcceleration(time) = driven.MountDashAccelerationMultiplier;

            AppliedMountSpeed(time) = OriginalMountSpeed(time) * compensation;
            AppliedMountManeuver(time) = OriginalMountManeuver(time) * compensation;
            AppliedMountDashAcceleration(time) = OriginalMountDashAcceleration(time) * compensation;

            driven.MountSpeed = AppliedMountSpeed(time);
            driven.MountManeuver = AppliedMountManeuver(time);
            driven.MountDashAccelerationMultiplier = AppliedMountDashAcceleration(time);

            MountSnapshotCaptured(time) = true;
            MountPropertiesApplied(time) = true;
        }
    }
}
