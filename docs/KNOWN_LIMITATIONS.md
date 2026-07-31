# Verified limitations

1. Cleave deliberately uses a synchronized body-aligned spell arc rather than forcing one native one-sided attack animation to represent a 340-degree sweep.
2. Synthetic melee contacts cannot reproduce every hidden physical collision-solver field. Shield interception is therefore approximate.
3. Projectile interaction for Windblast is disabled because the audited public API provides missile enumeration without a safe supported velocity setter.
4. The camera setting uses an ownership-checked FOV pulse rather than a view-layer camera-shake call.
5. Dark Vision interactable highlighting is not active because a bounded interactable query was not verified.
6. Optional particle and sound names vary by loaded asset set. Missing resources disable that element without failing the ability.
7. Player action-speed compensation during Bend Time has no public getter for the pre-existing per-channel action-speed multiplier. Cleanup restores `1.0`; concurrent mods changing the same channel require in-game compatibility testing.
8. TOR Q-wheel proxies are intentionally prevented from owning TOR's Spell/Prayer cast-stance animation. Voidstep handles proxy presentation and activation while TOR continues to provide selection UI and targeting state.
9. Source and invariant checks can prove ordering and ownership properties, while final skeletal pose, foot placement and action compatibility still require Bannerlord 1.3.15 runtime testing.
