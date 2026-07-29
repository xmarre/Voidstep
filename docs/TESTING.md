# Testing

## Automated pure logic tests

`tests/Voidstep.Core.Tests/CoreLogicTests.cs` covers:

- angle normalisation
- clockwise and counter-clockwise ordering
- sweep-gap handling
- radius filtering
- one-hit registry
- angle-to-animation damage timing
- destination fallback ordering
- resource modes and costs
- cooldown transitions
- Domino recursion blocking
- time ownership tokens
- cancellation cleanup

Run with:

```powershell
dotnet test .\tests\Voidstep.Core.Tests\Voidstep.Core.Tests.csproj -c Release
```

Independent standard-library Python mirrors and source-invariant checks are available:

```text
python scripts/run_logic_mirror_tests.py
python scripts/verify_source_invariants.py
python scripts/validate_api_surface.py references/runtime
```

## Required input-binding matrix

A release must record pass/fail and logs for:

- `Ctrl+1` through `Ctrl+6` each produce exactly one `Input accepted` log and activate only the corresponding Voidstep ability
- `Ctrl+1` through `Ctrl+6` do not open the order interface, select a formation or execute a native formation command
- plain `1` through `6` still select formations with the default Control modifiers
- releasing Control before releasing the number key does not trigger a delayed formation command
- changing one modifier in MCM during a mission, such as Cleave from Control to Alt, immediately makes `Alt+1` active and leaves plain `1` native
- exact modifier matching rejects additional unconfigured modifiers, such as `Ctrl+Alt+1` when only Control is configured
- rebinding an ability primary key under `Options > Keybindings > Voidstep` persists after restarting the game
- binding to a weapon-slot key suppresses only that weapon-slot action while the complete ability chord is active
- binding to a movement key suppresses movement while the complete chord is active and preserves ordinary movement without the configured modifier
- binding to an attack or mouse button suppresses the native action while the complete chord is active
- modifier values None, Control, Alt, Shift and combined modifiers activate exactly as configured
- two abilities deliberately assigned the same chord produce a visible/logged configuration conflict and never activate both from one press
- opening text input or the on-screen keyboard disables ability polling and suppression
- mission end, player death and disabling Voidstep clear every latched input key

## Required ability presentation and time-control matrix

A release must record pass/fail and logs for:

- Blink displays a double-ring casting sigil at the current destination, never an arrow model
- Domino displays the same casting-sigil language above each linked human target and never marks missiles or arrow entities
- Voidstep Cleave displays a destination sigil during wind-up
- Windblast and Bend Time display visible radial cast pulses
- Dark Vision immediately applies hostile-agent contours
- Blink destination selection freezes mission actors, missiles and animation while camera movement, preview movement and the confirmation chord remain responsive
- Blink confirmation and cancellation remove only the owned zero-speed request
- Blink targeting still expires after eight seconds of application time while mission time is frozen
- Bend Time leaves outside actors slowed by the configured factor while the player can move, turn, attack, ready, reload and recover materially faster
- mounted Bend Time compensates the controlled mount's speed, maneuver and acceleration
- ending Bend Time restores only values still equal to Voidstep's applied compensation
- player death, replacement, mission end and disabling Voidstep clean up every owned time request and compensation value

## Required Bannerlord runtime matrix

A release must record pass/fail and logs for:

- one enemy directly ahead
- one enemy behind
- enemies around the full sweep and inside the configured gap
- at least 30 enemies in radius
- repeated casts and rapid input
- enemy killed or removed during sweep
- player death during wind-up, teleport and recovery
- mission end during Bend Time
- walls, cliffs, props, water and occupied destinations
- a locked Cleave enemy surrounded by a dense formation, verifying the complete fallback field finds a safe point or reports the final safety failure
- Cleave fallback never exceeds the configured teleport range
- player mounted and mount near destination
- friendlies in radius with friendly fire off and on
- shielded enemies
- one-handed and two-handed weapons
- empty target area
- switching abilities during recovery
- repeated Dark Vision toggles
- Domino targets dying in different orders
- native game without TOR
- TOR 1.16 battle mission

No document in this repository treats these matrices as passed until Bannerlord is actually launched and the scenarios are executed.
