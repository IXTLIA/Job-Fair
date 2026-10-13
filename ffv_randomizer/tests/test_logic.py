"""Tests for the synthetic progression kernel; no real FFV bindings are used."""

import copy
import itertools
import random
import unittest

from ffv_randomizer.logic import audit_seed, generate_seed, validate_model


def model(checks, keys, jobs=(), starting=(), goal=None):
    return {
        "format_version": 1,
        "starting_inventory": list(starting),
        "goal_requires": list(keys if goal is None else goal),
        "checks": [{"id": check_id, "requires": list(requires)} for check_id, requires in checks],
        "progression_items": list(keys),
        "jobs": list(jobs),
    }


class PlacementTests(unittest.TestCase):
    def setUp(self):
        self.branching = model(
            [
                ("start_1", []), ("start_2", []),
                ("branch_a", ["a"]), ("branch_b", ["b"]),
                ("join", ["a", "b"]), ("last", ["c"]),
                ("bonus", ["a", "b", "c"]),
            ],
            ["a", "b", "c"], ["job_a", "job_b"],
        )

    def test_many_seeds_reach_every_key_job_and_check(self):
        signatures = set()
        for seed in range(100):
            with self.subTest(seed=seed):
                result = generate_seed(self.branching, seed)
                proof = audit_seed(self.branching, result)
                self.assertTrue(proof["goal_reachable"])
                self.assertEqual(proof["unreachable_checks"], [])
                self.assertEqual(proof["completion_inventory"], ["a", "b", "c"])
                self.assertEqual(sum(map(len, proof["spheres"])), 7)
                rewards = [placement["reward"] for placement in result["placements"]]
                self.assertEqual(sorted(r["id"] for r in rewards if r["kind"] == "key_item"), ["a", "b", "c"])
                self.assertEqual(sorted(r["id"] for r in rewards if r["kind"] == "job"), ["job_a", "job_b"])
                self.assertEqual(rewards.count({"kind": "item", "id": "empty"}), 2)
                signatures.add(tuple((p["check_id"], p["reward"]["id"]) for p in result["placements"]))
        self.assertGreater(len(signatures), 10)

    def test_same_seed_and_reordered_inputs_are_identical(self):
        reordered = copy.deepcopy(self.branching)
        for field in ("checks", "progression_items", "jobs", "goal_requires"):
            reordered[field].reverse()
        for check in reordered["checks"]:
            check["requires"].reverse()
        result = generate_seed(self.branching, 918273)
        self.assertEqual(result, generate_seed(self.branching, 918273))
        self.assertEqual(result, generate_seed(reordered, 918273))

    def test_private_rng_and_no_input_mutation(self):
        original = copy.deepcopy(self.branching)
        random.seed(123)
        state = random.getstate()
        result = generate_seed(self.branching, -42)
        audit_seed(self.branching, result)
        self.assertEqual(random.getstate(), state)
        self.assertEqual(self.branching, original)

    def test_backtracking_handles_a_capacity_trap(self):
        # Spending both initial slots on a+b gives just one new slot, leaving
        # c+d locked.  Completing c+d first gives enough slots for a+b.
        trap = model(
            [("s1", []), ("s2", []), ("ab", ["a", "b"])]
            + [(f"cd{i}", ["c", "d"]) for i in range(3)],
            ["a", "b", "c", "d"],
        )
        for seed in range(40):
            with self.subTest(seed=seed):
                result = generate_seed(trap, seed)
                audit_seed(trap, result)
                initial = {p["reward"]["id"] for p in result["placements"] if p["check_id"] in {"s1", "s2"}}
                self.assertEqual(initial, {"c", "d"})

    def test_solver_agrees_with_exhaustive_small_model_feasibility(self):
        # Exhaustive permutation checking uses a slot-count simulation separate
        # from the production search and includes unsatisfiable AND gates.
        keys = ["a", "b", "c"]
        requirement_options = [[], ["a"], ["b"], ["c"], ["a", "b"], keys]
        for index, requirements in enumerate(itertools.product(requirement_options, repeat=3)):
            candidate = model([(str(i), req) for i, req in enumerate(requirements)], keys)
            feasible = False
            for permutation in itertools.permutations(keys):
                inventory = set()
                for placed, key in enumerate(permutation):
                    available = sum(set(req).issubset(inventory) for req in requirements) - placed
                    if available <= 0:
                        break
                    inventory.add(key)
                else:
                    feasible = True
                    break
            with self.subTest(requirements=requirements):
                if feasible:
                    audit_seed(candidate, generate_seed(candidate, index))
                else:
                    with self.assertRaisesRegex(ValueError, "Impossible progression"):
                        generate_seed(candidate, index)

    def test_starting_inventory_and_simultaneous_spheres(self):
        chain = model(
            [("initial", ["start"]), ("second", ["a"]), ("third", ["a", "b"])],
            ["a", "b"], starting=["start"], goal=["b", "start"],
        )
        result = generate_seed(chain, 1)
        self.assertEqual(result["spheres"], [["initial"], ["second"], ["third"]])
        self.assertEqual(result["completion_inventory"], ["a", "b", "start"])

    def test_empty_model_and_job_only_model(self):
        empty = model([], [])
        result = generate_seed(empty, 0)
        self.assertEqual(result["placements"], [])
        self.assertEqual(result["spheres"], [])
        self.assertTrue(result["goal_reachable"])
        audit_seed(empty, result)
        jobs_only = model([("one", []), ("two", [])], [], ["job"])
        result = generate_seed(jobs_only, 0)
        self.assertEqual(result["spheres"], [["one", "two"]])
        audit_seed(jobs_only, result)

    def test_impossible_progression_is_rejected(self):
        cases = [
            model([("locked", ["a"])], ["a"]),
            model([("start", []), ("locked_1", ["a", "b"]), ("locked_2", ["a", "b"])], ["a", "b"]),
        ]
        for candidate in cases:
            with self.subTest(candidate=candidate):
                with self.assertRaisesRegex(ValueError, "Impossible progression"):
                    generate_seed(candidate, 17)

    def test_insufficient_checks_include_jobs(self):
        candidate = model([("one", [])], ["a"], ["job"])
        with self.assertRaisesRegex(ValueError, "Insufficient checks"):
            validate_model(candidate)


class ModelValidationTests(unittest.TestCase):
    def setUp(self):
        self.valid = model([("one", []), ("two", ["a"])], ["a"], ["job"])

    def test_unknown_required_keys_and_job_requirements(self):
        for field, key in [("goal_requires", "missing"), ("goal_requires", "job"), ("check", "missing")]:
            candidate = copy.deepcopy(self.valid)
            if field == "check":
                candidate["checks"][0]["requires"] = [key]
            else:
                candidate[field] = [key]
            with self.subTest(field=field, key=key), self.assertRaisesRegex(ValueError, "Unknown required keys"):
                validate_model(candidate)

    def test_duplicate_identities_are_rejected(self):
        cases = []
        for field in ("progression_items", "jobs", "goal_requires"):
            candidate = copy.deepcopy(self.valid)
            candidate[field] *= 2
            cases.append(candidate)
        candidate = copy.deepcopy(self.valid)
        candidate["starting_inventory"] = ["start", "start"]
        cases.append(candidate)
        candidate = copy.deepcopy(self.valid)
        candidate["starting_inventory"] = ["a"]
        cases.append(candidate)
        candidate = copy.deepcopy(self.valid)
        candidate["jobs"] = ["a"]
        cases.append(candidate)
        candidate = copy.deepcopy(self.valid)
        candidate["checks"][1]["id"] = "one"
        cases.append(candidate)
        candidate = copy.deepcopy(self.valid)
        candidate["checks"][1]["requires"] = ["a", "a"]
        cases.append(candidate)
        for candidate in cases:
            with self.subTest(candidate=candidate), self.assertRaisesRegex(ValueError, "Duplicate"):
                validate_model(candidate)

    def test_invalid_schema_types_are_value_errors(self):
        cases = [None, [], {}, {**self.valid, "format_version": True}]
        for field in ("checks", "progression_items", "starting_inventory", "goal_requires", "jobs"):
            cases.append({**self.valid, field: "invalid"})
        for bad_check in (None, {"id": " one", "requires": []}, {"id": "x", "requires": None}, {"id": "x", "requires": [], "region": ""}):
            cases.append({**self.valid, "checks": [bad_check]})
        for candidate in cases:
            with self.subTest(candidate=candidate), self.assertRaises(ValueError):
                validate_model(candidate)
        for seed in (None, True, "42", 1.5):
            with self.subTest(seed=seed), self.assertRaisesRegex(ValueError, "Seed must be an integer"):
                generate_seed(self.valid, seed)

    def test_canonical_copy_retains_optional_region(self):
        self.valid["checks"][0]["region"] = "region"
        self.valid.pop("jobs")
        canonical = validate_model(self.valid)
        self.assertEqual(canonical["jobs"], [])
        self.assertEqual(canonical["checks"][0]["region"], "region")
        canonical["checks"][0]["requires"].append("a")
        self.assertEqual(self.valid["checks"][0]["requires"], [])


class AuditTests(unittest.TestCase):
    def setUp(self):
        self.model = model([("start", []), ("gate", ["a"]), ("extra", ["b"])], ["a", "b"])
        self.seed = generate_seed(self.model, 10)

    def test_self_lock_is_detected_independently_of_stored_metadata(self):
        broken = copy.deepcopy(self.seed)
        start = next(p for p in broken["placements"] if p["check_id"] == "start")
        gate = next(p for p in broken["placements"] if p["check_id"] == "gate")
        start["reward"], gate["reward"] = gate["reward"], start["reward"]
        with self.assertRaisesRegex(ValueError, "Unreachable progression items.*Goal is unreachable"):
            audit_seed(self.model, broken)

    def test_reachable_goal_does_not_excuse_unreached_progression(self):
        candidate = copy.deepcopy(self.model)
        candidate["starting_inventory"] = ["already"]
        candidate["goal_requires"] = ["already"]
        broken = generate_seed(candidate, 10)
        start = next(p for p in broken["placements"] if p["check_id"] == "start")
        gate = next(p for p in broken["placements"] if p["check_id"] == "gate")
        start["reward"], gate["reward"] = gate["reward"], start["reward"]
        with self.assertRaisesRegex(ValueError, "Unreachable progression items"):
            audit_seed(candidate, broken)

    def test_missing_duplicate_and_unknown_checks_are_rejected(self):
        variants = []
        missing = copy.deepcopy(self.seed)
        missing["placements"].pop()
        variants.append((missing, "Missing placements"))
        duplicate = copy.deepcopy(self.seed)
        duplicate["placements"].append(copy.deepcopy(duplicate["placements"][0]))
        variants.append((duplicate, "Duplicate placement"))
        unknown = copy.deepcopy(self.seed)
        unknown["placements"][0]["check_id"] = "unknown"
        variants.append((unknown, "Unknown placement check"))
        for broken, message in variants:
            with self.subTest(message=message), self.assertRaisesRegex(ValueError, message):
                audit_seed(self.model, broken)

    def test_invalid_reward_pools_and_reward_types_are_rejected(self):
        extra_index = next(i for i, p in enumerate(self.seed["placements"]) if p["reward"]["kind"] == "item")
        key_index = next(i for i, p in enumerate(self.seed["placements"]) if p["reward"]["kind"] == "key_item")
        variants = [
            (extra_index, {"kind": "key_item", "id": "a"}, "Invalid progression reward pool"),
            (key_index, {"kind": "item", "id": "empty"}, "Invalid progression reward pool"),
            (extra_index, {"kind": "key_item", "id": "unknown"}, "Unknown progression reward"),
            (extra_index, {"kind": "job", "id": "unknown"}, "Unknown job reward"),
            (extra_index, {"kind": "item", "id": "potion"}, "Unsupported item reward"),
            (extra_index, {"kind": "unknown", "id": "empty"}, "Unknown reward kind"),
            (extra_index, None, "must be a dictionary"),
        ]
        for index, reward, message in variants:
            broken = copy.deepcopy(self.seed)
            broken["placements"][index]["reward"] = reward
            with self.subTest(reward=reward), self.assertRaisesRegex(ValueError, message):
                audit_seed(self.model, broken)

    def test_missing_and_duplicate_jobs_are_rejected(self):
        candidate = model([("one", []), ("two", []), ("three", [])], [], ["j1", "j2"])
        valid = generate_seed(candidate, 0)
        for replacement in ({"kind": "item", "id": "empty"}, {"kind": "job", "id": "j1"}):
            broken = copy.deepcopy(valid)
            index = next(i for i, p in enumerate(broken["placements"]) if p["reward"]["id"] == "j2")
            broken["placements"][index]["reward"] = replacement
            with self.subTest(replacement=replacement), self.assertRaisesRegex(ValueError, "Invalid job reward pool"):
                audit_seed(candidate, broken)

    def test_corrupt_and_missing_proof_metadata_are_rejected(self):
        for field, invalid in (("spheres", []), ("completion_inventory", []), ("goal_reachable", False)):
            for remove in (False, True):
                broken = copy.deepcopy(self.seed)
                if remove:
                    broken.pop(field)
                else:
                    broken[field] = invalid
                with self.subTest(field=field, remove=remove), self.assertRaisesRegex(ValueError, field):
                    audit_seed(self.model, broken)
        broken = copy.deepcopy(self.seed)
        broken["goal_reachable"] = 1
        with self.assertRaisesRegex(ValueError, "boolean"):
            audit_seed(self.model, broken)

    def test_invalid_record_types_raise_value_errors(self):
        variants = [None, [], {}, {**self.seed, "seed": True}, {**self.seed, "format_version": True}, {**self.seed, "placements": None}, {**self.seed, "placements": [None]}]
        for broken in variants:
            with self.subTest(broken=broken), self.assertRaises(ValueError):
                audit_seed(self.model, broken)

    def test_valid_record_is_not_mutated(self):
        original = copy.deepcopy(self.seed)
        proof = audit_seed(self.model, self.seed)
        self.assertEqual(self.seed, original)
        self.assertEqual(proof["spheres"], original["spheres"])


if __name__ == "__main__":
    unittest.main()
