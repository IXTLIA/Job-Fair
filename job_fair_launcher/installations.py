"""Read-only discovery and identity checks for Steam's Windows FFV installs."""
from __future__ import annotations

import os
from pathlib import Path
from typing import Mapping

APP_ID = "1173810"
SUPPORTED_BUILD_ID = "20265087"
EXECUTABLE = "FINAL FANTASY V.exe"
MANIFEST_NAME = f"appmanifest_{APP_ID}.acf"


def local_data_root(environ: Mapping[str, str] | None = None, home: Path | None = None) -> Path:
    """Find the per-user data location without creating directories."""
    environ = os.environ if environ is None else environ
    configured = environ.get("LOCALAPPDATA")
    base = Path(configured) if configured else (Path.home() if home is None else home) / "AppData/Local"
    if not base.is_absolute():
        raise ValueError("LOCALAPPDATA must identify an absolute folder.")
    return base / "JobFair"


def selected_folder(value: str | Path | None) -> Path:
    if value is None or not isinstance(value, (str, Path)) or not str(value).strip():
        raise ValueError("Choose your Final Fantasy V Steam game folder first.")
    path = Path(value)
    if not path.is_absolute() or any(ord(c) < 32 for c in str(path)):
        raise ValueError("Choose an absolute Final Fantasy V Steam game folder.")
    return path


def parse_vdf(text: str) -> dict:
    """Parse the small KeyValues subset used by Steam manifests and libraries.

    Quoted Windows paths, old numeric library entries, comments and nested app
    lists are supported. Duplicate fields and malformed braces fail closed.
    """
    tokens: list[tuple[str, bool]] = []
    position = 0
    while position < len(text):
        char = text[position]
        if char.isspace() or char == "\ufeff":
            position += 1
        elif text.startswith("//", position):
            end = text.find("\n", position)
            position = len(text) if end < 0 else end + 1
        elif char in "{}":
            tokens.append((char, True))
            position += 1
        elif char == '"':
            position += 1
            value = []
            while position < len(text) and text[position] != '"':
                char = text[position]
                if char == "\\" and position + 1 < len(text) and text[position + 1] in ('"', "\\"):
                    position += 1
                    char = text[position]
                value.append(char)
                position += 1
            if position == len(text):
                raise ValueError("Unterminated Steam manifest string.")
            position += 1
            tokens.append(("".join(value), False))
        else:
            start = position
            while position < len(text) and not text[position].isspace() and text[position] not in '{}"':
                position += 1
            tokens.append((text[start:position], False))
        if len(tokens) > 100000:
            raise ValueError("Steam metadata contains too many fields.")

    index = 0

    def object_value(nested: bool = False, depth: int = 0) -> dict:
        nonlocal index
        if depth > 32:
            raise ValueError("Steam metadata nesting is too deep.")
        result = {}
        while index < len(tokens):
            key, structural = tokens[index]
            index += 1
            if structural and key == "}":
                if not nested:
                    raise ValueError("Unexpected Steam metadata closing brace.")
                return result
            if structural or index >= len(tokens):
                raise ValueError("Missing Steam metadata field value.")
            folded = key.casefold()
            if folded in result:
                raise ValueError("Duplicate Steam metadata field.")
            value, structural = tokens[index]
            index += 1
            if structural and value == "}":
                raise ValueError("Missing Steam metadata field value.")
            result[folded] = object_value(True, depth + 1) if structural else value
        if nested:
            raise ValueError("Unclosed Steam metadata object.")
        return result

    return object_value()


def read_vdf(path: Path) -> dict:
    if path.stat().st_size > 4 * 1024 * 1024:
        raise ValueError("Steam metadata is unexpectedly large.")
    return parse_vdf(path.read_text(encoding="utf-8-sig"))


def _manifest_install_dir(manifest: dict) -> str:
    state = manifest.get("appstate")
    if not isinstance(state, dict) or state.get("appid") != APP_ID or state.get("buildid") != SUPPORTED_BUILD_ID:
        raise ValueError("FFV Steam identity/build is unsupported; this launcher requires build " + SUPPORTED_BUILD_ID + ".")
    name = state.get("installdir")
    if (not isinstance(name, str) or not name.strip() or name in (".", "..")
            or any(c in name for c in '/\\:') or any(ord(c) < 32 for c in name)):
        raise ValueError("FFV Steam manifest has an invalid install folder.")
    return name


def validate_game_identity(value: str | Path | None) -> Path:
    """Validate Steam identity independently of whether Job Fair is installed."""
    game = selected_folder(value)
    if game.parent.name.casefold() != "common" or game.parent.parent.name.casefold() != "steamapps":
        raise ValueError("Choose the FFV game folder inside a Steam library's steamapps/common folder.")
    if not (game / EXECUTABLE).is_file():
        raise ValueError("FINAL FANTASY V.exe was not found in the selected folder.")
    try:
        install_dir = _manifest_install_dir(read_vdf(game.parent.parent / MANIFEST_NAME))
    except OSError as error:
        raise ValueError("The selected folder's FFV Steam manifest could not be read.") from error
    if install_dir.casefold() != game.name.casefold():
        raise ValueError("The FFV Steam manifest does not match the selected install folder.")
    return game


def registry_steam_roots() -> list[Path]:
    """Read the current-user and machine Steam registrations, if available."""
    try:
        import winreg
    except ImportError:
        return []
    roots = []
    for hive, key_name, value_name, view in (
        (winreg.HKEY_CURRENT_USER, r"Software\Valve\Steam", "SteamPath", 0),
        (winreg.HKEY_CURRENT_USER, r"Software\Valve\Steam", "SteamExe", 0),
        (winreg.HKEY_LOCAL_MACHINE, r"Software\Valve\Steam", "InstallPath", winreg.KEY_WOW64_32KEY),
        (winreg.HKEY_LOCAL_MACHINE, r"Software\Valve\Steam", "InstallPath", winreg.KEY_WOW64_64KEY),
    ):
        try:
            with winreg.OpenKey(hive, key_name, 0, winreg.KEY_READ | view) as key:
                value, _ = winreg.QueryValueEx(key, value_name)
            if isinstance(value, str) and value:
                path = Path(value)
                roots.append(path.parent if value_name == "SteamExe" else path)
        except OSError:
            continue
    return roots


def standard_steam_roots() -> list[Path]:
    roots = []
    for variable in ("ProgramFiles(x86)", "ProgramFiles", "LOCALAPPDATA"):
        value = os.environ.get(variable)
        if value and Path(value).is_absolute():
            roots.append(Path(value) / "Steam")
    return roots


def discover_installations() -> list[Path]:
    """Inspect known Steam roots/libraries only; never scan drives or write."""
    libraries: dict[str, Path] = {}
    for root in registry_steam_roots() + standard_steam_roots():
        if not root.is_absolute():
            continue
        libraries[str(root).casefold()] = root
        for metadata in (root / "steamapps/libraryfolders.vdf", root / "config/libraryfolders.vdf"):
            try:
                entries = read_vdf(metadata).get("libraryfolders")
                if not isinstance(entries, dict):
                    continue
                for key, entry in entries.items():
                    if not key.isdecimal():
                        continue
                    value = entry.get("path") if isinstance(entry, dict) else entry
                    if isinstance(value, str) and value and Path(value).is_absolute():
                        libraries[str(Path(value)).casefold()] = Path(value)
            except (OSError, ValueError, UnicodeError):
                continue
    found: dict[str, Path] = {}
    for library in libraries.values():
        try:
            name = _manifest_install_dir(read_vdf(library / "steamapps" / MANIFEST_NAME))
            game = validate_game_identity(library / "steamapps/common" / name)
            found[str(game).casefold()] = game
        except (OSError, ValueError, UnicodeError):
            continue
    return sorted(found.values(), key=lambda path: str(path).casefold())
