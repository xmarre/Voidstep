# Voidstep — Arcane Melee Abilities

Mission-scoped single-player combat abilities for **Mount & Blade II: Bannerlord 1.3.15**, with optional visual compatibility for **The Old Realms 1.16**.

> Repository source is considered release-ready only after `./build.ps1` passes against the locked runtime references and the in-game matrix in `docs/TESTING.md` is completed. No TaleWorlds or TOR binary is redistributed.

## Features

- **Voidstep Cleave** teleports the player to a collision-validated position and performs a timed 340-degree rotating melee sweep. It captures the wielded melee weapon before teleporting, drives a native execution action through the sweep and applies one calculated native blow to every eligible enemy reached by the sweep.
- **Blink** uses a two-stage camera-aimed flow with a large green/red placement reticle, terrain and wall checks, enemy-relative placement, complete mission-time freeze during targeting and safe cancellation.
- **Windblast** processes a camera-aligned forward cone once per cast, applying centre-weighted force, distance falloff, knockback, optional knockdown and optional damage to individual agents.
- **Bend Time** uses a Voidstep-owned Bannerlord mission time-speed request and compensates the controlled player and mount so they remain materially faster than the slowed outside world.
- **Domino** stores linked human-agent indices, displays large target reticles and defers propagated damage or death until the native hit/removal callback has completed.
- **Dark Vision** immediately highlights nearby hostiles and refreshes them at a configurable low frequency. It never performs a full-agent scan every frame and clears every contour it applies.
- **Void Energy** is mission-local, configurable, regenerating and supports cooldown-only or unlimited modes.
- **Native cast actions** play on every successful ability activation, with suitable quick, heavy, vision and mounted fallbacks.

## Controls

| Ability | Default |
|---|---|
| Voidstep Cleave | `Ctrl+1` |
| Blink | `Ctrl+2`, then the same chord to confirm |
| Windblast | `Ctrl+3` |
| Bend Time | `Ctrl+4` |
| Domino | `Ctrl+5` |
| Dark Vision | `Ctrl+6` |

The six **primary keys are native serialized Bannerlord bindings**. Change them under:

```text
Options > Keybindings > Voidstep
```

Any keyboard or mouse button accepted by Bannerlord's keybinding screen can be selected. Each ability's optional modifier combination is configured separately under:

```text
MCM > Voidstep > Controls
```

Available modifier combinations are None, Control, Alt, Shift and their combinations. When the completed ability chord is active, Voidstep suppresses the same underlying raw key from the rest of Bannerlord for that press. Suppression remains latched until the primary key is released, even if the modifier is released first. This prevents formations, weapon slots, movement, attacks or another native action bound to the primary key from also firing. When a modifier is configured, pressing the primary key without that modifier remains completely native.

## Ability details

### Voidstep Cleave

Targeting prioritises a hostile agent in the camera aiming cone, then the aimed scene position, then a short forward fallback. Destination validation checks mission boundaries, terrain height and normal, water level, blocker navmeshes, nearby navigation geometry, sealed collision, standing clearance, nearby agents and mounts, and eight cliff probes. It searches the complete bounded fallback field, including outer rings, when the exact point is invalid.

The ability captures the currently wielded melee weapon before any teleport or delayed work. A large no-cull reticle shows the chosen destination during wind-up. After arrival, a native heavy execution action is paused and advanced by the same progress value that rotates the actor and schedules targets. Each accepted target receives a separately registered native melee blow using the captured weapon. Cleanup restores only the action speed owned by the sweep.

Live targeting allows an enemy entering an unpassed section of the sweep to be hit. Snapshot mode stores only agent indices and expected progress. A per-cast registry enforces one successful hit per agent and applies the configured whole-cast target cap.

### Blink

Press once to enter aiming and display a large placement reticle containing two ground rings, a vertical ring, directional spikes and a raised diamond. Green means the current validated destination is usable; red means validation failed. Mission time is frozen while targeting, while camera movement, preview updates and the confirmation chord remain responsive through application-time updates. Move the camera to reposition the target, then press the Blink chord again to confirm. Enemy-relative Blink places the requested point beyond the target and lets the common teleport validator select a safe nearby fallback. The owned zero-speed request is removed on confirmation, cancellation, timeout, death, replacement or mission end.

### Windblast

Windblast uses a nearby-enemy query only when cast. The cone follows camera aim rather than only actor facing. Force is strongest near the cone centre and near the player. Each target is processed once. Mount handling is configurable. A heavy native cast action and radial cast pulse identify activation before target impacts resolve.

### Bend Time

Bend Time adds one `Mission.TimeSpeedRequest` and removes it by its own request ID. It does not cache or restore a hard-coded global mission speed. Other active requests therefore remain authoritative after Voidstep releases its request. Player compensation covers locomotion, combat movement, swing, ready, reload, ranged ready/reload and Bannerlord's two verified native action channels, 0 and 1. The controlled mount receives speed, maneuver and acceleration compensation. Cleanup restores only values still equal to Voidstep's applied values.

### Domino

Domino links the nearest valid hostile humans up to the configured limit. Links are stored as agent indices and resolved through `Mission.FindAgentWithIndex`. Every linked target receives a large casting reticle; missiles and scene arrow entities are never target candidates. A hit or removal callback only queues identity-checked propagation records. The actual `RegisterBlow` path runs on the following mission tick after Bannerlord's native callback has unwound. Propagated hits remain tagged, and propagated lethal removals are consumed by a short-lived suppression ledger so they cannot start another death chain.

### Dark Vision

Dark Vision immediately queries nearby enemies on activation, then repeats the bounded query at the configured refresh interval. It applies temporary contour colours for unaware, alerted and engaged states. Removed, dead, distant and stale agents are cleared. No permanent material is changed.

## Installation

1. Install Harmony, ButterLib, UIExtenderEx and Mod Configuration Menu v5.
2. Delete any existing `Modules/Voidstep` folder.
3. Extract the release ZIP into the Bannerlord game directory.
4. Enable **Voidstep — Arcane Melee Abilities** in the launcher after its dependencies.

Expected layout:

```text
Modules/
└── Voidstep/
    ├── SubModule.xml
    ├── README.txt
    └── bin/
        └── Win64_Shipping_Client/
            ├── Voidstep.dll
            └── Voidstep.Core.dll
```

## Dependencies

Runtime:

- Bannerlord 1.3.15
- Harmony
- ButterLib
- UIExtenderEx
- Mod Configuration Menu v5

The Old Realms is optional. Voidstep detects TOR at runtime only to select optional particle-name candidates. Missing optional effects are caught and disabled without aborting an ability.

## MCM settings

MCM exposes the six per-ability modifier combinations, master switch, Void Energy modes and values, per-ability costs and cooldowns, teleport ranges, cleave radius, sweep, damage, direction, target cap, friendly fire, mounts, knockback and snapshot mode, Blink wall, momentum and targeting-freeze settings, Windblast cone values, Bend Time factor, duration and player compensation, Domino propagation options, Dark Vision range and refresh interval, effect intensity, camera emphasis and debug logging. Primary keys are configured through Bannerlord's native Keybindings screen, not a limited MCM dropdown.

## Presentation approach

Every successful ability activation requests a suitable native Bannerlord upper-body action. Cleave additionally owns a native heavy-release action for the duration of its sweep and drives its progress in sync with rotation and target timing. Placement feedback uses generated geometry with no back-face culling, high render order, synchronized mesh/factor/contour colours and optional particles. Arrow resources provide only a material donor; no arrow mesh is attached or rendered.

## Diagnostics

Enable **Debug logging** in MCM when testing. The log records the resolved native chord for every accepted input and every ability stage, including selected native cast action, camera aim, selected destination, validation result, reticle construction, teleport completion, queued and dispatched Domino propagations, candidate counts, registered Cleave hits, Windblast hits, Bend Time request acquisition and Dark Vision highlight counts.

Primary log location:

```text
Documents/Mount and Blade II Bannerlord/Configs/ModLogs/Voidstep.log
```

## Compatibility

- No campaign behavior is registered.
- No campaign-map tick is used.
- Harmony masks only the configured ability primary key while its complete modifier chord is active in a live mission.
- Boolean key state and movement-axis reads are both covered, so a reserved chord cannot leak into another native action.
- Domino never registers a propagated blow from inside Bannerlord's native hit or removal callbacks.
- Bend Time writes action speed only to Bannerlord's verified native agent channels 0 and 1.
- No static collection stores mission agents.
- Runtime state is created per mission and discarded on mission end.
- Native and TOR particle lookups are optional and non-fatal.

## Known limitations

- Shield collision is approximated because the public 1.3.15 API does not expose a safe way to fabricate the exact physical weapon-versus-shield contact generated by the engine's collision solver. Cleave still uses individual `Mission.CreateMeleeBlow` calls with the captured weapon and native blow registration.
- Windblast projectile deflection is disabled. `Mission.MissilesList` is readable, while a safe public per-missile velocity mutation path was not found in the audited 1.3.15 surface.
- The camera option currently uses an ownership-checked FOV emphasis pulse. It does not call a view-layer camera-shake API.
- Dark Vision highlights hostile agents. Interactable highlighting remains reserved because no bounded, mission-local interactable query was verified in the supplied API set.
- Cast and Cleave presentation uses existing native Bannerlord actions rather than a newly exported custom skeleton animation asset.

## Building

Requirements:

- Windows
- .NET SDK 8.0 or newer
- PowerShell 7 or Windows PowerShell 5.1
- Exact audit/reference files listed in `references/reference-manifest.json`

Place the references in `references/runtime` or set `BANNERLORD_REFERENCE_DIR`, then run:

```powershell
.\build.ps1
```

The script validates SHA-256 hashes, validates the audited API signatures when Python is available, restores pinned dependencies, runs pure logic tests, compiles Release, stages only runtime files, creates a directly installable ZIP, checks its contents, rejects bundled TaleWorlds/TOR/MCM DLLs, and prints ZIP and DLL SHA-256 hashes.

`-AllowNugetReferenceFallback` exists for non-release source validation. A release build must use the authoritative hash-locked files.

## Source structure

```text
src/Voidstep.Core/          Pure angle, scheduling, ownership, resource and state logic
tests/Voidstep.Core.Tests/  xUnit tests for deterministic logic
src/Voidstep/               Bannerlord mission runtime
module/Voidstep/            Release module template
docs/                       Architecture, API audit, testing and limitations
scripts/                    Source packaging and independent validation tools
references/                 Hash manifest; proprietary runtime files are ignored
```
