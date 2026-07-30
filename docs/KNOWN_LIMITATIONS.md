# Verified limitations

1. No original skeletal animation asset is included. Cleave now requests a normal Bannerlord left/right melee attack through the native `SetEventControlFlags` path and no longer forces a heavy-thrown/command action, scripted body yaw or action-progress override.
2. If another Bannerlord version or overhaul removes the reflected one-enum `SetEventControlFlags` overload, Cleave remains functional and uses its facing-aligned particle arc, but the native swing animation is omitted and a debug message is written.
3. Mounted Cleave uses the same stable facing, constrained destination, centered virtual sweep and native control request. The exact mounted attack animation depends on the active mount/action set supplied by the game or overhaul.
4. Synthetic melee contacts cannot reproduce every hidden physical collision-solver field. Shield interception is therefore approximate.
5. Projectile interaction for Windblast is disabled because the audited public API provides missile enumeration without a safe supported velocity setter.
6. Dark Vision interactable highlighting is not active because a bounded interactable query was not verified.
7. Optional particle and sound names vary by loaded asset set. Missing resources disable that element without failing the ability.
8. Player action-speed compensation during Bend Time has no public getter for the pre-existing per-channel action-speed multiplier. Cleanup restores `1.0`; concurrent mods changing the same channel require in-game compatibility testing.
9. Source and invariant checks prove that Cleave uses one live facing vector, constrained forward fallback placement and no native turn writes. Final skeletal pose and attack-action compatibility still require Bannerlord 1.3.15 runtime testing.
