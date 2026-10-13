"""Finite quest/event grants and ordinary treasures share original world pools.

Only the versioned expanded catalog calls this module. Legacy tables stay exact,
and the fifteen timed Karnak rewards keep their original saved representation.
"""
from __future__ import annotations
from collections import Counter
import random
from . import fixed_karnak, resources
from .runtime_seed import canonical_seed

TREASURE_SCOPE = "world-event-pools-v1"
POOL_ALGORITHM = "JobFair|world-event-rewards|1"


def _catalogs() -> tuple[dict, dict]:
    world = resources.read_catalog("world_catalog.json")
    event = resources.read_catalog("event_reward_catalog.json")
    fixed_karnak.validate_locations(world)
    if event.get("catalog_id") != TREASURE_SCOPE or event.get("schema_version") != 1:
        raise ValueError("Unsupported event reward catalog.")
    locations = world["locations"] + event["locations"]
    ids = [row["id"] for row in locations]
    if len(ids) != 295 or len(ids) != len(set(ids)):
        raise ValueError("Expanded event reward location set changed.")
    if any(row["world"] not in (1, 2, 3) for row in locations):
        raise ValueError("An event reward has no world ownership.")
    return world, event


def _native(location: dict, event: dict) -> tuple[str, str, int]:
    native = location["native_reward"]
    kind, content, quantity = native["kind"], native["content_id"], native["quantity"]
    if type(content) is not int or type(quantity) is not int:
        raise ValueError("Invalid native event reward.")
    if location["id"] not in fixed_karnak.LOCATION_IDS and content in event["magic_content_ids"]:
        kind = "magic"
    if kind in ("item", "magic") and content > 1 and 1 <= quantity <= 99:
        if kind == "magic" and quantity != 1:
            raise ValueError("Magic grants require quantity one.")
        return kind, str(content), quantity
    if kind == "gil" and content == 1 and 1 <= quantity <= 9999999:
        return kind, "", quantity
    raise ValueError("Unsupported event reward.")


def generate(seed: int, ordinary_placements: list[dict]) -> list[dict]:
    """Return all 260 ordinary and 35 event rows using original combined pools."""
    world, event = _catalogs()
    fixed_karnak.validate_placements(world, ordinary_placements)
    locations = world["locations"] + event["locations"]
    result = []
    for number in (1, 2, 3):
        eligible = sorted((row for row in locations if row["world"] == number
                           and row["id"] not in fixed_karnak.LOCATION_IDS), key=lambda row: row["id"])
        rewards = [_native(row, event) for row in eligible]
        random.Random(f"{POOL_ALGORITHM}|{canonical_seed(seed)}|world|{number}").shuffle(rewards)
        result.extend({"location_id":row["id"], "reward_kind":reward[0],
                       "reward_id":reward[1], "quantity":reward[2]}
                      for row, reward in zip(eligible, rewards, strict=True))
    result.extend(dict(row) for row in ordinary_placements if row["location_id"] in fixed_karnak.LOCATION_IDS)
    result.sort(key=lambda row: row["location_id"])
    validate_placements(result)
    return result


def validate_placements(rows: list[dict]) -> None:
    world, event = _catalogs()
    locations = {row["id"]:row for row in world["locations"] + event["locations"]}
    seen = set()
    actual = {number:Counter() for number in (1, 2, 3)}
    for row in rows:
        if not isinstance(row, dict) or set(row) != {"location_id", "reward_kind", "reward_id", "quantity"}:
            raise ValueError("Invalid event reward placement fields.")
        id_ = row["location_id"]
        if not isinstance(id_, str) or id_ not in locations or id_ in seen:
            raise ValueError("Unknown or duplicate event reward location.")
        seen.add(id_)
        if type(row["quantity"]) is not int or not isinstance(row["reward_kind"], str) or not isinstance(row["reward_id"], str):
            raise ValueError("Invalid event reward representation.")
        reward = (row["reward_kind"],row["reward_id"],row["quantity"])
        if id_ in fixed_karnak.LOCATION_IDS and reward != _native(locations[id_], event):
            raise ValueError("Timed Karnak rewards must retain their native rows.")
        actual[locations[id_]["world"]][reward] += 1
    if seen != set(locations):
        raise ValueError("Expanded event reward placement table is incomplete.")
    for number in (1, 2, 3):
        expected = Counter(_native(row, event) for row in locations.values() if row["world"] == number)
        if actual[number] != expected:
            raise ValueError(f"World {number} expanded reward multiset changed.")
