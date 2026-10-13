import copy
from collections import Counter
import hashlib
import json
import random
import unittest

from ffv_randomizer import bosses


def fixture():
    """All approved identities with transparent synthetic native battle data."""
    rows = []
    for rank, ident in enumerate(bosses.BOSS_IDS, 1):
        stats = {"monster_id": rank, "lv": rank, "hp": rank * 100, "mp": rank * 3,
                 "attack": rank * 2, "attack_count": rank + 1, "magic": rank, "defense": rank // 3,
                 "ability_defense": rank // 4, "agility": rank + 10}
        rows.append({"boss_id": ident, "reference_rank": rank, "name": ident.replace("_", " "),
                     "tier": "simple" if rank in bosses.SIMPLE_RANKS else "complex",
                     "world": 1 if rank <= 18 else 2 if rank <= 28 else 3,
                     "formation_id": 1000 + rank, "formation_alias_ids": [],
                     "monster_ids": [rank], "initial_monster_ids": [rank], "profile_monster_ids": [rank],
                     "stats": [stats], "native_hp_budget": stats["hp"],
                     "effective_hp_budget": stats["hp"], "requirements": []})
    return {"schema_version": 1, "catalog_id": bosses.CATALOG_ID, "bosses": rows}


def identity(catalog):
    return [{"location_id": bosses.PREFIX + row["boss_id"], "reward_kind": "boss",
             "reward_id": row["boss_id"], "quantity": 1} for row in catalog["bosses"]]


def ban(source, destination, modes=None):
    return {"boss_id": source, "destination_id": destination,
            "modes": [bosses.MIXED, bosses.REMIXED] if modes is None else modes,
            "reason": "Synthetic test constraint.", "evidence": "test_bosses.py fixture"}


class BossAssignmentTests(unittest.TestCase):
    def test_complete_roster_and_approved_tiers_are_pinned(self):
        catalog = bosses.validate_catalog(fixture())
        self.assertEqual(len(bosses.BOSS_IDS), 49)
        self.assertEqual(Counter(row["tier"] for row in catalog["bosses"]), {"simple": 27, "complex": 22})
        for change in ("missing", "duplicate_rank", "wrong_identity", "wrong_tier", "schema_bool", "bad_catalog"):
            with self.subTest(change=change):
                broken = copy.deepcopy(catalog)
                if change == "missing": broken["bosses"].pop()
                elif change == "duplicate_rank": broken["bosses"][1]["reference_rank"] = 1
                elif change == "wrong_identity": broken["bosses"][0]["boss_id"] = "shinryu"
                elif change == "wrong_tier": broken["bosses"][0]["tier"] = "complex"
                elif change == "schema_bool": broken["schema_version"] = True
                else: broken["catalog_id"] = "unknown"
                with self.assertRaises(ValueError): bosses.validate_catalog(broken)

    def test_every_seed_assigns_each_boss_and_destination_once_in_both_modes(self):
        catalog = fixture()
        expected_ids = set(bosses.BOSS_IDS)
        for seed in (0, 1, 8, 90, 2**63, 2**64 - 1):
            for mode in (bosses.MIXED, bosses.REMIXED):
                with self.subTest(seed=seed, mode=mode):
                    rows = bosses.generate(seed, mode, catalog)
                    self.assertEqual({row["reward_id"] for row in rows}, expected_ids)
                    self.assertEqual({row["location_id"] for row in rows}, {bosses.PREFIX + i for i in expected_ids})
                    self.assertEqual(rows, sorted(rows, key=lambda row: row["location_id"]))
                    self.assertEqual(rows, bosses.generate(seed, mode, catalog))
                    bosses.validate_placements(json.loads(json.dumps(rows)), mode, catalog)

    def test_mixed_preserves_tiers_and_allows_cross_world(self):
        catalog = fixture()
        index = {row["boss_id"]: row for row in catalog["bosses"]}
        moved_across_worlds = {"simple": False, "complex": False}
        for seed in range(12):
            for row in bosses.generate(seed, bosses.MIXED, catalog):
                destination, source = index[row["location_id"][len(bosses.PREFIX):]], index[row["reward_id"]]
                self.assertEqual(destination["tier"], source["tier"])
                if destination["world"] != source["world"]:
                    moved_across_worlds[destination["tier"]] = True
        self.assertEqual(moved_across_worlds, {"simple": True, "complex": True})

    def test_remixed_uses_one_pool_including_simple_complex_crossings(self):
        catalog = fixture()
        index = {row["boss_id"]: row for row in catalog["bosses"]}
        rows = bosses.generate(8, bosses.REMIXED, catalog)
        self.assertTrue(any(index[row["location_id"][len(bosses.PREFIX):]]["tier"] != index[row["reward_id"]]["tier"]
                            for row in rows))
        self.assertNotEqual(rows, bosses.generate(8, bosses.MIXED, catalog))

    def test_invalid_seed_and_mode_are_rejected(self):
        for seed in (-1, True, "8", 2**64, 1.5, None):
            with self.subTest(seed=seed), self.assertRaises(ValueError): bosses.generate(seed, bosses.MIXED, fixture())
        for mode in (None, "Mixed", "legacy", [], 1):
            with self.subTest(mode=mode), self.assertRaises(ValueError): bosses.generate(8, mode, fixture())

    def test_input_order_global_rng_and_input_mutation_do_not_affect_assignments(self):
        catalog = fixture()
        preserved = copy.deepcopy(catalog)
        baseline = bosses.generate(123, bosses.MIXED, catalog)
        self.assertEqual(catalog, preserved)
        catalog["bosses"].reverse()
        random.seed(173)
        for _ in range(100): random.random()
        self.assertEqual(baseline, bosses.generate(123, bosses.MIXED, catalog))
        self.assertNotEqual(baseline, bosses.generate(124, bosses.MIXED, catalog))

    def test_saved_valid_permutation_is_not_regenerated_or_reordered(self):
        catalog = fixture()
        rows = identity(catalog)
        snapshot = copy.deepcopy(rows)
        self.assertNotEqual(rows, bosses.generate(8, bosses.MIXED, catalog))
        bosses.validate_placements(rows, bosses.MIXED, catalog)
        self.assertEqual(rows, snapshot)

    def test_saved_fields_and_complete_permutation_are_strict(self):
        for change in ("missing", "duplicate_location", "duplicate_source", "unknown_location", "unknown_source",
                       "quantity_bool", "quantity_two", "kind", "extra_field", "non_dict"):
            with self.subTest(change=change):
                catalog = fixture(); rows = identity(catalog)
                if change == "missing": rows.pop()
                elif change == "duplicate_location": rows[1]["location_id"] = rows[0]["location_id"]
                elif change == "duplicate_source": rows[1]["reward_id"] = rows[0]["reward_id"]
                elif change == "unknown_location": rows[0]["location_id"] = bosses.PREFIX + "omega"
                elif change == "unknown_source": rows[0]["reward_id"] = "omega"
                elif change == "quantity_bool": rows[0]["quantity"] = True
                elif change == "quantity_two": rows[0]["quantity"] = 2
                elif change == "kind": rows[0]["reward_kind"] = "job"
                elif change == "extra_field": rows[0]["seed"] = "8"
                else: rows[0] = None
                with self.assertRaises(ValueError): bosses.validate_placements(rows, bosses.MIXED, catalog)

    def test_tier_violation_is_rejected_for_mixed_but_valid_for_remixed(self):
        catalog = fixture(); rows = identity(catalog)
        rows[0]["reward_id"], rows[5]["reward_id"] = rows[5]["reward_id"], rows[0]["reward_id"]
        with self.assertRaises(ValueError): bosses.validate_placements(rows, bosses.MIXED, catalog)
        bosses.validate_placements(rows, bosses.REMIXED, catalog)

    def test_matching_obeys_evidenced_restrictions_without_dropping_bosses(self):
        catalog = fixture()
        # Reserve the first four destinations to a rotation; each still must be
        # assigned, and their reserved sources must be moved from other matches.
        reserved = bosses.BOSS_IDS[:4]
        allowed = dict(zip(reserved, reserved[1:] + reserved[:1]))
        catalog["incompatible_placements"] = [ban(source, destination)
            for destination in reserved for source in bosses.BOSS_IDS if source != allowed[destination]]
        for mode in (bosses.MIXED, bosses.REMIXED):
            for seed in range(8):
                with self.subTest(mode=mode, seed=seed):
                    result = {row["location_id"][len(bosses.PREFIX):]: row["reward_id"]
                              for row in bosses.generate(seed, mode, catalog)}
                    self.assertEqual({ident: result[ident] for ident in reserved}, allowed)
                    self.assertEqual(set(result.values()), set(bosses.BOSS_IDS))

    def test_unsatisfiable_graph_fails_instead_of_omitting_or_fixing_a_boss(self):
        catalog = fixture()
        catalog["incompatible_placements"] = [ban(source, "wing_raptor") for source in bosses.BOSS_IDS]
        for mode in (bosses.MIXED, bosses.REMIXED):
            with self.subTest(mode=mode), self.assertRaisesRegex(ValueError, "full forty-nine"):
                bosses.generate(8, mode, catalog)

    def test_restrictions_require_evidence_and_apply_only_to_named_modes(self):
        catalog = fixture()
        catalog["incompatible_placements"] = [ban("wing_raptor", "wing_raptor", [bosses.MIXED])]
        rows = identity(catalog)
        with self.assertRaises(ValueError): bosses.validate_placements(rows, bosses.MIXED, catalog)
        bosses.validate_placements(rows, bosses.REMIXED, catalog)
        for field, value in (("evidence", ""), ("modes", []), ("boss_id", "omega"), ("modes", [bosses.MIXED] * 2)):
            broken = copy.deepcopy(catalog); broken["incompatible_placements"][0][field] = value
            with self.subTest(field=field), self.assertRaises(ValueError): bosses.validate_catalog(broken)

    def test_native_formation_and_companion_binding_validation(self):
        for change in ("formation_shared", "alias_shared", "alias_duplicate", "monster_missing", "stats_duplicate",
                       "hp_zero", "budget_bool", "requirements_duplicate"):
            with self.subTest(change=change):
                catalog = fixture(); first, second = catalog["bosses"][:2]
                if change == "formation_shared": second["formation_id"] = first["formation_id"]
                elif change == "alias_shared": second["formation_alias_ids"] = [first["formation_id"]]
                elif change == "alias_duplicate": second["formation_alias_ids"] = [5000, 5000]
                elif change == "monster_missing": first["monster_ids"].append(501)
                elif change == "stats_duplicate": first["stats"].append(copy.deepcopy(first["stats"][0]))
                elif change == "hp_zero": first["stats"][0]["hp"] = 0
                elif change == "budget_bool": first["effective_hp_budget"] = True
                else: first["requirements"] = ["timer", "timer"]
                with self.assertRaises(ValueError): bosses.validate_catalog(catalog)


class BossScalingTests(unittest.TestCase):
    def test_integer_rounding_is_half_up_with_bounds_and_int64_safe_intermediate(self):
        self.assertEqual(bosses.scale_integer(1, 1, 2), 1)
        self.assertEqual(bosses.scale_integer(1, 1, 3), 0)
        self.assertEqual(bosses.scale_integer(1, 1, 3, minimum=1), 1)
        self.assertEqual(bosses.scale_integer(100, 10, 1, maximum=255), 255)
        self.assertEqual(bosses.scale_integer(2**31 - 1, 2**31 - 1, 1), 2**31 - 1)
        self.assertEqual(bosses.scale_integer(1000, 2, 3), 667)
        for args in ((True, 1, 1), (1, 1, 0), (-1, 1, 1), (1, -1, 1), (2**31, 1, 1)):
            with self.subTest(args=args), self.assertRaises(ValueError): bosses.scale_integer(*args)

    def test_thresholds_track_scaled_hp_and_remain_below_full_health(self):
        self.assertEqual(bosses.scale_hp_threshold(10000, 11500, 115), 100)
        self.assertEqual(bosses.scale_hp_threshold(11500, 11500, 115), 115)
        self.assertEqual(bosses.scale_hp_threshold(9, 10, 1), 0)
        self.assertEqual(bosses.scale_hp_threshold(1, 10000, 100), 1)
        self.assertEqual(bosses.scale_hp_threshold(2, 10000, 100), 2)
        self.assertEqual(bosses.scale_hp_threshold(2, 100, 1), 2)
        for args in ((11, 10, 1), (1, 0, 1), (1, 1, 0), (True, 10, 10)):
            with self.subTest(args=args), self.assertRaises(ValueError): bosses.scale_hp_threshold(*args)

    def test_absolute_damage_uses_level_profile_with_native_cap_and_zero_preserved(self):
        catalog = fixture(); source, destination = catalog["bosses"][:2]
        source["stats"][0]["lv"] = 20
        destination["stats"][0]["lv"] = 3
        self.assertEqual(bosses.scale_fixed_damage("karlabos", "wing_raptor", 1000, catalog), 150)
        self.assertEqual(bosses.scale_fixed_damage("karlabos", "wing_raptor", 10, catalog), 2)
        self.assertEqual(bosses.scale_fixed_damage("karlabos", "wing_raptor", 1, catalog), 1)
        self.assertEqual(bosses.scale_fixed_damage("karlabos", "wing_raptor", 0, catalog), 0)
        self.assertEqual(bosses.scale_fixed_damage("wing_raptor", "karlabos", 9999, catalog), 9999)
        for damage in (-1, True, 2**31):
            with self.subTest(damage=damage), self.assertRaises(ValueError):
                bosses.scale_fixed_damage("karlabos", "wing_raptor", damage, catalog)
        plan = bosses.scaling_plan("karlabos", "wing_raptor", catalog)
        self.assertEqual(plan["fixed_damage_scale"], {"numerator": 3, "denominator": 20})

    def test_self_assignment_keeps_native_stats_identical(self):
        catalog = fixture()
        for boss in catalog["bosses"]:
            result = bosses.scaling_plan(boss["boss_id"], boss["boss_id"], catalog)
            self.assertEqual(result["stats"], boss["stats"])
            self.assertEqual(result["scaling_version"], bosses.SCALING_VERSION)

    def test_six_members_share_one_destination_hp_budget(self):
        catalog = fixture(); source = catalog["bosses"][16]
        original = source["stats"][0]
        source["stats"] = [{**original, "monster_id": 200 + member, "hp": 1000} for member in range(6)]
        source["monster_ids"] = [row["monster_id"] for row in source["stats"]]
        source["initial_monster_ids"] = source["monster_ids"][:]
        source["profile_monster_ids"] = source["monster_ids"][:]
        source["native_hp_budget"] = source["effective_hp_budget"] = 6000
        destination = catalog["bosses"][0]
        destination["effective_hp_budget"] = destination["native_hp_budget"] = 600
        plan = bosses.scaling_plan("wing_raptor", "purobolos", catalog)
        self.assertEqual([row["hp"] for row in plan["stats"]], [100] * 6)
        self.assertEqual(sum(row["hp"] for row in plan["stats"]), 600)

    def test_relative_forms_zeroes_immunity_and_mp_are_preserved(self):
        catalog = fixture(); source = catalog["bosses"][5]; destination = catalog["bosses"][4]
        source["stats"][0].update(attack=80, magic=0, defense=255, ability_defense=20, agility=40)
        companion = {**source["stats"][0], "monster_id": 600, "hp": 300, "attack": 40,
                     "defense": 10, "ability_defense": 0, "agility": 20}
        source["stats"].append(companion); source["monster_ids"].append(600)
        source["profile_monster_ids"].append(600)
        source["native_hp_budget"] = source["effective_hp_budget"] = 900
        destination["stats"][0].update(attack=20, magic=30, defense=5, ability_defense=5, agility=10)
        plan = bosses.scaling_plan("garula", "liquid_flame", catalog)
        a, b = plan["stats"]
        self.assertEqual((a["attack"], b["attack"]), (20, 10))
        self.assertEqual((a["magic"], b["magic"]), (0, 0))
        self.assertEqual((a["defense"], b["defense"]), (255, 7))
        self.assertEqual((a["ability_defense"], b["ability_defense"]), (15, 0))
        self.assertEqual((a["agility"], b["agility"]), (10, 5))
        self.assertEqual((a["mp"], b["mp"]), (18, 18))

    def test_native_hidden_hp_floor_uses_same_budget_scale(self):
        catalog = fixture(); source = catalog["bosses"][18]
        source["stats"][0]["hp"] = source["native_hp_budget"] = 11500
        source["effective_hp_budget"] = 1500
        destination = catalog["bosses"][0]
        destination["stats"][0]["hp"] = destination["effective_hp_budget"] = destination["native_hp_budget"] = 300
        hp = bosses.scaling_plan("wing_raptor", "gilgamesh_1", catalog)["stats"][0]["hp"]
        self.assertEqual(hp, 2300)
        self.assertEqual(hp - bosses.scale_hp_threshold(10000, 11500, hp), 300)


class NativeBossCatalogTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.catalog = bosses.read_catalog()
        cls.index = {row["boss_id"]: row for row in cls.catalog["bosses"]}

    def test_seed_two_wind_replacement_does_not_inherit_closed_wing_armor(self):
        row, = bosses.scaling_plan("wing_raptor", "halicarnassus", self.catalog)["stats"]
        self.assertEqual((row["hp"], row["lv"], row["defense"], row["ability_defense"]), (250, 2, 1, 2))

    def test_destination_armor_does_not_become_the_donors_permanent_armor(self):
        changed = copy.deepcopy(self.catalog)
        wind = next(b for b in changed["bosses"] if b["boss_id"] == "wing_raptor")
        # Alter only the location's closed-wing armor. A donor with no such
        # phase must have exactly the same combat plan before and after.
        wind["stats"][1].update(defense=200, ability_defense=200)
        for source in ("halicarnassus", "carbuncle", "siren"):
            self.assertEqual(bosses.scaling_plan("wing_raptor", source, changed),
                             bosses.scaling_plan("wing_raptor", source, self.catalog))

    def test_donor_defensive_phases_survive_zero_armor_destinations(self):
        wind = bosses.scaling_plan("karlabos", "wing_raptor", self.catalog)["stats"]
        self.assertEqual([(r["defense"], r["ability_defense"]) for r in wind], [(0, 16), (32, 16)])
        carbuncle = bosses.scaling_plan("wing_raptor", "carbuncle", self.catalog)["stats"]
        self.assertEqual([(r["defense"], r["ability_defense"]) for r in carbuncle], [(6, 6), (1, 0)])

    def test_ordinary_armor_cannot_become_an_immunity_sentinel(self):
        wing = bosses.scaling_plan("necrophobe", "wing_raptor", self.catalog)["stats"]
        self.assertEqual(wing[1]["defense"], 254)

    def test_low_native_level_is_not_a_defense_difficulty_budget(self):
        for source, expected in (("titan", (3, 0)), ("odin", (2, 2))):
            row, = bosses.scaling_plan("wing_raptor", source, self.catalog)["stats"]
            self.assertEqual((row["defense"], row["ability_defense"]), expected)

    def test_defense_rounding_caps_and_immunity_are_exact(self):
        for value, numerator, denominator, expected in (
                (1, 1, 4, 1), (1, 1, 5, 0), (1, 9, 4, 2), (1, 8, 4, 1),
                (10, 4, 1, 20), (10, 1, 4, 5), (254, 1, 1, 254),
                (254, bosses.INT32_MAX, 1, 254), (254, 1, bosses.INT32_MAX, 0),
                (255, 1, bosses.INT32_MAX, 255), (0, bosses.INT32_MAX, 1, 0)):
            self.assertEqual(bosses.scale_defense(value, numerator, denominator), expected)
        for arguments in ((-1, 1, 1), (256, 1, 1), (True, 1, 1), (1, 0, 1), (1, 1, 0)):
            with self.assertRaises(ValueError):
                bosses.scale_defense(*arguments)

    def test_actual_native_catalog_covers_approved_roster_and_native_worlds(self):
        self.assertEqual(tuple(row["boss_id"] for row in sorted(self.catalog["bosses"],
                         key=lambda row: row["reference_rank"])), bosses.BOSS_IDS)
        self.assertEqual(Counter(row["tier"] for row in self.catalog["bosses"]), {"simple": 27, "complex": 22})
        self.assertEqual(self.index["wing_raptor"]["formation_id"], 440)
        self.assertEqual(self.index["garula"]["formation_id"], 444)
        self.assertEqual(self.index["liquid_flame"]["formation_id"], 445)
        self.assertEqual([self.index[ident]["world"] for ident in ("ramuh", "catoblepas", "golem")], [1, 2, 2])

    def test_actual_native_seed_eight_assignment_vectors(self):
        expected = {bosses.MIXED: "d7d8a096d51a47988b5baacc717f64efcc385f4e1236a52d27d4fee4bcbff196",
                    bosses.REMIXED: "0ac73167ed6c0859c2bd28cc6eab96d12040ecc8620951af6cc65ee51974bbb6"}
        for mode, digest in expected.items():
            rows = bosses.generate(8, mode, self.catalog)
            self.assertEqual(hashlib.sha256(json.dumps(rows, sort_keys=True, separators=(",", ":")).encode()).hexdigest(), digest)

    def test_all_native_pairings_scale_every_form_with_bounded_integer_stats(self):
        for destination in bosses.BOSS_IDS:
            for source in bosses.BOSS_IDS:
                plan = bosses.scaling_plan(destination, source, self.catalog)
                expected = {row["monster_id"]: row for row in self.index[source]["stats"]}
                self.assertEqual({row["monster_id"] for row in plan["stats"]}, set(expected))
                for row in plan["stats"]:
                    native = expected[row["monster_id"]]
                    self.assertGreaterEqual(row["hp"], 1)
                    self.assertLessEqual(row["hp"], bosses.INT32_MAX)
                    self.assertEqual(row["mp"], native["mp"])
                    for field in bosses.SCALED_FIELDS:
                        self.assertIs(type(row[field]), int)
                        self.assertTrue(0 <= row[field] <= 255)
                        if native[field] == 0 or field in ("defense", "ability_defense") and native[field] == 255:
                            self.assertEqual(row[field], native[field])
                        if field in ("defense", "ability_defense") and native[field] != 255:
                            self.assertLess(row[field], 255)
                    if source == destination:
                        self.assertEqual(row, {"monster_id": native["monster_id"],
                                               **{field: native[field] for field in bosses.STAT_FIELDS}})

    def test_actual_purobolos_six_enemy_health_does_not_multiply_the_destination_budget(self):
        source = self.index["purobolos"]
        self.assertEqual(len(source["initial_monster_ids"]), 6)
        plan = bosses.scaling_plan("wing_raptor", "purobolos", self.catalog)
        hp = {row["monster_id"]: row["hp"] for row in plan["stats"]}
        total = sum(hp[ident] for ident in source["initial_monster_ids"])
        self.assertLessEqual(abs(total - self.index["wing_raptor"]["effective_hp_budget"]), 3)

    def test_helpers_do_not_inflate_actual_enemy_profile(self):
        self.assertNotIn(391, self.index["necrophobe"]["profile_monster_ids"])
        self.assertEqual(set(self.index["golem"]["profile_monster_ids"]), {162, 167})
        plan = bosses.scaling_plan("golem", "wing_raptor", self.catalog)
        self.assertEqual(plan["stat_scales"]["agility"]["numerator"], 35)
        self.assertEqual(plan["stat_scales"]["lv"]["numerator"], 37)

    def test_actual_azulmagia_needles_are_scaled_for_wing_raptor_position(self):
        # The native STATIC program for 1000 Needles bypasses ordinary stats;
        # Azulmagia is a Simple boss eligible for this early Mixed location.
        self.assertEqual(self.index["azulmagia"]["tier"], self.index["wing_raptor"]["tier"])
        plan = bosses.scaling_plan("wing_raptor", "azulmagia", self.catalog)
        ratio = plan["fixed_damage_scale"]
        self.assertEqual(ratio, {"numerator": 2, "denominator": 57})
        self.assertEqual(bosses.scale_fixed_damage("wing_raptor", "azulmagia", 1000, self.catalog), 35)
        self.assertEqual(bosses.scale_fixed_damage("azulmagia", "azulmagia", 1000, self.catalog), 1000)


if __name__ == "__main__":
    unittest.main()
