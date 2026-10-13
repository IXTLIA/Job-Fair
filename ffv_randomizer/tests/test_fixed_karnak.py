from collections import Counter
from copy import deepcopy
import unittest
from unittest.mock import patch

from ffv_randomizer import campaign, fixed_karnak, world_treasure, remixed_jobs, world_travel


class FixedKarnakTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.catalog = campaign.read_world_catalog()
        cls.locations = {row["id"]: row for row in cls.catalog["locations"]}

    def test_exact_native_castle_set_and_no_progression_candidates(self):
        fixed_karnak.validate_locations(self.catalog)
        self.assertEqual(len(fixed_karnak.LOCATION_IDS), 15)
        self.assertEqual(Counter(row["world"] for row in self.locations.values()
                                 if row["id"] not in fixed_karnak.LOCATION_IDS), {1: 91, 2: 54, 3: 100})
        for source, name in ((remixed_jobs.read_catalog(), "bonus_candidates"),
                             (world_travel.read_catalog(), "key_candidates")):
            self.assertTrue(fixed_karnak.LOCATION_IDS.isdisjoint(row["ordinary_id"] for row in source[name]))

    def test_fixed_chests_and_remaining_world_pools_for_both_modes(self):
        for seed in ("0", "1", "2", "8", "90", "18446744073709551615"):
            for mode in (campaign.MIXED, campaign.REMIXED):
                with self.subTest(seed=seed, mode=mode):
                    old = (campaign.build_request(seed, catalog_id=campaign.SHOP_CATALOG_ID)
                           if mode == campaign.MIXED else campaign.build_remixed_playtest(seed))
                    new = campaign.build_fixed_karnak(seed, mode)
                    self.assertEqual(new, campaign.build_fixed_karnak(seed, mode))
                    self.assertEqual(new["starting_job_ids"], old["starting_job_ids"])
                    self.assertEqual(len(new["placements"]), len(old["placements"]))
                    self.assertNotEqual(new["catalog_id"], old["catalog_id"])
                    self.assertNotEqual(new["placement_hash"], old["placement_hash"])
                    before = {row["location_id"]: row for row in old["placements"]}
                    actual = {1: Counter(), 2: Counter(), 3: Counter()}
                    expected = {1: Counter(), 2: Counter(), 3: Counter()}
                    for row in new["placements"]:
                        location = self.locations.get(row["location_id"])
                        if location is None:
                            self.assertEqual(row, before[row["location_id"]])
                            continue
                        reward = row["reward_kind"], row["reward_id"], row["quantity"]
                        native = world_treasure.reward_tuple(location["native_reward"])
                        if row["location_id"] in fixed_karnak.LOCATION_IDS:
                            self.assertEqual(reward, native)
                        else:
                            actual[location["world"]][reward] += 1
                            expected[location["world"]][native] += 1
                        if location["world"] != 1:
                            self.assertEqual(row, before[row["location_id"]])
                    self.assertEqual(actual, expected)

    def test_rehashed_pool_preserving_fixed_chest_swap_is_rejected(self):
        for mode in (campaign.MIXED, campaign.REMIXED):
            value = campaign.build_fixed_karnak("2", mode)
            fixed = next(row for row in value["placements"] if row["location_id"] == "native_treasure_086")
            other = next(row for row in value["placements"] if row["location_id"] in self.locations
                         and self.locations[row["location_id"]]["world"] == 1
                         and row["location_id"] not in fixed_karnak.LOCATION_IDS and row["reward_kind"] == "item")
            for key in ("reward_kind", "reward_id", "quantity"):
                fixed[key], other[key] = other[key], fixed[key]
            value["placement_hash"] = campaign.placement_hash(value)
            with self.assertRaisesRegex(ValueError, "retain native rewards"):
                campaign.validate_request(value)

    def test_validation_never_regenerates_new_or_old_saved_rows(self):
        requests = [campaign.build_fixed_karnak("2", mode) for mode in (campaign.MIXED, campaign.REMIXED)]
        requests += [campaign.build_request("2", catalog_id=campaign.SHOP_CATALOG_ID),
                     campaign.build_remixed_playtest("2")]
        originals = deepcopy(requests)
        with patch.object(world_treasure, "generate", side_effect=AssertionError("reroll")), \
             patch.object(fixed_karnak, "generate", side_effect=AssertionError("reroll")), \
             patch.object(remixed_jobs, "generate", side_effect=AssertionError("reroll")), \
             patch.object(world_travel, "generate_keys", side_effect=AssertionError("reroll")):
            for request in requests:
                self.assertIs(campaign.validate_request(request), request)
        self.assertEqual(requests, originals)

    def test_catalog_and_settings_cannot_silently_change_saved_policy(self):
        for mode in (campaign.MIXED, campaign.REMIXED):
            value = campaign.build_fixed_karnak("2", mode)
            value["settings"]["treasure_scope"] = "world-pools-v2"
            value["placement_hash"] = campaign.placement_hash(value)
            with self.assertRaises(ValueError):
                campaign.validate_request(value)


if __name__ == "__main__":
    unittest.main()
