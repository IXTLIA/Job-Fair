"""Pin source inputs, copy player guides and record a portable build's hashes."""
from __future__ import annotations

import argparse
import hashlib
from importlib.metadata import version
import json
from pathlib import Path
import shutil
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "ffv_randomizer"))
from ffv_randomizer import resources

GENERATOR_MODULES = ("__init__", "bosses", "campaign", "enemy_loot", "event_rewards", "fixed_karnak", "logic", "remixed_jobs",
                     "resources", "runtime_seed", "shops", "world_travel", "world_treasure")
LAUNCHER_FILES = ("launcher.py", "model.py", "installations.py", "README.md", "SHARING.md")
PACKAGING_FILES = ("entrypoint.py", "JobFairLauncher.spec", "build_portable.ps1", "build_requirements.txt",
                   "prepare_resources.py", "parity_vectors.json", "boss_parity_vectors.json", "rewards_parity_vectors.json", "write_build_receipt.py")
NOTICE_FILES = ("Tcl-license.terms", "OpenSSL-LICENSE.txt", "zlib-LICENSE.txt", "provenance.json")


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def source_snapshot() -> dict:
    paths = ["REMIXED_PLAYTEST.md", "BOSS_SHUFFLE_PLAYTEST.md", "FIESTA_REWARDS_PLAYTEST.md", "KICK_PLAYTEST.md", "CONTROLLER_PLAYTEST.md", "WORLD_MAP_PLAYTEST.md"]
    paths += ["job_fair_launcher/" + name for name in LAUNCHER_FILES]
    paths += ["job_fair_launcher/packaging/" + name for name in PACKAGING_FILES]
    paths += ["job_fair_launcher/packaging/notices/" + name for name in NOTICE_FILES]
    paths += ["ffv_randomizer/ffv_randomizer/" + name + ".py" for name in GENERATOR_MODULES]
    paths += ["ffv_randomizer/ffv_randomizer/data/" + name
              for name in (*resources.CATALOG_FILES, resources.MANIFEST_FILE)]
    return {name: digest(ROOT / name) for name in sorted(paths)}


def write(path: Path, value: dict) -> None:
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--inputs", required=True, type=Path)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--capture", action="store_true")
    mode.add_argument("--finish", type=Path, metavar="PORTABLE_FOLDER")
    mode.add_argument("--archive", type=Path, metavar="ZIP")
    args = parser.parse_args()
    if args.capture:
        if args.inputs.exists():
            raise ValueError("Build input receipt already exists.")
        write(args.inputs, {"format_version": 1, "source_sha256": source_snapshot()})
    elif args.finish is not None:
        recorded = json.loads(args.inputs.read_bytes())
        if recorded["source_sha256"] != source_snapshot():
            raise ValueError("Source inputs changed during the build; build again before sharing.")
        for source in ("job_fair_launcher/README.md", "job_fair_launcher/SHARING.md",
                       "REMIXED_PLAYTEST.md", "BOSS_SHUFFLE_PLAYTEST.md", "FIESTA_REWARDS_PLAYTEST.md", "KICK_PLAYTEST.md", "CONTROLLER_PLAYTEST.md", "WORLD_MAP_PLAYTEST.md"):
            shutil.copyfile(ROOT / source, args.finish / Path(source).name)
        write(args.finish / "BUILD_INFO.json", {**recorded,
              "python": sys.version.split()[0], "pyinstaller": version("pyinstaller"),
              "runtime_required": "Job Fair 0.9.5 (separate installation)",
              "launcher_exe_sha256": digest(args.finish / "JobFairLauncher.exe")})
    else:
        info = json.loads((args.archive.parent / "JobFairLauncher/BUILD_INFO.json").read_bytes())
        write(args.inputs.parent / "package_receipt.json", {**info,
              "archive": args.archive.name, "archive_sha256": digest(args.archive),
              "archive_bytes": args.archive.stat().st_size,
              "self_check": json.loads((args.inputs.parent / "self-check.json").read_bytes())})
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
