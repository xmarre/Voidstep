# Changelog

## 1.1.1

- Fixed the standalone Q wheel permanently capturing mouse-wheel input by making its Gauntlet layer display-only.
- Repaired TOR's live Q-wheel component before opening so all six Voidstep entries remain visible with stable, distinct icons.
- Released TOR's temporary targeting stance for every Voidstep proxy and restored wielded items and weapon bindings without putting Blink's weapon away.
- Added retry-safe TOR weapon-state restoration when TOR's reflected state is temporarily unavailable.
- Moved Bend Time compensation after Bannerlord and TOR finish recalculating driven properties so the controlled player and mount remain responsive while the world is slowed.
- Added owned native maximum-speed multiplier compensation for the player and controlled mount.
- Fixed Bend Time's post-effect speed tail by restoring native speed multipliers in the same multiplier mode and only after local time-control cleanup completes.
- Fixed Domino ignoring player-owned missile and synthetic hits when Bannerlord reports a missing or inactive affector.
- Preserved valid existing Domino affectors and retained deferred propagation, identity checks and recursion suppression.
- Added focused regression gates for TOR targeting handoff, mouse-wheel preservation, Bend Time cleanup and Domino hit ownership.

## 1.1.0

- Added a unified Dishonored-style casting flow: open the Q wheel, select an ability, aim with a live cast indicator and confirm with Right Mouse Button.
- Added a standalone six-segment Voidstep Q wheel for battles without The Old Realms.
- Added reflection-isolated TOR 1.16 integration that injects six `[Voidstep]` selections into TOR's existing Q ability wheel without adding a hard TOR dependency.
- Converted the six configurable direct bindings into ability selectors so they use the same indicator and Mouse2 confirmation path instead of bypassing targeting.
- Added live destination, cone, radius and linked-target previews for all six abilities; Blink retains its complete mission-time freeze during destination selection.
- Separated persistent Domino links from transient cast selection so linked enemies do not occupy the active casting state or block other abilities.
- Fixed Domino ignoring valid Cleave, Windblast and other player-generated hits merely because they used `BlowFlags.NoSound`.
- Replaced the overloaded `NoSound` recursion test with an explicit per-target propagated-hit ownership ledger that suppresses only Domino's own synchronous callbacks.
- Added controlled-mount hits as valid Domino trigger sources and guaranteed at least one propagated damage when the original hit inflicted damage.
- Added deterministic cleanup for wheel UI, TOR proxy abilities, selection markers, Right Mouse Button suppression, pending Domino propagation and all mission-owned state.
- Added independent mirror tests and locked source/package invariants for the wheel lifecycle, Mouse2 casting, TOR proxy isolation and explicit Domino propagation ownership.

## 1.0.9

- Fixed Bend Time corrupting native mission memory by writing action-speed values to unsupported Bannerlord action channels 2 and 3.
- Restricted Bend Time action-speed compensation and restoration to the two verified native agent channels, 0 and 1.
- Preserved locomotion, combat movement, swing, ready, reload, ranged and mount compensation while removing the out-of-range native writes.
- Added a locked source invariant that rejects any return to four-channel Bend Time mutation.

## 1.0.8

- Fixed Domino re-entering Bannerlord's native melee-hit callback by deferring propagated blows until the following mission tick.
- Added identity-checked pending propagation records so removed agents and reused agent indices cannot receive stale Domino damage.
- Prevented propagated Domino deaths from starting another death-propagation chain.
- Added native upper-body cast actions for all six abilities, including separate quick, heavy, vision and mounted fallbacks.
- Added a sweep-synchronised Voidstep Cleave execution action with owned action-speed and progress cleanup.
- Replaced the subtle casting sigil with a large no-cull placement reticle containing two ground rings, a vertical ring, eight directional spikes and a raised diamond.
- Forced placement reticles to high render order with no shadow, no season tint and synchronized contour, factor and mesh colours.
- Added locked API checks and regression tests for deferred Domino propagation, cast actions, Cleave action ownership and reticle visibility.

## 1.0.7

- Replaced arrow-shaped Blink, Cleave and Domino markers with procedural double-ring casting sigils and persistent particle clusters.
- Added stronger cast pulses for Windblast and Bend Time while preserving Dark Vision contour feedback.
- Fixed Voidstep Cleave repeatedly rejecting crowded enemy-relative destinations by searching the complete bounded fallback field instead of only the first sixteen candidates.
- Expanded Cleave fallback coverage with additional rings and angular samples while retaining mission-boundary, wall, terrain, cliff, occupancy and configured-range checks.
- Changed Blink destination selection from partial slowdown to an owned zero-speed mission request that freezes the outside world until confirmation, cancellation or expiry.
- Made Blink preview refresh and timeout use application time so targeting remains responsive while mission time is frozen.
- Expanded Bend Time player compensation from two animation channels to locomotion, combat movement, swing, ready, reload, ranged ready/reload and the two verified native action channels.
- Added equivalent speed and maneuver compensation for the controlled mount.
- Added ownership-checked Bend Time restoration so cleanup does not overwrite values changed by another system during the effect.
- Added source invariants for casting sigils, Blink freeze behavior, complete Cleave fallback search and Bend Time player/mount compensation.

## 1.0.6

- Fixed every configured ability chord being rejected because modifier state could remain frozen at the first mission snapshot.
- Removed the runtime dependency on Bannerlord calling the public static `Input.UpdateKeyData` wrapper before Voidstep evaluates a chord.
- Refreshes Control, Alt and Shift directly at the actual ability-poll and conflicting-input query points.
- Added suppression for Bannerlord's real integer `InputContext` GameKey path used by `SelectOrder1` through `SelectOrder6`.
- Prevented `Ctrl+1` through `Ctrl+6` from also opening or changing native formation commands when those chords are assigned to Voidstep.
- Preserved plain `1` through `6` and every other unmodified primary key whenever its configured ability modifier is not held.
- Preserved press-to-release suppression latching so releasing the modifier before the primary key cannot leak a delayed native command.

## 1.0.5

- Replaced the limited MCM primary-key dropdowns with six native serialized Bannerlord bindings under `Options > Keybindings > Voidstep`.
- Made every ability primary key rebindable to any keyboard or mouse button accepted by Bannerlord's keybinding screen.
- Added a separate configurable modifier combination for each ability in MCM, including no modifier and Control, Alt, Shift combinations.
- Removed the ineffective v1.0.4 GameKey-overload interception and moved conflict handling to Bannerlord's lower-level input API.
- Suppressed the configured primary key at the raw boolean input layer while the complete ability chord is active.
- Suppressed the same key at the raw axis layer, preventing movement bindings and other axis consumers from also activating.
- Latched suppression from chord press through release so releasing a modifier before the primary key cannot trigger the underlying native action afterward.
- Preserved the unmodified native key whenever an ability modifier is configured, so plain `1` through `6` still perform formation selection with the default controls.
- Added fail-closed native-hotkey registration and runtime diagnostics showing the resolved live chords.

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
- Added end-to-end diagnostics for every ability, including targeting, validation, teleport completion, candidate counts, registered Cleave hits, Windblast hits, Bend Time request acquisition, Domino links and Dark Vision highlight counts.

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
