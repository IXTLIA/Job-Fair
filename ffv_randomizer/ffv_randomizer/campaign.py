"""Versioned launcher payloads for the bounded native Mixed Crystals slice.

Reuse the existing native job prototype and placement solver. Frozen placements,
not a future generator, are authoritative once a run has started.
"""
from __future__ import annotations

from collections import Counter
import hashlib
import json
from pathlib import Path
import random

from . import resources
from .runtime_seed import canonical_seed, try_parse_seed
from . import world_treasure

ROOT = Path(__file__).resolve().parents[2]
MIXED = "Mixed Crystals"
REMIXED = "Remixed Crystals"
MIXED_FIESTA = "Mixed-Fiesta"
REMIXED_FIESTA = "Remixed-Fiesta"
MODES = (MIXED, REMIXED, MIXED_FIESTA, REMIXED_FIESTA)
GENERATOR = "job-fair-native-solver-v1"
CATALOG_ID = "mixed-native-jobs-wind-treasure-v1"
SETTINGS = {"job_rewards": "native-crystals", "key_items": "vanilla", "treasure": "randomized",
            "treasure_scope": "wind-shrine-v1", "world_access": "story"}
ASSETS = ROOT / "research/ff5/events/extracted/assets/Assets/GameAssets/Serial"
OPERATIONS = ROOT / "research/ff5/events/native_operations.json"
CATALOG = resources.DATA_ROOT / "mixed_catalog.json"
WORLD_CATALOG_ID = "mixed-native-jobs-world-treasure-v2"
WORLD_CATALOG = resources.DATA_ROOT / "world_catalog.json"
WORLD_SETTINGS = {**SETTINGS, "treasure_scope": "world-pools-v2"}
SHOP_CATALOG_ID = "mixed-world-treasure-shops-v3"
SHOP_SETTINGS = {**WORLD_SETTINGS, "shop_scope": "world-type-pools-v1", "shop_prices": "native-item-prices"}
REMIXED_JOB_SLICE_CATALOG_ID = "remixed-job-crystals-slice-v1"
REMIXED_JOB_SLICE_SETTINGS = {**SHOP_SETTINGS, "job_rewards": "container-job-bonuses-v1"}
WORLD_TRAVEL_CATALOG_ID = "remixed-world-travel-slice-v1"
REMIXED_PLAYTEST_CATALOG_ID = "remixed-crystals-playtest-v1"
FIXED_KARNAK_MIXED_CATALOG_ID = "mixed-world-treasure-shops-fixed-karnak-v1"
FIXED_KARNAK_REMIXED_CATALOG_ID = "remixed-crystals-fixed-karnak-v1"
MIXED_BOSS_CATALOG_ID = "mixed-crystals-bosses-v1"
REMIXED_BOSS_CATALOG_ID = "remixed-crystals-bosses-v1"
MIXED_REWARDS_CATALOG_ID = "mixed-crystals-rewards-v1"
REMIXED_REWARDS_CATALOG_ID = "remixed-crystals-rewards-v1"
MIXED_FIESTA_CATALOG_ID = "mixed-fiesta-rewards-v1"
REMIXED_FIESTA_CATALOG_ID = "remixed-fiesta-rewards-v1"
REWARD_CATALOGS = dict(zip(MODES, (MIXED_REWARDS_CATALOG_ID, REMIXED_REWARDS_CATALOG_ID,
                                  MIXED_FIESTA_CATALOG_ID, REMIXED_FIESTA_CATALOG_ID), strict=True))
# The first four grants in the actual Wind event are Knight, Monk, Thief, White
# Mage. Slot suffixes identify original jobs, not event order (04 is Water).
FIESTA_WIND_LOCATIONS = tuple(f"native_job_slot_{job:02d}" for job in (7, 3, 2, 5))
WORLD_TRAVEL_SETTINGS = {**REMIXED_JOB_SLICE_SETTINGS, "key_items": "world-key-bonuses-v1",
                         "world_access": "reversible-worlds-v1"}


def placement_hash(request: dict) -> str:
    settings = request["settings"]
    parts = ["JobFair|placements|1", "seed|" + request["seed"], "generator|" + request["generator"],
             "mode|" + request["mode"], "catalog|" + request["catalog_id"],
             "settings|" + "|".join(settings[k] for k in SETTINGS)]
    if "shop_scope" in settings:
        parts.append("shops|" + settings["shop_scope"] + "|" + settings["shop_prices"])
    if "boss_scope" in settings:
        parts.append("bosses|" + settings["boss_scope"] + "|" + settings["boss_scaling"])
    if "loot_scope" in settings:
        parts.append("loot|" + settings["loot_scope"])
    if "boss_experience" in settings:
        parts.append("boss-experience|" + settings["boss_experience"])
    parts.append("starters|" + ",".join(str(x) for x in request["starting_job_ids"]))
    parts.extend("placement|{location_id}|{reward_kind}|{reward_id}|{quantity}".format(**p)
                 for p in sorted(request["placements"], key=lambda p: p["location_id"]))
    return hashlib.sha256(("\n".join(parts) + "\n").encode("utf-8")).hexdigest()


def read_catalog() -> dict:
    return resources.read_catalog("mixed_catalog.json")


def read_world_catalog() -> dict:
    return world_treasure.validate_catalog(resources.read_catalog("world_catalog.json"))


def build_request(text: str, mode: str = MIXED, *, catalog_id: str | None = None) -> dict:
    if catalog_id in REWARD_CATALOGS.values() or (catalog_id is None and mode in (MIXED_FIESTA, REMIXED_FIESTA)):
        if catalog_id is not None and REWARD_CATALOGS.get(mode) != catalog_id:
            raise ValueError("Reward catalog and mode disagree.")
        return build_reward_campaign(text, mode)
    if catalog_id is None:
        catalog_id = REMIXED_PLAYTEST_CATALOG_ID if mode == REMIXED else CATALOG_ID
    if catalog_id in (MIXED_BOSS_CATALOG_ID, REMIXED_BOSS_CATALOG_ID):
        if mode != (MIXED if catalog_id == MIXED_BOSS_CATALOG_ID else REMIXED):
            raise ValueError("Boss catalog and mode disagree.")
        return build_boss_campaign(text, mode)
    if catalog_id in (FIXED_KARNAK_MIXED_CATALOG_ID, FIXED_KARNAK_REMIXED_CATALOG_ID):
        if (catalog_id == FIXED_KARNAK_MIXED_CATALOG_ID) != (mode == MIXED) or mode not in (MIXED, REMIXED):
            raise ValueError("Fixed Karnak catalog and mode disagree.")
        return build_fixed_karnak(text, mode)
    if mode == REMIXED and catalog_id == REMIXED_PLAYTEST_CATALOG_ID:
        return build_remixed_playtest(text)
    if mode != MIXED:
        raise ValueError("Unsupported mode or historical Remixed draft catalog.")
    if catalog_id == SHOP_CATALOG_ID:
        from . import shops
        request = build_request(text, mode, catalog_id=WORLD_CATALOG_ID)
        request["catalog_id"] = SHOP_CATALOG_ID
        request["settings"] = dict(SHOP_SETTINGS)
        request["placements"] = sorted(request["placements"] + shops.generate(int(request["seed"])),
                                       key=lambda row: row["location_id"])
        request["placement_hash"] = placement_hash(request)
        return validate_request(request)
    if catalog_id == WORLD_CATALOG_ID:
        request = build_request(text, mode)
        request["catalog_id"] = WORLD_CATALOG_ID
        request["settings"] = dict(WORLD_SETTINGS)
        jobs = [row for row in request["placements"] if row["reward_kind"] == "job"]
        request["placements"] = sorted(jobs + world_treasure.generate(int(request["seed"]), read_world_catalog()),
                                       key=lambda row: row["location_id"])
        request["placement_hash"] = placement_hash(request)
        return validate_request(request)
    if catalog_id != CATALOG_ID:
        raise ValueError("Unsupported native reward catalog.")
    valid, seed = try_parse_seed(text)
    if not valid:
        raise ValueError("Enter an unsigned 64-bit decimal seed.")
    placements = resources.native_job_placements(seed)
    catalog = read_catalog()
    chests = sorted(catalog["wind_chests"], key=lambda c: c["id"])
    rewards = [(c["content_id"], c["quantity"]) for c in chests]
    # Independent domain: treasure must not perturb the existing 21-job solver result.
    random.Random("JobFair|wind-treasure|1|" + canonical_seed(seed)).shuffle(rewards)
    placements.extend({"location_id": c["id"], "reward_kind": "item",
                       "reward_id": str(reward[0]), "quantity": reward[1]}
                      for c, reward in zip(chests, rewards, strict=True))
    request = {"schema_version": 2, "seed": canonical_seed(seed), "generator": GENERATOR,
               "mode": mode, "settings": dict(SETTINGS), "catalog_id": CATALOG_ID,
               "starting_job_ids": [], "placements": sorted(placements, key=lambda p: p["location_id"])}
    request["placement_hash"] = placement_hash(request)
    return validate_request(request)


def validate_request(value: object) -> dict:
    if isinstance(value, dict) and value.get("catalog_id") in REWARD_CATALOGS.values():
        return validate_reward_campaign(value)
    if isinstance(value, dict) and value.get("catalog_id") in (MIXED_BOSS_CATALOG_ID, REMIXED_BOSS_CATALOG_ID):
        return validate_boss_campaign(value)
    if isinstance(value, dict) and value.get("catalog_id") in (FIXED_KARNAK_MIXED_CATALOG_ID, FIXED_KARNAK_REMIXED_CATALOG_ID):
        return validate_fixed_karnak(value)
    if isinstance(value, dict) and value.get("catalog_id") == REMIXED_PLAYTEST_CATALOG_ID:
        return validate_remixed_playtest(value)
    fields = {"schema_version", "seed", "generator", "mode", "settings", "catalog_id",
              "starting_job_ids", "placements", "placement_hash"}
    if not isinstance(value, dict) or set(value) != fields:
        raise ValueError("Unsupported campaign request fields.")
    if type(value["schema_version"]) is not int or value["schema_version"] != 2:
        raise ValueError("Unsupported campaign schema.")
    if value["catalog_id"] in (WORLD_CATALOG_ID, SHOP_CATALOG_ID):
        return _validate_world_request(value)
    if value["generator"] != GENERATOR or value["catalog_id"] != CATALOG_ID or value["mode"] != MIXED:
        raise ValueError("Unsupported campaign mode, generator or catalog.")
    valid, seed = try_parse_seed(value["seed"])
    if not valid or canonical_seed(seed) != value["seed"]:
        raise ValueError("Campaign seed must be canonical uint64 decimal text.")
    if value["settings"] != SETTINGS or value["starting_job_ids"] != []:
        raise ValueError("Unsupported normalized Mixed Crystals settings.")
    catalog = read_catalog()
    chest_ids = {c["id"] for c in catalog["wind_chests"]}
    job_ids = {f"native_job_slot_{j:02d}" for j in range(2, 23)}
    placements = value["placements"]
    if not isinstance(placements, list) or len(placements) != len(chest_ids | job_ids):
        raise ValueError("Incomplete native placement table.")
    seen, jobs, treasure = set(), [], []
    for row in placements:
        if not isinstance(row, dict) or set(row) != {"location_id", "reward_kind", "reward_id", "quantity"}:
            raise ValueError("Invalid reward placement fields.")
        location, reward, quantity = row["location_id"], row["reward_id"], row["quantity"]
        if not isinstance(location, str) or location not in job_ids | chest_ids or location in seen:
            raise ValueError("Unknown or duplicate native location.")
        seen.add(location)
        if not isinstance(reward, str) or not reward.isascii() or not reward.isdecimal() or str(int(reward)) != reward:
            raise ValueError("Reward ID must be canonical decimal.")
        if type(quantity) is not int or quantity != 1:
            raise ValueError("This slice requires native quantity one.")
        if location in job_ids:
            if row["reward_kind"] != "job":
                raise ValueError("Crystal location requires a job.")
            jobs.append(int(reward))
        else:
            if row["reward_kind"] != "item":
                raise ValueError("Wind treasure location requires an ordinary item.")
            treasure.append((int(reward), quantity))
    if sorted(jobs) != list(range(2, 23)):
        raise ValueError("All 21 jobs must appear once.")
    expected = Counter((c["content_id"], c["quantity"]) for c in catalog["wind_chests"])
    if Counter(treasure) != expected:
        raise ValueError("Native treasure quantities/pool changed.")
    if value["placement_hash"] != placement_hash(value):
        raise ValueError("Campaign placement hash mismatch.")
    return value


def _validate_world_request(value: dict) -> dict:
    if value["generator"] != GENERATOR or value["mode"] != MIXED:
        raise ValueError("Unsupported campaign mode or generator.")
    valid, seed = try_parse_seed(value["seed"])
    if not valid or canonical_seed(seed) != value["seed"]:
        raise ValueError("Campaign seed must be canonical uint64 decimal text.")
    has_shops = value["catalog_id"] == SHOP_CATALOG_ID
    if value["settings"] != (SHOP_SETTINGS if has_shops else WORLD_SETTINGS) or value["starting_job_ids"] != []:
        raise ValueError("Unsupported normalized world-pool settings.")
    placements = value["placements"]
    if not isinstance(placements, list):
        raise ValueError("Invalid native placement table.")
    job_ids = {f"native_job_slot_{j:02d}" for j in range(2, 23)}
    seen, jobs, ordinary, shop_rows = set(), [], [], []
    for row in placements:
        if not isinstance(row, dict) or set(row) != {"location_id", "reward_kind", "reward_id", "quantity"}:
            raise ValueError("Invalid reward placement fields.")
        location = row["location_id"]
        if not isinstance(location, str) or location in seen:
            raise ValueError("Invalid or duplicate native location.")
        seen.add(location)
        if has_shops and row["reward_kind"] == "shop":
            shop_rows.append(row)
            continue
        if location not in job_ids:
            ordinary.append(row)
            continue
        reward = row["reward_id"]
        if (row["reward_kind"] != "job" or type(row["quantity"]) is not int or row["quantity"] != 1
                or not isinstance(reward, str) or reward not in {str(j) for j in range(2, 23)}):
            raise ValueError("Native crystal location requires one valid job.")
        jobs.append(int(reward))
    if sorted(jobs) != list(range(2, 23)):
        raise ValueError("All 21 jobs must appear once.")
    world_treasure.validate_placements(read_world_catalog(), ordinary)
    if has_shops:
        from . import shops
        shops.validate_placements(shops.read_catalog(), shop_rows)
    if value["placement_hash"] != placement_hash(value):
        raise ValueError("Campaign placement hash mismatch.")
    return value


def build_remixed_job_slice(text: str) -> dict:
    """Build an isolated draft, never accepted by the normal launcher API."""
    from . import remixed_jobs
    request = build_request(text, catalog_id=SHOP_CATALOG_ID)
    bonus = remixed_jobs.generate(int(request["seed"]))
    request["mode"] = REMIXED
    request["catalog_id"] = REMIXED_JOB_SLICE_CATALOG_ID
    request["settings"] = dict(REMIXED_JOB_SLICE_SETTINGS)
    request["starting_job_ids"] = bonus["starting_job_ids"]
    request["placements"] = sorted([row for row in request["placements"] if row["reward_kind"] != "job"]
                                   + bonus["placements"], key=lambda row: row["location_id"])
    request["placement_hash"] = placement_hash(request)
    return validate_remixed_job_slice(request)


def validate_remixed_job_slice(value: object) -> dict:
    """Validate frozen draft data without enabling production play or rerolling."""
    from . import remixed_jobs, shops
    fields = {"schema_version", "seed", "generator", "mode", "settings", "catalog_id",
              "starting_job_ids", "placements", "placement_hash"}
    if not isinstance(value, dict) or set(value) != fields:
        raise ValueError("Unsupported Remixed draft request fields.")
    if (type(value["schema_version"]) is not int or value["schema_version"] != 2
            or value["generator"] != GENERATOR or value["mode"] != REMIXED
            or value["catalog_id"] != REMIXED_JOB_SLICE_CATALOG_ID
            or value["settings"] != REMIXED_JOB_SLICE_SETTINGS):
        raise ValueError("Unsupported Remixed job-only draft contract.")
    valid, seed = try_parse_seed(value["seed"])
    if not valid or canonical_seed(seed) != value["seed"]:
        raise ValueError("Campaign seed must be canonical uint64 decimal text.")
    if not isinstance(value["placements"], list) or len(value["placements"]) != 1113:
        raise ValueError("Incomplete Remixed draft placement table.")
    bonuses, ordinary, shop_rows, seen = [], [], [], set()
    for row in value["placements"]:
        if not isinstance(row, dict) or set(row) != {"location_id", "reward_kind", "reward_id", "quantity"}:
            raise ValueError("Invalid Remixed draft placement fields.")
        location = row["location_id"]
        if not isinstance(location, str) or location in seen:
            raise ValueError("Invalid or duplicate Remixed draft location.")
        seen.add(location)
        if location.startswith(remixed_jobs.PREFIX):
            bonuses.append(row)
        elif row["reward_kind"] == "shop":
            shop_rows.append(row)
        else:
            ordinary.append(row)
    remixed_jobs.validate_placements(remixed_jobs.read_catalog(), value["starting_job_ids"], bonuses)
    world_treasure.validate_placements(read_world_catalog(), ordinary)
    shops.validate_placements(shops.read_catalog(), shop_rows)
    if value["placement_hash"] != placement_hash(value):
        raise ValueError("Campaign placement hash mismatch.")
    return value


def build_world_travel_slice(text: str) -> dict:
    """Explicit schema3 draft; never accepted by the production launcher API."""
    from . import world_travel
    request = build_remixed_job_slice(text)
    jobs = [row for row in request["placements"] if row["reward_kind"] == "job"]
    keys = world_travel.generate_keys(int(request["seed"]), request["starting_job_ids"], jobs)
    request["schema_version"] = 3
    request["catalog_id"] = WORLD_TRAVEL_CATALOG_ID
    request["settings"] = dict(WORLD_TRAVEL_SETTINGS)
    request["placements"] = sorted(request["placements"] + keys, key=lambda row: row["location_id"])
    request["placement_hash"] = placement_hash(request)
    return validate_world_travel_slice(request)


def validate_world_travel_slice(value: object) -> dict:
    """Validate the complete saved placement table without regenerating it."""
    from . import world_travel
    fields = {"schema_version", "seed", "generator", "mode", "settings", "catalog_id",
              "starting_job_ids", "placements", "placement_hash"}
    if (not isinstance(value, dict) or set(value) != fields
            or type(value["schema_version"]) is not int or value["schema_version"] != 3
            or value["catalog_id"] != WORLD_TRAVEL_CATALOG_ID or value["generator"] != GENERATOR
            or value["mode"] != REMIXED or value["settings"] != WORLD_TRAVEL_SETTINGS
            or not isinstance(value["placements"], list) or len(value["placements"]) != 1115):
        raise ValueError("Unsupported isolated world-travel request.")
    keys, original = [], []
    for row in value["placements"]:
        if not isinstance(row, dict) or not isinstance(row.get("location_id"), str):
            raise ValueError("Invalid world-travel placement.")
        (keys if row["location_id"].startswith(world_travel.PREFIX) else original).append(row)
    # Reuse all frozen job, world-treasure and shop validators. This projected
    # contract validates stored rows only; it never invokes their generators.
    base = {**value, "schema_version": 2, "catalog_id": REMIXED_JOB_SLICE_CATALOG_ID,
            "settings": dict(REMIXED_JOB_SLICE_SETTINGS), "placements": original}
    # Validate malformed fields before attempting canonical hash formatting.
    base["placement_hash"] = value["placement_hash"]
    try:
        base["placement_hash"] = placement_hash(base)
    except (KeyError, TypeError, ValueError) as error:
        raise ValueError("Malformed world-travel placement fields.") from error
    validate_remixed_job_slice(base)
    jobs = [row for row in original if row["reward_kind"] == "job"]
    world_travel.validate_placements(world_travel.read_catalog(), value["starting_job_ids"], jobs, keys)
    ordinary = {row["location_id"] for row in original if row["reward_kind"] in ("item", "gil")}
    if any(row["location_id"].removeprefix(world_travel.PREFIX) not in ordinary for row in keys):
        raise ValueError("A world key replaced its ordinary reward.")
    if value["placement_hash"] != placement_hash(value):
        raise ValueError("World-travel placement hash mismatch.")
    return value


def build_remixed_playtest(text: str) -> dict:
    """Admit the tested native contract without rerolling the previous draft's rows."""
    request = build_world_travel_slice(text)
    request["catalog_id"] = REMIXED_PLAYTEST_CATALOG_ID
    request["placement_hash"] = placement_hash(request)
    return validate_remixed_playtest(request)


def validate_remixed_playtest(value: object) -> dict:
    """Validate frozen saved rows; the new catalog identity is an admission boundary."""
    if not isinstance(value, dict) or value.get("catalog_id") != REMIXED_PLAYTEST_CATALOG_ID:
        raise ValueError("Unsupported Remixed playtest catalog.")
    # Project through the frozen draft validator without running any generator.
    projected = {**value, "catalog_id": WORLD_TRAVEL_CATALOG_ID}
    try:
        projected["placement_hash"] = placement_hash(projected)
    except (KeyError, TypeError, ValueError) as error:
        raise ValueError("Malformed Remixed playtest request.") from error
    validate_world_travel_slice(projected)
    if value.get("placement_hash") != placement_hash(value):
        raise ValueError("Remixed playtest placement hash mismatch.")
    return value


def build_fixed_karnak(text: str, mode: str) -> dict:
    """New catalog identities retain native timed chests; no saved run is migrated."""
    from . import fixed_karnak
    if mode not in (MIXED, REMIXED):
        raise ValueError("Unsupported fixed Karnak mode.")
    request = (build_request(text, catalog_id=SHOP_CATALOG_ID) if mode == MIXED
               else build_remixed_playtest(text))
    ordinary = fixed_karnak.generate(int(request["seed"]), read_world_catalog())
    request["placements"] = sorted([row for row in request["placements"]
                                    if row["reward_kind"] not in ("item", "gil", "empty")]
                                   + ordinary, key=lambda row: row["location_id"])
    request["catalog_id"] = FIXED_KARNAK_MIXED_CATALOG_ID if mode == MIXED else FIXED_KARNAK_REMIXED_CATALOG_ID
    request["settings"] = {**request["settings"], "treasure_scope": fixed_karnak.TREASURE_SCOPE}
    request["placement_hash"] = placement_hash(request)
    return validate_fixed_karnak(request)


def validate_fixed_karnak(value: object) -> dict:
    """Validate saved rows against the base pools and the additional fixed policy."""
    from . import fixed_karnak
    if not isinstance(value, dict) or value.get("catalog_id") not in (FIXED_KARNAK_MIXED_CATALOG_ID, FIXED_KARNAK_REMIXED_CATALOG_ID):
        raise ValueError("Unsupported fixed Karnak catalog.")
    mixed = value["catalog_id"] == FIXED_KARNAK_MIXED_CATALOG_ID
    expected = {**(SHOP_SETTINGS if mixed else WORLD_TRAVEL_SETTINGS), "treasure_scope": fixed_karnak.TREASURE_SCOPE}
    if value.get("settings") != expected:
        raise ValueError("Unsupported fixed Karnak settings.")
    projected = {**value, "catalog_id": SHOP_CATALOG_ID if mixed else REMIXED_PLAYTEST_CATALOG_ID,
                 "settings": dict(SHOP_SETTINGS if mixed else WORLD_TRAVEL_SETTINGS)}
    try:
        projected["placement_hash"] = placement_hash(projected)
    except (KeyError, TypeError, ValueError) as error:
        raise ValueError("Malformed fixed Karnak placement fields.") from error
    validate_request(projected)
    ordinary = [row for row in value["placements"] if row["reward_kind"] in ("item", "gil", "empty")]
    fixed_karnak.validate_placements(read_world_catalog(), ordinary)
    if any(row["location_id"].removeprefix("remixed_job/").removeprefix("remixed_key/") in fixed_karnak.LOCATION_IDS
           for row in value["placements"] if row["reward_kind"] in ("job", "key")):
        raise ValueError("Missable Karnak chests cannot contain progression bonuses.")
    if value.get("placement_hash") != placement_hash(value):
        raise ValueError("Fixed Karnak placement hash mismatch.")
    return value


def build_boss_campaign(text: str, mode: str) -> dict:
    """Add independently seeded boss assignments without changing existing rewards."""
    from . import bosses
    if mode not in (MIXED, REMIXED):
        raise ValueError("Unsupported boss campaign mode.")
    request = build_fixed_karnak(text, mode)
    request["catalog_id"] = MIXED_BOSS_CATALOG_ID if mode == MIXED else REMIXED_BOSS_CATALOG_ID
    request["settings"] = {**request["settings"],
                           "boss_scope": "mixed-tiers-v1" if mode == MIXED else "remixed-all-v1",
                           "boss_scaling": "destination-budget-v1"}
    request["placements"] = sorted(request["placements"] + list(bosses.generate(int(request["seed"]), mode)),
                                   key=lambda row: row["location_id"])
    request["placement_hash"] = placement_hash(request)
    return validate_boss_campaign(request)


def validate_boss_campaign(value: object) -> dict:
    """Validate frozen boss and ordinary tables; never regenerate a saved assignment."""
    from . import bosses
    if not isinstance(value, dict) or value.get("catalog_id") not in (MIXED_BOSS_CATALOG_ID, REMIXED_BOSS_CATALOG_ID):
        raise ValueError("Unsupported boss campaign catalog.")
    mixed = value["catalog_id"] == MIXED_BOSS_CATALOG_ID
    mode = MIXED if mixed else REMIXED
    if value.get("mode") != mode:
        raise ValueError("Boss catalog and mode disagree.")
    settings = value.get("settings")
    if (not isinstance(settings, dict)
            or settings.get("boss_scope") != ("mixed-tiers-v1" if mixed else "remixed-all-v1")
            or settings.get("boss_scaling") != "destination-budget-v1"):
        raise ValueError("Unsupported boss shuffle or scaling settings.")
    rows = value.get("placements")
    if not isinstance(rows, list) or any(not isinstance(row, dict) for row in rows):
        raise ValueError("Invalid campaign placement table.")
    boss_rows = [row for row in rows if row.get("reward_kind") == "boss"]
    bosses.validate_placements(boss_rows, mode)
    projected = {**value,
                 "catalog_id": FIXED_KARNAK_MIXED_CATALOG_ID if mixed else FIXED_KARNAK_REMIXED_CATALOG_ID,
                 "settings": {key: setting for key, setting in settings.items()
                              if key not in ("boss_scope", "boss_scaling")},
                 "placements": [row for row in rows if row.get("reward_kind") != "boss"]}
    try:
        projected["placement_hash"] = placement_hash(projected)
    except (KeyError, TypeError, ValueError) as error:
        raise ValueError("Malformed boss campaign placement fields.") from error
    validate_fixed_karnak(projected)
    if value.get("placement_hash") != placement_hash(value):
        raise ValueError("Boss campaign placement hash mismatch.")
    return value


def base_mode(mode: str) -> str:
    if mode in (MIXED, MIXED_FIESTA):
        return MIXED
    if mode in (REMIXED, REMIXED_FIESTA):
        return REMIXED
    raise ValueError("Unsupported Job Fair mode.")


def reward_settings(mode: str) -> dict:
    mixed = base_mode(mode) == MIXED
    settings = {**(SHOP_SETTINGS if mixed else WORLD_TRAVEL_SETTINGS),
                "treasure_scope": "world-event-pools-v1",
                "boss_scope": "mixed-tiers-v1" if mixed else "remixed-all-v1",
                "boss_scaling": "destination-budget-v1",
                "loot_scope": "native-occupied-slots-v1",
                "boss_experience": "destination-hp-v1"}
    if mode in (MIXED_FIESTA, REMIXED_FIESTA):
        settings["job_rewards"] = "fiesta-wind-v1" if mixed else "fiesta-start-v1"
    return settings


def build_reward_campaign(text: str, mode: str) -> dict:
    """Compose the new reward catalog; historical tables and domains stay frozen."""
    from . import enemy_loot, event_rewards
    from .runtime_seed import select_starting_jobs
    original_mode = base_mode(mode)
    request = build_boss_campaign(text, original_mode)
    seed = int(request["seed"])
    ordinary = [row for row in request["placements"]
                if row["location_id"].startswith(("native_treasure_", "native_cache_"))]
    ordinary_ids = {row["location_id"] for row in ordinary}
    retained = [row for row in request["placements"] if row["location_id"] not in ordinary_ids]
    if mode in (MIXED_FIESTA, REMIXED_FIESTA):
        selected = select_starting_jobs(seed, range(2, 23))
        retained = [row for row in retained if row["reward_kind"] != "job"]
        if mode == MIXED_FIESTA:
            retained.extend({"location_id": location, "reward_kind": "job",
                             "reward_id": str(job), "quantity": 1}
                            for location, job in zip(FIESTA_WIND_LOCATIONS, selected, strict=True))
        else:
            request["starting_job_ids"] = selected
    request["mode"] = mode
    request["catalog_id"] = REWARD_CATALOGS[mode]
    request["settings"] = reward_settings(mode)
    request["placements"] = sorted(retained + event_rewards.generate(seed, ordinary) + enemy_loot.generate(seed),
                                   key=lambda row: row["location_id"])
    request["placement_hash"] = placement_hash(request)
    return validate_reward_campaign(request)


def validate_reward_campaign(value: object) -> dict:
    """Validate saved tables independently; never regenerate any saved reward."""
    from . import bosses, enemy_loot, event_rewards, remixed_jobs, shops, world_travel
    fields = {"schema_version", "seed", "generator", "mode", "settings", "catalog_id",
              "starting_job_ids", "placements", "placement_hash"}
    if not isinstance(value, dict) or set(value) != fields:
        raise ValueError("Unsupported expanded-reward request fields.")
    mode = value["mode"]
    if not isinstance(mode, str) or mode not in MODES:
        raise ValueError("Unsupported expanded-reward mode.")
    mixed = base_mode(mode) == MIXED
    fiesta = mode in (MIXED_FIESTA, REMIXED_FIESTA)
    if (value["catalog_id"] != REWARD_CATALOGS[mode] or value["generator"] != GENERATOR
            or type(value["schema_version"]) is not int or value["schema_version"] != (2 if mixed else 3)
            or value["settings"] != reward_settings(mode)):
        raise ValueError("Expanded-reward identity or settings disagree.")
    valid, seed = try_parse_seed(value["seed"])
    if not valid or canonical_seed(seed) != value["seed"]:
        raise ValueError("Campaign seed must be canonical uint64 decimal text.")
    starters = value["starting_job_ids"]
    if mixed:
        if starters != []:
            raise ValueError("Mixed jobs must unlock at Wind, not before it.")
    else:
        remixed_jobs.validate_starters(starters)
    rows = value["placements"]
    if not isinstance(rows, list) or not rows or len(rows) > 4096:
        raise ValueError("Invalid expanded-reward placement table.")
    groups = {kind: [] for kind in ("job", "key", "shop", "boss", "loot", "ordinary")}
    seen = set()
    for row in rows:
        if not isinstance(row, dict) or set(row) != {"location_id", "reward_kind", "reward_id", "quantity"}:
            raise ValueError("Invalid expanded-reward placement fields.")
        location, kind = row["location_id"], row["reward_kind"]
        if (not isinstance(location, str) or not location or location in seen
                or not isinstance(kind, str) or not isinstance(row["reward_id"], str)
                or type(row["quantity"]) is not int):
            raise ValueError("Invalid or duplicate expanded-reward placement.")
        seen.add(location)
        if kind in ("job", "key", "shop", "boss", "loot"):
            groups[kind].append(row)
        elif kind in ("item", "gil", "magic", "empty"):
            groups["ordinary"].append(row)
        else:
            raise ValueError("Unknown expanded reward kind.")
    jobs = groups["job"]
    if mixed:
        expected = set(FIESTA_WIND_LOCATIONS) if fiesta else {f"native_job_slot_{job:02d}" for job in range(2, 23)}
        if {row["location_id"] for row in jobs} != expected:
            raise ValueError("Mixed job locations disagree with the selected job policy.")
        unlocked = []
        for row in jobs:
            if row["quantity"] != 1 or row["reward_id"] not in {str(job) for job in range(2, 23)}:
                raise ValueError("Mixed crystal requires one valid job.")
            unlocked.append(int(row["reward_id"]))
        if len(set(unlocked)) != (4 if fiesta else 21):
            raise ValueError("The job policy requires distinct unlockable jobs.")
        if groups["key"]:
            raise ValueError("Mixed progression retains native keys.")
    elif fiesta:
        if jobs:
            raise ValueError("Remixed-Fiesta has no later job crystals.")
        world_travel.validate_fiesta_placements(world_travel.read_catalog(), starters, groups["key"])
    else:
        world_travel.validate_placements(world_travel.read_catalog(), starters, jobs, groups["key"])
    event_rewards.validate_placements(groups["ordinary"])
    shops.validate_placements(shops.read_catalog(), groups["shop"])
    bosses.validate_placements(groups["boss"], base_mode(mode))
    enemy_loot.validate_placements(groups["loot"])
    ordinary_ids = {row["location_id"] for row in groups["ordinary"]}
    for bonus in groups["key"] + ([] if mixed else jobs):
        base = bonus["location_id"].removeprefix(remixed_jobs.PREFIX).removeprefix(world_travel.PREFIX)
        if base not in ordinary_ids:
            raise ValueError("A progression bonus replaced its ordinary reward.")
    if value["placement_hash"] != placement_hash(value):
        raise ValueError("Expanded-reward placement hash mismatch.")
    return value
