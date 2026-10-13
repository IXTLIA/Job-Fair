"""A source export must work without the development checkout or game files."""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "job_fair_launcher/tools"))
import export_source


class SourceExportTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="jobfair-export-test-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)

    def test_manifest_covers_exact_portable_allowlist_and_no_private_state(self):
        output = self.root / "source"
        result = export_source.export_source(output)
        paths = {entry["path"] for entry in result["files"]}
        self.assertEqual(paths, set(export_source.SOURCE_FILES) | {"README.md", ".gitignore"}
                         | {export_source.DATA_DIRECTORY + "/" + name for name in export_source.DATA_FILES})
        self.assertEqual(paths | {"SOURCE_MANIFEST.json"},
                         {p.relative_to(output).as_posix() for p in output.rglob("*") if p.is_file()})
        self.assertEqual(json.loads((output / "SOURCE_MANIFEST.json").read_bytes()), result)
        self.assertTrue((output / "REMIXED_PLAYTEST.md").is_file())
        self.assertTrue((output / "BOSS_SHUFFLE_PLAYTEST.md").is_file())
        self.assertIn("**/.build-venv/", (output / ".gitignore").read_text(encoding="utf-8"))
        self.assertTrue(any(path.startswith("ffv_randomizer/ffv_randomizer/data/") for path in paths))
        for entry in result["files"]:
            path = Path(entry["path"])
            self.assertFalse(path.is_absolute())
            self.assertTrue(set(path.parts).isdisjoint({"research", "extracted", "backups", ".venv", "__pycache__"}))
            self.assertNotIn(path.suffix.lower(), {".dll", ".exe", ".log", ".sav", ".dat", ".pyc"})
            self.assertFalse(entry["path"].startswith("job_fair_launcher/data/"))
            payload = (output / path).read_bytes()
            self.assertEqual(entry["size"], len(payload))
            self.assertEqual(entry["sha256"], hashlib.sha256(payload).hexdigest())

    def test_archive_is_deterministic_and_has_the_same_relative_files(self):
        first, second = self.root / "one.zip", self.root / "two.zip"
        manifest = export_source.export_source(first, archive=True)
        export_source.export_source(second, archive=True)
        self.assertEqual(first.read_bytes(), second.read_bytes())
        with zipfile.ZipFile(first) as archive:
            self.assertEqual(set(archive.namelist()),
                             {entry["path"] for entry in manifest["files"]} | {"SOURCE_MANIFEST.json"})
            self.assertIsNone(archive.testzip())
            for entry in manifest["files"]:
                self.assertEqual(hashlib.sha256(archive.read(entry["path"])).hexdigest(), entry["sha256"])

    def test_exported_copy_generates_identical_seeds_and_can_export_itself(self):
        output = self.root / "source"
        export_source.export_source(output)
        self.assertFalse((output / "research").exists())
        # Isolated Python avoids the checkout's imports and environment paths.
        code = """import json, pathlib, sys
root = pathlib.Path(sys.argv[1])
sys.path.insert(0, str(root / 'ffv_randomizer'))
sys.path.insert(0, str(root / 'job_fair_launcher/packaging'))
from write_build_receipt import source_snapshot
assert source_snapshot()
from ffv_randomizer import campaign
values = {}
for seed in ('0', '1', '2', '8', '90', '18446744073709551615'):
    for mode in (campaign.MIXED, campaign.REMIXED):
        for family, build in (('fixed', campaign.build_fixed_karnak), ('bosses', campaign.build_boss_campaign)):
            request = build(seed, mode)
            assert campaign.validate_request(request) == request
            values[family + '|' + seed + '|' + mode] = request['placement_hash']
    for mode in (campaign.MIXED, campaign.REMIXED, campaign.MIXED_FIESTA, campaign.REMIXED_FIESTA):
        request = campaign.build_request(seed, mode)
        assert campaign.validate_request(request) == request
        values['current|' + seed + '|' + mode] = request['placement_hash']
print(json.dumps(values, sort_keys=True))
"""
        env = dict(os.environ, LOCALAPPDATA=str(self.root / "appdata"))
        expected = subprocess.run([sys.executable, "-I", "-c", code, str(ROOT)], cwd=self.root,
                                  env=env, text=True, capture_output=True, check=True, timeout=120)
        actual = subprocess.run([sys.executable, "-I", "-c", code, str(output)], cwd=self.root,
                                env=env, text=True, capture_output=True, check=True, timeout=120)
        self.assertEqual(json.loads(expected.stdout), json.loads(actual.stdout))
        # Unlisted local files must remain excluded when exporting a source copy.
        private = output / "job_fair_launcher/data/profiles/private.json"
        private.parent.mkdir(parents=True)
        private.write_text('{"private":true}', encoding="utf-8")
        (output / "unlisted.log").write_text("private log", encoding="utf-8")
        again = self.root / "export-again"
        subprocess.run([sys.executable, "-I", str(output / "job_fair_launcher/tools/export_source.py"), str(again)],
                       cwd=self.root, env=env, text=True, capture_output=True, check=True, timeout=120)
        self.assertFalse((again / "job_fair_launcher/data").exists())
        self.assertFalse((again / "unlisted.log").exists())
        self.assertEqual((again / "SOURCE_MANIFEST.json").read_bytes(),
                         (output / "SOURCE_MANIFEST.json").read_bytes())
        self.assertFalse((self.root / "appdata").exists())

    def test_existing_targets_are_never_overwritten(self):
        for path in (self.root / "existing", self.root / "existing.zip"):
            path.write_bytes(b"retain me")
            with patch.object(export_source, "_prepare") as prepare:
                with self.assertRaisesRegex(ValueError, "already exists"):
                    export_source.export_source(path, archive=path.suffix == ".zip")
                prepare.assert_not_called()
            self.assertEqual(path.read_bytes(), b"retain me")
        directory = self.root / "existing-directory"
        directory.mkdir()
        with self.assertRaisesRegex(ValueError, "already exists"):
            export_source.export_source(directory)
        self.assertEqual(list(directory.iterdir()), [])

    def test_absolute_machine_paths_and_binary_payloads_are_refused(self):
        machine_path = "Q" + chr(58) + chr(92) + "private" + chr(92) + "source"
        for payload in (machine_path.encode(), b"binary\0payload", bytes([255, 254, 1])):
            with self.subTest(payload=payload), self.assertRaises(ValueError):
                export_source._check_text("sample.txt", payload)
        export_source._check_text("sample.txt", b"relative/path and %LOCALAPPDATA% are portable")
        export_source._check_text("sample.txt", b"https://www.python.org and https://pyinstaller.org")
        with self.assertRaisesRegex(ValueError, "Native program"):
            export_source._check_catalog({"nested": [{"Mnemonics": []}]})
        export_source._check_catalog({"source": {"map_name": "Map_20071_1"}})

    def test_missing_source_and_invalid_archive_target_fail_without_output(self):
        output = self.root / "source"
        with patch.object(export_source, "SOURCE_FILES", ("missing-source.py",)):
            with self.assertRaisesRegex(ValueError, "Missing"):
                export_source.export_source(output)
        self.assertFalse(output.exists())
        with self.assertRaisesRegex(ValueError, ".zip"):
            export_source.export_source(output, archive=True)
        self.assertFalse(output.exists())


if __name__ == "__main__":
    unittest.main()
