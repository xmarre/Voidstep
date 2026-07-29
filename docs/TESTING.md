# Testing

## Automated tests

`tests/Voidstep.Core.Tests/CoreLogicTests.cs` covers deterministic sweep, targeting fallback, hit registry, resource, cooldown, ownership and cancellation logic.

The independent Python mirror additionally covers:

- Domino callbacks enqueue without dispatching
- deferred propagation dispatches only on the following mission tick
- a legitimate `NoSound` player hit remains eligible to trigger Domino
- explicit propagated-hit markers suppress only Domino-owned synchronous callbacks
- unconsumed propagated-hit markers are removed immediately
- propagated lethal removals cannot start another death chain
- persistent Domino links do not block selecting another ability
- Blink owns only its targeting phase until Mouse2 confirmation
- plain formation-number passthrough without the configured modifier
- Bend Time source is restricted to native action channels 0 and 1

Run:

```text
dotnet test tests/Voidstep.Core.Tests/Voidstep.Core.Tests.csproj -c Release
python scripts/run_logic_mirror_tests.py
python scripts/verify_source_invariants.py
python scripts/validate_api_surface.py references/runtime
```

## Required wheel and input matrix

A release must record pass/fail and logs for:

- without TOR, holding `Q` opens the standalone six-segment Voidstep wheel
- moving the mouse around the centre highlights exactly one of six entries
- releasing `Q` selects the highlighted ability and closes the wheel
- reopening the wheel or pressing `Escape` cancels the previous selection and clears its preview
- Right Mouse Button casts the selected ability exactly once
- Right Mouse Button does not block, attack, cancel or trigger another native action during the owned confirmation press
- Right Mouse Button returns to native behaviour after release
- the standalone wheel prefab is visible at 16:9, 16:10, ultrawide and UI-scale variants
- wheel focus and cursor ownership are returned after selection, cancellation, death and mission end
- with TOR 1.16, the six `[Voidstep]` entries appear in TOR's existing Q wheel alongside native TOR abilities
- selecting a normal TOR spell still follows TOR's native targeting and casting flow
- selecting a `[Voidstep]` entry starts Voidstep's indicator and Mouse2-confirmation flow
- selecting and casting a Voidstep proxy does not execute TOR's placeholder spell object
- removing/changing the player agent removes old proxies and injects exactly one new set for the current agent
- without TOR, no `TOR_Core` type is required and no TOR assembly exception is logged
- when TOR integration fails safely, the standalone wheel remains usable
- `Ctrl+1` through `Ctrl+6` select only the corresponding ability and do not cast before Mouse2
- completed selector chords do not open the order interface or execute formation commands
- plain `1` through `6` remain native with the default Control modifiers
- releasing Control before the number key does not leak a delayed formation command
- MCM modifier changes and native key rebindings apply during the mission
- exact modifier matching rejects unconfigured extra modifiers
- duplicate selector chords produce a visible/logged conflict and never select two abilities
- text input or on-screen keyboard disables selector polling and suppression
- disabling Voidstep, player death and mission end clear selector, wheel and suppression ownership

## Required cast-indicator matrix

- Voidstep Cleave shows the current validated teleport destination before payment or teleport
- invalid Cleave placement is visibly rejected and does not consume resources
- Blink selection freezes mission actors, missiles and animation while camera, preview and Mouse2 remain responsive
- Blink confirmation and cancellation remove only the owned zero-speed request
- Windblast shows the camera-aligned cone footprint before confirmation
- Bend Time shows its radius/centre preview before confirmation
- Domino shows the exact hostile humans proposed for linking and never marks missiles, arrows or dropped projectiles
- Dark Vision shows its radius preview before confirmation
- every successful confirmed ability plays a visible native cast action
- mounted activation uses a valid mounted fallback and does not dismount the player
- selection previews reuse/move marker entities rather than creating new markers every frame
- all preview markers are removed on cast, cancellation, actor replacement, disable and mission end

## Required Domino matrix

- selecting and confirming Domino creates at least two links when valid enemies exist
- active Domino links do not make `AbilityManager.IsBusy` true and do not block Cleave, Blink, Windblast, Bend Time or Dark Vision
- a normal weapon hit on one linked target queues propagation to the others
- a Cleave hit carrying `BlowFlags.NoSound` still queues Domino propagation
- a Windblast synthetic hit carrying `BlowFlags.NoSound` still queues Domino propagation
- a controlled-mount hit counts as a player source
- queued damage is registered only on the following mission tick, never inside `OnAgentHit`
- each Domino-owned synchronous hit callback consumes exactly one explicit suppression marker and never requeues
- a failed direct-blow registration removes its unconsumed marker so the next real player hit is not discarded
- low-damage source hits that inflicted damage propagate at least one point after scaling
- repeated strikes and rapid multi-hit weapons do not recurse or crash
- killing a linked target queues lethal propagation outside `OnAgentRemoved`
- propagated lethal removals are consumed once and cannot start another death wave
- removed targets and reused agent indices cannot receive stale queued damage
- recasting Domino replaces the previous link set and clears old markers and pending work
- actor replacement, disable and mission end clear links, markers, pending propagation and both suppression ledgers

## Required ability and time-control matrix

- Cleave preserves the captured melee weapon and drives its execution action through the full sweep
- Cleave action progress and speed return to normal on completion, interruption and cleanup
- Blink targeting expires after eight seconds of application time while mission time is frozen
- Bend Time activation survives the first mission tick without protected-memory failure
- Bend Time writes action speed only to native channels 0 and 1
- the outside world remains slowed while player movement, turning, attacks, ready, reload and recovery are materially faster
- mounted Bend Time compensates speed, manoeuvre and acceleration
- expiration, repeated casts, manual disable, death, replacement and mission end restore only owned values
- Dark Vision applies hostile contours immediately and clears stale agents

## Required battle scenarios

- native game without TOR
- TOR 1.16 battle mission with native TOR spells and Voidstep entries in the same wheel
- one enemy directly ahead and behind
- dense formations with at least 30 enemies
- walls, cliffs, props, water and occupied teleport destinations
- player mounted and dismounted
- friendlies in radius with friendly fire off and on
- shielded enemies
- one-handed and two-handed melee weapons
- empty target areas
- rapid Q-wheel reopening, Mouse2 confirmation and Escape cancellation
- switching selections during cooldown, persistent Domino, Bend Time and Dark Vision
- player death during selection, Blink freeze, Cleave wind-up and active sweep
- mission end during wheel display, Blink targeting, Bend Time and queued Domino propagation

No document in this repository treats this runtime matrix as passed until Bannerlord is launched and the scenarios are executed.
