"""Frozen, full-roster boss assignments and portable integer scaling plans.

The assignment graph proves only that every approved boss is used exactly once
and every declared placement rule is satisfied. It does not prove that every
party can win every encounter. Combat scripts and destination continuation are
the native adapter's responsibility; no AI or event program is stored here.
"""
from __future__ import annotations

import hashlib
import re

from . import resources
from .runtime_seed import canonical_seed, INT32_MAX

CATALOG_ID = "ff5-bosses-v1"
CATALOG_FILE = "boss_catalog.json"
SCHEMA_VERSION = 1
SCALING_VERSION = "destination-budget-v1"
ALGORITHM = "JobFair|boss-assignment|1"
PREFIX = "native_boss/"
MIXED = "Mixed Crystals"
REMIXED = "Remixed Crystals"
SCOPES = {MIXED: "mixed-tiers-v1", REMIXED: "remixed-all-v1"}
PLACEMENT_FIELDS = {"location_id", "reward_kind", "reward_id", "quantity"}
# These reference ranks are the owner's approved tactical classification,
# independent of PR formation IDs and ordinary difficulty/power tiers.
SIMPLE_RANKS = frozenset((1, 2, 3, 4, 5, 7, 8, 9, 11, 13, 14, 16, 18,
                        19, 20, 21, 22, 24, 28, 29, 31, 40, 42, 44, 45, 46, 47))
BOSS_IDS = (
    "wing_raptor", "karlabos", "siren", "magissa_forza", "garula", "liquid_flame",
    "iron_claw", "ifrit", "shiva", "byblos", "cray_claw", "sandworm", "adamantoise",
    "soul_cannon", "archeoaevis", "manticore", "purobolos", "titan", "gilgamesh_1",
    "gilgamesh_2", "tyrannosaur", "abductor", "dragon_pod", "gilgamesh_3", "atomos",
    "crystals", "carbuncle", "gilgamesh_4", "antlion", "gargoyles", "ramuh",
    "catoblepas", "golem", "melusine", "wendigo", "odin", "triton_nereid_phobos",
    "omniscient", "minotaur", "leviathan", "gogo", "bahamut", "calofisteri", "apanda",
    "azulmagia", "catastrophe", "halicarnassus", "twintania", "necrophobe",
)
STAT_FIELDS = ("lv", "hp", "mp", "attack", "attack_count", "magic", "defense",
               "ability_defense", "agility")
SCALED_FIELDS = ("lv", "attack", "attack_count", "magic", "defense", "ability_defense", "agility")
_ID = re.compile(r"[a-z][a-z0-9_]{0,63}\Z")


def _integer(value: object, minimum: int = 0, maximum: int = INT32_MAX) -> bool:
    return type(value) is int and minimum <= value <= maximum


def _mode(mode: object) -> str:
    if not isinstance(mode, str) or mode not in SCOPES:
        raise ValueError("Boss randomization requires Mixed Crystals or Remixed Crystals.")
    return mode


def validate_catalog(catalog: object) -> dict:
    """Reject partial rosters and malformed native bindings before generation."""
    if (not isinstance(catalog, dict) or catalog.get("catalog_id") != CATALOG_ID
            or type(catalog.get("schema_version")) is not int
            or catalog["schema_version"] != SCHEMA_VERSION):
        raise ValueError("Unsupported native boss catalog.")
    rows = catalog.get("bosses")
    if not isinstance(rows, list) or len(rows) != 49:
        raise ValueError("The complete forty-nine-boss roster is required.")
    ids, ranks, formations = set(), set(), set()
    for row in rows:
        if not isinstance(row, dict):
            raise ValueError("Invalid native boss record.")
        ident, rank = row.get("boss_id"), row.get("reference_rank")
        if (not isinstance(ident, str) or not _ID.fullmatch(ident) or ident in ids
                or not _integer(rank, 1, 49) or rank in ranks
                or ident != BOSS_IDS[rank - 1]
                or row.get("tier") != ("simple" if rank in SIMPLE_RANKS else "complex")
                or not isinstance(row.get("name"), str) or not row["name"].strip()
                or not _integer(row.get("world"), 1, 3)):
            raise ValueError("Invalid boss identity, approved tier, reference rank, or world.")
        formation, aliases = row.get("formation_id"), row.get("formation_alias_ids")
        if (not _integer(formation, 1) or not isinstance(aliases, list)
                or any(not _integer(alias, 1) for alias in aliases)
                or len(aliases) != len(set(aliases))):
            raise ValueError("Invalid native boss formation binding.")
        bindings = set(aliases) | {formation}
        if formations & bindings:
            raise ValueError("One native formation is bound to multiple boss identities.")
        monsters, stats = row.get("monster_ids"), row.get("stats")
        if (not isinstance(monsters, list) or not monsters
                or any(not _integer(monster, 1) for monster in monsters)
                or len(monsters) != len(set(monsters)) or not isinstance(stats, list)
                or len(stats) != len(monsters)):
            raise ValueError("Every boss form and companion requires a native stat record.")
        profile = row.get("profile_monster_ids")
        initial = row.get("initial_monster_ids")
        if (not isinstance(profile, list) or not profile
                or any(not _integer(monster, 1) or monster not in monsters for monster in profile)
                or len(profile) != len(set(profile)) or not isinstance(initial, list) or not initial
                or any(not _integer(monster, 1) or monster not in monsters for monster in initial)):
            raise ValueError("Boss stat profiles and initial formation must refer to catalogued monsters.")
        stat_ids = set()
        for stat in stats:
            if (not isinstance(stat, dict) or not _integer(stat.get("monster_id"), 1)
                    or stat["monster_id"] not in monsters or stat["monster_id"] in stat_ids
                    or any(not _integer(stat.get(field), 1 if field == "hp" else 0)
                           for field in STAT_FIELDS)):
                raise ValueError("Invalid or duplicate native boss stats.")
            stat_ids.add(stat["monster_id"])
        for field in ("native_hp_budget", "effective_hp_budget"):
            if not _integer(row.get(field), 1):
                raise ValueError("Each boss requires a positive encounter HP budget.")
        requirements = row.get("requirements")
        if (not isinstance(requirements, list)
                or any(not isinstance(requirement, str) or not requirement.strip() for requirement in requirements)
                or len(requirements) != len(set(requirements))):
            raise ValueError("Invalid boss mechanic requirements.")
        ids.add(ident); ranks.add(rank); formations.update(bindings)
    restrictions = catalog.get("incompatible_placements", [])
    if not isinstance(restrictions, list):
        raise ValueError("Boss placement restrictions must be an array.")
    seen = set()
    for restriction in restrictions:
        if (not isinstance(restriction, dict)
                or set(restriction) != {"boss_id", "destination_id", "modes", "reason", "evidence"}):
            raise ValueError("Boss restrictions require explicit evidence and mode scope.")
        source, destination, modes = (restriction["boss_id"], restriction["destination_id"], restriction["modes"])
        if (not isinstance(source, str) or source not in ids
                or not isinstance(destination, str) or destination not in ids
                or not isinstance(modes, list) or not modes
                or any(not isinstance(mode, str) or mode not in SCOPES for mode in modes)
                or len(modes) != len(set(modes))
                or any(not isinstance(restriction[field], str) or not restriction[field].strip()
                       for field in ("reason", "evidence"))):
            raise ValueError("Invalid evidenced boss placement restriction.")
        for mode in modes:
            key = (mode, source, destination)
            if key in seen:
                raise ValueError("Duplicate boss placement restriction.")
            seen.add(key)
    return catalog


def read_catalog() -> dict:
    return validate_catalog(resources.read_catalog(CATALOG_FILE))


def _catalog(catalog: dict | None) -> dict:
    return read_catalog() if catalog is None else validate_catalog(catalog)


def _blocked(catalog: dict, mode: str) -> set[tuple[str, str]]:
    return {(r["destination_id"], r["boss_id"])
            for r in catalog.get("incompatible_placements", []) if mode in r["modes"]}


def _eligible(destination: dict, source: dict, mode: str, blocked: set[tuple[str, str]]) -> bool:
    return ((mode == REMIXED or destination["tier"] == source["tier"])
            and (destination["boss_id"], source["boss_id"]) not in blocked)


def _rank(seed_text: str, mode: str, *parts: str) -> bytes:
    return hashlib.sha256("|".join((ALGORITHM, seed_text, mode, *parts)).encode("utf-8")).digest()


def generate(seed: int, mode: str, catalog: dict | None = None) -> list[dict]:
    """Assign all 49 bosses with a finite deterministic matching algorithm.

    SHA-256 ranks are isolated from every job/treasure/shop namespace. Residual
    paths relocate earlier choices when a later destination has fewer options;
    an unsatisfiable graph fails instead of dropping bosses or retrying seeds.
    """
    seed_text, mode, catalog = canonical_seed(seed), _mode(mode), _catalog(catalog)
    bosses = {row["boss_id"]: row for row in catalog["bosses"]}
    blocked = _blocked(catalog, mode)
    candidates = {
        ident: sorted((other for other in bosses if _eligible(row, bosses[other], mode, blocked)),
                      key=lambda other: (_rank(seed_text, mode, "candidate", ident, other), other))
        for ident, row in bosses.items()
    }
    order = sorted(bosses, key=lambda ident: (len(candidates[ident]),
                   _rank(seed_text, mode, "destination", ident), ident))
    source_owner: dict[str, str] = {}

    def augment(destination: str, visited: set[str]) -> bool:
        for source in candidates[destination]:
            if source in visited:
                continue
            visited.add(source)
            if source not in source_owner or augment(source_owner[source], visited):
                source_owner[source] = destination
                return True
        return False

    for destination in order:
        if not augment(destination, set()):
            raise ValueError("Boss compatibility rules cannot assign the full forty-nine-boss roster.")
    result = sorted(({"location_id": PREFIX + destination, "reward_kind": "boss",
                      "reward_id": source, "quantity": 1}
                     for source, destination in source_owner.items()), key=lambda row: row["location_id"])
    validate_placements(result, mode, catalog)
    return result


def validate_placements(placements: object, mode: str, catalog: dict | None = None) -> None:
    """Validate the saved assignment itself; never regenerate it on load."""
    mode, catalog = _mode(mode), _catalog(catalog)
    bosses = {row["boss_id"]: row for row in catalog["bosses"]}
    blocked = _blocked(catalog, mode)
    if not isinstance(placements, list) or len(placements) != len(bosses):
        raise ValueError("Exactly forty-nine saved boss assignments are required.")
    locations, sources = set(), set()
    for row in placements:
        if not isinstance(row, dict) or set(row) != PLACEMENT_FIELDS:
            raise ValueError("Invalid boss placement fields.")
        location, source = row["location_id"], row["reward_id"]
        if (not isinstance(location, str) or not location.startswith(PREFIX)
                or location[len(PREFIX):] not in bosses or location in locations
                or row["reward_kind"] != "boss" or type(row["quantity"]) is not int or row["quantity"] != 1
                or not isinstance(source, str) or source not in bosses or source in sources):
            raise ValueError("Unknown, duplicate, or invalid saved boss assignment.")
        destination = location[len(PREFIX):]
        if not _eligible(bosses[destination], bosses[source], mode, blocked):
            raise ValueError("Boss assignment violates its tactical tier or an evidenced compatibility rule.")
        locations.add(location); sources.add(source)
    if locations != {PREFIX + ident for ident in bosses} or sources != set(bosses):
        raise ValueError("Saved boss assignments do not preserve the complete roster.")


def scale_integer(value: int, numerator: int, denominator: int, *, minimum: int = 0,
                  maximum: int = INT32_MAX) -> int:
    """Positive rational scaling with integer half-up rounding and bounded output.

    Multiplication fits a signed 64-bit integer for every permitted argument;
    C# uses the identical expression with a long intermediate. No floats or
    platform-dependent rounding participate in persisted encounter plans.
    """
    if (not _integer(value) or not _integer(numerator) or not _integer(denominator, 1)
            or not _integer(minimum) or not _integer(maximum, minimum)):
        raise ValueError("Invalid bounded integer boss scaling arguments.")
    return min(maximum, max(minimum, (value * numerator + denominator // 2) // denominator))


def scale_hp_threshold(value: int, native_hp: int, scaled_hp: int) -> int:
    """Scale HP phase points while retaining literal death/survival sentinels."""
    if (not _integer(native_hp, 1) or not _integer(scaled_hp, 1)
            or not _integer(value, 0, native_hp)):
        raise ValueError("HP threshold must lie within its native maximum HP.")
    # Native final-HP branches use literal 1/2 to keep an actor alive until its
    # ending script runs. Rounding those to zero would strand that script.
    if value <= 2:
        return value
    maximum = scaled_hp if value == native_hp else max(0, scaled_hp - 1)
    return scale_integer(value, scaled_hp, native_hp, maximum=maximum)


def _profile(boss: dict, field: str) -> int:
    # Defense 255 represents an intentionally closed damage route in audited
    # native forms. It is a mechanic sentinel, never the encounter's baseline.
    return max((stat[field] for stat in boss["stats"] if stat["monster_id"] in boss["profile_monster_ids"]
                and (field not in ("defense", "ability_defense") or stat[field] != 255)), default=0)


def scale_fixed_damage(destination_id: str, source_id: str, damage: int, catalog: dict | None = None) -> int:
    """Scale absolute STATIC damage, such as enemy 1000 Needles, once.

    Native STATIC damage bypasses the monster's level and magic formulas. Its
    separate level-profile ratio preserves the attack while fitting its new
    position. Percentage/HP-based attacks and enemy-to-enemy scripted finishers
    must not use this helper; the runtime restricts it to the current shuffled
    enemy attacking a player. The native 9999 damage cap is retained.
    """
    catalog = _catalog(catalog)
    bosses = {row["boss_id"]: row for row in catalog["bosses"]}
    if (not isinstance(destination_id, str) or destination_id not in bosses
            or not isinstance(source_id, str) or source_id not in bosses or not _integer(damage)):
        raise ValueError("Invalid fixed-damage boss source, destination, or damage.")
    if damage == 0:
        return 0
    return scale_integer(damage, _profile(bosses[destination_id], "lv"),
                         max(1, _profile(bosses[source_id], "lv")), minimum=1, maximum=9999)


def scale_defense(value: int, numerator: int, denominator: int) -> int:
    """Round value * sqrt(HP-budget ratio) half-up with exact integer math.

    Native zeroes and immunity sentinels survive. Ordinary armor is capped at
    254 so scaling cannot manufacture a native 255 immunity sentinel.
    """
    if not (_integer(value, 0, 255) and _integer(numerator, 1) and _integer(denominator, 1)):
        raise ValueError("Invalid boss defense scaling ratio.")
    if value in (0, 255):
        return value
    squared = 4 * value * value * numerator
    low, high = 0, 254
    while low < high:
        candidate = (low + high + 1) // 2
        if squared >= (2 * candidate - 1) ** 2 * denominator:
            low = candidate
        else:
            high = candidate - 1
    return low


def scaling_plan(destination_id: str, source_id: str, catalog: dict | None = None) -> dict:
    """Return the stat plan for the complete replacement encounter.

    HP uses one encounter budget ratio shared by every form and companion. This
    retains their relative shares instead of granting each the entire location
    budget. Defenses use the square root of the HP-budget ratio: a destination's
    temporary armored phase must not become a donor's permanent armor. Native
    levels are unsuitable here (Titan is level 1; Odin is level 2). Other fields retain
    relative differences against their encounter's maximum ordinary value.
    MP and native immunity sentinels retain their semantics.
    Native scripts must also scale absolute HP mechanics using the resulting HP.
    """
    catalog = _catalog(catalog)
    bosses = {row["boss_id"]: row for row in catalog["bosses"]}
    if (not isinstance(destination_id, str) or destination_id not in bosses
            or not isinstance(source_id, str) or source_id not in bosses):
        raise ValueError("Unknown boss scaling source or destination.")
    destination, source = bosses[destination_id], bosses[source_id]
    numerator, denominator = destination["effective_hp_budget"], source["effective_hp_budget"]
    ratios = {}
    for field in SCALED_FIELDS:
        if field in ("defense", "ability_defense"):
            ratios[field] = {"numerator": numerator, "denominator": denominator, "method": "square-root"}
        else:
            ratios[field] = {"numerator": _profile(destination, field),
                             "denominator": max(1, _profile(source, field))}
    result = []
    for stat in sorted(source["stats"], key=lambda row: row["monster_id"]):
        output = {"monster_id": stat["monster_id"],
                  "hp": scale_integer(stat["hp"], numerator, denominator, minimum=1), "mp": stat["mp"]}
        for field, ratio in ratios.items():
            value = stat[field]
            if field in ("defense", "ability_defense"):
                output[field] = scale_defense(value, ratio["numerator"], ratio["denominator"])
            elif value == 0:
                output[field] = value
            else:
                output[field] = scale_integer(value, ratio["numerator"], ratio["denominator"],
                                              minimum=1, maximum=255)
        result.append(output)
    return {"catalog_id": CATALOG_ID, "scaling_version": SCALING_VERSION,
            "destination_id": destination_id, "source_id": source_id,
            "destination_formation_id": destination["formation_id"], "source_formation_id": source["formation_id"],
            "hp_scale": {"numerator": numerator, "denominator": denominator},
            "fixed_damage_scale": dict(ratios["lv"]), "stat_scales": ratios, "stats": result}
