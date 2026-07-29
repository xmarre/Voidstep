using System;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using Voidstep.Core;

namespace Voidstep
{
    internal sealed class AbilitySelectionController
    {
        private const float PreviewRefreshInterval = 0.075f;
        private const int MaximumPreviewCreationFailures = 3;
        private readonly Mission _mission;
        private readonly AbilityManager _manager;
        private readonly TargetingService _targeting;
        private readonly TeleportValidator _teleportValidator;
        private readonly EffectController _effects;
        private readonly VoidstepLogger _logger;
        private readonly MBList<Agent> _nearby = new MBList<Agent>();
        private readonly List<AgentDistance> _sorted = new List<AgentDistance>(32);
        private readonly List<GameEntity> _markers = new List<GameEntity>(16);
        private readonly List<Vec3> _positions = new List<Vec3>(16);
        private readonly MethodInfo _cancelCurrent;
        private AbilityId? _selected;
        private float _previewRefreshRemaining;
        private bool _blinkTargetingOwned;
        private int _previewCreationFailures;
        private bool _previewCreationDisabled;

        internal AbilitySelectionController(Mission mission, AbilityManager manager, VoidstepLogger logger)
        {
            _mission = mission;
            _manager = manager;
            _logger = logger;
            _targeting = new TargetingService(mission);
            _teleportValidator = new TeleportValidator(mission);
            _effects = new EffectController(mission, logger);
            _cancelCurrent = typeof(AbilityManager).GetMethod("CancelCurrent", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_cancelCurrent == null)
                _logger.Info("Blink selection is unavailable because the owned AbilityManager cancellation method could not be resolved.");
        }

        internal bool HasSelection => _selected.HasValue;
        internal AbilityId? SelectedAbility => _selected;

        internal bool Select(AbilityId ability, string source)
        {
            var player = _mission.MainAgent;
            if (player == null || !player.IsActive() || player.Health <= 0f)
            {
                Show("No active player agent is available.");
                return false;
            }

            if (ability == AbilityId.Blink && _cancelCurrent == null)
            {
                Show("Blink targeting is unavailable because its owned cancellation path could not be initialized.");
                _logger.Debug($"Ability selection rejected ability={ability}, source={source}: cancellation ownership is unavailable.");
                return false;
            }

            if (_selected.HasValue && _selected.Value == ability)
                return true;

            if (_manager.IsBusy)
            {
                if (_blinkTargetingOwned && _manager.ActiveAbility == AbilityId.Blink &&
                    _manager.Phase == AbilityPhase.Targeting)
                {
                    if (!Cancel(true))
                        return false;
                }
                else
                {
                    Show("Another ability is already being cast.");
                    _logger.Debug($"Ability selection rejected ability={ability}, source={source}: transient cast ownership is active.");
                    return false;
                }
            }
            else if (!Cancel(false))
            {
                return false;
            }

            _selected = ability;
            _previewRefreshRemaining = 0f;
            _blinkTargetingOwned = false;
            _previewCreationFailures = 0;
            _previewCreationDisabled = false;
            if (ability == AbilityId.Blink)
            {
                if (!_manager.TryActivate(AbilityId.Blink))
                {
                    _selected = null;
                    return false;
                }
                _blinkTargetingOwned = true;
            }
            else
            {
                RefreshPreview(player);
            }

            var name = AbilityPresentation.Name(ability);
            Show(name + " selected — Right Mouse Button to cast; Escape to cancel.");
            _logger.Debug($"Ability selected ability={ability}, source={source}; waiting for RightMouseButton confirmation.");
            return true;
        }

        internal bool Confirm()
        {
            if (!_selected.HasValue)
                return false;

            var ability = _selected.Value;
            var success = _manager.TryActivate(ability);
            if (!success)
            {
                _previewRefreshRemaining = 0f;
                return false;
            }

            _logger.Debug($"RightMouseButton confirmed selected ability={ability}.");
            ClearSelectionVisuals();
            _selected = null;
            _blinkTargetingOwned = false;
            return true;
        }

        internal void Tick(float dt)
        {
            if (!_selected.HasValue)
                return;

            var player = _mission.MainAgent;
            if (player == null || !player.IsActive() || player.Health <= 0f)
            {
                Cancel(true);
                return;
            }

            if (_selected.Value == AbilityId.Blink)
            {
                if (!_manager.IsBusy || _manager.ActiveAbility != AbilityId.Blink ||
                    _manager.Phase != AbilityPhase.Targeting)
                {
                    ClearSelectionVisuals();
                    _selected = null;
                    _blinkTargetingOwned = false;
                }
                return;
            }

            if (_previewCreationDisabled)
                return;
            _previewRefreshRemaining -= Math.Max(0f, dt);
            if (_previewRefreshRemaining > 0f)
                return;
            _previewRefreshRemaining = PreviewRefreshInterval;
            RefreshPreview(player);
        }

        internal bool Cancel(bool cancelPendingBlink)
        {
            if (!_selected.HasValue && _markers.Count == 0 && !_blinkTargetingOwned)
                return true;

            if (cancelPendingBlink && _blinkTargetingOwned && _manager.IsBusy &&
                _manager.ActiveAbility == AbilityId.Blink && _manager.Phase == AbilityPhase.Targeting)
            {
                if (_cancelCurrent == null)
                {
                    _logger.Debug("Blink selection cancellation was refused because the owned cancellation method is unavailable.");
                    Show("Blink targeting could not be cancelled safely.");
                    return false;
                }

                try
                {
                    _cancelCurrent.Invoke(_manager, new object[] { CancelReason.UserCancelled });
                }
                catch (Exception ex)
                {
                    _logger.Debug("Blink selection cancellation failed safely: " + Unwrap(ex).Message);
                    Show("Blink targeting could not be cancelled safely.");
                    return false;
                }

                if (_manager.IsBusy && _manager.ActiveAbility == AbilityId.Blink &&
                    _manager.Phase == AbilityPhase.Targeting)
                {
                    _logger.Debug("Blink selection cancellation returned without releasing targeting ownership.");
                    Show("Blink targeting could not be cancelled safely.");
                    return false;
                }
            }

            if (_selected.HasValue)
                _logger.Debug("Ability selection cancelled ability=" + _selected.Value + ".");
            _selected = null;
            _blinkTargetingOwned = false;
            ClearSelectionVisuals();
            return true;
        }

        internal void Cleanup()
        {
            Cancel(true);
            _effects.Cleanup();
            _nearby.Clear();
            _sorted.Clear();
            _positions.Clear();
        }

        private void RefreshPreview(Agent player)
        {
            if (!_selected.HasValue || _previewCreationDisabled)
                return;

            _positions.Clear();
            var ability = _selected.Value;
            var color = AbilityPresentation.MarkerColor(ability);
            switch (ability)
            {
                case AbilityId.VoidstepCleave:
                    BuildCleavePreview(player, ref color);
                    break;
                case AbilityId.Windblast:
                    BuildWindblastPreview(player);
                    break;
                case AbilityId.BendTime:
                    BuildRadiusPreview(player.Position, 2.4f, 8);
                    break;
                case AbilityId.Domino:
                    BuildDominoPreview(player);
                    break;
                case AbilityId.DarkVision:
                    BuildRadiusPreview(player.Position, Math.Min(8f, Math.Max(3f, VoidstepSettings.Current.DarkVisionRange * 0.22f)), 10);
                    break;
            }
            ApplyPreview(_positions, color);
        }

        private void BuildCleavePreview(Agent player, ref uint color)
        {
            var settings = VoidstepSettings.Current;
            var requested = ResolveCleaveDestination(player, settings.VoidstepRange);
            var validation = _teleportValidator.Validate(player, requested, settings.VoidstepRange, false);
            _positions.Add((validation.Success ? validation.Position : requested) + Vec3.Up * 0.2f);
            color = validation.Success ? AbilityPresentation.MarkerColor(AbilityId.VoidstepCleave) : 0xE04040FFu;
        }

        private Vec3 ResolveCleaveDestination(Agent player, float range)
        {
            var locked = _targeting.FindLockedEnemy(player, range, 30f);
            if (locked != null)
            {
                var travel = locked.Position - player.Position;
                travel.z = 0f;
                if (travel.Normalize() < 0.001f)
                    travel = _targeting.GetAimDirection(player);
                return locked.Position + travel * 1.5f;
            }
            if (_targeting.TryGetAimedGroundPosition(player, range, out var aimed))
                return aimed;
            return _targeting.GetForwardFallback(player, Math.Min(range, 5f));
        }

        private void BuildWindblastPreview(Agent player)
        {
            var settings = VoidstepSettings.Current;
            var direction = _targeting.GetAimDirection(player);
            direction.z = 0f;
            if (direction.Normalize() < 0.001f)
                direction = player.LookDirection;
            direction.z = 0f;
            direction.Normalize();
            var side = new Vec3(-direction.y, direction.x, 0f, 0f);
            var range = settings.WindblastRange;
            var halfWidth = (float)Math.Tan(settings.WindblastAngle * Math.PI / 360.0) * range;
            _positions.Add(player.Position + direction * (range * 0.33f) + Vec3.Up * 0.15f);
            _positions.Add(player.Position + direction * (range * 0.66f) + Vec3.Up * 0.15f);
            _positions.Add(player.Position + direction * range + Vec3.Up * 0.15f);
            _positions.Add(player.Position + direction * range + side * halfWidth + Vec3.Up * 0.15f);
            _positions.Add(player.Position + direction * range - side * halfWidth + Vec3.Up * 0.15f);
            _positions.Add(player.Position + direction * (range * 0.66f) + side * (halfWidth * 0.55f) + Vec3.Up * 0.15f);
            _positions.Add(player.Position + direction * (range * 0.66f) - side * (halfWidth * 0.55f) + Vec3.Up * 0.15f);
        }

        private void BuildDominoPreview(Agent player)
        {
            var settings = VoidstepSettings.Current;
            if (player.Team == null)
                return;
            _nearby.Clear();
            _mission.GetNearbyEnemyAgents(player.Position.AsVec2, settings.DominoRange, player.Team, _nearby);
            _sorted.Clear();
            for (var i = 0; i < _nearby.Count; i++)
            {
                var target = _nearby[i];
                if (!TargetingService.IsUsableTarget(player, target, true) || !target.IsHuman)
                    continue;
                var delta = target.Position - player.Position;
                _sorted.Add(new AgentDistance(target, delta.x * delta.x + delta.y * delta.y));
            }
            _sorted.Sort(CompareAgentDistance);
            var limit = Math.Min(settings.DominoMaximumLinks, _sorted.Count);
            for (var i = 0; i < limit; i++)
                _positions.Add(_sorted[i].Agent.GetChestGlobalPosition() + Vec3.Up * 0.75f);
            _nearby.Clear();
            _sorted.Clear();
        }

        private void BuildRadiusPreview(Vec3 centre, float radius, int count)
        {
            _positions.Add(centre + Vec3.Up * 0.15f);
            for (var i = 0; i < count; i++)
            {
                var angle = Math.PI * 2.0 * i / count;
                _positions.Add(new Vec3(
                    centre.x + (float)Math.Cos(angle) * radius,
                    centre.y + (float)Math.Sin(angle) * radius,
                    centre.z + 0.15f,
                    1f));
            }
        }

        private void ApplyPreview(List<Vec3> positions, uint color)
        {
            while (_markers.Count > positions.Count)
            {
                var last = _markers.Count - 1;
                _effects.RemoveMarker(_markers[last]);
                _markers.RemoveAt(last);
            }

            while (_markers.Count < positions.Count)
            {
                var marker = _effects.CreateWorldMarker(positions[_markers.Count], color);
                if (marker == null)
                {
                    RecordPreviewCreationFailure();
                    return;
                }
                _markers.Add(marker);
            }

            for (var i = 0; i < positions.Count; i++)
            {
                var marker = _markers[i];
                if (marker == null)
                {
                    marker = _effects.CreateWorldMarker(positions[i], color);
                    if (marker == null)
                    {
                        RecordPreviewCreationFailure();
                        return;
                    }
                    _markers[i] = marker;
                }
                else
                {
                    _effects.MoveMarker(marker, positions[i]);
                    _effects.SetMarkerColor(marker, color);
                }
            }

            _previewCreationFailures = 0;
        }

        private void RecordPreviewCreationFailure()
        {
            _previewCreationFailures++;
            if (_previewCreationFailures < MaximumPreviewCreationFailures)
                return;
            _previewCreationDisabled = true;
            _logger.Info("Cast-indicator creation failed repeatedly; preview retries are disabled for the current selection.");
            Show("Cast indicator unavailable for this selection. Escape to cancel or Mouse 2 to attempt the cast.");
        }

        private void ClearSelectionVisuals()
        {
            for (var i = _markers.Count - 1; i >= 0; i--)
                _effects.RemoveMarker(_markers[i]);
            _markers.Clear();
            _positions.Clear();
            _previewCreationFailures = 0;
            _previewCreationDisabled = false;
        }

        private static void Show(string message)
        {
            try { InformationManager.DisplayMessage(new InformationMessage(message)); }
            catch { }
        }

        private static int CompareAgentDistance(AgentDistance left, AgentDistance right)
        {
            var distance = left.DistanceSquared.CompareTo(right.DistanceSquared);
            return distance != 0 ? distance : left.Agent.Index.CompareTo(right.Agent.Index);
        }

        private static Exception Unwrap(Exception exception)
        {
            while (exception is TargetInvocationException invocation && invocation.InnerException != null)
                exception = invocation.InnerException;
            return exception;
        }

        private readonly struct AgentDistance
        {
            internal AgentDistance(Agent agent, float distanceSquared)
            {
                Agent = agent;
                DistanceSquared = distanceSquared;
            }

            internal Agent Agent { get; }
            internal float DistanceSquared { get; }
        }
    }
}
