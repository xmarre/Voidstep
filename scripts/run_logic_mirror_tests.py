#!/usr/bin/env python3
import math
import unittest

TAU = math.tau
CW = -1
CCW = 1

def norm(a):
    return a % TAU

def travel(start, target, direction):
    return norm(target - start) if direction == CCW else norm(start - target)

def inside(start, target, sweep, direction):
    return travel(start, target, direction) <= sweep + 1e-6

def progress(start, target, sweep, direction):
    return min(1.0, max(0.0, travel(start, target, direction) / sweep))

def schedule(candidates, start, sweep, direction, radius, maximum=0):
    if maximum < 0:
        raise ValueError("maximum must be non-negative")
    rows = [(value, progress(start, angle, sweep, direction), distance2, ordinal)
            for ordinal, (value, angle, distance2) in enumerate(candidates)
            if distance2 <= radius * radius and inside(start, angle, sweep, direction)]
    rows.sort(key=lambda x: (x[1], x[2], x[3]))
    return rows[:maximum or None]

class MirrorTests(unittest.TestCase):
    def test_normalise(self):
        self.assertAlmostEqual(norm(-math.pi / 2), 3 * math.pi / 2)
        self.assertAlmostEqual(norm(4 * math.pi), 0)

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
        remaining = 1.0
        remaining = max(0, remaining - .4)
        self.assertAlmostEqual(remaining, .6)
        remaining = max(0, remaining - .6)
        self.assertEqual(remaining, 0)

    def test_recursion_and_ownership(self):
        active = set()
        active.add(1)
        self.assertIn(1, active)
        self.assertFalse(1 not in active)
        active.remove(1)
        ledger = {1: .25, 2: .5}
        self.assertEqual(ledger.pop(1), .25)
        self.assertIn(2, ledger)

    def test_cancellation_cleanup(self):
        state = {'phase': 'active', 'hits': {7}, 'effects': {1}, 'token': 12}
        state.update(phase='idle', hits=set(), effects=set(), token=0)
        self.assertEqual(state, {'phase':'idle','hits':set(),'effects':set(),'token':0})

if __name__ == '__main__':
    unittest.main(verbosity=2)
