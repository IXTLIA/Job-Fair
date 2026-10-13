"""Small, audited launcher data; independent of extracted game assets.

PyInstaller keeps this package-relative layout, as does the curated source
export. Missing or modified files fail closed instead of consulting local game
or developer research folders.
"""
from __future__ import annotations

import hashlib
import json
from pathlib import Path

from .logic import generate_seed, validate_model

DATA_ROOT = Path(__file__).resolve().parent / "data"
CATALOG_FILES = (
    "mixed_catalog.json", "world_catalog.json", "shop_catalog.json",
    "remixed_jobs_catalog.json", "world_travel_catalog.json", "native_job_model.json",
    "boss_catalog.json", "enemy_loot_catalog.json", "event_reward_catalog.json",
)
MANIFEST_FILE = "catalog_manifest.json"


def read_catalog(name: str, *, directory: Path | None = None) -> dict:
    if name not in CATALOG_FILES:
        raise ValueError("Unknown launcher catalog.")
    directory = DATA_ROOT if directory is None else directory
    manifest = json.loads((directory / MANIFEST_FILE).read_bytes())
    if (not isinstance(manifest, dict) or manifest.get("format_version") != 1
            or not isinstance(manifest.get("sha256"), dict)
            or set(manifest["sha256"]) != set(CATALOG_FILES)):
        raise ValueError("Invalid launcher catalog manifest.")
    raw = (directory / name).read_bytes()
    if hashlib.sha256(raw).hexdigest() != manifest["sha256"][name]:
        raise ValueError(f"Launcher catalog hash mismatch: {name}")
    value = json.loads(raw)
    if not isinstance(value, dict):
        raise ValueError(f"Invalid launcher catalog: {name}")
    return value


def verify_all(directory: Path | None = None) -> dict[str, str]:
    directory = DATA_ROOT if directory is None else directory
    for name in CATALOG_FILES:
        read_catalog(name, directory=directory)
    return json.loads((directory / MANIFEST_FILE).read_bytes())["sha256"]


def native_job_placements(seed: int) -> list[dict]:
    model = read_catalog("native_job_model.json")
    expected = {"format_version": 1, "starting_inventory": [], "goal_requires": [],
                "progression_items": [], "jobs": [f"job_{job:02d}" for job in range(2, 23)],
                "checks": [{"id": f"native_job_slot_{job:02d}", "requires": [],
                            "region": "native_reward_probe"} for job in range(2, 23)]}
    if validate_model(model) != expected:
        raise ValueError("The audited native job solver model changed.")
    record = generate_seed(model, seed)
    return [{"location_id": row["check_id"], "reward_kind": "job",
             "reward_id": str(int(row["reward"]["id"].removeprefix("job_"))), "quantity": 1}
            for row in record["placements"]]
