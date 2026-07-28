# Bannerlord 1.3.15 / TOR 1.16 API audit

The supplied binaries were parsed directly as ECMA-335 metadata and hash-locked in `references/reference-manifest.json`. `scripts/validate_api_surface.py` repeats the load-bearing signature checks.

Verified Bannerlord surfaces include:

- `MBSubModuleBase.OnMissionBehaviorInitialize(Mission)`
- `MissionBehavior.OnMissionTick(float)`, `OnEndMission()`, `OnAgentHit(...)`, `OnAgentRemoved(...)`, `OnAgentDeleted(Agent)`, `OnAgentControllerSetToPlayer(Agent)`
- `Mission.GetNearbyEnemyAgents(Vec2, float, Team, MBList<Agent>)`
- `Mission.GetNearbyAgents(Vec2, float, MBList<Agent>)`
- `Mission.CreateMeleeBlow(...)`
- `Mission.AddTimeSpeedRequest`, `RemoveTimeSpeedRequest`, `GetRequestedTimeSpeed`
- `Mission.FindAgentWithIndex(int)`
- `Mission.IsPositionInsideBoundaries`, `IsPositionInsideHardBoundaries`, blocker-navmesh checks
- `Scene.GetGroundHeightAtPosition`, `GetWaterLevelAtPosition`, `RayCastForClosestEntityOrTerrain`, `GetNearestNavigationMeshForPosition`
- `Agent.TeleportToPosition`, action-channel progress/speed calls, `RegisterBlow`, visual and targeting accessors
- `ActionIndexCache.act_strike_bent_over` as a concrete verified action field
- `GameEntity.CreateEmpty`, `SetContourColor`, `Remove`
- `ParticleSystemManager.GetRuntimeIdByName`, `Scene.CreateBurstParticle`
- `SoundEvent.CreateEventFromString`, `PlayInPosition`

TOR inspection confirmed `TOR_Core.Utilities.TORParticleSystem` helper methods. Voidstep keeps TOR optional and does not compile against or distribute `TOR_Core.dll`; visual candidates are resolved by name and failure is non-fatal.

## Locked input hashes

The canonical hashes are machine-readable in `references/reference-manifest.json`. Release builds reject missing or mismatched files before restore or compilation.
