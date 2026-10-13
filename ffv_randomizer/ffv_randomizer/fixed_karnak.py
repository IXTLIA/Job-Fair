"""New-run treasure policy: retain all fifteen missable Karnak chest rewards.

Keep their native rows in the saved table so old catalog bindings remain usable.
The rows and their rewards are excluded from the shuffle, not from save ownership.
"""
from __future__ import annotations

import random

from . import world_treasure
from .runtime_seed import canonical_seed

LOCATION_IDS = frozenset(f"native_treasure_{flag:03d}" for flag in range(86, 101))
TREASURE_SCOPE = "world-pools-karnak-fixed-v1"


def validate_locations(catalog: dict) -> None:
    world_treasure.validate_catalog(catalog)
    castle = {row["id"] for row in catalog["locations"]
              if any(binding["source"]["map_name"].startswith("Map_20071_")
                     for binding in row["bindings"])}
    if castle != LOCATION_IDS:
        raise ValueError("Native Karnak Castle location set changed.")
    for row in catalog["locations"]:
        if row["id"] in LOCATION_IDS and (row["world"] != 1 or len(row["native_reward_variants"]) != 1):
            raise ValueError("Unsupported Karnak reward world or variant.")


def validate_placements(catalog: dict, placements: list[dict]) -> None:
    validate_locations(catalog)
    world_treasure.validate_placements(catalog, placements)
    actual = {row["location_id"]: row for row in placements}
    for location in catalog["locations"]:
        if location["id"] not in LOCATION_IDS:
            continue
        row = actual[location["id"]]
        if (row["reward_kind"], row["reward_id"], row["quantity"]) != world_treasure.reward_tuple(location["native_reward"]):
            raise ValueError("Timed Karnak Castle chests must retain native rewards.")


def generate(seed: int, catalog: dict) -> list[dict]:
    validate_locations(catalog)
    # Worlds 2/3 keep their established independent seeded results.
    result = {row["location_id"]: row for row in world_treasure.generate(seed, catalog)}
    locations = sorted((row for row in catalog["locations"] if row["world"] == 1
                        and row["id"] not in LOCATION_IDS), key=lambda row: row["id"])
    rewards = [world_treasure.reward_tuple(row["native_reward"]) for row in locations]
    random.Random(f"JobFair|world-treasure|karnak-fixed-v1|{canonical_seed(seed)}|world|1").shuffle(rewards)
    pairs = list(zip(locations, rewards, strict=True))
    pairs.extend((row, world_treasure.reward_tuple(row["native_reward"]))
                 for row in catalog["locations"] if row["id"] in LOCATION_IDS)
    for location, reward in pairs:
        result[location["id"]] = {"location_id": location["id"], "reward_kind": reward[0],
                                  "reward_id": reward[1], "quantity": reward[2]}
    placements = sorted(result.values(), key=lambda row: row["location_id"])
    validate_placements(catalog, placements)
    return placements
