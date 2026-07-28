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

No document in this repository treats this matrix as passed until Bannerlord is actually launched and the scenarios are executed.
