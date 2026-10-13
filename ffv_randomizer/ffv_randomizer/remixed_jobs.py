"""Draft Remixed job bonuses; ordinary rewards and world access stay separate.

The existing solver allocates each world's fixed job quota. Empty requirements
prove allocation only: they do not certify native travel, event or combat access.
"""
from __future__ import annotations

from collections import Counter
import hashlib
import json
from pathlib import Path
import re

from .logic import generate_seed
from . import resources
from .runtime_seed import canonical_seed, select_starting_jobs

ROOT = Path(__file__).resolve().parents[2]
CATALOG_ID = "remixed-job-crystals-slice-v1"
CATALOG_PATH = resources.DATA_ROOT / "remixed_jobs_catalog.json"
QUOTAS = {1: 6, 2: 6, 3: 5}
PREFIX = "remixed_job/"
ALGORITHM = "JobFair|remixed-job-bonuses|1"


def validate_catalog(catalog: object) -> dict:
    if not isinstance(catalog, dict) or catalog.get("catalog_id") != CATALOG_ID:
        raise ValueError("Unsupported draft Remixed job catalog.")
    quotas = catalog.get("quotas")
    if (not isinstance(quotas, dict) or quotas != {str(world): count for world, count in QUOTAS.items()}
            or any(type(count) is not int for count in quotas.values())):
        raise ValueError("Remixed job quotas must be six, six and five.")
    candidates = catalog.get("bonus_candidates")
    if not isinstance(candidates, list):
        raise ValueError("Missing native job-bonus candidates.")
    seen, worlds = set(), Counter()
    for row in candidates:
        if not isinstance(row, dict):
            raise ValueError("Invalid job-bonus candidate.")
        location, ordinary, world = row.get("id"), row.get("ordinary_id"), row.get("world")
        if (not isinstance(ordinary, str) or not re.fullmatch(r"native_treasure_[0-9]{3}", ordinary)
                or location != PREFIX + ordinary or location in seen
                or type(world) is not int or world not in QUOTAS):
            raise ValueError("Invalid or duplicate native job-bonus binding.")
        seen.add(location)
        worlds[world] += 1
    if any(worlds[world] < count for world, count in QUOTAS.items()):
        raise ValueError("Insufficient persistent containers for a world's job quota.")
    return catalog


def read_catalog() -> dict:
    return validate_catalog(resources.read_catalog("remixed_jobs_catalog.json"))


def _domain(seed: int, suffix: str) -> bytes:
    return hashlib.sha256(f"{ALGORITHM}|{canonical_seed(seed)}|{suffix}".encode("utf-8")).digest()


def generate(seed: int, catalog: dict | None = None) -> dict:
    canonical_seed(seed)
    catalog = read_catalog() if catalog is None else validate_catalog(catalog)
    starters = select_starting_jobs(seed, range(2, 23))
    remaining = sorted((job for job in range(2, 23) if job not in starters),
                       key=lambda job: (_domain(seed, f"world-pool|job|{job}"), job))
    placements, offset = [], 0
    for world, count in QUOTAS.items():
        jobs = remaining[offset:offset + count]
        offset += count
        model = {"format_version": 1, "starting_inventory": [], "progression_items": [],
                 "goal_requires": [], "jobs": [f"job_{job:02d}" for job in jobs],
                 "checks": [{"id": row["id"], "requires": [], "region": f"world_{world}_job_slice"}
                            for row in catalog["bonus_candidates"] if row["world"] == world]}
        record = generate_seed(model, int.from_bytes(_domain(seed, f"locations|world|{world}"), "big"))
        for row in record["placements"]:
            if row["reward"]["kind"] == "job":
                placements.append({"location_id": row["check_id"], "reward_kind": "job",
                                   "reward_id": str(int(row["reward"]["id"][4:])), "quantity": 1})
    placements.sort(key=lambda row: row["location_id"])
    validate_placements(catalog, starters, placements)
    return {"starting_job_ids": starters, "placements": placements}


def validate_starters(starters: object) -> None:
    if (not isinstance(starters, list) or len(starters) != 4
            or any(type(job) is not int or not 2 <= job <= 22 for job in starters)
            or len(set(starters)) != 4):
        raise ValueError("Four distinct unlockable starting jobs are required.")


def validate_placements(catalog: dict, starters: object, placements: object) -> None:
    validate_catalog(catalog)
    validate_starters(starters)
    if not isinstance(placements, list) or len(placements) != 17:
        raise ValueError("Exactly seventeen hidden job bonuses are required.")
    candidates = {row["id"]: row for row in catalog["bonus_candidates"]}
    seen, jobs, worlds = set(), set(starters), Counter()
    for row in placements:
        if not isinstance(row, dict) or set(row) != {"location_id", "reward_kind", "reward_id", "quantity"}:
            raise ValueError("Invalid job-bonus placement fields.")
        location, job = row["location_id"], row["reward_id"]
        if (not isinstance(location, str) or location not in candidates or location in seen
                or row["reward_kind"] != "job" or type(row["quantity"]) is not int or row["quantity"] != 1
                or not isinstance(job, str) or job not in {str(value) for value in range(2, 23)}):
            raise ValueError("Invalid or duplicate native job-bonus placement.")
        if int(job) in jobs:
            raise ValueError("Hidden job duplicates another bonus or starting job.")
        seen.add(location)
        jobs.add(int(job))
        worlds[candidates[location]["world"]] += 1
    if jobs != set(range(2, 23)) or worlds != QUOTAS:
        raise ValueError("Remixed job pool or six/six/five world quotas changed.")
