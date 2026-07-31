# Verified limitations

1. Cleave deliberately uses a synchronized camera-aligned spell arc rather than forcing one native one-sided attack animation to represent a 340-degree sweep.
2. Synthetic melee contacts cannot reproduce every hidden physical collision-solver field. Shield interception is therefore approximate.
3. Projectile interaction for Windblast is disabled because the audited public API provides missile enumeration without a safe supported velocity setter.
4. The camera setting uses an ownership-checked FOV pulse rather than a view-layer camera-shake call.
5. Dark Vision interactable highlighting is not active because a bounded interactable query was not verified.
6. Optional particle and sound names vary by loaded asset set. Missing resources disable that element without failing the ability.
7. Bend Time leaves mission time at native 1.00x and slows registered non-player agents through Bannerlord's public custom-driven-property push, native per-agent speed limit and verified action channels 0–1. The player and controlled mount are never mutated. Final feel still requires Bannerlord 1.3.15 runtime testing.
8. TOR Q-wheel proxies are intentionally prevented from owning TOR's Spell/Prayer cast-stance animation. Voidstep handles proxy presentation and activation while TOR continues to provide selection UI and targeting state.
9. Blink and Voidstep indicators use the native mission-screen projected reticle across the complete circular range. Static mission geometry and safe-standing validation remain authoritative.
10. Teleports submit one atomic native position-and-camera-facing frame. No frame is replayed from mission ticks; the immediate Blink postfix duplicate is suppressed to prevent repeated full-body rotations.
11. Domino damage propagation uses the finalized collision damage and `Blow.OwnerId` when Bannerlord's supplied affector is missing or not the controlled player. Death propagation remains a separate optional MCM setting.
12. Source and invariant checks can prove ordering and ownership properties, while final skeletal pose, foot placement and action compatibility still require Bannerlord 1.3.15 runtime testing.
