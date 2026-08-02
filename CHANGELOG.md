# Changelog

## 1.2.5

- Added weapon-specific Voidstep Cleave swing presentation for mounted and unmounted one-handed, two-handed and polearm weapons.
- Kept Cleave damage resolution at the established 0.22 seconds while scaling the portion of the weapon animation used to the configured sweep breadth.
- Lowered mounted Cleave trails from rider-chest height to an infantry-torso strike plane and scaled visual reach from the currently wielded weapon length.
- Fixed live Cleave targeting dropping enemies that moved behind the previous angular boundary during the strike; newly observed targets in an already-swept sector are now immediately eligible.
- Re-resolved the wielded melee weapon immediately before Cleave execution while retaining the captured weapon as a safe fallback.
- Removed cross-ability mastery prerequisites so Blink, Windblast, Bend Time, Domino and Dark Vision no longer require Cleave or another unrelated ability path.
- Made Deep Reservoir independent and limited advanced mastery prerequisites to the preceding skill in the same path.
- Changed Singularity to require one rank in each of the six ability foundations; Avatar of the Void retains Singularity 5 and Unbound Power 5.
- Preserved every existing mastery XP value, invested rank, serialized skill ID and v1 save key; no save migration, refund or remapping is required.
- Added regression coverage for mounted Cleave timing and moving-target acquisition, independent mastery foundations, stable serialized IDs and save compatibility.

## 1.2.3

- Removed the unresolved Harmony constructor patch that disabled the entire Voidstep runtime during module startup.
- Moved mission-entry progression synchronization and scoped settings access directly into Voidstep's owned `AbilityContext` constructor with guaranteed cleanup.
- Confirmed that invested Void Affinity rank 1 now makes Voidstep Cleave available in battle.
- Greatly enlarged the Voidstep Cleave spell effect with layered outer, inner and raised impact bursts.
- Added bounded void effects to Blink, Bend Time, Domino and Dark Vision.
- Added a directional widening gust effect to Windblast.
- Added regression validation for runtime patch installation and all new ability-effect compositions.

## 1.2.2

- Synchronized invested mastery ranks with the immutable mission profile before battle initialization and ability activation.
- Ensured Void Affinity rank 1 unlocks Voidstep Cleave in battle without requiring a reload or another mastery-state change.
- Replaced all mastery descriptions with direct gameplay descriptions of their effects.
- Added dedicated regression validation for mission-boundary unlock synchronization and all nineteen player-facing mastery descriptions.

## 1.2.1

- Rebuilt Voidstep Mastery so skill investment directly improves the abilities instead of primarily reducing energy costs and cooldowns.
- Added progressive Blink range and Voidstep Cleave teleport-range growth through Rift Step, Rift Reach, Void Dancer, Unbound Power, Singularity and Avatar of the Void.
- Added progressive Cleave radius, sweep angle, damage, knockback, knockdown reliability and target-capacity growth.
- Added progressive Windblast cone angle, range, force and damage growth.
- Added progressive Bend Time duration and slowdown-strength growth.
- Added progressive Domino marking range, maximum links and propagated-damage growth.
- Added progressive Dark Vision detection range and refresh-speed growth.
- Added late mastery gates for configured sealed-wall Blink traversal and configured unlimited Cleave targets.
- Reduced the dominance of cost and cooldown reduction by limiting them to secondary support bonuses with a 35% reduction floor.
- Preserved all v1.2.0 save keys and skill IDs so existing mastery XP and investments carry into the new effects.
- Added independent monotonic power-curve validation for every affected ability and all twenty progression-controlled combat settings.

## 1.2.0

- Added an optional level-99 Voidstep Mastery progression system with 19 skills across Core, Mobility, Force, Dominion, Reservoir and Convergence branches.
- Added campaign-save persistence for mastery XP, rank and per-hero skill investments, with full point respec support.
- Added a dedicated Gauntlet mastery screen accessible from a new Character screen button or `Ctrl+Shift+V` on the campaign map.
- Reused the proven deferred character-state transition from Guided Arrow: close the native character state, wait for the rebuilt campaign map, settle it, then push the mastery screen.
- Added a separate `Voidstep — Mastery Progression` MCM entry with an enable toggle and mastery XP multiplier.
- Added explicit progression unlocks for all six abilities and skill-driven energy-cost, cooldown, maximum-energy and regeneration growth.
- Gated configured Blink momentum preservation, complete time suspension, cooldown-only mode and unlimited energy behind their corresponding advanced mastery skills.
- Added bounded mastery XP awards for successful ability use, including Cleave XP scaled by successfully registered hits.
- Made the final convergence path and Avatar of the Void capstone fully reachable within the rank-99 point budget.
- Preserved the original unrestricted Voidstep MCM behavior whenever progression is disabled.
- Kept mission hot paths on one immutable cached profile with allocation-free thread-local runtime scoping and no periodic campaign tick or campaign-map agent scan.
- Added progression-specific source invariants and required both mastery prefabs in verified release packages.

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
