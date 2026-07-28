# Changelog

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
