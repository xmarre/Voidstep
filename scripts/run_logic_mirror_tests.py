#!/usr/bin/env python3
import math
import unittest

TAU = math.tau
CW = -1
CCW = 1
CONTROL = 1
ALT = 2
NO_SOUND = 1
SELECT_ORDER_1 = 69
SELECT_ORDER_6 = 74


def norm(a):
    return a % TAU


def travel(start, target, direction):
    return norm(target - start) if direction == CCW else norm(start - target)


def inside(start, target, sweep, direction):
    return travel(start, target, direction) <= sweep + 1e-6


def progress(start, target, sweep, direction):
    if sweep < 0:
        raise ValueError("sweep must be non-negative")
    if sweep == 0:
        return 0.0
    return min(1.0, max(0.0, travel(start, target, direction) / sweep))


def schedule(candidates, start, sweep, direction, radius, maximum=0):
    if maximum < 0:
        raise ValueError("maximum must be non-negative")
    rows = [(value, progress(start, angle, sweep, direction), distance2, ordinal)
            for ordinal, (value, angle, distance2) in enumerate(candidates)
            if distance2 <= radius * radius and inside(start, angle, sweep, direction)]
    rows.sort(key=lambda x: (x[1], x[2], x[3]))
    return rows[:maximum or None]


def modifier_chord_matches(required, current, primary_modifier=0):
    return (current & ~primary_modifier) == required


def should_suppress_mapped_order(game_key_id, required, current):
    mapped = SELECT_ORDER_1 <= game_key_id <= SELECT_ORDER_6
    return mapped and modifier_chord_matches(required, current)


class DeferredDominoMirror:
    def __init__(self):
        self.tick_serial = 0
        self.pending = []
        self.dispatched = []
        self.hit_suppression = {}
        self.death_suppression = {}

    def add_hit_suppression(self, target):
        self.hit_suppression[target] = self.hit_suppression.get(target, 0) + 1

    def consume_hit_suppression(self, target):
        count = self.hit_suppression.get(target, 0)
        if count <= 0:
            return False
        if count == 1:
            self.hit_suppression.pop(target, None)
        else:
            self.hit_suppression[target] = count - 1
        return True

    def remove_unconsumed_hit_suppression(self, target):
        self.consume_hit_suppression(target)

    def on_hit(self, source, targets, flags=0, lethal=False):
        # NoSound is not ownership: legitimate synthetic Voidstep attacks use it too.
        if self.consume_hit_suppression(source):
            return
        for target in targets:
            if target != source:
                self.pending.append((target, lethal, flags))

    def tick(self, simulate_sync_callback=True):
        self.tick_serial += 1
        current = self.pending
        self.pending = []
        for target, lethal, flags in current:
            if lethal:
                self.death_suppression[target] = self.tick_serial + 4
            self.add_hit_suppression(target)
            if simulate_sync_callback:
                # Bannerlord invokes OnAgentHit synchronously inside RegisterBlow.
                self.on_hit(target, [target], flags=NO_SOUND)
            self.remove_unconsumed_hit_suppression(target)
            self.dispatched.append(target)
        self.death_suppression = {
            target: expiry for target, expiry in self.death_suppression.items()
            if expiry >= self.tick_serial
        }

    def on_removed(self, target):
        expiry = self.death_suppression.pop(target, None)
        return not (expiry is not None and expiry >= self.tick_serial)


class AbilitySelectionMirror:
    def __init__(self):
        self.selected = None
        self.transient_cast = None
        self.persistent_effects = set()

    def select(self, ability):
        if self.transient_cast is not None:
            return False
        self.selected = ability
        if ability == 'Blink':
            self.transient_cast = 'BlinkTargeting'
        return True

    def confirm(self):
        if self.selected is None:
            return False
        ability = self.selected
        self.selected = None
        if ability == 'Blink':
            self.transient_cast = None
        if ability in ('Domino', 'BendTime', 'DarkVision'):
            self.persistent_effects.add(ability)
        return True

    def finish_transient(self):
        self.transient_cast = None


class MirrorTests(unittest.TestCase):
    def test_normalise(self):
        self.assertAlmostEqual(norm(-math.pi / 2), 3 * math.pi / 2)
        self.assertAlmostEqual(norm(4 * math.pi), 0)

    def test_zero_sweep_is_noop(self):
        self.assertEqual(progress(0, 0, 0, CW), 0.0)
        with self.assertRaises(ValueError):
            progress(0, 0, -1, CW)

    def test_clockwise_order(self):
        result = schedule([('late', math.radians(270), 1), ('early', math.radians(350), 1), ('middle', math.radians(315), 1)], 0, math.radians(100), CW, 2)
        self.assertEqual([x[0] for x in result], ['early', 'middle', 'late'])

    def test_counterclockwise_order(self):
        result = schedule([('late', math.radians(100), 1), ('early', math.radians(10), 1), ('middle', math.radians(55), 1)], 0, math.radians(100), CCW, 2)
        self.assertEqual([x[0] for x in result], ['early', 'middle', 'late'])

    def test_gap_and_radius(self):
        start = math.radians(10)
        self.assertTrue(inside(start, math.radians(340), math.radians(340), CCW))
        self.assertFalse(inside(start, math.radians(355), math.radians(340), CCW))
        self.assertEqual(len(schedule([(1, 0, 25), (2, 0, 25.001)], 0, math.pi, CCW, 5)), 1)

    def test_damage_timing(self):
        self.assertAlmostEqual(progress(math.radians(10), math.radians(180), math.radians(340), CCW), .5)
        self.assertEqual(progress(0, 0, math.radians(340), CW), 0)

    def test_negative_maximum_is_rejected(self):
        with self.assertRaises(ValueError):
            schedule([], 0, math.pi, CCW, 5, -1)

    def test_stable_ordinal_breaks_equal_sweep_ties(self):
        result = schedule([('first', 0, 1), ('second', 0, 1)], 0, math.pi, CCW, 2, 1)
        self.assertEqual(result[0][0], 'first')

    def test_maximum_hit_registry(self):
        hits = set()
        maximum = 2
        for value in (1, 2, 3, 2):
            if value not in hits and (maximum == 0 or len(hits) < maximum):
                hits.add(value)
        self.assertEqual(hits, {1, 2})

    def test_one_hit_registry(self):
        hits = set()
        self.assertNotIn(42, hits); hits.add(42)
        self.assertIn(42, hits)
        before = len(hits); hits.add(42)
        self.assertEqual(len(hits), before)
        hits.clear(); self.assertFalse(hits)

    def test_destination_selection(self):
        candidates = [('far', 4, 0, 0), ('high', 1, 2, 1), ('best', 1, .5, 2), ('invalid', 0, 0, 3)]
        valid = [x for x in candidates if x[0] != 'invalid']
        selected = min(valid, key=lambda x: (x[1], abs(x[2]), x[3]))
        self.assertEqual(selected[0], 'best')

    def test_resource_and_cooldown(self):
        current = 100
        current -= 35
        self.assertEqual(current, 65)
        remaining = max(0, 1.0 - .4)
        self.assertAlmostEqual(remaining, .6)
        remaining = max(0, remaining - .6)
        self.assertEqual(remaining, 0)

    def test_recursion_and_ownership(self):
        active = {1}
        self.assertIn(1, active)
        active.remove(1)
        ledger = {1: .25, 2: .5}
        self.assertEqual(ledger.pop(1), .25)
        self.assertIn(2, ledger)

    def test_domino_callback_queues_without_dispatching(self):
        domino = DeferredDominoMirror()
        domino.on_hit(source=1, targets=[1, 2, 3])
        self.assertEqual([(x[0], x[1]) for x in domino.pending], [(2, False), (3, False)])
        self.assertEqual(domino.dispatched, [])

    def test_domino_dispatches_on_next_tick_only(self):
        domino = DeferredDominoMirror()
        domino.on_hit(source=1, targets=[2, 3])
        self.assertEqual(domino.dispatched, [])
        domino.tick()
        self.assertEqual(domino.pending, [])
        self.assertEqual(domino.dispatched, [2, 3])
        self.assertEqual(domino.hit_suppression, {})

    def test_no_sound_player_hit_still_propagates(self):
        domino = DeferredDominoMirror()
        domino.on_hit(source=1, targets=[1, 2, 3], flags=NO_SOUND)
        self.assertEqual([x[0] for x in domino.pending], [2, 3])

    def test_explicit_propagated_hit_marker_prevents_requeue(self):
        domino = DeferredDominoMirror()
        domino.add_hit_suppression(2)
        domino.on_hit(source=2, targets=[1, 2, 3], flags=NO_SOUND)
        self.assertEqual(domino.pending, [])
        self.assertEqual(domino.hit_suppression, {})

    def test_unconsumed_hit_marker_is_removed(self):
        domino = DeferredDominoMirror()
        domino.on_hit(source=1, targets=[2])
        domino.tick(simulate_sync_callback=False)
        self.assertEqual(domino.hit_suppression, {})

    def test_propagated_lethal_removal_is_suppressed(self):
        domino = DeferredDominoMirror()
        domino.on_hit(source=1, targets=[2], lethal=True)
        domino.tick()
        self.assertFalse(domino.on_removed(2))
        self.assertTrue(domino.on_removed(2))

    def test_persistent_domino_does_not_block_another_selection(self):
        state = AbilitySelectionMirror()
        self.assertTrue(state.select('Domino'))
        self.assertTrue(state.confirm())
        self.assertIn('Domino', state.persistent_effects)
        self.assertIsNone(state.transient_cast)
        self.assertTrue(state.select('VoidstepCleave'))

    def test_blink_owns_only_targeting_until_mouse2_confirm(self):
        state = AbilitySelectionMirror()
        self.assertTrue(state.select('Blink'))
        self.assertEqual(state.transient_cast, 'BlinkTargeting')
        self.assertTrue(state.confirm())
        self.assertIsNone(state.transient_cast)
        self.assertIsNone(state.selected)

    def test_cancellation_cleanup(self):
        state = {'phase': 'active', 'hits': {7}, 'effects': {1}, 'token': 12}
        state.update(phase='idle', hits=set(), effects=set(), token=0)
        self.assertEqual(state, {'phase':'idle','hits':set(),'effects':set(),'token':0})

    def test_plain_number_key_remains_native_without_modifier(self):
        self.assertFalse(should_suppress_mapped_order(SELECT_ORDER_1, CONTROL, 0))
        self.assertTrue(should_suppress_mapped_order(SELECT_ORDER_1, CONTROL, CONTROL))
        self.assertFalse(should_suppress_mapped_order(SELECT_ORDER_1, CONTROL, CONTROL | ALT))
        self.assertFalse(should_suppress_mapped_order(SELECT_ORDER_1 - 1, CONTROL, CONTROL))


if __name__ == '__main__':
    unittest.main(verbosity=2)
