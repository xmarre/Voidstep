VOIDSTEP — ARCANE MELEE ABILITIES v1.2.4
Target: Mount & Blade II: Bannerlord 1.3.15, single-player
Optional integration: The Old Realms 1.16

INSTALLATION
Delete any older Modules/Voidstep folder first, then extract this archive into the Bannerlord game directory. The result must include:
Modules/Voidstep/SubModule.xml
Modules/Voidstep/GUI/Prefabs/VoidstepAbilityWheel.xml
Modules/Voidstep/GUI/Prefabs/VoidstepCharacterButton.xml
Modules/Voidstep/GUI/Prefabs/VoidstepMastery.xml
Modules/Voidstep/bin/Win64_Shipping_Client/Voidstep.dll
Modules/Voidstep/bin/Win64_Shipping_Client/Voidstep.Core.dll

REQUIREMENTS
Harmony
ButterLib
UIExtenderEx
Mod Configuration Menu v5

CASTING FLOW
Hold Q to open the cast wheel and release Q over an ability to select it.
A live area, destination, cone, radius or target-link indicator appears.
Press Right Mouse Button to cast the selected ability.
Press Escape or reopen the wheel to cancel the current selection.

TOR INTEGRATION
When TOR 1.16 is loaded, the six entries appear in TOR's existing Q ability wheel with [Voidstep] names and distinct icons. TOR exposes a flat known-ability list, so the entries are grouped by their shared prefix rather than a nested sub-wheel.
Voidstep releases TOR's temporary targeting stance after selection and restores wielded items and weapon bindings without taking ownership of native mouse-wheel weapon cycling.
Without TOR, Voidstep loads its own display-only six-segment Q wheel using the same selection and Mouse2 confirmation pipeline.

VOIDSTEP MASTERY
Mastery progression is optional and disabled by default.
Enable it under MCM > Voidstep — Mastery Progression > Progression.
The system provides 99 mastery ranks and 19 skills across Core, Mobility, Force, Dominion, Reservoir and Convergence branches.
Successful ability use awards bounded mastery XP. Each mastery rank grants one skill point.
Foundation skills unlock the six abilities.
Void Affinity unlocks Voidstep Cleave and increases its teleport range, radius, sweep, damage, knockback and target capacity.
Rift Step unlocks Blink and increases its teleport range.
The Force branch increases Windblast cone, range, force and damage and strengthens Bend Time duration and slowdown.
The Dominion branch increases Domino range, links and propagated damage and increases Dark Vision range and refresh speed.
The Reservoir branch increases Void Energy capacity and regeneration and reduces ability costs and cooldowns.
Convergence skills strengthen every ability and increase target capacity.
Momentum preservation, sealed-wall traversal, complete suspension, cooldown-only mode, unlimited energy and unlimited Cleave targets require advanced masteries.
Existing mastery XP and skill investments remain compatible.
Disabling progression restores unrestricted use of the normal Voidstep MCM configuration.

Open the mastery tree using the Voidstep Mastery button on the native Character screen or Ctrl+Shift+V on the campaign map.
The Character-screen button uses a deferred state transition and does not push the mastery UI until Bannerlord has returned to a settled campaign map.

DIRECT SELECTORS
Ctrl+1: select Voidstep Cleave
Ctrl+2: select Blink
Ctrl+3: select Windblast
Ctrl+4: select Bend Time
Ctrl+5: select Domino
Ctrl+6: select Dark Vision

Primary keys remain configurable under Options > Keybindings > Voidstep. Modifiers remain configurable under MCM > Voidstep > Controls. Direct bindings select an ability; they no longer bypass the targeting/confirmation stage.

CAST INDICATORS
Voidstep Cleave displays its validated teleport destination.
Blink freezes mission time while its validated destination reticle follows the camera and preserves the wielded weapon.
Windblast displays its aimed cone footprint.
Bend Time and Dark Vision display radius indicators.
Domino previews the human enemies that will be linked. Missiles, arrows and dropped projectile entities are not candidates.

TELEPORT ORIENTATION
Blink and Voidstep Cleave use one mission-scoped native position translator.
Mounted teleports move the mount attachment origin once and preserve the rider offset.
Occupied destinations remain allowed.
The current rider and mount body/look vectors are restored only for the exact mission main agent while native attachment and collision state settles.
No global Agent patch, Agent.Main lookup or presentation-agent mutation is used.

DOMINO
Domino links are persistent effects, not an active cast lock. Other abilities remain selectable and castable while links exist. Damage and death propagation are queued inside Bannerlord callbacks and dispatched on the following mission tick. An explicit propagation ledger prevents only Domino-owned callbacks from recursing; NoSound attacks from Cleave, Windblast or other valid player sources remain eligible to trigger Domino. Missing missile or synthetic affectors are repaired from the authoritative player or controlled-mount Blow owner.

TIME CONTROL
Blink freezes mission time during destination selection while camera and confirmation input remain responsive through application-time updates.
Bend Time slows the outside world while compensating locomotion, combat movement, swing, ready, reload, the two verified native action channels, the native maximum-speed multiplier and the controlled mount. Cleanup restores normal driven properties, action speed and native speed limits together when the effect ends.

PERFORMANCE
The combat runtime remains mission-scoped.
Progression stores only hero-keyed integer data and registers no campaign-map, hourly or daily tick.
Mission reads use one immutable cached mastery profile with no per-tick progression allocation or agent scan.

Enable Debug logging in MCM when reporting a problem. The log is written to Documents/Mount and Blade II Bannerlord/Configs/ModLogs/Voidstep.log.
