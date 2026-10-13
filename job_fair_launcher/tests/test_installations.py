from __future__ import annotations

import json
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from contextlib import nullcontext
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import installations
import model


class InstallationTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix="job-fair-installation-test-")
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.data = self.root / "local/JobFair"

    def installation(self, library="Steam", name="FINAL FANTASY V PR", *, appid="1173810", buildid="20265087"):
        base = self.root / library
        game = base / "steamapps/common" / name
        game.mkdir(parents=True)
        (game / installations.EXECUTABLE).write_bytes(b"fixture executable")
        manifest = base / "steamapps" / installations.MANIFEST_NAME
        manifest.write_text('"AppState" { "appid" "' + appid + '" "buildid" "' + buildid +
                            '" "installdir" "' + name + '" }', encoding="utf-8")
        return base, game

    def inventory(self):
        return {str(path.relative_to(self.root)): path.read_bytes()
                for path in self.root.rglob("*") if path.is_file()}

    def test_local_appdata_is_absolute_and_does_not_create_directories(self):
        base = self.root / "LocalAppData"
        self.assertEqual(installations.local_data_root({"LOCALAPPDATA": str(base)}), base / "JobFair")
        self.assertEqual(installations.local_data_root({}, self.root / "home"), self.root / "home/AppData/Local/JobFair")
        with self.assertRaises(ValueError):
            installations.local_data_root({"LOCALAPPDATA": "relative"})
        self.assertFalse(base.exists())

    def test_vdf_supports_comments_old_libraries_and_escaped_windows_paths(self):
        text = '// fixture\n"LibraryFolders" { "0" "C:\\\\Steam" "1" { "path" "D:\\\\Games" "apps" { "1173810" "1" } } }'
        self.assertEqual(installations.parse_vdf(text), {"libraryfolders": {
            "0": r"C:\Steam", "1": {"path": r"D:\Games", "apps": {"1173810": "1"}}}})
        self.assertEqual(installations.parse_vdf('"value" "{"'), {"value": "{"})
        self.assertEqual(installations.parse_vdf('root { value plain }'), {"root": {"value": "plain"}})

    def test_vdf_rejects_duplicate_and_incomplete_identity(self):
        for text in ('"appid" "1" "AppId" "2"', '"AppState" { "appid" "1"',
                     '"AppState" { "appid" }', '"unterminated', '}'):
            with self.subTest(text=text), self.assertRaises(ValueError):
                installations.parse_vdf(text)

    def test_identity_accepts_missing_runtime_but_requires_exact_steam_metadata(self):
        _, game = self.installation()
        before = self.inventory()
        self.assertEqual(installations.validate_game_identity(game), game)
        with self.assertRaisesRegex(ValueError, "runtime"):
            model.validate_game_root(game)
        self.assertEqual(before, self.inventory())
        for appid, build in (("1", "20265087"), ("1173810", "wrong")):
            _, wrong = self.installation(appid + build, appid=appid, buildid=build)
            with self.assertRaisesRegex(ValueError, "identity/build"):
                installations.validate_game_identity(wrong)

    def test_identity_rejects_installdir_mismatch_and_unrelated_nested_values(self):
        base, game = self.installation()
        manifest = base / "steamapps" / installations.MANIFEST_NAME
        for text in (
            '"AppState" { "appid" "1173810" "buildid" "20265087" "installdir" "Other game" }',
            '"AppState" { "appid" "1173810" "buildid" "20265087" "installdir" "../Elsewhere" }',
            '"Unrelated" { "appid" "1173810" "buildid" "20265087" "installdir" "FINAL FANTASY V PR" }',
        ):
            manifest.write_text(text, encoding="utf-8")
            with self.subTest(text=text), self.assertRaises(ValueError):
                installations.validate_game_identity(game)

    def test_identity_rejects_missing_exe_missing_manifest_and_non_steam_folder(self):
        base, game = self.installation()
        (game / installations.EXECUTABLE).unlink()
        with self.assertRaisesRegex(ValueError, "exe"):
            installations.validate_game_identity(game)
        (game / installations.EXECUTABLE).write_bytes(b"fixture executable")
        (base / "steamapps" / installations.MANIFEST_NAME).unlink()
        with self.assertRaisesRegex(ValueError, "manifest"):
            installations.validate_game_identity(game)
        with self.assertRaisesRegex(ValueError, "steamapps/common"):
            installations.validate_game_identity(self.root)
        for value in (None, "", ".", "relative"):
            with self.subTest(value=value), self.assertRaises(ValueError):
                installations.validate_game_identity(value)

    def test_discovery_uses_registered_and_standard_libraries_without_scanning(self):
        steam, first = self.installation("Steam")
        second_library, second = self.installation("Extra library")
        old_library, third = self.installation("Old library")
        invalid_library, _ = self.installation("Wrong build", buildid="different")
        modern = ('"libraryfolders" { "0" { "path" ' + json.dumps(str(steam)) + ' } "1" { "path" ' +
                  json.dumps(str(second_library)) + ' "apps" { "1173810" "1" } } "2" { "path" ' +
                  json.dumps(str(invalid_library)) + ' } "3" { "path" "relative" } }')
        (steam / "steamapps/libraryfolders.vdf").write_text(modern, encoding="utf-8")
        standard = self.root / "standard"
        (standard / "config").mkdir(parents=True)
        (standard / "config/libraryfolders.vdf").write_text(
            '"LibraryFolders" { "1" ' + json.dumps(str(old_library)) + ' }', encoding="utf-8")
        # An unregistered install exists, but no directory walk may discover it.
        _, unregistered = self.installation("Not registered")
        before = self.inventory()
        with patch.object(installations, "registry_steam_roots", return_value=[steam, steam]), \
             patch.object(installations, "standard_steam_roots", return_value=[standard, self.root / "missing"]):
            candidates = installations.discover_installations()
        self.assertEqual(set(candidates), {first, second, third})
        self.assertNotIn(unregistered, candidates)
        self.assertEqual(len(candidates), 3)
        self.assertEqual(self.inventory(), before)

    def test_malformed_library_file_does_not_hide_valid_primary_install(self):
        steam, game = self.installation()
        (steam / "steamapps/libraryfolders.vdf").write_bytes(b'"unclosed')
        with patch.object(installations, "registry_steam_roots", return_value=[steam]), \
             patch.object(installations, "standard_steam_roots", return_value=[]):
            self.assertEqual(installations.discover_installations(), [game])

    def test_registry_reads_user_and_both_machine_views_without_mutations(self):
        steam = self.root / "Steam"
        machine = self.root / "Machine Steam"
        accesses = []
        def open_key(hive, name, reserved, access):
            accesses.append((hive, name, access))
            return nullcontext((hive, access))
        def query_value(key, name):
            if key[0] == "user":
                return (str(steam / "steam.exe") if name == "SteamExe" else str(steam), 1)
            if key[1] == 3:
                return str(machine), 1
            raise FileNotFoundError("fixture absent 64-bit registration")
        registry = SimpleNamespace(HKEY_CURRENT_USER="user", HKEY_LOCAL_MACHINE="machine",
                                   KEY_READ=1, KEY_WOW64_32KEY=2, KEY_WOW64_64KEY=4,
                                   OpenKey=open_key, QueryValueEx=query_value)
        with patch.dict(sys.modules, {"winreg": registry}):
            self.assertEqual(installations.registry_steam_roots(), [steam, steam, machine])
        self.assertEqual(len(accesses), 4)
        self.assertEqual({entry[2] for entry in accesses}, {1, 3, 5})
        self.assertFalse(steam.exists())

    def test_standard_steam_roots_use_only_absolute_known_environment_locations(self):
        environment = {"ProgramFiles(x86)": str(self.root / "Program Files (x86)"),
                       "ProgramFiles": str(self.root / "Program Files"), "LOCALAPPDATA": "relative"}
        with patch.dict(installations.os.environ, environment, clear=True):
            self.assertEqual(installations.standard_steam_roots(),
                             [self.root / "Program Files (x86)/Steam", self.root / "Program Files/Steam"])

    def test_settings_save_explicit_identity_and_read_without_runtime_or_writes(self):
        _, game = self.installation()
        self.assertIsNone(model.load_game_folder(self.data))
        self.assertFalse(self.data.exists())
        before_game = self.inventory()
        self.assertEqual(model.save_game_folder(game, self.data), game)
        settings = self.data / model.SETTINGS_FILENAME
        self.assertEqual(model.read_json(settings), {"schema_version": 1, "game_folder": str(game)})
        before = self.inventory()
        with patch.object(model, "atomic_write") as write, patch.object(model, "discover_installations") as discover:
            self.assertEqual(model.load_game_folder(self.data), game)
        write.assert_not_called()
        discover.assert_not_called()
        self.assertEqual(self.inventory(), before)
        self.assertTrue(all(before[key] == value for key, value in before_game.items()))

    def test_invalid_or_stale_settings_preserved_and_invalid_selection_never_writes(self):
        settings = self.data / model.SETTINGS_FILENAME
        for value in ({"schema_version": True, "game_folder": str(self.root)},
                      {"schema_version": 1, "game_folder": "relative"},
                      {"schema_version": 1, "game_folder": str(self.root / "missing")},
                      {"schema_version": 1, "game_folder": str(self.root), "extra": True}):
            settings.parent.mkdir(parents=True, exist_ok=True)
            settings.write_bytes(model.json_bytes(value))
            before = settings.read_bytes()
            with self.subTest(value=value), self.assertRaises(ValueError):
                model.load_game_folder(self.data)
            with self.assertRaises(ValueError):
                model.save_game_folder(self.root / "missing", self.data)
            self.assertEqual(settings.read_bytes(), before)


if __name__ == "__main__":
    unittest.main()
