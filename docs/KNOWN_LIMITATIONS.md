# Verified limitations

1. Blink and Voidstep Cleave use the camera ray as the authoritative cast frame. Their indicators, validated destinations, post-teleport facing, and Cleave sweep alignment follow the camera rather than rider yaw, mount movement, or nearby enemies.
2. Dynamic agents and projectiles are ignored by teleport targeting and path checks. Static world geometry, mission boundaries, steep terrain, water, cliffs, navigation blockers, and standing clearance still restrict destinations.
3. Teleporting can place the player very close to or overlapping an agent because agent occupancy no longer blocks escape from a surrounding crowd; Bannerlord's native collision response resolves subsequent separation.
4. Cleave uses a synchronized camera-aligned spell arc rather than forcing one native one-sided attack animation to represent a 340-degree sweep.
5. Synthetic melee contacts cannot reproduce every hidden physical collision-solver field. Shield interception is therefore approximate.
6. Projectile interaction for Windblast is disabled because the audited public API provides missile enumeration without a safe supported velocity setter.
7. Bend Time compensates the player and controlled mount through driven properties, native action speed, and a native maximum-speed multiplier. Perceived speed can still vary during animation-specific locks or when another mod continuously rewrites the same native properties.
8. Dark Vision interactable highlighting is not active because a bounded interactable query was not verified.
9. Optional particle and sound names vary by loaded asset set. Missing resources disable that element without failing the ability.
10. TOR Q-wheel proxies are prevented from owning TOR's Spell/Prayer cast-stance animation. Voidstep handles proxy presentation and activation while TOR continues to provide selection UI and targeting state.
11. Source and invariant checks can prove ordering and ownership properties, while final skeletal pose, collision separation, and action compatibility still require Bannerlord 1.3.15 runtime testing.
