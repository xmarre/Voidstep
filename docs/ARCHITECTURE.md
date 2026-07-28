# Technical architecture

## Mission lifetime

`VoidstepSubModule.OnMissionBehaviorInitialize` adds one `VoidstepMissionBehavior`. The behavior owns `AbilityManager`, all services and every mutable collection. `OnEndMission` calls one idempotent cleanup path. No campaign behavior, campaign event or global agent patch exists.

## Ability state machine

A `CastToken` identifies the sole active cast. Legal phases are:

```text
Targeting -> Validating -> WindUp -> Departing -> Teleporting -> Arriving -> Active -> Recovery -> Idle
```

Immediate abilities traverse a reduced legal path. `CancelCurrent` clears Blink preview/time state, cleave hit state, FOV ownership, destination and cast ownership. Player death, deletion, replacement, invalid destination, mission end and exceptions route through cancellation.

## Teleport validation

`TeleportValidator` performs these checks in order:

1. Clamp to ability range.
2. Mission boundary and hard-boundary containment.
3. Terrain height and surface normal.
4. Water depth.
5. Blocker navmesh containment.
6. Nearby navigation mesh availability.
7. Sealed path ray cast when wall traversal is disabled.
8. Eight radial cliff/step probes.
9. Occupancy query, excluding the actor and its rider/mount pair.
10. Vertical standing clearance, increased for mounted use.

Failure at the requested point triggers deterministic concentric fallback candidates. `DestinationSelector` chooses by planar distance, vertical delta and ordinal.

## Cleave target ordering

The active sweep stores start angle, sweep radians and direction. A nearby-agent query builds reusable candidate and schedule buffers. `AngleMath.TravelFromStart` normalises clockwise or counter-clockwise travel. `SweepPlanner` filters by radius and sweep gap, computes expected animation progress and sorts by progress then distance.

Live mode rebuilds the schedule during the active strike. A target is eligible when its angle has not already been passed. Snapshot mode stores agent index to expected progress and resolves the agent at contact time. Both modes use `HitRegistry<int>`.

## Blow construction

`BlowFactory.ApplyMeleeBlow` builds an `AttackCollisionData` record, calls `Mission.CreateMeleeBlow` with the player's current `MissionWeapon`, adjusts configured magnitude/knock flags, and calls `Agent.RegisterBlow`. This is the closest audited 1.3.15 path to native melee processing without a fabricated area-damage event.

## Animation synchronisation

`AnimationController` is the replaceable presentation boundary. v1.0.0 uses the concrete `ActionIndexCache.act_strike_bent_over` field verified in the supplied Bannerlord 1.3.15 assembly; it does not claim an original skeletal asset. The sweep controller drives actor yaw and action-channel progress from the same scalar. Target progress therefore corresponds to visual body/weapon progress. Recovery rotates the configured unhit gap, returning the player to its starting facing. A future one-handed/two-handed action asset can replace this controller without changing target resolution or blow construction.

## Time ownership

Bend Time uses request ID `0x56535450`. Blink aiming uses `0x5653424C`. Each service removes only its own request. `OwnershipLedger<int>` prevents release of a token that the service no longer owns. Other mission speed requests are never cached or overwritten.

## Domino recursion prevention

Domino stores `HashSet<int>` agent indices. `OnAgentHit` enters one constant recursion key before creating propagated blows. A synchronous propagated callback cannot enter the same key, so it returns without another propagation pass. Invalid indices are removed on tick, removal and deletion callbacks.

## Cleanup guarantees

- Per-cast hit and snapshot collections clear at cleave end or cancellation.
- Blink preview entities and aim-speed requests clear on every exit.
- FOV restoration occurs only while the current value still equals Voidstep's owned value.
- Bend Time removes only its request ID.
- Domino stores no `Agent` beyond the current player pointer and resolves linked agents by index.
- Dark Vision clears every contour it applied.
- `EffectController` owns and removes every marker entity it creates.
- Exceptions in activation or mission tick invoke cleanup before control returns to Bannerlord.

## Extension points

New abilities should add one controller, one `AbilityId`, settings cost/cooldown mappings, and one `AbilityManager` activation path. They should use the existing cast token, resource/cooldown, effect ownership and cleanup conventions.
