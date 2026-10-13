import io
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "job_fair_launcher"))
import core
from prepare_release import delta

class InstallerTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.game = self.root / "game"
        self.game.mkdir()
        self.backups = self.root / "backups"
        self.addCleanup(patch.stopall)
        patch.object(core, "game_closed").start()
        patch.object(core, "validate_game", side_effect=lambda p: p).start()

    def install(self, payload):
        return core.install(self.game, payload, self.backups, lambda _: None)

    def test_clean_install_restore_preserves_saves_and_generated_files(self):
        (self.game / "save.sav").write_bytes(b"owner save")
        journal = self.install({"winhttp.dll": b"loader", "BepInEx/plugins/JobFair/JobFair.Runtime.dll": b"mod"})
        generated = self.game / "BepInEx/generated.log"
        generated.write_bytes(b"later")
        core.restore(journal, lambda _: None)
        self.assertFalse((self.game / "winhttp.dll").exists())
        self.assertEqual((self.game / "save.sav").read_bytes(), b"owner save")
        self.assertEqual(generated.read_bytes(), b"later")

    def test_update_restores_old_bytes(self):
        (self.game / "old.dll").write_bytes(b"old")
        journal = self.install({"old.dll": b"new"})
        self.assertEqual((journal.parent / "original/old.dll").read_bytes(), b"old")
        core.restore(journal, lambda _: None)
        self.assertEqual((self.game / "old.dll").read_bytes(), b"old")

    def test_identical_install_no_changes(self):
        self.install({"file": b"same"})
        journal = self.install({"file": b"same"})
        self.assertEqual(json.loads(journal.read_text())["files"], [])

    def test_different_file_blocks_entire_restore(self):
        journal = self.install({"a": b"a", "b": b"b"})
        (self.game / "b").write_bytes(b"user changed")
        with self.assertRaises(ValueError):
            core.restore(journal)
        self.assertTrue((self.game / "a").exists())

    def test_corrupt_backup_blocks_restore(self):
        (self.game / "a").write_bytes(b"old")
        journal = self.install({"a": b"new"})
        (journal.parent / "original/a").write_bytes(b"bad")
        with self.assertRaises(ValueError):
            core.restore(journal)
        self.assertEqual((self.game / "a").read_bytes(), b"new")

    def test_expanded_loader_configuration_is_retained(self):
        journal = self.install({"BepInEx/config/BepInEx.cfg": b"initial", "winhttp.dll": b"loader"})
        cfg = self.game / "BepInEx/config/BepInEx.cfg"
        cfg.write_bytes(b"expanded after native launch")
        core.restore(journal, lambda _: None)
        self.assertEqual(cfg.read_bytes(), b"expanded after native launch")
        self.assertFalse((self.game / "winhttp.dll").exists())

    def test_backup_inside_game_rejected(self):
        with self.assertRaises(ValueError):
            core.install(self.game, {"a": b"x"}, self.game / "backups")

    def test_destination_directory_rejected_before_writes(self):
        (self.game / "occupied").mkdir()
        with self.assertRaises(ValueError):
            self.install({"a": b"x", "occupied": b"x"})
        self.assertFalse((self.game / "a").exists())

    def test_activation_is_last(self):
        journal = self.install({"winhttp.dll": b"x", "zzz": b"x"})
        self.assertEqual(json.loads(journal.read_text())["files"][-1]["path"], "winhttp.dll")

    def test_interrupted_install_can_restore(self):
        original = core.os.replace
        def fail(src, dest):
            if Path(dest).name == "b":
                raise OSError("simulated disk failure")
            return original(src, dest)
        with patch.object(core.os, "replace", side_effect=fail), self.assertRaises(RuntimeError):
            self.install({"a": b"one", "b": b"two", "winhttp.dll": b"loader"})
        self.assertFalse((self.game / "winhttp.dll").exists())
        journal = next(self.backups.glob("*/restore.json"))
        core.restore(journal, lambda _: None)
        self.assertFalse((self.game / "a").exists())

    def test_restored_receipt_cannot_replay(self):
        journal = self.install({"a": b"one"})
        core.restore(journal, lambda _: None)
        with self.assertRaises(ValueError):
            core.restore(journal)

    def test_path_validation(self):
        for name in ("../x", "C:/x", "/x", "x\\y", "x/../y", "NUL.txt", "x.", "a//b", "a/./b", "a:stream"):
            with self.subTest(name=name), self.assertRaises(ValueError):
                core.safe_relative(name)
        self.assertEqual(str(core.safe_relative("BepInEx/plugins/mod.dll")), "BepInEx/plugins/mod.dll")

    def test_delta_reconstructs_insertions_deletions_and_binary(self):
        base = bytes(range(256)) * 10
        data = b"new" + base[:500] + b"changed" + base[800:] + b"tail"
        recipe = delta(base, data)
        self.assertEqual(core.apply_delta(base, recipe), data)
        with self.assertRaises(ValueError):
            core.apply_delta(b"wrong", recipe)
        recipe["sha256"] = "wrong"
        with self.assertRaises(ValueError):
            core.apply_delta(base, recipe)

    def test_zip_traversal_and_duplicate_rejected(self):
        for names in (("../escape",), ("same", "SAME")):
            stream = io.BytesIO()
            with zipfile.ZipFile(stream, "w") as archive:
                for name in names:
                    archive.writestr(name, b"x")
            with self.assertRaises(ValueError):
                core.read_archive(stream.getvalue())

    def test_zip_normal(self):
        stream = io.BytesIO()
        with zipfile.ZipFile(stream, "w") as archive:
            archive.writestr("BepInEx/", b"")
            archive.writestr("BepInEx/plugin.dll", b"binary")
        self.assertEqual(core.read_archive(stream.getvalue()), {"BepInEx/plugin.dll": b"binary"})

    def test_game_running_refuses_before_backup(self):
        core.game_closed.side_effect = ValueError("game running")
        with self.assertRaises(ValueError):
            self.install({"file": b"x"})
        self.assertFalse(self.backups.exists())

    def test_download_hash_mismatch_no_game_changes(self):
        resources = self.root / "payload"
        resources.mkdir()
        (resources / "payload.json").write_text('{"patches": [], "files": []}')
        with patch.object(core.urllib.request, "urlopen", return_value=io.BytesIO(b"bad")):
            with self.assertRaises(ValueError):
                core.prepare(self.game, resources, lambda _: None)
        self.assertEqual(list(self.game.iterdir()), [])

if __name__ == "__main__":
    unittest.main()
