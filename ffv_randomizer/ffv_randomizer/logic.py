"""Deterministic, game-independent progression placement and seed auditing.

Requirements are monotone: every listed key is required, and acquiring a key
never consumes it.  The model must supply verified check bindings separately;
this module does not describe FFV's world or alter any game files.

This proof covers only the supplied AND-dependency model.  It cannot establish
event correctness, combat feasibility, traversal rules omitted from the model,
or live engine behavior.  Search is exact but can require exponential work for
large adversarial models.  Reproducibility assumes this implementation and the
same Python RNG behavior; record the runtime when distributing seed artifacts.
"""

from __future__ import annotations

import random
from collections import Counter
from typing import Any


def _identifier(value: Any, label: str) -> str:
    if not isinstance(value, str) or not value or value.strip() != value:
        raise ValueError(f"{label} must be a nonempty string without outer whitespace")
    return value


def _identifiers(value: Any, label: str) -> list[str]:
    if not isinstance(value, list):
        raise ValueError(f"{label} must be a list of identifiers")
    result = [_identifier(item, label) for item in value]
    duplicates = sorted(item for item, count in Counter(result).items() if count > 1)
    if duplicates:
        raise ValueError(f"Duplicate identities in {label}: {', '.join(duplicates)}")
    return sorted(result)


def validate_model(model: dict[str, Any]) -> dict[str, Any]:
    """Validate model structure and return a canonical copy without mutating it.

    Only starting inventory and progression items may appear in requirements.
    Jobs are rewards, not progression predicates.  Feasibility of allocating the
    progression pool is established by ``generate_seed``.
    """
    if not isinstance(model, dict):
        raise ValueError("Model must be a dictionary")
    if type(model.get("format_version")) is not int or model["format_version"] != 1:
        raise ValueError("Model format_version must be 1")
    inventory = _identifiers(model.get("starting_inventory"), "starting_inventory")
    progression = _identifiers(model.get("progression_items"), "progression_items")
    jobs = _identifiers(model.get("jobs", []), "jobs")
    goals = _identifiers(model.get("goal_requires"), "goal_requires")
    pools = inventory + progression + jobs
    duplicates = sorted(item for item, count in Counter(pools).items() if count > 1)
    if duplicates:
        raise ValueError(f"Duplicate identities across reward/inventory pools: {', '.join(duplicates)}")
    known_keys = set(inventory + progression)
    unknown_goals = sorted(set(goals) - known_keys)
    if unknown_goals:
        raise ValueError(f"Unknown required keys in goal_requires: {', '.join(unknown_goals)}")
    raw_checks = model.get("checks")
    if not isinstance(raw_checks, list):
        raise ValueError("checks must be a list")
    checks: list[dict[str, Any]] = []
    seen_checks: set[str] = set()
    for raw_check in raw_checks:
        if not isinstance(raw_check, dict):
            raise ValueError("Each check must be a dictionary")
        check_id = _identifier(raw_check.get("id"), "Check id")
        if check_id in seen_checks:
            raise ValueError(f"Duplicate check identity: {check_id}")
        seen_checks.add(check_id)
        requires = _identifiers(raw_check.get("requires"), f"Requirements for {check_id}")
        unknown = sorted(set(requires) - known_keys)
        if unknown:
            raise ValueError(f"Unknown required keys for {check_id}: {', '.join(unknown)}")
        check: dict[str, Any] = {"id": check_id, "requires": requires}
        if "region" in raw_check:
            check["region"] = _identifier(raw_check["region"], f"Region for {check_id}")
        checks.append(check)
    if len(checks) < len(progression) + len(jobs):
        raise ValueError(
            f"Insufficient checks: {len(checks)} checks for "
            f"{len(progression)} progression items and {len(jobs)} jobs"
        )
    return {
        "format_version": 1,
        "starting_inventory": inventory,
        "goal_requires": goals,
        "checks": sorted(checks, key=lambda check: check["id"]),
        "progression_items": progression,
        "jobs": jobs,
    }


def _progression_order(model: dict[str, Any], rng: random.Random) -> list[str]:
    """Find an acquisition order using capacity pruning and exact backtracking.

    A reachable check remains reachable forever.  Consequently the available
    slot count depends on the acquired key set and number of placed keys, not
    which reachable slots were used.  This lets the search memoize failed key
    sets instead of permutations of item/check assignments.
    """
    items = model["progression_items"]
    bits = {item: 1 << index for index, item in enumerate(items)}
    requirements = [
        sum(bits[key] for key in check["requires"] if key in bits)
        for check in model["checks"]
    ]
    all_keys = (1 << len(items)) - 1
    failed: set[int] = set()

    def visit(acquired: int) -> list[str] | None:
        remaining = all_keys ^ acquired
        if not remaining:
            return []
        if acquired in failed:
            return None
        reachable = sum(required & acquired == required for required in requirements)
        capacity = reachable - acquired.bit_count()
        if capacity <= 0:
            failed.add(acquired)
            return None
        candidates = [item for item in items if bits[item] & remaining]
        if len(candidates) <= capacity:
            rng.shuffle(candidates)
            return candidates

        # A new check must unlock before the existing slots are exhausted.
        # Keys outside all such check prerequisites can safely be postponed.
        useful = 0
        for required in requirements:
            missing = required & remaining
            if missing and missing.bit_count() <= capacity:
                useful |= missing
        if not useful:
            failed.add(acquired)
            return None
        candidates = [item for item in candidates if bits[item] & useful]
        rng.shuffle(candidates)
        candidates.sort(
            key=lambda item: sum(
                required & (acquired | bits[item]) == required
                for required in requirements
            ),
            reverse=True,
        )
        for item in candidates:
            tail = visit(acquired | bits[item])
            if tail is not None:
                return [item] + tail
        failed.add(acquired)
        return None

    order = visit(0)
    if order is None:
        raise ValueError("Impossible progression pool: no placement can reach every progression item")
    return order


def _prove_placement(
    model: dict[str, Any], placements: list[dict[str, Any]]
) -> dict[str, Any]:
    """Independently validate rewards and traverse the completed check graph."""
    checks = {check["id"]: check for check in model["checks"]}
    if not isinstance(placements, list):
        raise ValueError("Seed placements must be a list")
    rewards: dict[str, dict[str, str]] = {}
    keys: list[str] = []
    jobs: list[str] = []
    for placement in placements:
        if not isinstance(placement, dict):
            raise ValueError("Each placement must be a dictionary")
        check_id = _identifier(placement.get("check_id"), "Placement check_id")
        if check_id not in checks:
            raise ValueError(f"Unknown placement check: {check_id}")
        if check_id in rewards:
            raise ValueError(f"Duplicate placement for check: {check_id}")
        reward = placement.get("reward")
        if not isinstance(reward, dict):
            raise ValueError(f"Reward for {check_id} must be a dictionary")
        kind = reward.get("kind")
        reward_id = _identifier(reward.get("id"), f"Reward id for {check_id}")
        if kind == "key_item":
            if reward_id not in model["progression_items"]:
                raise ValueError(f"Unknown progression reward at {check_id}: {reward_id}")
            keys.append(reward_id)
        elif kind == "job":
            if reward_id not in model["jobs"]:
                raise ValueError(f"Unknown job reward at {check_id}: {reward_id}")
            jobs.append(reward_id)
        elif kind == "item":
            if reward_id != "empty":
                raise ValueError(f"Unsupported item reward at {check_id}: {reward_id}; expected empty")
        else:
            raise ValueError(f"Unknown reward kind at {check_id}: {kind!r}")
        rewards[check_id] = {"kind": kind, "id": reward_id}
    missing_checks = sorted(set(checks) - set(rewards))
    if missing_checks:
        raise ValueError(f"Missing placements for checks: {', '.join(missing_checks)}")
    for label, found, expected in (
        ("progression", keys, model["progression_items"]),
        ("job", jobs, model["jobs"]),
    ):
        duplicates = sorted(item for item, count in Counter(found).items() if count > 1)
        missing = sorted(set(expected) - set(found))
        if duplicates or missing:
            raise ValueError(
                f"Invalid {label} reward pool: duplicate={duplicates}, missing={missing}"
            )

    inventory = set(model["starting_inventory"])
    pending = set(checks)
    spheres: list[list[str]] = []
    while pending:
        sphere = sorted(
            check_id
            for check_id in pending
            if set(checks[check_id]["requires"]).issubset(inventory)
        )
        if not sphere:
            break
        spheres.append(sphere)
        pending.difference_update(sphere)
        # Rewards become available only after the whole sphere is evaluated.
        inventory.update(
            rewards[check_id]["id"]
            for check_id in sphere
            if rewards[check_id]["kind"] == "key_item"
        )
    unreachable_keys = sorted(
        rewards[check_id]["id"]
        for check_id in pending
        if rewards[check_id]["kind"] == "key_item"
    )
    missing_goals = sorted(set(model["goal_requires"]) - inventory)
    errors = []
    if unreachable_keys:
        errors.append(f"Unreachable progression items: {', '.join(unreachable_keys)}")
    if missing_goals:
        errors.append(f"Goal is unreachable; missing required keys: {', '.join(missing_goals)}")
    if pending:
        errors.append(f"Unreachable checks: {', '.join(sorted(pending))}")
    if errors:
        raise ValueError("; ".join(errors))
    return {
        "spheres": spheres,
        "completion_inventory": sorted(inventory),
        "goal_reachable": not missing_goals,
        "unreachable_checks": sorted(pending),
    }


def audit_seed(model: dict[str, Any], seed_record: dict[str, Any]) -> dict[str, Any]:
    """Reject corrupt, incomplete, or self-locked seeds and return their proof.

    Audit uses only the final reward placements, never the placement search or
    its acquisition order.  Stored sphere and completion metadata must match
    the independently recomputed result.  This proves placement validity, not
    that an artifact was originally generated using its recorded seed number.
    """
    canonical = validate_model(model)
    if not isinstance(seed_record, dict):
        raise ValueError("Seed record must be a dictionary")
    if type(seed_record.get("format_version")) is not int or seed_record["format_version"] != 1:
        raise ValueError("Seed format_version must be 1")
    if type(seed_record.get("seed")) is not int:
        raise ValueError("Seed must be an integer")
    proof = _prove_placement(canonical, seed_record.get("placements"))
    for field in ("spheres", "completion_inventory", "goal_reachable"):
        if field not in seed_record or seed_record[field] != proof[field]:
            raise ValueError(f"Seed {field} does not match the independently computed result")
    if type(seed_record["goal_reachable"]) is not bool:
        raise ValueError("Seed goal_reachable must be a boolean")
    return {"format_version": 1, "seed": seed_record["seed"], **proof}


def generate_seed(model: dict[str, Any], seed: int) -> dict[str, Any]:
    """Generate a repeatable, audited placement using a private RNG instance.

    Every progression item and job appears once.  Remaining checks explicitly
    receive ``{"kind": "item", "id": "empty"}``.  All placements are returned
    in check-ID order, regardless of the model's original ordering.
    """
    canonical = validate_model(model)
    if type(seed) is not int:
        raise ValueError("Seed must be an integer")
    rng = random.Random(seed)
    order = _progression_order(canonical, rng)
    inventory = set(canonical["starting_inventory"])
    unfilled = {check["id"]: check for check in canonical["checks"]}
    rewards: dict[str, dict[str, str]] = {}
    for item in order:
        reachable = sorted(
            check_id
            for check_id, check in unfilled.items()
            if set(check["requires"]).issubset(inventory)
        )
        if not reachable:
            raise ValueError("Progression placement exhausted reachable checks")
        check_id = rng.choice(reachable)
        rewards[check_id] = {"kind": "key_item", "id": item}
        del unfilled[check_id]
        inventory.add(item)
    remaining_checks = sorted(unfilled)
    rng.shuffle(remaining_checks)
    jobs = list(canonical["jobs"])
    rng.shuffle(jobs)
    for check_id, job in zip(remaining_checks, jobs):
        rewards[check_id] = {"kind": "job", "id": job}
    for check_id in remaining_checks[len(jobs):]:
        rewards[check_id] = {"kind": "item", "id": "empty"}
    placements = [
        {"check_id": check_id, "reward": rewards[check_id]}
        for check_id in sorted(rewards)
    ]
    proof = _prove_placement(canonical, placements)
    return {
        "format_version": 1,
        "seed": seed,
        "placements": placements,
        "spheres": proof["spheres"],
        "completion_inventory": proof["completion_inventory"],
        "goal_reachable": proof["goal_reachable"],
    }
