# Changelog

## 1.0.4

- Restored `Ctrl+1` through `Ctrl+6` as the default ability controls.
- Suppressed only the matching Bannerlord formation command while Ctrl is held, while preserving normal formation selection with plain `1` through `6`, including custom cross-remapped Ctrl+number ability bindings.
- Disabled ability input entirely when the owned formation-suppression patches cannot be confirmed, preventing Ctrl+number abilities from running alongside native formation commands.
- Fixed Voidstep Cleave disrupting the player's weapon state by removing the invalid victim-reaction action and capturing the wielded melee weapon before teleport and delayed hit processing.
- Rejected bows, crossbows, shields, throwing weapons and other non-melee equipment before a Cleave cast starts.
- Fixed Cleave registering ineffective hits by preserving the original weapon and guaranteeing a calculated native blow damage value when Bannerlord returns zero during synthetic collision construction.
- Applied the Cleave target cap only to successfully registered hits, so an earlier failed blow no longer prevents later valid targets from being attempted.
- Refunded Cleave energy and cooldown when the destination becomes invalid or the active sweep cannot begin after payment.
- Replaced invisible empty targeting entities with visible mesh-backed world markers for Blink, Voidstep Cleave and Domino.
- Aligned Blink, Voidstep Cleave and Windblast targeting with the mission camera instead of relying only on actor facing.
- Fixed delayed Domino hit callbacks being able to propagate already-propagated damage again after the synchronous recursion guard had been released.
- Added immediate Dark Vision highlighting on activation and counted only successful contour operations in diagnostics.
- Added end-to-end diagnostics for every ability, including targeting, validation, teleport completion, candidate counts, registered hits, time control and linked targets.

## 1.0.3

- Fixed Blink aim slowdown and Bend Time failing with `ArgumentOutOfRangeException` when Bannerlord could not find a time-speed request ID.
- Removed unsafe pre-emptive `RemoveTimeSpeedRequest` calls before a request had been registered.
- Added ownership-safe existence checks before adding or removing mission time-speed requests.
- Prevented Voidstep from replacing a time-speed request it did not create when a reserved ID is already present.

## 1.0.2

- Fixed the actual mission-runtime lifecycle bug: Bannerlord 1.3.15 invokes `OnMissionBehaviorInitialize` after the normal `OnBehaviorInitialize` pass, so late-added Voidstep behavior now initializes idempotently during `EarlyStart` with a first-tick fallback.
- Added lifecycle-stage diagnostics so the log explicitly identifies whether initialization occurred during `OnBehaviorInitialize`, `EarlyStart` or the fallback path.
- Replaced the conflicting `Ctrl+1` through `Ctrl+6` defaults with `Numpad1` through `Numpad6` and disabled the global Ctrl requirement by default.
- Automatically migrates the untouched legacy v1.0.1 control set to the new numpad defaults at mission startup.
- Added a visible and logged warning when a custom number-row binding can also trigger Bannerlord formation selection.
- Added API and source-invariant checks for the late-added behavior lifecycle and the six numpad key values.

## 1.0.1

- Fixed ability keys not registering reliably by polling Bannerlord's validated global input state instead of depending on a mission input context.
- Removed the `PauseAITick` input gate that could suppress every spell key in otherwise playable missions.
- Added submodule, mission-registration and pre-construction bootstrap logging so initialization failures are recorded before the ability manager is created.
- Added guarded mission initialization with an in-game error message instead of silently leaving MCM available while the runtime is inactive.
- Added a one-time in-battle activation message showing that Voidstep is ready and reminding players that the defaults are `Ctrl+1` through `Ctrl+6`.
- Added logging to Bannerlord's engine log, the Documents ModLogs folder and the module folder when writable.
- Fixed the release workflow's absent-tag handling and source packaging so the published source ZIP is the exact tracked commit without generated `bin` or `obj` files.

## 1.0.0

- Added Voidstep Cleave with validated teleport placement, angle-synchronised target timing, individual native melee blows, whole-cast target caps and one-hit-per-cast enforcement.
- Added Blink with ground and enemy-relative targeting, destination preview, wall restrictions, fallback placement and optional aiming slowdown.
- Added Windblast with configurable cone, range, centre weighting, distance falloff, damage, knockback, knockdown and mount handling.
- Added Bend Time with mission-owned speed requests, configurable strength and duration, player action-speed compensation and deterministic restoration.
- Added Domino links with agent-index storage, optional damage, knockdown and death propagation, recursion prevention and strict cleanup.
- Added Dark Vision with low-frequency hostile queries, awareness-state colours and complete contour cleanup.
- Added configurable Void Energy, regeneration, per-ability costs, cooldown-only mode, unlimited mode and combat HUD messages.
- Added a staged rotating melee presentation using a verified Bannerlord 1.3.15 action, explicit action progress, actor rotation, capped weapon-trail effects and isolated animation integration.
- Added MCM v5 settings, deterministic reference validation, pure logic tests, build automation, clean install packaging and technical documentation.
