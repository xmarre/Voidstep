VOIDSTEP — ARCANE MELEE ABILITIES v1.1.0
Target: Mount & Blade II: Bannerlord 1.3.15, single-player
Optional integration: The Old Realms 1.16

INSTALLATION
Delete any older Modules/Voidstep folder first, then extract this archive into the Bannerlord game directory. The result must include:
Modules/Voidstep/SubModule.xml
Modules/Voidstep/GUI/Prefabs/VoidstepAbilityWheel.xml
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
When TOR 1.16 is loaded, the six entries appear in TOR's existing Q ability wheel with [Voidstep] names. TOR exposes a flat known-ability list, so the entries are grouped by their shared prefix rather than a nested sub-wheel.
Without TOR, Voidstep loads its own six-segment Q wheel using the same selection and Mouse2 confirmation pipeline.

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
Blink freezes mission time while its validated destination reticle follows the camera.
Windblast displays its aimed cone footprint.
Bend Time and Dark Vision display radius indicators.
Domino previews the human enemies that will be linked. Missiles, arrows and dropped projectile entities are not candidates.

DOMINO
Domino links are persistent effects, not an active cast lock. Other abilities remain selectable and castable while links exist. Damage and death propagation are queued inside Bannerlord callbacks and dispatched on the following mission tick. An explicit propagation ledger prevents only Domino-owned callbacks from recursing; NoSound attacks from Cleave, Windblast or other valid player sources remain eligible to trigger Domino. Controlled-mount hits are accepted as player sources.

TIME CONTROL
Blink freezes mission time during destination selection while camera and confirmation input remain responsive through application-time updates.
Bend Time slows the outside world while compensating locomotion, combat movement, swing, ready, reload, the two verified native action channels, and the controlled mount. Cleanup restores only values still owned by Voidstep.

Enable Debug logging in MCM when reporting a problem. The log is written to Documents/Mount and Blade II Bannerlord/Configs/ModLogs/Voidstep.log.
