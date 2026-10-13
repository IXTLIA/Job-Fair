"""Offline portable-data parity and failure-boundary checks; no game or UI."""
from __future__ import annotations

import json
import io
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from contextlib import redirect_stderr
from unittest.mock import patch

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import entrypoint
import prepare_resources
from ffv_randomizer import campaign, resources


class PortableTests(unittest.TestCase):
    def test_optional_real_ui_is_withdrawn_fits_and_blocks_side_effects(self):
        report = entrypoint.check_ui()
        self.assertEqual(report["status"], "passed")
        self.assertEqual({row["mode"] for row in report["modes"]}, {campaign.MIXED, campaign.REMIXED, campaign.MIXED_FIESTA, campaign.REMIXED_FIESTA})
        self.assertTrue(all(row["state"] == "withdrawn" for row in report["modes"]))
        self.assertEqual((report["dialogs"], report["settings_writes"], report["game_actions"]), (0, 0, 0))
        import launcher
        original = launcher.Launcher

        def accidental_write(window):
            original(window)
            launcher.model.save_profile("Must not be written", "8")

        with patch.object(launcher, "Launcher", side_effect=accidental_write):
            with self.assertRaisesRegex(RuntimeError, "UI diagnostic attempted"):
                entrypoint.check_ui()

        def destroy_callback_write(window):
            ui = original(window)
            window.bind("<Destroy>", lambda event: launcher.model.save_profile("Must not be written", "8")
                        if event.widget is window else None)
            return ui

        with patch.object(launcher, "Launcher", side_effect=destroy_callback_write):
            with self.assertRaisesRegex(ValueError, "callback failed or attempted external I/O"):
                entrypoint.check_ui()

    def test_ui_check_requires_self_check_and_default_does_not_construct_tk(self):
        with patch.object(entrypoint, "check_ui", side_effect=AssertionError("UI was not requested")):
            self.assertNotIn("ui_check", entrypoint.self_check())
            with redirect_stderr(io.StringIO()), self.assertRaises(SystemExit) as error:
                entrypoint.main(["--check-ui"])
            self.assertEqual(error.exception.code, 2)

    def test_pre_separation_current_mode_vectors(self):
        report = entrypoint.self_check()
        self.assertEqual(report["status"], "passed")
        self.assertEqual(len(report["verified_vectors"]), 40)
        self.assertEqual({row["mode"] for row in report["verified_vectors"]}, {campaign.MIXED, campaign.REMIXED, campaign.MIXED_FIESTA, campaign.REMIXED_FIESTA})
        self.assertEqual(sum(row["catalog_id"] in (campaign.MIXED_BOSS_CATALOG_ID, campaign.REMIXED_BOSS_CATALOG_ID)
                             for row in report["verified_vectors"]), 12)

    def test_native_audited_model_parity_when_developer_sources_exist(self):
        if not campaign.OPERATIONS.is_file():
            self.skipTest("Native developer audit is intentionally absent in source exports.")
        from ffv_randomizer.jobs import build_job_probe
        native = json.loads(campaign.OPERATIONS.read_bytes())
        for seed in (0, 1, 8, 90, 2**63, 2**64 - 1):
            expected = build_job_probe(campaign.ASSETS, native, seed)
            self.assertEqual({row["location_id"]: int(row["reward_id"]) for row in resources.native_job_placements(seed)},
                             {row["check_id"]: row["current_job_id"] for row in expected["spoiler"]})

    def test_missing_and_modified_resources_fail_closed(self):
        with tempfile.TemporaryDirectory() as directory:
            target = Path(directory)
            prepare_resources.export_resources(target)
            with patch.object(resources, "DATA_ROOT", target):
                path = target / "native_job_model.json"
                original = path.read_bytes()
                path.write_bytes(original + b" ")
                with self.assertRaisesRegex(ValueError, "hash mismatch"):
                    campaign.build_request("8")
                path.unlink()
                with self.assertRaises(OSError):
                    campaign.build_request("8")

    def test_data_projection_contains_no_native_programs(self):
        def inspect(value):
            if isinstance(value, dict):
                self.assertFalse(set(value) & {"Mnemonics", "mnemonic", "operands", "scripts", "native_events"})
                for child in value.values(): inspect(child)
            elif isinstance(value, list):
                for child in value: inspect(child)
        for name in resources.CATALOG_FILES:
            inspect(resources.read_catalog(name))

    def test_self_check_from_unrelated_directory_makes_no_settings_or_ui(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            env = {**os.environ, "LOCALAPPDATA": str(root), "APPDATA": str(root),
                   "PYTHONDONTWRITEBYTECODE": "1"}
            result = subprocess.run([sys.executable, "-B", str(HERE / "entrypoint.py"), "--smoke-test"],
                                    cwd=root, env=env, text=True, capture_output=True, timeout=60)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertEqual(json.loads(result.stdout)["status"], "passed")
            self.assertEqual(list(root.iterdir()), [])

    def test_preview_labels_preserved(self):
        jobs = resources.read_catalog("remixed_jobs_catalog.json")
        travel = resources.read_catalog("world_travel_catalog.json")
        self.assertTrue(all(isinstance(row["region"], str) and row["region"] for row in jobs["bonus_candidates"]))
        self.assertEqual({row["world"] for row in travel["anchors"]}, {1, 2, 3})
        self.assertTrue(all(row["name"] for row in travel["anchors"]))


if __name__ == "__main__":
    unittest.main()
