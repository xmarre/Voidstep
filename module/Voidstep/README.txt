VOIDSTEP — ARCANE MELEE ABILITIES v1.0.6
Target: Mount & Blade II: Bannerlord 1.3.15, single-player
Optional compatibility preset: The Old Realms 1.16

INSTALLATION
Delete any older Modules/Voidstep folder first, then extract this archive into the Bannerlord game directory. The result must be:
Modules/Voidstep/SubModule.xml
Modules/Voidstep/bin/Win64_Shipping_Client/Voidstep.dll
Modules/Voidstep/bin/Win64_Shipping_Client/Voidstep.Core.dll

REQUIREMENTS
Harmony
ButterLib
UIExtenderEx
Mod Configuration Menu v5

DEFAULT CONTROLS
Ctrl+1: Voidstep Cleave
Ctrl+2: Blink (press once to aim, again to confirm)
Ctrl+3: Windblast
Ctrl+4: Bend Time
Ctrl+5: Domino
Ctrl+6: Dark Vision

PRIMARY KEY CONFIGURATION
Options > Keybindings > Voidstep

MODIFIER CONFIGURATION
MCM > Voidstep > Controls

Primary keys use Bannerlord's native serialized keybinding system and can be changed to any accepted keyboard or mouse button. Each ability has its own optional modifier selection in MCM. Modifier state is read live when the chord is evaluated. While a completed ability chord is active, the same raw key and the corresponding Bannerlord formation GameKey are blocked from native consumers. Suppression remains latched until the primary key is released, even if the modifier is released first. Pressing the primary key without its configured modifier remains native.

Enable Debug logging in MCM when reporting an ability problem. The log is written to Documents/Mount and Blade II Bannerlord/Configs/ModLogs/Voidstep.log.
