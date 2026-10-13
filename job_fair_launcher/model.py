"""Job Fair launcher model. Importing this module never writes or launches anything."""
from __future__ import annotations

import csv
import hashlib
import io
import json
import os
from pathlib import Path
import stat
import subprocess
import sys
import tempfile
from datetime import datetime, timezone
from typing import Callable

import installations

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "ffv_randomizer"))
from ffv_randomizer.runtime_seed import (  # noqa: E402
    ALGORITHM_VERSION, canonical_seed, generate_seed, select_starting_jobs, try_parse_seed,
)
from ffv_randomizer import campaign

DATA_ROOT = installations.local_data_root()
SETTINGS_FILENAME = "launcher-settings.json"
discover_installations = installations.discover_installations
PRESET_ID = "four-starting-jobs-prototype"
PRESET_LABEL = "Four starting jobs prototype"
MIXED_PRESET_ID = "mixed-crystals-wind-slice-v1"
WORLD_PRESET_ID = "mixed-crystals-world-pools-v2"
SHOP_PRESET_ID = "mixed-crystals-world-shops-v3"
REMIXED_PRESET_ID = "remixed-crystals-playtest-v1"
FIXED_KARNAK_SHOP_PRESET_ID = "mixed-crystals-fixed-karnak-v1"
FIXED_KARNAK_REMIXED_PRESET_ID = "remixed-crystals-fixed-karnak-v1"
MIXED_BOSS_PRESET_ID = "mixed-crystals-bosses-v1"
REMIXED_BOSS_PRESET_ID = "remixed-crystals-bosses-v1"
MIXED_REWARDS_PRESET_ID = campaign.MIXED_REWARDS_CATALOG_ID
REMIXED_REWARDS_PRESET_ID = campaign.REMIXED_REWARDS_CATALOG_ID
MIXED_FIESTA_PRESET_ID = campaign.MIXED_FIESTA_CATALOG_ID
REMIXED_FIESTA_PRESET_ID = campaign.REMIXED_FIESTA_CATALOG_ID
TARGET_RELATIVE = Path("BepInEx/config/JobFair.next-run.json")
# Reviewed Job Fair 0.9.3 runtime; Apply verifies the selected installation.
COMPATIBLE_RUNTIME_SHA256 = "9154e7dd41e7c2b774c76665cf80364c2f77870e902a4845b1bb278d3d920996"
JOB_NAMES = {
    2: "Thief", 3: "Monk", 4: "Red Mage", 5: "White Mage", 6: "Black Mage",
    7: "Knight", 8: "Ninja", 9: "Ranger", 10: "Geomancer", 11: "Dragoon",
    12: "Bard", 13: "Summoner", 14: "Berserker", 15: "Samurai", 16: "Time Mage",
    17: "Chemist", 18: "Dancer", 19: "Blue Mage", 20: "Mystic Knight",
    21: "Beastmaster", 22: "Mime",
}


def _current_mode(mode: str | None) -> str:
    # Historical starter-only profiles had no mode. Remixed keeps their closest
    # supported intent: four random starter jobs, with the current full rules.
    if mode is None or mode == PRESET_LABEL:
        return campaign.REMIXED
    if mode not in campaign.MODES:
        raise ValueError("Choose Mixed Crystals, Remixed Crystals, Mixed-Fiesta or Remixed-Fiesta.")
    return mode


def seed_request(text: str, mode: str | None = None) -> dict:
    mode = _current_mode(mode)
    return campaign.build_request(text, mode, catalog_id=campaign.REWARD_CATALOGS[mode])


def validate_request(value: object) -> dict:
    if isinstance(value, dict) and value.get("schema_version") in (2, 3):
        return campaign.validate_request(value)
    if not isinstance(value, dict) or set(value) != {"schema_version", "generator", "seed"}:
        raise ValueError("Unsupported next-run request fields.")
    if type(value["schema_version"]) is not int or value["schema_version"] != 1:
        raise ValueError("Unsupported next-run schema.")
    if value["generator"] != ALGORITHM_VERSION or not isinstance(value["seed"], str):
        raise ValueError("Unsupported seed generator or seed type.")
    valid, seed = try_parse_seed(value["seed"])
    if not valid or canonical_seed(seed) != value["seed"]:
        raise ValueError("Saved seed must be canonical decimal.")
    return value


def current_rules_request(value: object) -> dict:
    """Read a verified configuration's seed/mode and regenerate current rules.

    Historical validation still rejects damaged or unsupported requests before
    their identity is used. This returns new data without changing a file.
    """
    request = validate_request(value)
    return seed_request(request["seed"], _current_mode(request.get("mode")))


def preview(text: str, mode: str | None = None) -> list[tuple[int, str]]:
    mode = _current_mode(mode)
    valid, seed = try_parse_seed(text)
    if not valid:
        raise ValueError("Invalid seed.")
    if mode == campaign.MIXED:
        return [(1, "Freelancer")]
    return [(job, JOB_NAMES[job]) for job in select_starting_jobs(seed, range(2, 23))]


def json_bytes(value: object) -> bytes:
    return (json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False) + "\n").encode("utf-8")


def _unique_object(pairs: list[tuple[str, object]]) -> dict:
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"Duplicate JSON field: {key}")
        result[key] = value
    return result


def read_json(path: Path) -> object:
    if path.stat().st_size > 1024 * 1024:
        raise ValueError("Configuration is unexpectedly large.")
    return json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=_unique_object,
                      parse_constant=lambda value: (_ for _ in ()).throw(ValueError(value)))


def _reject_links(path: Path) -> None:
    """Reject symlinks/junctions along an owned write path, including the target."""
    for part in (path, *path.parents):
        if part.exists() or part.is_symlink():
            attributes = getattr(part.lstat(), "st_file_attributes", 0)
            if part.is_symlink() or attributes & getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400):
                raise ValueError(f"Linked write paths are not supported: {part}")


def atomic_write(path: Path, payload: bytes, before_commit: Callable[[], None] | None = None,
                 *, replace_existing: bool = True) -> None:
    _reject_links(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary: str | None = None
    try:
        with tempfile.NamedTemporaryFile(prefix=path.name + ".", suffix=".tmp", dir=path.parent,
                                         delete=False) as stream:
            temporary = stream.name
            stream.write(payload)
            stream.flush()
            os.fsync(stream.fileno())
        if before_commit:
            before_commit()
        _reject_links(path)
        if replace_existing:
            os.replace(temporary, path)
            temporary = None
        else:
            # Linking the flushed temporary file claims a new name atomically.
            # Unlike replace(), this cannot overwrite a concurrent import.
            os.link(temporary, path)
        if path.read_bytes() != payload:
            raise OSError("Written configuration could not be verified.")
    finally:
        if temporary is not None:
            Path(temporary).unlink(missing_ok=True)


def preset_for(request: dict) -> str:
    if request["schema_version"] == 1: return PRESET_ID
    if request["catalog_id"] in campaign.REWARD_CATALOGS.values(): return request["catalog_id"]
    if request["catalog_id"] == campaign.MIXED_BOSS_CATALOG_ID: return MIXED_BOSS_PRESET_ID
    if request["catalog_id"] == campaign.REMIXED_BOSS_CATALOG_ID: return REMIXED_BOSS_PRESET_ID
    if request["catalog_id"] == campaign.FIXED_KARNAK_REMIXED_CATALOG_ID: return FIXED_KARNAK_REMIXED_PRESET_ID
    if request["catalog_id"] == campaign.FIXED_KARNAK_MIXED_CATALOG_ID: return FIXED_KARNAK_SHOP_PRESET_ID
    if request["catalog_id"] == campaign.REMIXED_PLAYTEST_CATALOG_ID: return REMIXED_PRESET_ID
    if request["catalog_id"] == campaign.SHOP_CATALOG_ID: return SHOP_PRESET_ID
    return WORLD_PRESET_ID if request["catalog_id"] == campaign.WORLD_CATALOG_ID else MIXED_PRESET_ID


def selected_request(seed_text: str, mode: str | None, stored_request: dict | None) -> dict:
    if stored_request is None: return seed_request(seed_text, mode)
    request = validate_request(stored_request)
    valid, seed = try_parse_seed(seed_text)
    if (not valid or canonical_seed(seed) != request["seed"]
            or _current_mode(mode) != _current_mode(request.get("mode"))):
        raise ValueError("The loaded profile no longer matches the selected seed and mode.")
    return current_rules_request(request)


def save_profile(name: str, seed_text: str, data_root: Path = DATA_ROOT, mode: str | None = None,
                 *, stored_request: dict | None = None) -> Path:
    name = name.strip()
    if not 1 <= len(name) <= 80 or any(ord(c) < 32 for c in name):
        raise ValueError("Use a profile name of 1–80 characters without control characters.")
    request = selected_request(seed_text, mode, stored_request)
    profile = {"profile_schema": 1, "name": name,
               "preset": preset_for(request),
               "request": request}
    # The human label is never interpreted as a path.
    filename = hashlib.sha256(name.encode("utf-8")).hexdigest() + ".json"
    path = data_root / "profiles" / filename
    atomic_write(path, json_bytes(profile))
    return path


def load_profile(path: Path, data_root: Path = DATA_ROOT) -> dict:
    parent = data_root / "profiles"
    _reject_links(path)
    if path.parent.resolve() != parent.resolve() or path.suffix != ".json":
        raise ValueError("Choose a profile saved by this launcher.")
    value = _profile_value(read_json(path))
    if path.name != hashlib.sha256(value["name"].encode("utf-8")).hexdigest() + ".json":
        raise ValueError("Profile name does not match its file.")
    return value


def _profile_value(value: object) -> dict:
    if not isinstance(value, dict) or set(value) != {"profile_schema", "name", "preset", "request"}:
        raise ValueError("Unsupported profile format.")
    if type(value["profile_schema"]) is not int or value["profile_schema"] != 1 or value["preset"] not in (PRESET_ID, MIXED_PRESET_ID, WORLD_PRESET_ID, SHOP_PRESET_ID, REMIXED_PRESET_ID, FIXED_KARNAK_SHOP_PRESET_ID, FIXED_KARNAK_REMIXED_PRESET_ID, MIXED_BOSS_PRESET_ID, REMIXED_BOSS_PRESET_ID, *campaign.REWARD_CATALOGS.values()):
        raise ValueError("Unsupported profile version or preset.")
    name = value["name"]
    if not isinstance(name, str) or not 1 <= len(name) <= 80 or name != name.strip() or any(ord(c) < 32 for c in name):
        raise ValueError("Invalid profile name.")
    validate_request(value["request"])
    expected_preset = preset_for(value["request"])
    if value["preset"] != expected_preset:
        raise ValueError("Profile preset does not match its stored configuration.")
    request = current_rules_request(value["request"])
    return {**value, "preset": preset_for(request), "request": request}


def import_profile(path: Path, data_root: Path = DATA_ROOT) -> Path:
    """Import one explicitly chosen profile, without replacing another name."""
    if not path.is_absolute():
        raise ValueError("Choose an absolute profile file path.")
    _reject_links(path)
    profile = _profile_value(read_json(path))
    target = data_root / "profiles" / (hashlib.sha256(profile["name"].encode("utf-8")).hexdigest() + ".json")
    _reject_links(target)
    if target.exists():
        if load_profile(target, data_root) == profile:
            return target
        raise ValueError("A different profile with this name already exists. Rename it before importing.")
    atomic_write(target, json_bytes(profile), replace_existing=False)
    return target


def load_game_folder(data_root: Path = DATA_ROOT) -> Path | None:
    """Read an explicit selection; never discover, select, or write on load."""
    path = data_root / SETTINGS_FILENAME
    _reject_links(path)
    if not path.exists():
        return None
    value = read_json(path)
    if (not isinstance(value, dict) or set(value) != {"schema_version", "game_folder"}
            or type(value["schema_version"]) is not int or value["schema_version"] != 1
            or not isinstance(value["game_folder"], str)):
        raise ValueError("Unsupported launcher settings.")
    return installations.validate_game_identity(value["game_folder"])


def save_game_folder(game_root: str | Path | None, data_root: Path = DATA_ROOT) -> Path:
    """Persist only a user-selected, verified Steam folder; runtime is optional."""
    game = installations.validate_game_identity(game_root)
    atomic_write(data_root / SETTINGS_FILENAME, json_bytes({"schema_version": 1, "game_folder": str(game)}))
    return game


def list_profiles(data_root: Path = DATA_ROOT) -> tuple[list[tuple[str, Path]], list[str]]:
    valid, errors = [], []
    for path in sorted((data_root / "profiles").glob("*.json")):
        try:
            profile = load_profile(path, data_root)
            valid.append((profile["name"], path))
        except (ValueError, OSError) as error:
            errors.append(f"{path.name}: {error}")
    return sorted(valid, key=lambda entry: entry[0].casefold()), errors


def require_game_closed() -> None:
    if os.name != "nt":
        raise ValueError("Applying a seed requires the verified Windows installation.")
    result = subprocess.run(
        ["tasklist.exe", "/FI", "IMAGENAME eq FINAL FANTASY V.exe", "/FO", "CSV", "/NH"],
        capture_output=True, text=True, check=True, timeout=15,
        creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
    )
    if any(row and row[0].casefold() == "final fantasy v.exe"
           for row in csv.reader(io.StringIO(result.stdout))):
        raise ValueError("Close Final Fantasy V before applying a seed.")


def validate_game_root(game_root: str | Path | None = None) -> Path:
    game_root = installations.validate_game_identity(game_root)
    _reject_links(game_root / TARGET_RELATIVE)
    runtime = game_root / "BepInEx/plugins/JobFair/JobFair.Runtime.dll"
    if not runtime.is_file():
        raise ValueError("The Job Fair runtime must be installed before applying a seed.")
    if hashlib.sha256(runtime.read_bytes()).hexdigest() != COMPATIBLE_RUNTIME_SHA256:
        raise ValueError("The installed Job Fair runtime is incompatible with this launcher. Install the reviewed launcher-compatible runtime before applying a seed.")
    return game_root / TARGET_RELATIVE


def boss_spoiler(request: dict) -> list[dict]:
    """Describe the saved boss table without generating another assignment."""
    request = validate_request(request)
    rows = [row for row in request.get("placements", []) if row["reward_kind"] == "boss"]
    if not rows:
        return []
    from ffv_randomizer import bosses
    catalog = {row["boss_id"]: row for row in bosses.read_catalog()["bosses"]}
    result = []
    for row in sorted(rows, key=lambda row: row["location_id"]):
        destination = catalog[row["location_id"].removeprefix(bosses.PREFIX)]
        source = catalog[row["reward_id"]]
        result.append({"location_id": row["location_id"], "location": destination["name"] + " encounter",
                       "world": destination["world"], "tier": destination["tier"],
                       "boss_id": source["boss_id"], "boss": source["name"], "source_world": source["world"],
                       "scaling": request["settings"]["boss_scaling"]})
    return result


def apply_next_run(seed_text: str, game_root: str | Path | None = None, data_root: Path = DATA_ROOT, mode: str | None = None,
                   *, stored_request: dict | None = None) -> dict:
    """Only the explicit UI Apply action calls this; no game process is started."""
    request = selected_request(seed_text, mode, stored_request)
    require_game_closed()
    target = validate_game_root(game_root)
    payload = json_bytes(request)
    def recheck_target():
        require_game_closed()
        if validate_game_root(game_root) != target:
            raise ValueError("The selected FFV installation changed before applying.")
    atomic_write(target, payload, before_commit=recheck_target)
    record = {"applied_utc": datetime.now(timezone.utc).isoformat(), "target": str(target),
              "sha256": hashlib.sha256(payload).hexdigest(), "request": request}
    # A local receipt failure must not falsely report that the verified game write failed.
    try:
        atomic_write(data_root / "last_apply.json", json_bytes(record))
        if request["schema_version"] in (2, 3):
            mode = request["mode"]
            scope = {campaign.MIXED: "21 native job slots",
                     campaign.REMIXED: "Four unlocked starting jobs; 17 hidden job bonuses (6 / 6 / 5); two independent world keys",
                     campaign.MIXED_FIESTA: "Four distinct jobs unlocked at Wind Shrine; Freelancer always available; no later jobs",
                     campaign.REMIXED_FIESTA: "Four distinct jobs unlocked at the start; Freelancer always available; no later jobs; two independent world keys"}[mode]
            scope += ("; 15 timed Karnak chests retain native rewards; treasures and event rewards shuffled by world; "
                      "shops shuffled by world and type at normal item prices")
            scope += ("; 49 bosses shuffled across worlds " +
                      ("within Simple/Complex tiers" if campaign.base_mode(mode) == campaign.MIXED else "in one pool") +
                      "; boss stats and EXP scaled to the destination encounter; occupied enemy drop and steal slots shuffled")
            spoiler = {"request": request, "scope": scope,
                       "jobs": [{"location": p["location_id"], "job": JOB_NAMES[int(p["reward_id"])]}
                                for p in request["placements"] if p["reward_kind"] == "job"],
                       "keys": [{"location": p["location_id"], "key": p["reward_id"]}
                                for p in request["placements"] if p["reward_kind"] == "key"],
                       "bosses": boss_spoiler(request)}
            atomic_write(data_root / "runs" / (request["placement_hash"] + ".json"), json_bytes(spoiler))
    except (OSError, ValueError) as error:
        record["receipt_warning"] = str(error)
    return record


def current_request(game_root: str | Path | None = None) -> dict | None:
    """Read only: retain the selected seed/mode and prepare current placements."""
    if game_root is None:
        return None
    path = installations.validate_game_identity(game_root) / TARGET_RELATIVE
    if not path.exists(): return None
    return current_rules_request(read_json(path))
