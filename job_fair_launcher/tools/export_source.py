"""Export the explicit, game-file-free Job Fair launcher source set.

This command creates a new directory or ZIP. It never initializes a repository,
publishes files, copies profiles, installs the mod or changes a game directory.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import shutil
import stat
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parents[2]
SOURCE_FILES = (
    "LICENSE",
    "REMIXED_PLAYTEST.md",
    "BOSS_SHUFFLE_PLAYTEST.md",
    "FIESTA_REWARDS_PLAYTEST.md",
    "KICK_PLAYTEST.md",
    "CONTROLLER_PLAYTEST.md",
    "WORLD_MAP_PLAYTEST.md",
    "job_fair_launcher/launcher.py",
    "job_fair_launcher/model.py",
    "job_fair_launcher/installations.py",
    "job_fair_launcher/LaunchJobFair.ps1",
    "job_fair_launcher/README.md",
    "job_fair_launcher/SHARING.md",
    "job_fair_launcher/tools/export_source.py",
    "job_fair_launcher/tests/test_source_export.py",
    "job_fair_launcher/tests/test_installations.py",
    "job_fair_launcher/tests/test_boss_launcher.py",
    "job_fair_launcher/packaging/entrypoint.py",
    "job_fair_launcher/packaging/parity_vectors.json",
    "job_fair_launcher/packaging/boss_parity_vectors.json",
    "job_fair_launcher/packaging/rewards_parity_vectors.json",
    "job_fair_launcher/packaging/JobFairLauncher.spec",
    "job_fair_launcher/packaging/build_portable.ps1",
    "job_fair_launcher/packaging/build_requirements.txt",
    "job_fair_launcher/packaging/prepare_resources.py",
    "job_fair_launcher/packaging/test_portable.py",
    "job_fair_launcher/packaging/write_build_receipt.py",
    "job_fair_launcher/packaging/notices/Tcl-license.terms",
    "job_fair_launcher/packaging/notices/OpenSSL-LICENSE.txt",
    "job_fair_launcher/packaging/notices/zlib-LICENSE.txt",
    "job_fair_launcher/packaging/notices/provenance.json",
    "ffv_randomizer/ffv_randomizer/__init__.py",
    "ffv_randomizer/ffv_randomizer/campaign.py",
    "ffv_randomizer/ffv_randomizer/bosses.py",
    "ffv_randomizer/ffv_randomizer/enemy_loot.py",
    "ffv_randomizer/ffv_randomizer/event_rewards.py",
    "ffv_randomizer/ffv_randomizer/resources.py",
    "ffv_randomizer/ffv_randomizer/logic.py",
    "ffv_randomizer/ffv_randomizer/runtime_seed.py",
    "ffv_randomizer/ffv_randomizer/world_treasure.py",
    "ffv_randomizer/ffv_randomizer/fixed_karnak.py",
    "ffv_randomizer/ffv_randomizer/remixed_jobs.py",
    "ffv_randomizer/ffv_randomizer/world_travel.py",
    "ffv_randomizer/ffv_randomizer/shops.py",
    "ffv_randomizer/tests/test_logic.py",
    "ffv_randomizer/tests/test_fixed_karnak.py",
    "ffv_randomizer/tests/test_bosses.py",
    "ffv_randomizer/tests/test_boss_campaign.py",
)
DATA_FILES = (
    "mixed_catalog.json", "world_catalog.json", "shop_catalog.json",
    "remixed_jobs_catalog.json", "world_travel_catalog.json", "native_job_model.json",
    "boss_catalog.json", "enemy_loot_catalog.json", "event_reward_catalog.json",
    "catalog_manifest.json",
)
DATA_DIRECTORY = "ffv_randomizer/ffv_randomizer/data"

README = """# Job Fair launcher source

This curated source package can prepare Job Fair seeds and build the Windows
launcher. It requires an already-modded, supported Steam installation of Final
Fantasy V Pixel Remaster to apply a seed. It does not include or install the game,
the Job Fair runtime, BepInEx, Magicite or startup overrides.

Start with [setup, build, test and sharing instructions](job_fair_launcher/SHARING.md).
`SOURCE_MANIFEST.json` records every exported file's SHA-256 and size. The export
contains no existing profiles, saves, logs, backups or extracted game programs.

Job Fair original code is MIT licensed; see LICENSE. Third-party components
retain their own notices. This export does not publish anything.
"""

GITIGNORE = """# Generated files and private local state
.venv/
**/.venv/
**/.build-venv/
__pycache__/
*.pyc
*.egg-info/
build/
dist/
**/build/
**/dist/
job_fair_launcher/data/
*.log
"""


def _unlinked(path: Path) -> None:
    for part in (path, *path.parents):
        if part.exists() or part.is_symlink():
            attributes = getattr(part.lstat(), "st_file_attributes", 0)
            if part.is_symlink() or attributes & getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400):
                raise ValueError(f"Linked export paths are not supported: {part}")


def _source(relative: str) -> Path:
    path = ROOT.joinpath(*relative.split("/"))
    _unlinked(path)
    if not path.is_file() or not path.resolve().is_relative_to(ROOT.resolve()):
        raise ValueError(f"Missing or uncontained export source: {relative}")
    return path


def _check_text(relative: str, payload: bytes) -> None:
    try:
        text = payload.decode("utf-8-sig")
    except UnicodeDecodeError as error:
        raise ValueError(f"Binary file is not allowed in source export: {relative}") from error
    # The VDF parser test contains two literal, synthetic Windows paths. They
    # are inputs to an in-memory parser, never local installation destinations.
    scanned = text
    if relative == "job_fair_launcher/tests/test_installations.py":
        scanned = re.sub(r"\b(?:[C]:\\+Steam|[D]:\\+Games)\b", "fixture-path", scanned)
    if "\0" in text or re.search(r"(?<![A-Za-z0-9_])[A-Za-z]:[\\/]", scanned):
        raise ValueError(f"Absolute machine path or binary content in source export: {relative}")


def _write(path: Path, payload: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(payload)


def _check_catalog(value: object) -> None:
    if isinstance(value, dict):
        if {str(key).casefold() for key in value} & {"mnemonics", "mnemonic", "operands", "scripts"}:
            raise ValueError("Native program records are not allowed in source catalogs.")
        for item in value.values():
            _check_catalog(item)
    elif isinstance(value, list):
        for item in value:
            _check_catalog(item)


def _prepare(staging: Path) -> dict:
    for relative in SOURCE_FILES:
        payload = _source(relative).read_bytes()
        _check_text(relative, payload)
        _write(staging / relative, payload)
    for name in DATA_FILES:
        _source(f"{DATA_DIRECTORY}/{name}")
    helper_path = _source("job_fair_launcher/packaging/prepare_resources.py")
    spec = importlib.util.spec_from_file_location("jobfair_export_resources", helper_path)
    if spec is None or spec.loader is None:
        raise ValueError("Could not load the catalog export helper.")
    helper = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(helper)
    helper.export_resources(staging / DATA_DIRECTORY)
    if {path.name for path in (staging / DATA_DIRECTORY).iterdir()} != set(DATA_FILES):
        raise ValueError("Catalog export differs from the explicit data allowlist.")
    for name in DATA_FILES:
        _check_catalog(json.loads((staging / DATA_DIRECTORY / name).read_bytes()))
    _write(staging / "README.md", README.encode("utf-8"))
    _write(staging / ".gitignore", GITIGNORE.encode("utf-8"))
    files = []
    for path in sorted(staging.rglob("*")):
        if not path.is_file():
            continue
        relative = path.relative_to(staging).as_posix()
        payload = path.read_bytes()
        _check_text(relative, payload)
        files.append({"path": relative, "size": len(payload),
                      "sha256": hashlib.sha256(payload).hexdigest()})
    manifest = {"format_version": 1, "kind": "job-fair-launcher-source",
                "scope": "launcher-source-and-data-catalogs-only", "files": files}
    _write(staging / "SOURCE_MANIFEST.json", (json.dumps(manifest, indent=2) + "\n").encode("utf-8"))
    return manifest


def export_source(destination: Path, *, archive: bool = False) -> dict:
    """Create a fresh source export; reject existing targets and linked paths."""
    destination = Path(destination).absolute()
    _unlinked(destination)
    if destination.exists():
        raise ValueError("Export destination already exists; choose a new directory or ZIP.")
    if archive and destination.suffix.lower() != ".zip":
        raise ValueError("Archive exports require a .zip destination.")
    with tempfile.TemporaryDirectory(prefix="jobfair-source-") as temporary:
        staging = Path(temporary)
        manifest = _prepare(staging)
        _unlinked(destination)
        destination.parent.mkdir(parents=True, exist_ok=True)
        if archive:
            with zipfile.ZipFile(destination, "x", compression=zipfile.ZIP_DEFLATED) as package:
                for path in sorted(staging.rglob("*")):
                    if path.is_file():
                        info = zipfile.ZipInfo(path.relative_to(staging).as_posix(), (1980, 1, 1, 0, 0, 0))
                        info.compress_type = zipfile.ZIP_DEFLATED
                        info.external_attr = 0o100644 << 16
                        package.writestr(info, path.read_bytes())
        else:
            shutil.copytree(staging, destination)
    return manifest


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("destination", type=Path, help="A new directory, or a new .zip with --zip")
    parser.add_argument("--zip", action="store_true", help="Create a ZIP instead of a directory")
    args = parser.parse_args()
    result = export_source(args.destination, archive=args.zip)
    print(json.dumps({"kind": result["kind"], "files": len(result["files"]),
                      "destination": str(args.destination), "published": False}, indent=2))


if __name__ == "__main__":
    main()
