# Voidstep — Arcane Melee Abilities

Single-player combat abilities for **Mount & Blade II: Bannerlord 1.3.15**, with optional **The Old Realms 1.16** cast-wheel integration and an optional campaign-persisted mastery system.

> Repository source is considered release-ready only after `./build.ps1` passes against the locked runtime references and the in-game matrix in `docs/TESTING.md` is completed. No TaleWorlds or TOR binary is redistributed.

## Casting flow

1. Hold `Q` to open the cast wheel.
2. Move to an ability and release `Q` to select it.
3. Aim using the live destination, area, cone, radius or linked-target indicator.
4. Press **Right Mouse Button** to cast.
5. Press `Escape` or reopen the wheel to cancel the selection.

When TOR 1.16 is loaded, Voidstep injects six `[Voidstep]` entries into TOR's existing Q ability wheel. TOR exposes one flat known-ability list, so the entries are grouped by their shared prefix rather than a nested sub-wheel. The integration uses runtime reflection and adds no hard TOR assembly dependency.

Without TOR, Voidstep loads its own six-segment Gauntlet Q wheel. Both wheel paths feed the same mission-scoped selection, preview and Mouse2-confirmation controller.

## Features

- **Voidstep Cleave** previews a collision-validated teleport destination and performs a timed 340-degree rotating melee sweep against every eligible enemy reached.
- **Blink** freezes the outside world while a large green/red placement reticle follows camera aim, then teleports on Mouse2 confirmation.
- **Windblast** previews a camera-aligned cone before applying centre-weighted force, falloff, knockback, optional knockdown and optional damage.
- **Bend Time** previews its effect area, owns one mission speed request and compensates the controlled player and mount.
- **Domino** previews and links nearby hostile humans, then propagates eligible player or controlled-mount hits to the other linked targets on the following mission tick.
- **Dark Vision** previews its radius, immediately highlights nearby hostiles and refreshes them at a configurable low frequency.
- **Void Energy** is mission-local, configurable, regenerating and supports cooldown-only or unlimited modes.
- **Native cast actions** play on successful activation, with suitable quick, heavy, vision and mounted fallbacks.
- **Voidstep Mastery** optionally adds a level-99, 19-skill progression tree with save persistence, specialisation and respec support.

## Voidstep Mastery

Progression is disabled by default. Enable it under:

```text
MCM > Voidstep — Mastery Progression > Progression
```

While enabled:

- successful ability use awards bounded mastery XP;
- each mastery rank grants one skill point, up to rank 99;
- the tree contains Core, Mobility, Force, Dominion, Reservoir and Convergence branches;
- rank 1 in the corresponding foundation skill unlocks each of the six abilities;
- **Blink and Voidstep Cleave gain real teleport-range progression** through Rift Step, Rift Reach and Void Dancer;
- Cleave also gains radius, sweep angle, damage, knockback, knockdown reliability and target capacity;
- Windblast gains cone angle, range, force and damage;
- Bend Time gains duration and slowdown strength;
- Domino gains marking range, link capacity and damage propagation;
- Dark Vision gains detection range and refresh speed;
- Unbound Power, Singularity and Avatar of the Void amplify mechanical ability effects across every branch;
- energy-cost and cooldown reductions remain secondary support bonuses rather than the tree's primary reward;
- Blink momentum preservation, sealed-wall traversal, complete time suspension, cooldown-only mode, unlimited energy and unlimited Cleave targets require advanced mastery skills;
- **Avatar of the Void** is fully reachable and releases progression energy and regeneration caps at rank 10.

The normal Voidstep MCM configuration remains unrestricted whenever progression is disabled. Existing v1.2.0 mastery XP and invested points remain compatible because the persisted skill IDs are unchanged; those points now map to the stronger v1.2.1 effects.

Open the mastery tree from the **Voidstep Mastery** button on the native Character screen, or press:

```text
Ctrl+Shift+V on the campaign map
```

The Character-screen route closes the native character state, waits for the campaign map to rebuild, allows it to settle, and only then opens the mastery screen. This avoids pushing a Gauntlet screen onto an invalid Bannerlord state stack.

## Direct ability selectors

The six native configurable bindings remain available, but they **select** an ability instead of casting it immediately.

| Ability | Default selector |
|---|---|
| Voidstep Cleave | `Ctrl+1` |
| Blink | `Ctrl+2` |
| Windblast | `Ctrl+3` |
| Bend Time | `Ctrl+4` |
| Domino | `Ctrl+5` |
| Dark Vision | `Ctrl+6` |

Change primary keys under:

```text
Options > Keybindings > Voidstep
```

Change each optional modifier under:

```text
MCM > Voidstep > Controls
```

A completed selector chord suppresses the same underlying native key until release. Plain number keys remain native when their configured modifier is not held. The selected ability still requires Right Mouse Button confirmation.

## Ability details

### Voidstep Cleave

Targeting prioritises a hostile in the camera cone, then the aimed scene point, then a short forward fallback. Validation checks mission boundaries, terrain, water, blocker navmeshes, collision, standing clearance, nearby agents and mounts, cliffs and configured range. The preview reticle turns invalid when no safe destination exists.

The currently wielded melee weapon is captured before teleport. A native heavy execution action is driven through the same progress that rotates the actor and schedules separately registered blows. Cleanup restores only action state owned by the sweep.

### Blink

Selection immediately enters Blink's frozen targeting mode. The reticle follows camera aim and remains responsive through application-time updates while mission actors, missiles and animation are frozen. Right Mouse Button confirms. Escape, wheel reselection, timeout, death, replacement and mission end release only Blink's owned zero-speed request.

### Windblast

The pre-cast indicator shows the aimed cone footprint. Target collection occurs only on confirmation. Force is strongest near the cone centre and near the player. Each valid target is processed once; mount handling remains configurable.

### Bend Time

Bend Time uses one owned `Mission.TimeSpeedRequest`. Player compensation covers locomotion, combat movement, swing, ready, reload, ranged ready/reload and Bannerlord's verified native action channels 0 and 1. The controlled mount receives speed, manoeuvre and acceleration compensation. Cleanup restores only values still equal to Voidstep's applied values.

### Domino

Domino links the nearest valid hostile humans up to the configured limit. Missiles, arrow entities and dropped projectiles are never candidates. Persistent links are independent from transient casting state, so Domino does not block selecting or casting another ability.

Hit and removal callbacks only queue identity-checked work. The actual `RegisterBlow` path runs on the following mission tick after Bannerlord's native callback has unwound. An explicit per-target propagation ledger suppresses only Domino's own synchronous callbacks. `BlowFlags.NoSound` is not used as ownership because Cleave, Windblast and other valid synthetic player attacks can also carry it. Controlled-mount hits count as player sources.

### Dark Vision

Dark Vision performs one bounded nearby-enemy query on activation and repeats it at the configured interval. Removed, dead, distant and stale agents are cleared; no permanent material is changed.

## Installation

1. Install Harmony, ButterLib, UIExtenderEx and Mod Configuration Menu v5.
2. Delete any existing `Modules/Voidstep` folder.
3. Extract the release ZIP into the Bannerlord game directory.
4. Enable **Voidstep — Arcane Melee Abilities** after its dependencies.

Expected layout:

```text
Modules/
└── Voidstep/
    ├── SubModule.xml
    ├── README.txt
    ├── GUI/
    │   └── Prefabs/
    │       ├── VoidstepAbilityWheel.xml
    │       ├── VoidstepCharacterButton.xml
    │       └── VoidstepMastery.xml
    └── bin/
        └── Win64_Shipping_Client/
            ├── Voidstep.dll
            └── Voidstep.Core.dll
```

## Dependencies

- Bannerlord 1.3.15
- Harmony
- ButterLib
- UIExtenderEx
- Mod Configuration Menu v5

The Old Realms is optional. When its audited 1.16 ability API is present, Voidstep integrates with TOR's existing wheel. If TOR is absent or integration initialization fails, the standalone wheel is used.

## Diagnostics

Enable **Debug logging** in MCM. The log records wheel mode, selected ability, Mouse2 confirmation, preview state, TOR proxy injection, teleport validation, cast actions, Cleave blows, Windblast hits, time-request ownership, Domino queue/dispatch ownership and Dark Vision refreshes.

```text
Documents/Mount and Blade II Bannerlord/Configs/ModLogs/Voidstep.log
```

Development console helpers:

```text
voidstep.open_mastery
voidstep.add_mastery_xp <positive amount>
```

## Compatibility and performance

- The combat runtime remains mission-scoped and owns all mission agents, markers, input state and effects for one mission lifetime.
- The progression campaign behavior stores only versioned hero-keyed integer state and registers no hourly, daily or campaign-map tick.
- Mission code reads one immutable volatile mastery profile rebuilt only on lifecycle/state mutations.
- Progression setting interception uses allocation-free thread-static ownership limited to Voidstep ability execution.
- Mechanical mastery scaling patches only Voidstep-owned settings reads and performs constant-time arithmetic with no allocations or agent scans.
- No static collection stores mission agents.
- XP throttle state is weakly owned by mission controllers/managers.
- Wheel, preview, proxy, marker and input state is deterministically removed.
- TOR integration is reflection-isolated and does not redistribute or compile against `TOR_Core.dll`.
- Domino never registers a propagated blow from inside Bannerlord's native hit or removal callbacks.
- Preview updates reuse marker entities and bounded buffers rather than allocating or scanning the full agent list every frame.

## Known limitations

- TOR 1.16 exposes a flat ability collection; Voidstep entries use a `[Voidstep]` prefix rather than a nested category page.
- Cast and Cleave presentation uses existing native Bannerlord actions rather than a newly exported skeleton animation asset.
- Shield collision is approximated because the public 1.3.15 API does not expose the exact engine weapon-versus-shield collision contact.
- Windblast projectile deflection remains disabled because no safe public per-missile velocity mutation path was verified.
- Dark Vision highlights hostile agents; bounded interactable discovery remains unavailable in the audited API.

## Building

Place the exact files listed in `references/reference-manifest.json` in `references/runtime`, or set `BANNERLORD_REFERENCE_DIR`, then run:

```powershell
.\build.ps1
```

The build validates reference hashes and API signatures, runs xUnit and independent mirrors, checks mission, progression, mastery-power, wheel/TOR and runtime-regression invariants, compiles Release, stages only runtime files, requires all three Gauntlet prefabs, rejects bundled TaleWorlds/TOR/MCM DLLs and emits ZIP/DLL/source SHA-256 identities.
