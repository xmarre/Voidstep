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
        private readonly Dictionary<GameEntity, Mesh> _markerMeshes = new Dictionary<GameEntity, Mesh>();
        private readonly Dictionary<string, int> _particleIds = new Dictionary<string, int>(StringComparer.Ordinal);

        private static readonly string[] NativeDeparture = { "psys_game_missile_hit_wood", "psys_game_blood_sword_enter" };
        private static readonly string[] NativeArrival = { "psys_game_blood_sword_exit", "psys_game_missile_hit_ground" };
        private static readonly string[] NativeImpact = { "psys_game_blood_sword_enter", "psys_game_missile_hit_human" };
        private static readonly string[] NativeWind = { "psys_game_missile_hit_ground", "psys_game_broken_shield" };
        private static readonly string[] TorDeparture = { "vfx_vortex_purple", "vfx_shadow_step", "psys_magic_shadow" };
        private static readonly string[] TorArrival = { "vfx_vortex_purple", "psys_magic_shadow_impact", "vfx_teleport" };
        private static readonly string[] TorImpact = { "psys_magic_hit", "vfx_dark_magic_hit", "psys_shadow_hit" };
        private static readonly string[] TorWind = { "psys_magic_wind", "vfx_wind_blast", "psys_gust" };
        private static readonly string[] NativeMarker = { "psys_game_missile_hit_ground", "psys_game_broken_shield" };
        private static readonly string[] TorMarker = { "psys_magic_shadow", "vfx_vortex_purple", "psys_shadow_hit" };
        private static readonly string[] MarkerMaterialDonors = { "arrow_bl_a", "arrow_bodkin_a", "arrow_barbed_a" };
        private static readonly Vec3[] MarkerOffsets =
        {
            Vec3.Zero,
            new Vec3(0.32f, 0f, 0f, 1f),
            new Vec3(-0.32f, 0f, 0f, 1f),
            new Vec3(0f, 0.32f, 0f, 1f),
            new Vec3(0f, -0.32f, 0f, 1f)
        };

        public EffectController(Mission mission, VoidstepLogger logger)
        {
            _mission = mission;
            _logger = logger;
        }

        public void Departure(Vec3 position) => Burst(UseTorPreset() ? TorDeparture : NativeDeparture, position);
        public void Arrival(Vec3 position) => Burst(UseTorPreset() ? TorArrival : NativeArrival, position);
        public void Impact(Vec3 position) => Burst(UseTorPreset() ? TorImpact : NativeImpact, position);
        public void Windblast(Vec3 position) => RadialBurst(UseTorPreset() ? TorWind : NativeWind, position, 0.75f, 6);
        public void WeaponTrail(Vec3 position) => Burst(UseTorPreset() ? TorImpact : NativeImpact, position);
        public void BendTime(Vec3 position) => RadialBurst(UseTorPreset() ? TorDeparture : NativeDeparture, position, 1.15f, 8);

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

                var sigil = CreateCastingSigilMesh(entity, color);
                entity.SetContourColor(color, alwaysVisible);
                entity.SetDoNotCheckVisibility(true);
                entity.SetReadyToRender(true);

                var particleId = ResolveFirst(UseTorPreset() ? TorMarker : NativeMarker);
                var attachedParticles = 0;
                var intensity = VoidstepSettings.Current.EffectIntensity;
                if (particleId >= 0 && intensity > 0f)
                {
                    var offsetCount = intensity >= 1f ? MarkerOffsets.Length : 1;
                    for (var i = 0; i < offsetCount; i++)
                    {
                        var localFrame = MatrixFrame.Identity;
                        localFrame.origin = MarkerOffsets[i];
                        ParticleSystem.CreateParticleSystemAttachedToEntity(particleId, entity, ref localFrame);
                        attachedParticles++;
                    }
                }

                _ownedEntities.Add(entity);
                if (sigil != null) _markerMeshes[entity] = sigil;
                _logger.Debug($"Created cast indicator at {Format(position)}; sigil={sigil != null}, particles={attachedParticles}, particleId={particleId}.");
                return entity;
            }
            catch (Exception ex)
            {
                if (entity != null)
                {
                    try { entity.Remove(0); } catch { }
                    _ownedEntities.Remove(entity);
                    _markerMeshes.Remove(entity);
                }
                _logger.Debug("Cast indicator creation failed: " + ex.Message);
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
                _logger.Debug("Cast indicator move failed: " + ex.Message);
            }
        }

        public void SetMarkerColor(GameEntity marker, uint color)
        {
            if (marker == null) return;
            try
            {
                marker.SetContourColor(color, true);
                if (_markerMeshes.TryGetValue(marker, out var mesh))
                {
                    mesh.Color = color;
                    mesh.Color2 = color;
                }
            }
            catch (Exception ex) { _logger.Debug("Cast indicator colour update failed: " + ex.Message); }
        }

        public void RemoveMarker(GameEntity marker)
        {
            if (marker == null) return;
            _markerMeshes.Remove(marker);
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
            _markerMeshes.Clear();
            _particleIds.Clear();
        }

        private Mesh CreateCastingSigilMesh(GameEntity entity, uint color)
        {
            for (var i = 0; i < MarkerMaterialDonors.Length; i++)
            {
                Mesh mesh = null;
                try
                {
                    var donor = Mesh.GetFromResource(MarkerMaterialDonors[i]);
                    var material = donor?.GetMaterial();
                    if (material == null) continue;

                    mesh = Mesh.CreateMeshWithMaterial(material);
                    if (mesh == null) continue;
                    mesh.Color = color;
                    mesh.Color2 = color;
                    var handle = mesh.LockEditDataWrite();
                    try
                    {
                        AddRing(mesh, handle, color, false);
                        AddRing(mesh, handle, color, true);
                    }
                    finally
                    {
                        mesh.UnlockEditDataWrite(handle);
                    }
                    mesh.ComputeNormals();
                    mesh.ComputeTangents();
                    mesh.RecomputeBoundingBox();
                    mesh.PreloadForRendering();
                    entity.AddMesh(mesh, false);
                    return mesh;
                }
                catch (Exception ex)
                {
                    // Mesh exposes no public release method in the locked 1.3.15 API.
                    // Drop the wrapper immediately so a failed donor cannot remain owned here.
                    mesh = null;
                    _logger.Debug($"Cast sigil material donor '{MarkerMaterialDonors[i]}' unavailable: {ex.Message}");
                }
            }
            return null;
        }

        private static void AddRing(Mesh mesh, UIntPtr handle, uint color, bool vertical)
        {
            const int segments = 24;
            const float outerRadius = 0.42f;
            const float innerRadius = 0.29f;
            var uv = Vec2.Zero;
            for (var i = 0; i < segments; i++)
            {
                var angle0 = i * Math.PI * 2.0 / segments;
                var angle1 = (i + 1) * Math.PI * 2.0 / segments;
                var outer0 = RingPoint(angle0, outerRadius, vertical);
                var outer1 = RingPoint(angle1, outerRadius, vertical);
                var inner1 = RingPoint(angle1, innerRadius, vertical);
                var inner0 = RingPoint(angle0, innerRadius, vertical);
                mesh.AddTriangle(outer0, outer1, inner1, uv, uv, uv, color, handle);
                mesh.AddTriangle(outer0, inner1, inner0, uv, uv, uv, color, handle);
            }
        }

        private static Vec3 RingPoint(double angle, float radius, bool vertical)
        {
            var first = (float)Math.Cos(angle) * radius;
            var second = (float)Math.Sin(angle) * radius;
            return vertical
                ? new Vec3(first, 0f, second, 1f)
                : new Vec3(first, second, 0f, 1f);
        }

        private void RadialBurst(string[] candidates, Vec3 center, float radius, int count)
        {
            Burst(candidates, center);
            for (var i = 0; i < count; i++)
            {
                var angle = i * Math.PI * 2.0 / count;
                var position = new Vec3(
                    center.x + (float)Math.Cos(angle) * radius,
                    center.y + (float)Math.Sin(angle) * radius,
                    center.z + 0.05f,
                    1f);
                Burst(candidates, position);
            }
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
