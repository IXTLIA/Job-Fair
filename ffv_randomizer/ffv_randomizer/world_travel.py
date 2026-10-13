"""Isolated world-key allocation and proof; native travel remains unverified.

Keys are additional bonuses. Existing job, ordinary and shop placements remain
frozen. Reachability proves only the supplied world-key graph, not native access.
"""
from __future__ import annotations

import hashlib
import json
from pathlib import Path
import re

from . import remixed_jobs
from . import resources
from .logic import generate_seed, validate_model, _prove_placement
from .runtime_seed import canonical_seed

CATALOG_ID = "remixed-world-travel-slice-v1"
CATALOG_PATH = resources.DATA_ROOT / "world_travel_catalog.json"
PREFIX = "remixed_key/"
WORLD_2_KEY = "world_2_key"
WORLD_3_KEY = "world_3_key"
KEY_WORLDS = {WORLD_2_KEY: {1}, WORLD_3_KEY: {1, 2}}
ALGORITHM = "JobFair|world-key-bonuses|1"


def validate_catalog(catalog: object) -> dict:
    if not isinstance(catalog, dict) or catalog.get("catalog_id") != CATALOG_ID:
        raise ValueError("Unsupported isolated world-travel catalog.")
    keys = catalog.get("keys")
    if not isinstance(keys, list) or len(keys) != 2:
        raise ValueError("Exactly two world-key definitions are required.")
    seen = set()
    for key in keys:
        if not isinstance(key, dict) or not isinstance(key.get("id"), str) or key["id"] not in KEY_WORLDS:
            raise ValueError("Unknown world-key definition.")
        worlds = key.get("allowed_worlds")
        if (key["id"] in seen or not isinstance(worlds, list)
                or any(type(world) is not int for world in worlds)
                or len(worlds) != len(set(worlds)) or set(worlds) != KEY_WORLDS[key["id"]]):
            raise ValueError("World-key location restrictions changed.")
        seen.add(key["id"])
    candidates = catalog.get("key_candidates")
    if not isinstance(candidates, list) or not candidates:
        raise ValueError("Missing world-key container candidates.")
    seen = set()
    for row in candidates:
        if not isinstance(row, dict):
            raise ValueError("Invalid world-key candidate.")
        base, location, world = row.get("ordinary_id"), row.get("id"), row.get("world")
        if (not isinstance(base, str) or not re.fullmatch(r"native_treasure_[0-9]{3}", base)
                or location != PREFIX + base or base in seen or type(world) is not int or world not in (1, 2)):
            raise ValueError("Invalid or duplicate world-key container.")
        seen.add(base)
    return catalog


def read_catalog() -> dict:
    return validate_catalog(resources.read_catalog("world_travel_catalog.json"))


def available_worlds(keys: object) -> tuple[int, ...]:
    if (not isinstance(keys, (list, tuple)) or any(not isinstance(key, str) for key in keys)
            or len(keys) != len(set(keys)) or not set(keys).issubset(KEY_WORLDS)):
        raise ValueError("Invalid world-key possession.")
    return tuple(world for world, key in ((1, None), (2, WORLD_2_KEY), (3, WORLD_3_KEY))
                 if key is None or key in keys)


def validate_world_state(value: object, keys: object) -> dict:
    if (not isinstance(value, dict) or set(value) != {"version", "active_world"}
            or type(value["version"]) is not int or value["version"] != 1
            or type(value["active_world"]) is not int or value["active_world"] not in available_worlds(keys)):
        raise ValueError("Invalid saved world-travel state.")
    return value


def _requirements(world: int) -> list[str]:
    return [] if world == 1 else [WORLD_2_KEY if world == 2 else WORLD_3_KEY]


def _domain(seed: int, key: str) -> int:
    return int.from_bytes(hashlib.sha256(f"{ALGORITHM}|{canonical_seed(seed)}|{key}".encode()).digest(), "big")


def generate_keys(seed: int, starters: list[int], jobs: list[dict], catalog: dict | None = None) -> list[dict]:
    canonical_seed(seed)
    catalog = read_catalog() if catalog is None else validate_catalog(catalog)
    remixed_jobs.validate_placements(remixed_jobs.read_catalog(), starters, jobs)
    used = {row["location_id"].removeprefix(remixed_jobs.PREFIX) for row in jobs}
    placements = []
    for key, allowed in KEY_WORLDS.items():
        candidates = [row for row in catalog["key_candidates"] if row["ordinary_id"] not in used
                      and row["world"] in allowed]
        if not candidates:
            raise ValueError("No unused world-key candidate remains; existing jobs will not be rerolled.")
        model = {"format_version": 1, "starting_inventory": [] if key == WORLD_2_KEY else [WORLD_2_KEY],
                 "progression_items": [key], "goal_requires": [key], "jobs": [],
                 "checks": [{"id": row["id"], "requires": _requirements(row["world"]),
                             "region": f"world_{row['world']}_draft"} for row in candidates]}
        result = generate_seed(model, _domain(seed, key))
        chosen = next(row for row in result["placements"] if row["reward"]["kind"] == "key_item")
        placements.append({"location_id": chosen["check_id"], "reward_kind": "key", "reward_id": key, "quantity": 1})
        used.add(chosen["check_id"].removeprefix(PREFIX))
    placements.sort(key=lambda row: row["location_id"])
    validate_placements(catalog, starters, jobs, placements)
    return placements


def validate_placements(catalog: dict, starters: list[int], jobs: list[dict], placements: object) -> dict:
    """Validate saved tables and prove all19 bonuses within the declared world graph.

    This draft assumes access inside each world; it does not certify native
    entrance events, vehicles or paths to these containers.
    """
    validate_catalog(catalog)
    job_catalog = remixed_jobs.read_catalog()
    remixed_jobs.validate_placements(job_catalog, starters, jobs)
    return _validate_keys(catalog, job_catalog, jobs, placements)


def validate_fiesta_placements(catalog: dict, starters: list[int], placements: object) -> dict:
    """The same two permanent keys, with no hidden-job reservations or claims."""
    validate_catalog(catalog)
    remixed_jobs.validate_starters(starters)
    return _validate_keys(catalog, remixed_jobs.read_catalog(), [], placements)


def _validate_keys(catalog: dict, job_catalog: dict, jobs: list[dict], placements: object) -> dict:
    if not isinstance(placements, list) or len(placements) != 2:
        raise ValueError("Exactly two saved world-key bonuses are required.")
    candidates = {row["id"]: row for row in catalog["key_candidates"]}
    job_candidates = {row["id"]: row for row in job_catalog["bonus_candidates"]}
    used, keys, checks, rewards = set(), set(), [], []
    for row in jobs:
        candidate = job_candidates[row["location_id"]]
        base = candidate["ordinary_id"]
        used.add(base)
        checks.append({"id": base, "requires": _requirements(candidate["world"])})
        rewards.append({"check_id": base, "reward": {"kind": "job", "id": "job_" + row["reward_id"].zfill(2)}})
    for row in placements:
        if not isinstance(row, dict) or set(row) != {"location_id", "reward_kind", "reward_id", "quantity"}:
            raise ValueError("Invalid world-key placement fields.")
        location, key = row["location_id"], row["reward_id"]
        if (not isinstance(location, str) or location not in candidates
                or not isinstance(key, str) or key not in KEY_WORLDS or key in keys
                or row["reward_kind"] != "key" or type(row["quantity"]) is not int or row["quantity"] != 1):
            raise ValueError("Invalid or duplicate world-key bonus.")
        candidate = candidates[location]
        base = candidate["ordinary_id"]
        if base in used or candidate["world"] not in KEY_WORLDS[key]:
            raise ValueError("World keys must use permitted containers distinct from all other bonuses.")
        used.add(base); keys.add(key)
        checks.append({"id": base, "requires": _requirements(candidate["world"])})
        rewards.append({"check_id": base, "reward": {"kind": "key_item", "id": key}})
    model = validate_model({"format_version": 1, "starting_inventory": [], "progression_items": list(KEY_WORLDS),
                            "goal_requires": list(KEY_WORLDS), "checks": checks,
                            "jobs": ["job_" + row["reward_id"].zfill(2) for row in jobs]})
    return _prove_placement(model, rewards)
