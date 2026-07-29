VOIDSTEP — ARCANE MELEE ABILITIES v1.0.9
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

Primary keys use Bannerlord's native serialized keybinding system and can be changed to any accepted keyboard or mouse button. Each ability has its own optional modifier selection in MCM. Modifier state is read live when the chord is evaluated. While a completed ability chord is active, the same raw primary key is blocked from native consumers. For number-row D1-D6 bindings, the corresponding Bannerlord formation GameKey is also blocked. Suppression remains latched until the primary key is released, even if the modifier is released first. Pressing the primary key without its configured modifier remains native.

CASTING FEEDBACK
All six abilities play a suitable native upper-body cast action. Voidstep Cleave also drives a visible execution action through the rotating sweep while preserving the captured melee weapon and separately registered hits.
Blink, Voidstep Cleave and Domino use large no-cull placement reticles with two ground rings, a vertical ring, directional spikes, a raised diamond and particles. Windblast and Bend Time use radial cast pulses. Dark Vision uses hostile-agent contours. Arrow geometry is not used for casting indicators.

DOMINO SAFETY
Domino never registers propagated blows from inside Bannerlord's native hit or removal callbacks. Damage and death propagation are queued and dispatched on the following mission tick. Propagated callbacks are tagged and lethal propagation is suppressed from starting another chain.

TIME CONTROL
Blink freezes mission time while destination targeting is active. Preview movement, confirmation, cancellation and expiry remain responsive through application-time updates.
Bend Time slows the outside world while compensating the controlled player for locomotion, combat movement, swing, ready, reload and the two verified Bannerlord native action channels. The controlled mount receives corresponding speed and maneuver compensation. Cleanup restores only values still owned by Voidstep.

Enable Debug logging in MCM when reporting an ability problem. The log is written to Documents/Mount and Blade II Bannerlord/Configs/ModLogs/Voidstep.log.
