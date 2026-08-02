# Technical architecture

## Runtime lifetimes

`VoidstepSubModule.OnMissionBehaviorInitialize` adds one `VoidstepMissionBehavior`. Bannerlord 1.3.15 runs that hook after its normal `OnBehaviorInitialize` pass, so the behavior performs idempotent runtime initialization from `EarlyStart`, with `OnBehaviorInitialize` and the first mission tick retained as compatibility/fallback paths. The behavior owns `AbilityManager`, all mission services and every mission-owned mutable collection. `OnEndMission` calls one idempotent cleanup path.

`VoidstepProgressionBehavior` is a separate campaign save component. It stores only hero-keyed integer XP and skill-level dictionaries plus the progression-enabled flag. It listens to session launch, game load and new-game lifecycle events and participates in `SyncData`; it registers no hourly, daily or campaign-map tick and stores no mission agent.

## Mastery persistence and cached profile

Mastery state is keyed by `Hero.MainHero.StringId`, allowing the save model to remain stable across campaign sessions. The data keys are versioned:

```text
_voidstepMasteryXp_v1
_voidstepSkillLevels_v1
_voidstepProgressionEnabled_v1
_voidstepProgressionDataVersion
```

Skill levels remain keyed by the original integer `VoidstepSkillId` values. `MasteryGraphPolicy` validates all 19 identifiers before applying the current prerequisite graph and never rewrites XP, levels or invested points. Existing saves therefore retain every investment when prerequisites are relaxed or reorganized.

Mission code never queries those dictionaries directly. `VoidstepProgressionService` builds one immutable `VoidstepProgressionProfile` containing an indexed skill-level array. The volatile profile reference is replaced only when campaign state changes: attach/load, XP gain, rank change, investment, respec, enable/disable or detach. Mission reads are constant-time and lock-free.

Progression-disabled and no-campaign states use a shared disabled profile. Every runtime modifier returns the original configured value in that state.

## Progression integration scope

The ordinary MCM values remain the source configuration. Progression applies caps and multipliers only while Voidstep-owned ability code is executing.

Harmony prefixes enter a thread-static integer scope for:

- `AbilityContext` construction;
- `AbilityManager.Tick`;
- `AbilityManager.TryActivate`.

Harmony finalizers release the scope on normal return, prefix rejection or exception. No lease object or closure is allocated on the mission-tick path. Patched MCM property getters consult the cached mastery profile only while the scope is active, so MCM UI and unrelated callers still observe their raw configured values.

Ability unlock validation occurs before `AbilityManager.TryActivate`. Active Dark Vision remains removable after a respec or progression-state change so a locked ability cannot become permanently active.

## Mastery point economy

The catalogue contains 19 skills across Core, Mobility, Force, Dominion, Reservoir and Convergence branches. One mastery rank grants one point, up to rank 99. Rank, melee-skill and prerequisite checks are evaluated before each investment.

The six ability foundation skills are independent. Unlocking Blink, Windblast, Bend Time, Domino or Dark Vision does not require investment in Cleave or another ability. Deep Reservoir is also independent of ability investment. Advanced nodes require only the preceding node in their own path.

`Singularity` requires one rank in each of the six ability foundations, expressing actual convergence without forcing deep investment in unrelated abilities. `Avatar of the Void` retains the deliberate final requirements of Singularity 5 and Unbound Power 5. The complete route to maximum Avatar costs 41 points and remains comfortably reachable within the rank-99 budget.

## Mastery XP ownership

XP is awarded only from successful owned ability outcomes. Cleave scales its award from successfully registered hits; other abilities use bounded fixed awards. A `ConditionalWeakTable` keys throttle state to the owning mission controller or manager, so mission teardown releases award state without a global registry. One cached factory delegate creates throttle state and avoids repeated delegate allocation.

## Mastery UI state transition

The native Character screen button does not directly push a Gauntlet screen. Its controller follows this sequence:

1. suspend and detach the overlay button;
2. pop `CharacterDeveloperState`;
3. wait until Bannerlord rebuilds the campaign map screen;
4. allow two additional application frames for the map to settle;
5. push `VoidstepMasteryScreen`.

Any unexpected screen, mission start, campaign end or timeout cancels the transition. Campaign end and submodule unload explicitly pop the mastery screen before its view model and progression service are detached.

The campaign-map shortcut (`Ctrl+Shift+V`) opens the same screen only when the settled map is already the top screen.

## Ability state machine

A `CastToken` identifies the sole active cast. Legal phases are:

```text
Targeting -> Validating -> WindUp -> Departing -> Teleporting -> Arriving -> Active -> Recovery -> Idle
```

Immediate abilities traverse a reduced legal path. `CancelCurrent` clears Blink preview/time state, cleave hit state, FOV ownership, destination and cast ownership. Player death, deletion, replacement, invalid destination, mission end and exceptions route through cancellation.

Voidstep Cleave captures one immutable execution snapshot before wind-up. The snapshot contains the normalized starting facing, mastery-scaled teleport range, sweep geometry, damage/force values, target cap and targeting flags. Revalidation, execution, effects and recovery all use that same snapshot, preventing MCM or progression changes from altering a cast halfway through its phase sequence.

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

`AbilityManager.TeleportActor` treats facing as owned state. It captures normalized horizontal look directions for the actor and active mount, performs native teleport and optional momentum reset, then reapplies both directions after those mutations. Cleave also reapplies its pre-cast direction before teleport, before the execution action and during every active/recovery update.

## Cleave target ordering

The active sweep stores start angle, sweep radians and direction from the immutable execution snapshot. A nearby-agent query builds reusable candidate and schedule buffers. `AngleMath.TravelFromStart` normalises clockwise or counter-clockwise travel. `SweepPlanner` filters by radius and sweep gap, computes expected animation progress and sorts by progress then distance.

Live mode rebuilds the schedule during the active strike. A target is eligible when its angle has not already been passed. Snapshot mode stores agent index to expected progress and resolves the agent at contact time. Both modes use `HitRegistry<int>`.

## Blow construction

`BlowFactory.ApplyMeleeBlow` builds an `AttackCollisionData` record, calls `Mission.CreateMeleeBlow` with the player's captured `MissionWeapon`, adjusts configured magnitude/knock flags, and calls `Agent.RegisterBlow`. This is the closest audited 1.3.15 path to native melee processing without a fabricated area-damage event.

## Animation synchronisation

`AnimationController` is the replaceable presentation boundary. Cleave owns one execution action after arrival; the generic successful-cast animation hook explicitly excludes Cleave so no earlier action can compete for channel or body state. The sweep controller computes actor yaw absolutely from `startAngle + signedSweep * progress` and drives action-channel progress from the same scalar. Engine/action mutations therefore cannot accumulate yaw drift away from the angle used by target scheduling. Recovery also computes an absolute facing and finishes by assigning the exact pre-cast direction.

The current implementation uses best-effort native action names and does not claim an original skeletal asset. A future one-handed/two-handed action asset can replace this controller without changing target resolution or blow construction.

## Time ownership

Bend Time uses request ID `0x56535450`. Blink aiming uses `0x5653424C`. Each service removes only its own request. `OwnershipLedger<int>` prevents release of a token that the service no longer owns. Other mission speed requests are never cached or overwritten.

## Domino callback ownership

Domino keeps identity-checked linked targets and marker ownership for the current mission. Native hit and removal callbacks enqueue propagation records and return without registering a new blow. Dispatch occurs on the following mission tick after the native callback has unwound.

Per-target propagated-hit and propagated-death ledgers suppress only Domino-owned callbacks. Removed agents, changed identities and reused indices are rejected before dispatch. Persistent links remain independent of the transient ability-cast state.

## Cleanup guarantees

- Per-cast hit and snapshot collections clear at cleave end or cancellation.
- Blink preview entities and aim-speed requests clear on every exit.
- FOV restoration occurs only while the current value still equals Voidstep's owned value.
- Bend Time removes only its request ID and restores only owned driven-property changes.
- Domino clears links, markers, pending propagation and suppression ledgers.
- Dark Vision clears every contour it applied.
- `EffectController` owns and removes every marker entity it creates.
- Character and mastery Gauntlet layers release their movies, input restrictions and view models.
- Campaign end and module unload close the mastery screen before detaching progression state.
- Exceptions in activation or mission tick invoke cleanup before control returns to Bannerlord.

## Extension points

New abilities should add one controller, one `AbilityId`, settings cost/cooldown mappings, one `AbilityManager` activation path and an explicit mastery foundation skill when progression should gate it. They should use the existing cast token, resource/cooldown, effect ownership, cached progression profile and cleanup conventions.
