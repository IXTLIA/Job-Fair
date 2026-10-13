from pathlib import Path
import sys
import tempfile
import tkinter as tk
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import launcher
import model
from ffv_randomizer import bosses


class BossLauncherTests(unittest.TestCase):
    def test_current_rules_generate_all_bosses_in_both_modes(self):
        for mode, scope, catalog_id in (
                (model.campaign.MIXED, "mixed-tiers-v1", model.campaign.MIXED_REWARDS_CATALOG_ID),
                (model.campaign.REMIXED, "remixed-all-v1", model.campaign.REMIXED_REWARDS_CATALOG_ID)):
            with self.subTest(mode=mode):
                request = model.seed_request("8", mode)
                self.assertEqual(request["catalog_id"], catalog_id)
                self.assertEqual(request["settings"]["boss_scope"], scope)
                self.assertEqual(request["settings"]["boss_scaling"], bosses.SCALING_VERSION)
                rows = [row for row in request["placements"] if row["reward_kind"] == "boss"]
                self.assertEqual(len(rows), 49)
                bosses.validate_placements(rows, mode)

    def test_previous_fixed_karnak_profiles_normalize_without_rewriting_original(self):
        for mode in (model.campaign.MIXED, model.campaign.REMIXED):
            with self.subTest(mode=mode), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                old = model.campaign.build_fixed_karnak("8", mode)
                name = "Previous rules"
                path = root / "profiles" / (model.hashlib.sha256(name.encode()).hexdigest() + ".json")
                path.parent.mkdir()
                original = model.json_bytes({"profile_schema": 1, "name": name,
                                            "preset": model.preset_for(old), "request": old})
                path.write_bytes(original)
                with patch.object(model, "atomic_write", side_effect=AssertionError("Loading must not write")):
                    current = model.load_profile(path, root)
                self.assertEqual(current["request"], model.seed_request("8", mode))
                self.assertEqual(path.read_bytes(), original)
                self.assertNotEqual(current["preset"], model.preset_for(old))

    def test_spoiler_describes_saved_boss_assignment_without_regenerating(self):
        request = model.seed_request("8", model.campaign.REMIXED)
        rows = [row for row in request["placements"] if row["reward_kind"] == "boss"]
        rows[0]["reward_id"], rows[1]["reward_id"] = rows[1]["reward_id"], rows[0]["reward_id"]
        request["placement_hash"] = model.campaign.placement_hash(request)
        with patch.object(bosses, "generate", side_effect=AssertionError("Spoiler must use the saved table")):
            spoiler = model.boss_spoiler(request)
        self.assertEqual(len(spoiler), 49)
        self.assertEqual({row["location_id"]: row["boss_id"] for row in spoiler},
                         {row["location_id"]: row["reward_id"] for row in rows})
        self.assertTrue(all(row["boss"] and row["location"] and row["scaling"] == bosses.SCALING_VERSION for row in spoiler))
        self.assertEqual(model.boss_spoiler(model.campaign.build_fixed_karnak("8", model.campaign.MIXED)), [])

    def test_disposable_apply_receipt_lists_all_boss_replacements(self):
        for mode in (model.campaign.MIXED, model.campaign.REMIXED):
            with self.subTest(mode=mode), tempfile.TemporaryDirectory() as directory:
                root = Path(directory); target = root / "next-run.json"
                with patch.object(model, "require_game_closed"), patch.object(model, "validate_game_root", return_value=target):
                    receipt = model.apply_next_run("8", data_root=root, mode=mode)
                self.assertNotIn("receipt_warning", receipt)
                request = receipt["request"]
                spoiler = model.read_json(root / "runs" / (request["placement_hash"] + ".json"))
                self.assertEqual(spoiler["bosses"], model.boss_spoiler(request))
                self.assertEqual(spoiler["request"], model.read_json(target))
                self.assertIn("49 bosses", spoiler["scope"])
                self.assertIn("Simple/Complex tiers" if mode == model.campaign.MIXED else "in one pool", spoiler["scope"])

    def test_mode_descriptions_explain_tiers_and_scaling_within_hidden_window(self):
        window = tk.Tk(); window.withdraw()
        try:
            with patch.object(model, "current_request", return_value=None), \
                    patch.object(model, "load_game_folder", return_value=None), \
                    patch.object(model, "list_profiles", return_value=([], [])), \
                    patch.object(launcher.installations, "discover_installations", return_value=[]), \
                    patch.object(model, "apply_next_run", side_effect=AssertionError("UI preview must not apply")):
                ui = launcher.Launcher(window)
                for mode in (model.campaign.MIXED, model.campaign.REMIXED):
                    ui.mode.set(mode); window.update_idletasks()
                    description = ui.mode_description.get()
                    self.assertIn("stats scaled to their location", description)
                    self.assertIn("Simple/Complex tiers" if mode == model.campaign.MIXED else "49 bosses shuffle in one pool", description)
                    self.assertLessEqual(window.winfo_reqheight(), launcher.MINIMUM_SIZE[1])
                    self.assertLessEqual(window.winfo_reqwidth(), launcher.MINIMUM_SIZE[0])
                    self.assertEqual(window.state(), "withdrawn")
        finally:
            window.destroy()


if __name__ == "__main__":
    unittest.main()
