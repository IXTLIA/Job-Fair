"""Regenerate/audit minimal launcher catalogs, or copy them into a source export.

Only --regenerate/--audit needs developer research. Normal export/build uses the
checked-in, hash-verified data and never needs any original game scripts.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import shutil
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "ffv_randomizer"))
from ffv_randomizer import resources


def _bytes(value: object) -> bytes:
    return (json.dumps(value, indent=2, ensure_ascii=True) + "\n").encode("utf-8")


def _pick(row: dict, *keys: str) -> dict:
    return {key: row[key] for key in keys}


def audited_resources() -> dict[str, bytes]:
    """Project numerical identities/pools only, after the original native audit."""
    from ffv_randomizer.jobs import build_job_probe
    research = ROOT / "research/ff5"
    originals = {}

    def source(relative):
        raw = (research / relative).read_bytes()
        originals[relative] = hashlib.sha256(raw).hexdigest()
        return json.loads(raw)

    mixed = source("rewards/mixed_runtime_catalog.json")
    world = source("rewards/world_runtime_catalog.json")
    shops = source("shops/shop_runtime_catalog.json")
    jobs = source("rewards/remixed_jobs_catalog.json")
    travel = source("rewards/world_travel_catalog.json")
    bosses = source("bosses/catalog_runtime.json")
    loot = source("loot/catalog_runtime.json")
    events = source("events_rewards/catalog_runtime.json")
    native = source("events/native_operations.json")
    probe = build_job_probe(research / "events/extracted/assets/Assets/GameAssets/Serial", native, 0)
    catalogs = {
        "mixed_catalog.json": {"catalog_id": mixed["catalog_id"], "wind_chests": [
            _pick(row, "id", "content_id", "quantity") for row in mixed["wind_chests"]]},
        "world_catalog.json": {"catalog_id": world["catalog_id"], "locations": [
            {**_pick(row, "id", "world", "native_reward"),
             **({"native_reward_variants": row["native_reward_variants"]} if "native_reward_variants" in row else {}),
             "bindings": [{"id": binding["id"], "source": {"map_name": binding["source"]["map_name"]}}
                          for binding in row["bindings"]]} for row in world["locations"]]},
        "shop_catalog.json": {"catalog_id": shops["catalog_id"],
            "shops": [_pick(row, "id", "world", "shop_type", "slot_ids") for row in shops["shops"]],
            "slots": [_pick(row, "id", "shop_id", "content_id", "world", "shop_type") for row in shops["slots"]]},
        "remixed_jobs_catalog.json": {**_pick(jobs, "catalog_id", "quotas"), "bonus_candidates": [
            _pick(row, "id", "ordinary_id", "world", "region") for row in jobs["bonus_candidates"]]},
        "world_travel_catalog.json": {"catalog_id": travel["catalog_id"],
            "keys": [_pick(row, "id", "allowed_worlds") for row in travel["keys"]],
            "key_candidates": [_pick(row, "id", "ordinary_id", "world", "region") for row in travel["key_candidates"]],
            "anchors": [_pick(row, "world", "name") for row in travel["anchors"]]},
        "native_job_model.json": probe["model"],
        "enemy_loot_catalog.json": loot,
        "event_reward_catalog.json": {
            **_pick(events, "catalog_id", "schema_version", "source_pins", "magic_content_ids"),
            "locations": [_pick(row, "id", "world", "native_reward") for row in events["locations"]]},
        "boss_catalog.json": {**_pick(bosses, "catalog_id", "schema_version"),
            "bosses": [{**_pick(row, "boss_id", "reference_rank", "name", "tier", "world",
                               "formation_id", "formation_alias_ids", "monster_ids",
                               "initial_monster_ids", "profile_monster_ids",
                               "native_hp_budget", "effective_hp_budget", "requirements"),
                        "stats": [_pick(stat, "monster_id", "lv", "hp", "mp", "attack", "attack_count", "magic",
                                        "defense", "ability_defense", "agility") for stat in row["stats"]]}
                       for row in bosses["bosses"]],
            "incompatible_placements": [_pick(row, "boss_id", "destination_id", "modes", "reason", "evidence")
                                        for row in bosses.get("incompatible_placements", [])]},
    }
    output = {name: _bytes(value) for name, value in catalogs.items()}
    output[resources.MANIFEST_FILE] = _bytes({"format_version": 1,
        "sha256": {name: hashlib.sha256(raw).hexdigest() for name, raw in output.items()},
        "audited_source_sha256": originals})
    return output


def export_resources(destination: Path | str) -> dict[str, str]:
    """Copy only the allowlisted files into the exact destination data directory."""
    destination = Path(destination)
    hashes = resources.verify_all()
    names = (*resources.CATALOG_FILES, resources.MANIFEST_FILE)
    if destination.exists() and any(path.name not in names or not path.is_file()
                                    for path in destination.iterdir()):
        raise ValueError("Resource destination contains files outside the catalog allowlist.")
    destination.mkdir(parents=True, exist_ok=True)
    for name in names:
        source = resources.DATA_ROOT / name
        target = destination / name
        if source.resolve() != target.resolve():
            shutil.copyfile(source, target)
    resources.verify_all(destination)
    return {**hashes, resources.MANIFEST_FILE:
            hashlib.sha256((destination / resources.MANIFEST_FILE).read_bytes()).hexdigest()}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    operation = parser.add_mutually_exclusive_group(required=True)
    operation.add_argument("--regenerate", action="store_true")
    operation.add_argument("--audit", action="store_true")
    operation.add_argument("--export", type=Path)
    args = parser.parse_args()
    if args.export is not None:
        export_resources(args.export)
    else:
        expected = audited_resources()
        if args.regenerate:
            resources.DATA_ROOT.mkdir(parents=True, exist_ok=True)
            for name, raw in expected.items():
                (resources.DATA_ROOT / name).write_bytes(raw)
        elif any((resources.DATA_ROOT / name).read_bytes() != raw for name, raw in expected.items()):
            raise ValueError("Curated launcher catalogs differ from the audited source projection.")
        resources.verify_all()
    print("Launcher resources verified.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
