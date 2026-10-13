"""Mode-neutral finite shop-stock shuffling with exact per-world/type pools.

Repeated products at different merchants remain separate source stock entries.
The flow constraint keeps every merchant's stock distinct without dropping any
source entry. Native destination product IDs, purchase limits and prices belong
to the pinned catalog; saved placements contain the assigned content only.
"""
from __future__ import annotations

from collections import Counter, defaultdict, deque
import json
from pathlib import Path
import random

from .runtime_seed import canonical_seed
from . import resources

ROOT = Path(__file__).resolve().parents[2]
CATALOG_PATH = resources.DATA_ROOT / "shop_catalog.json"
ALGORITHM = "JobFair|world-shop-stock|1"
PLACEMENT_FIELDS = {"location_id", "reward_kind", "reward_id", "quantity"}


def _catalog(catalog: object) -> tuple[dict, dict, dict]:
    if not isinstance(catalog, dict) or not isinstance(catalog.get("shops"), list) or not isinstance(catalog.get("slots"), list):
        raise ValueError("Shop catalog requires shops and slots arrays.")
    merchants, slots, by_shop = {}, {}, defaultdict(list)
    for shop in catalog["shops"]:
        if not isinstance(shop, dict):
            raise ValueError("Invalid native shop record.")
        ident, world = shop.get("id"), shop.get("world")
        kind = shop.get("shop_type", shop.get("type"))
        if (not isinstance(ident, str) or not ident or ident in merchants
                or type(world) is not int or world not in (1, 2, 3)
                or not isinstance(kind, str) or not kind):
            raise ValueError("Invalid or duplicate native shop identity/world/type.")
        if "type" in shop and shop["type"] != kind:
            raise ValueError("Native shop type aliases disagree.")
        merchants[ident] = (world, kind, shop)
    for slot in catalog["slots"]:
        if not isinstance(slot, dict):
            raise ValueError("Invalid native shop slot.")
        ident, shop_id, content = slot.get("id"), slot.get("shop_id"), slot.get("content_id")
        if (not isinstance(ident, str) or not ident or ident in slots
                or not isinstance(shop_id, str) or shop_id not in merchants
                or type(content) is not int or not 1 < content <= 2147483647):
            raise ValueError("Invalid or duplicate native stock slot/content.")
        world, kind, _ = merchants[shop_id]
        if (type(slot.get("world")) is not int or slot["world"] != world
                or slot.get("shop_type") != kind):
            raise ValueError("Native stock slot belongs to another world or shop type.")
        slots[ident] = slot
        by_shop[shop_id].append(ident)
    if not merchants or not slots:
        raise ValueError("Native shop catalog cannot be empty.")
    for ident, (_, _, shop) in merchants.items():
        if not by_shop[ident]:
            raise ValueError("A native shop has no stock slots.")
        if "slot_ids" in shop:
            ids = shop["slot_ids"]
            if (not isinstance(ids, list) or any(not isinstance(x, str) for x in ids)
                    or len(set(ids)) != len(ids) or set(ids) != set(by_shop[ident])):
                raise ValueError("Native shop slot index is incomplete or inconsistent.")
    return merchants, slots, by_shop


def read_catalog() -> dict:
    catalog = resources.read_catalog("shop_catalog.json")
    _catalog(catalog)
    return catalog


def _allocate(counts: Counter, capacities: dict[str, int], rng: random.Random) -> dict[str, list[int]]:
    """Randomized edge ordering, then finite integral max-flow (no retries).

    Source -> content has native multiplicity, content -> merchant capacity one,
    merchant -> sink has the exact original stock size. Residual edges allow
    earlier choices to move whenever necessary to complete the allocation.
    """
    contents, merchant_ids = sorted(counts), sorted(capacities)
    rng.shuffle(contents)
    rng.shuffle(merchant_ids)
    item_nodes = {content: index + 1 for index, content in enumerate(contents)}
    shop_nodes = {shop: len(contents) + index + 1 for index, shop in enumerate(merchant_ids)}
    sink = len(contents) + len(merchant_ids) + 1
    graph = [[] for _ in range(sink + 1)]

    def edge(start, end, capacity):
        forward = [end, len(graph[end]), capacity]
        backward = [start, len(graph[start]), 0]
        graph[start].append(forward)
        graph[end].append(backward)
        return forward

    for content in contents:
        edge(0, item_nodes[content], counts[content])
    assignments = []
    for content in contents:
        destinations = merchant_ids[:]
        rng.shuffle(destinations)
        for shop in destinations:
            assignments.append((content, shop, edge(item_nodes[content], shop_nodes[shop], 1)))
    for shop in merchant_ids:
        edge(shop_nodes[shop], sink, capacities[shop])
    required = sum(capacities.values())
    if required != sum(counts.values()):
        raise ValueError("Shop stock pool and destination capacity differ.")
    total = 0
    while total < required:
        previous = [None] * len(graph)
        previous[0] = (-1, -1)
        queue = deque([0])
        while queue and previous[sink] is None:
            start = queue.popleft()
            for index, (end, _, capacity) in enumerate(graph[start]):
                if capacity and previous[end] is None:
                    previous[end] = (start, index)
                    queue.append(end)
                    if end == sink:
                        break
        if previous[sink] is None:
            raise ValueError("Native stock pool cannot fill every shop without duplicate contents.")
        # Every augmenting path crosses a content->merchant capacity-one edge.
        end = sink
        while end != 0:
            start, index = previous[end]
            forward = graph[start][index]
            forward[2] -= 1
            graph[end][forward[1]][2] += 1
            end = start
        total += 1
    result = {shop: [] for shop in merchant_ids}
    for content, shop, forward in assignments:
        if forward[2] == 0:
            result[shop].append(content)
    return result


def generate(seed: int, catalog: dict | None = None) -> list[dict]:
    seed_text = canonical_seed(seed)
    catalog = read_catalog() if catalog is None else catalog
    merchants, slots, by_shop = _catalog(catalog)
    pools = defaultdict(list)
    for shop_id, (world, kind, _) in merchants.items():
        pools[(world, kind)].append(shop_id)
    result = []
    for (world, kind), ids in sorted(pools.items()):
        rng = random.Random(f"{ALGORITHM}|{seed_text}|world|{world}|type|{kind}")
        counts = Counter(slots[ident]["content_id"] for shop in ids for ident in by_shop[shop])
        allocation = _allocate(counts, {shop: len(by_shop[shop]) for shop in ids}, rng)
        for shop in sorted(ids):
            contents = sorted(allocation[shop])
            rng.shuffle(contents)
            for ident, content in zip(sorted(by_shop[shop]), contents, strict=True):
                result.append({"location_id": ident, "reward_kind": "shop",
                               "reward_id": str(content), "quantity": 1})
    result.sort(key=lambda p: p["location_id"])
    validate_placements(catalog, result)
    return result


def validate_placements(catalog: dict, placements: object) -> None:
    merchants, slots, _ = _catalog(catalog)
    if not isinstance(placements, list):
        raise ValueError("Shop placements must be an array.")
    seen, by_shop, actual = set(), defaultdict(set), defaultdict(Counter)
    for row in placements:
        if not isinstance(row, dict) or set(row) != PLACEMENT_FIELDS:
            raise ValueError("Invalid shop placement fields.")
        ident, content = row["location_id"], row["reward_id"]
        if not isinstance(ident, str) or ident not in slots or ident in seen:
            raise ValueError("Unknown or duplicate shop stock slot.")
        if (row["reward_kind"] != "shop" or not isinstance(row["reward_kind"], str)
                or not isinstance(content, str) or not content.isascii() or not content.isdecimal()
                or len(content) > 10 or str(int(content)) != content or not 1 < int(content) <= 2147483647
                or type(row["quantity"]) is not int or row["quantity"] != 1):
            raise ValueError("Shop stock requires one canonical content ID.")
        seen.add(ident)
        slot = slots[ident]
        shop_id = slot["shop_id"]
        if content in by_shop[shop_id]:
            raise ValueError("One shop cannot contain duplicate stock contents.")
        by_shop[shop_id].add(content)
        world, kind, _ = merchants[shop_id]
        actual[(world, kind)][int(content)] += 1
    if seen != set(slots):
        raise ValueError("Shop stock placement table is incomplete.")
    expected = defaultdict(Counter)
    for slot in slots.values():
        world, kind, _ = merchants[slot["shop_id"]]
        expected[(world, kind)][slot["content_id"]] += 1
    if dict(actual) != dict(expected):
        raise ValueError("A world/shop-type stock pool changed.")
