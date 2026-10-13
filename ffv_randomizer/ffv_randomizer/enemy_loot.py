"""Frozen contents for occupied native regular-enemy loot slots.

Native probabilities, quantity fields, empty slots, enemy identities and combat
data are not randomized. Drop and steal multisets remain separate.
"""
from __future__ import annotations

from collections import Counter
import hashlib

from . import resources
from .runtime_seed import canonical_seed

SCOPE = "native-occupied-slots-v1"
CATALOG_ID = "ff5-enemy-loot-v1"
CATALOG_FILE = "enemy_loot_catalog.json"
PREFIX = "native_loot/"
FIELDS = {"location_id", "reward_kind", "reward_id", "quantity"}


def validate_catalog(catalog: dict) -> dict:
    if (not isinstance(catalog, dict) or catalog.get("schema_version") != 1
            or catalog.get("catalog_id") != CATALOG_ID or catalog.get("scope") != SCOPE
            or catalog.get("steam_build") != "20265087"
            or catalog.get("policy") != "shuffle-existing-contents-separately-by-drop-or-steal"
            or catalog.get("critical_content_ids") != [33, 34, 35, 36, 37]):
        raise ValueError("Unsupported regular-enemy loot catalog.")
    monsters = {}
    expected = {}
    for monster in catalog.get("monsters", []):
        ident = monster.get("monster_id")
        if type(ident) is not int or ident <= 0 or ident in monsters:
            raise ValueError("Invalid or duplicate loot monster.")
        drops, steals, rate = monster.get("drops"), monster.get("steals"), monster.get("drop_rate")
        if (type(rate) is not int or not 0 <= rate <= 100 or not isinstance(drops, list) or len(drops) != 8
                or not isinstance(steals, list) or len(steals) != 4):
            raise ValueError("Invalid native loot slot shape.")
        monsters[ident] = monster
        for kind, values in (("drop", drops), ("steal", [[x, 1] for x in steals])):
            for index, pair in enumerate(values, 1):
                if (not isinstance(pair, list) or len(pair) != 2 or any(type(x) is not int for x in pair)
                        or pair[0] < 0 or not 0 <= pair[1] <= 99 or (pair[0] and pair[1] == 0)):
                    raise ValueError("Invalid native loot contents or quantities.")
                if pair[0]:
                    if pair[0] in catalog["critical_content_ids"]:
                        raise ValueError("Critical content cannot enter regular loot.")
                    location = f"{PREFIX}{ident}/{kind}/{index}"
                    expected[location] = {"location_id": location, "monster_id": ident,
                                          "kind": kind, "slot": index, "native_content_id": pair[0]}
    slots = catalog.get("slots")
    if (not monsters or not isinstance(slots, list) or slots != sorted(expected.values(), key=lambda r: r["location_id"])):
        raise ValueError("Loot catalog must contain exactly its occupied native slots.")
    return catalog


def read_catalog() -> dict:
    return validate_catalog(resources.read_catalog(CATALOG_FILE))


def generate(seed: int, catalog: dict | None = None) -> list[dict]:
    catalog = read_catalog() if catalog is None else validate_catalog(catalog)
    seed_text = canonical_seed(seed)
    placements = []
    for kind in ("drop", "steal"):
        rows = [row for row in catalog["slots"] if row["kind"] == kind]
        # Rank each source occurrence, not only distinct values, so duplicates
        # preserve their original counts and results do not depend on RNG state.
        ranked = sorted(rows, key=lambda row: hashlib.sha256(
            f"JobFair|enemy-loot|1|{seed_text}|{kind}|{row['location_id']}".encode("utf-8")).digest())
        placements.extend({"location_id": destination["location_id"], "reward_kind": "loot",
                           "reward_id": str(source["native_content_id"]), "quantity": 1}
                          for destination, source in zip(rows, ranked, strict=True))
    result = sorted(placements, key=lambda row: row["location_id"])
    validate_placements(result, catalog)
    return result


def validate_placements(placements: object, catalog: dict | None = None) -> None:
    catalog = read_catalog() if catalog is None else validate_catalog(catalog)
    slots = {row["location_id"]: row for row in catalog["slots"]}
    if not isinstance(placements, list) or len(placements) != len(slots):
        raise ValueError("Loot assignments must cover every occupied native slot.")
    seen, actual = set(), {"drop": Counter(), "steal": Counter()}
    expected = {kind: Counter(row["native_content_id"] for row in slots.values() if row["kind"] == kind)
                for kind in actual}
    for row in placements:
        if not isinstance(row, dict) or set(row) != FIELDS:
            raise ValueError("Invalid loot placement fields.")
        location, reward = row["location_id"], row["reward_id"]
        if (not isinstance(location, str) or location not in slots or location in seen
                or row["reward_kind"] != "loot" or type(row["quantity"]) is not int or row["quantity"] != 1
                or not isinstance(reward, str) or not reward.isascii() or not reward.isdigit()
                or str(int(reward)) != reward or int(reward) <= 0):
            raise ValueError("Invalid, unknown or duplicate native loot assignment.")
        seen.add(location)
        actual[slots[location]["kind"]][int(reward)] += 1
    if actual != expected:
        raise ValueError("Loot assignments must preserve native drop and steal pools independently.")
