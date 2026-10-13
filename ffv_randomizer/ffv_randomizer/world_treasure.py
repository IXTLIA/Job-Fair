"""World-wide ordinary reward pools, independent of the job placement domain.

The catalog contains logical, one-time locations. Map-state aliases are bindings
of a location, never extra rewards. Existing saved tables are never regenerated.
"""
from __future__ import annotations

from collections import Counter
import random

from .runtime_seed import canonical_seed

POOL_ALGORITHM = "JobFair|world-treasure|2"


def reward_tuple(reward: dict) -> tuple[str, str, int]:
    kind, content, quantity = reward["kind"], reward["content_id"], reward["quantity"]
    if type(content) is not int or type(quantity) is not int:
        raise ValueError("Native rewards require integer content and quantity.")
    if kind == "item" and content > 1 and 1 <= quantity <= 99:
        return kind, str(content), quantity
    if kind == "gil" and content == 1 and 1 <= quantity <= 9999999:
        return kind, "", quantity
    if kind == "empty" and content == 0 and quantity == 0:
        return kind, "", 0
    raise ValueError("Unsupported ordinary native reward.")


def validate_catalog(catalog: dict) -> dict:
    locations = catalog["locations"]
    ids, bindings = set(), set()
    for location in locations:
        if location["id"] in ids or location["world"] not in (1, 2, 3):
            raise ValueError("Duplicate location or missing world ownership.")
        ids.add(location["id"])
        reward_tuple(location["native_reward"])
        if not location["bindings"]:
            raise ValueError("A location needs at least one native binding.")
        for binding in location["bindings"]:
            if binding["id"] in bindings:
                raise ValueError("Native binding belongs to multiple locations.")
            bindings.add(binding["id"])
    if not ids or {row["world"] for row in locations} != {1, 2, 3}:
        raise ValueError("World treasure requires all three world pools.")
    return catalog


def generate(seed: int, catalog: dict) -> list[dict]:
    """Shuffle every reward in a world together, including gil and quantities."""
    validate_catalog(catalog)
    placements = []
    for world in (1, 2, 3):
        locations = sorted((row for row in catalog["locations"] if row["world"] == world),
                           key=lambda row: row["id"])
        rewards = [reward_tuple(row["native_reward"]) for row in locations]
        random.Random(f"{POOL_ALGORITHM}|{canonical_seed(seed)}|world|{world}").shuffle(rewards)
        placements.extend({"location_id": location["id"], "reward_kind": reward[0],
                           "reward_id": reward[1], "quantity": reward[2]}
                          for location, reward in zip(locations, rewards, strict=True))
    validate_placements(catalog, placements)
    return sorted(placements, key=lambda row: row["location_id"])


def validate_placements(catalog: dict, placements: list[dict]) -> None:
    validate_catalog(catalog)
    locations = {row["id"]: row for row in catalog["locations"]}
    seen, actual = set(), {world: Counter() for world in (1, 2, 3)}
    for row in placements:
        if not isinstance(row, dict) or set(row) != {"location_id", "reward_kind", "reward_id", "quantity"}:
            raise ValueError("Invalid ordinary placement fields.")
        location_id = row["location_id"]
        if location_id not in locations or location_id in seen:
            raise ValueError("Unknown or duplicate ordinary location.")
        seen.add(location_id)
        if (type(row["quantity"]) is not int or not isinstance(row["reward_kind"], str)
                or not isinstance(row["reward_id"], str)):
            raise ValueError("Ordinary rewards require string kind/ID and integer quantity.")
        actual[locations[location_id]["world"]][(row["reward_kind"], row["reward_id"], row["quantity"])] += 1
    if seen != set(locations):
        raise ValueError("Ordinary placement table is incomplete.")
    for world in (1, 2, 3):
        expected = Counter(reward_tuple(row["native_reward"]) for row in locations.values()
                           if row["world"] == world)
        if actual[world] != expected:
            raise ValueError(f"World {world} reward pool or quantities changed.")
