using System;
using System.Collections.Generic;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace Voidstep
{
    internal sealed class EffectController
    {
        private readonly Mission _mission;
        private readonly VoidstepLogger _logger;
        private readonly List<GameEntity> _ownedEntities = new List<GameEntity>();
        private readonly Dictionary<string, int> _particleIds = new Dictionary<string, int>(StringComparer.Ordinal);

        private static readonly string[] NativeDeparture = { "psys_game_missile_hit_wood", "psys_game_blood_sword_enter" };
        private static readonly string[] NativeArrival = { "psys_game_blood_sword_exit", "psys_game_missile_hit_ground" };
        private static readonly string[] NativeImpact = { "psys_game_blood_sword_enter", "psys_game_missile_hit_human" };
        private static readonly string[] NativeWind = { "psys_game_missile_hit_ground", "psys_game_broken_shield" };
        private static readonly string[] TorDeparture = { "vfx_vortex_purple", "vfx_shadow_step", "psys_magic_shadow" };
        private static readonly string[] TorArrival = { "vfx_vortex_purple", "psys_magic_shadow_impact", "vfx_teleport" };
        private static readonly string[] TorImpact = { "psys_magic_hit", "vfx_dark_magic_hit", "psys_shadow_hit" };
        private static readonly string[] TorWind = { "psys_magic_wind", "vfx_wind_blast", "psys_gust" };
        private static readonly string[] NativeMarker = { "psys_game_blood_sword_enter", "psys_game_missile_hit_human" };
        private static readonly string[] TorMarker = { "psys_magic_shadow", "vfx_vortex_purple", "psys_shadow_hit" };
        private static readonly string[] MarkerMeshes = { "arrow_bl_a", "arrow_bodkin_a", "arrow_barbed_a" };

        public EffectController(Mission mission, VoidstepLogger logger)
        {
            _mission = mission;
            _logger = logger;
        }

        public void Departure(Vec3 position) => Burst(UseTorPreset() ? TorDeparture : NativeDeparture, position);
        public void Arrival(Vec3 position) => Burst(UseTorPreset() ? TorArrival : NativeArrival, position);
        public void Impact(Vec3 position) => Burst(UseTorPreset() ? TorImpact : NativeImpact, position);
        public void Windblast(Vec3 position) => Burst(UseTorPreset() ? TorWind : NativeWind, position);
        public void WeaponTrail(Vec3 position) => Burst(UseTorPreset() ? TorImpact : NativeImpact, position);
        public void BendTime(Vec3 position) => Burst(UseTorPreset() ? TorDeparture : NativeDeparture, position);

        public GameEntity CreateWorldMarker(Vec3 position, uint color) => CreateMarker(position, color, true);

        public GameEntity CreateMarker(Vec3 position, uint color, bool alwaysVisible)
        {
            GameEntity entity = null;
            try
            {
                entity = GameEntity.CreateEmpty(_mission.Scene, false, false, false);
                var frame = MatrixFrame.Identity;
                frame.origin = position;
                entity.SetFrame(ref frame, true);

                var meshAdded = AddMarkerMesh(entity);
                entity.SetContourColor(color, alwaysVisible);
                entity.SetDoNotCheckVisibility(true);
                entity.SetReadyToRender(true);

                var particleId = ResolveFirst(UseTorPreset() ? TorMarker : NativeMarker);
                if (particleId >= 0)
                {
                    var localFrame = MatrixFrame.Identity;
                    ParticleSystem.CreateParticleSystemAttachedToEntity(particleId, entity, ref localFrame);
                }

                _ownedEntities.Add(entity);
                _logger.Debug($"Created visible target marker at {Format(position)}; mesh={meshAdded}, particle={particleId}.");
                return entity;
            }
            catch (Exception ex)
            {
                if (entity != null)
                {
                    try { entity.Remove(0); } catch { }
                    _ownedEntities.Remove(entity);
                }
                _logger.Debug("Marker creation failed: " + ex.Message);
                return null;
            }
        }

        public void MoveMarker(GameEntity marker, Vec3 position)
        {
            if (marker == null) return;
            try
            {
                var frame = marker.GetFrame();
                frame.origin = position;
                marker.SetFrame(ref frame, true);
            }
            catch (Exception ex)
            {
                _logger.Debug("Marker move failed: " + ex.Message);
            }
        }

        public void SetMarkerColor(GameEntity marker, uint color)
        {
            if (marker == null) return;
            try { marker.SetContourColor(color, true); }
            catch (Exception ex) { _logger.Debug("Marker colour update failed: " + ex.Message); }
        }

        public void RemoveMarker(GameEntity marker)
        {
            if (marker == null) return;
            try { marker.Remove(0); } catch { }
            _ownedEntities.Remove(marker);
        }

        public void PlaySound(string eventName, Vec3 position)
        {
            if (string.IsNullOrWhiteSpace(eventName)) return;
            try
            {
                var sound = SoundEvent.CreateEventFromString(eventName, _mission.Scene);
                if (sound != null && sound.IsValid)
                    sound.PlayInPosition(position);
            }
            catch (Exception ex)
            {
                _logger.Debug("Optional sound unavailable: " + eventName + " — " + ex.Message);
            }
        }

        public void Cleanup()
        {
            for (var i = _ownedEntities.Count - 1; i >= 0; i--)
            {
                try { _ownedEntities[i]?.Remove(0); } catch { }
            }
            _ownedEntities.Clear();
            _particleIds.Clear();
        }

        private bool AddMarkerMesh(GameEntity entity)
        {
            for (var i = 0; i < MarkerMeshes.Length; i++)
            {
                try
                {
                    var source = Mesh.GetFromResource(MarkerMeshes[i]);
                    if (source == null) continue;
                    var mesh = source.CreateCopy();
                    if (mesh == null) continue;
                    var local = MatrixFrame.Identity;
                    local.origin = Vec3.Up * 0.35f;
                    local.rotation.RotateAboutSide((float)Math.PI * 0.5f);
                    local.rotation.ApplyScaleLocal(0.75f);
                    mesh.SetLocalFrame(local);
                    entity.AddMesh(mesh, false);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.Debug($"Marker mesh '{MarkerMeshes[i]}' unavailable: {ex.Message}");
                }
            }
            return false;
        }

        private void Burst(string[] candidates, Vec3 position)
        {
            if (VoidstepSettings.Current.EffectIntensity <= 0f) return;
            try
            {
                var id = ResolveFirst(candidates);
                if (id < 0) return;
                var frame = MatrixFrame.Identity;
                frame.origin = position;
                _mission.Scene.CreateBurstParticle(id, frame);
            }
            catch (Exception ex)
            {
                _logger.Debug("Optional particle failed: " + ex.Message);
            }
        }

        private int ResolveFirst(string[] candidates)
        {
            for (var i = 0; i < candidates.Length; i++)
            {
                var name = candidates[i];
                if (_particleIds.TryGetValue(name, out var cached))
                {
                    if (cached >= 0) return cached;
                    continue;
                }
                var id = ParticleSystemManager.GetRuntimeIdByName(name);
                _particleIds[name] = id;
                if (id >= 0) return id;
            }
            return -1;
        }

        private static string Format(Vec3 value) => $"({value.x:0.00}, {value.y:0.00}, {value.z:0.00})";

        private static readonly bool TorPresetAvailable = Type.GetType("TOR_Core.TOR_CoreSubModule, TOR_Core", false) != null;
        private static bool UseTorPreset() => TorPresetAvailable;
    }
}
