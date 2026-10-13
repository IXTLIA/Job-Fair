"""Boss assignments compose with frozen campaign rewards and strict save payloads."""
import copy
import unittest

from ffv_randomizer import campaign


class BossCampaignTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.requests = {mode: campaign.build_boss_campaign("8", mode)
                        for mode in (campaign.MIXED, campaign.REMIXED)}

    def test_bosses_do_not_perturb_existing_campaign_rewards(self):
        for mode in (campaign.MIXED, campaign.REMIXED):
            for seed in ("0", "8", "18446744073709551615"):
                with self.subTest(mode=mode, seed=seed):
                    previous = campaign.build_fixed_karnak(seed, mode)
                    current = campaign.build_boss_campaign(seed, mode)
                    self.assertEqual(previous["starting_job_ids"], current["starting_job_ids"])
                    self.assertEqual(previous["placements"], [row for row in current["placements"]
                                                              if row["reward_kind"] != "boss"])
                    self.assertEqual(49, sum(row["reward_kind"] == "boss" for row in current["placements"]))
                    self.assertEqual(previous["schema_version"], current["schema_version"])

    def test_catalog_dispatch_and_determinism(self):
        for mode, catalog in ((campaign.MIXED, campaign.MIXED_BOSS_CATALOG_ID),
                              (campaign.REMIXED, campaign.REMIXED_BOSS_CATALOG_ID)):
            self.assertEqual(self.requests[mode], campaign.build_request("8", mode, catalog_id=catalog))
            self.assertEqual(self.requests[mode], campaign.validate_request(copy.deepcopy(self.requests[mode])))
        with self.assertRaises(ValueError):
            campaign.build_request("8", campaign.MIXED, catalog_id=campaign.REMIXED_BOSS_CATALOG_ID)

    def test_boss_settings_participate_in_integrity_hash(self):
        for original in self.requests.values():
            for field in ("boss_scope", "boss_scaling"):
                value = copy.deepcopy(original)
                value["settings"][field] += "-changed"
                self.assertNotEqual(original["placement_hash"], campaign.placement_hash(value))
                value["placement_hash"] = campaign.placement_hash(value)
                with self.assertRaises(ValueError):
                    campaign.validate_request(value)

    def test_missing_duplicate_and_extra_rows_rejected_even_with_new_hash(self):
        for original in self.requests.values():
            boss = next(row for row in original["placements"] if row["reward_kind"] == "boss")
            for alteration in ("missing", "duplicate", "extra"):
                with self.subTest(mode=original["mode"], alteration=alteration):
                    value = copy.deepcopy(original)
                    if alteration == "missing":
                        value["placements"].remove(boss)
                    elif alteration == "duplicate":
                        value["placements"].append(copy.deepcopy(boss))
                    else:
                        value["placements"].append({**boss, "location_id": "native_boss/unknown"})
                    value["placement_hash"] = campaign.placement_hash(value)
                    with self.assertRaises(ValueError):
                        campaign.validate_request(value)

    def test_cannot_smuggle_boss_location_as_an_ordinary_reward(self):
        value = copy.deepcopy(self.requests[campaign.MIXED])
        row = next(row for row in value["placements"] if row["reward_kind"] == "boss")
        row.update(reward_kind="item", reward_id="1")
        value["placement_hash"] = campaign.placement_hash(value)
        with self.assertRaises(ValueError):
            campaign.validate_request(value)

    def test_old_catalogs_reject_boss_fields_and_rows(self):
        value = copy.deepcopy(self.requests[campaign.MIXED])
        value["catalog_id"] = campaign.FIXED_KARNAK_MIXED_CATALOG_ID
        value["placement_hash"] = campaign.placement_hash(value)
        with self.assertRaises(ValueError):
            campaign.validate_request(value)
        value["settings"].pop("boss_scope")
        value["settings"].pop("boss_scaling")
        value["placement_hash"] = campaign.placement_hash(value)
        with self.assertRaises(ValueError):
            campaign.validate_request(value)

    def test_campaign_identity_and_fields_stay_strict(self):
        for mode, original in self.requests.items():
            for field, replacement in (("mode", campaign.REMIXED if mode == campaign.MIXED else campaign.MIXED),
                                       ("schema_version", True), ("seed", "08"),
                                       ("placements", None), ("settings", None)):
                with self.subTest(mode=mode, field=field):
                    value = copy.deepcopy(original)
                    value[field] = replacement
                    with self.assertRaises(ValueError):
                        campaign.validate_request(value)
            value = copy.deepcopy(original)
            value["unrecognized"] = "field"
            with self.assertRaises(ValueError):
                campaign.validate_request(value)


if __name__ == "__main__":
    unittest.main()
