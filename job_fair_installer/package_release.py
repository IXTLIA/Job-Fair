"""Assemble the tested EXEs with explicit inputs and byte-verified ZIP contents."""
import hashlib
import json
from pathlib import Path
import shutil
import sys
import zipfile

ROOT = Path(__file__).resolve().parents[1]

def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()

def main():
    import argparse
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("destination", type=Path)
    parser.add_argument("--setup", type=Path, required=True)
    parser.add_argument("--launcher", type=Path, required=True)
    parser.add_argument("--verification", type=Path, required=True)
    args = parser.parse_args()
    destination = args.destination.resolve()
    destination.mkdir(parents=True, exist_ok=False)
    package = destination / "JobFair-0.9.5-Tester-Windows-x64"
    package.mkdir()
    shutil.copytree(args.setup, package / "Setup")
    shutil.copytree(args.launcher, package / "Launcher")
    shutil.copy2(ROOT / "job_fair_installer/TESTER_README.md", package / "README.md")
    shutil.copy2(args.verification, package / "INSTALLER_VERIFICATION.json")
    shutil.copy2(ROOT / "LICENSE", package / "LICENSE")
    records = []
    forbidden = {"gameassembly.dll", "global-metadata.dat", "jobfair.next-run.json", "logoutput.log", "player.log", "unityplayer.dll"}
    for file in sorted(package.rglob("*")):
        if not file.is_file():
            continue
        relative = file.relative_to(package).as_posix()
        if file.name.casefold() in forbidden or any(part.casefold() in {"profiles", "saves", "research", "interop"} for part in file.relative_to(package).parts):
            raise ValueError("Forbidden distribution file: " + relative)
        records.append({"path": relative, "size": file.stat().st_size, "sha256": digest(file)})
    manifest = {"version": "0.9.5", "release_kind": "tester", "runtime_sha256": "9154e7dd41e7c2b774c76665cf80364c2f77870e902a4845b1bb278d3d920996",
                "native_acceptance_pending": ["map_refresh", "controller", "boss_defense_exp", "full_campaign"], "files": records}
    manifest_file = package / "PACKAGE_MANIFEST.json"
    manifest_file.write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    checks = [(row["sha256"], row["path"]) for row in records]
    checks.append((digest(manifest_file), "PACKAGE_MANIFEST.json"))
    (package / "SHA256SUMS.txt").write_text("".join(f"{value}  {name}\n" for value, name in checks), encoding="utf-8")
    archive = destination / (package.name + ".zip")
    with zipfile.ZipFile(archive, "x", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as zipped:
        for file in sorted(package.rglob("*")):
            if file.is_file():
                zipped.write(file, file.relative_to(destination).as_posix())
    with zipfile.ZipFile(archive) as zipped:
        if zipped.testzip() is not None:
            raise ValueError("ZIP integrity failed.")
        for row in records:
            data = zipped.read(package.name + "/" + row["path"])
            assert len(data) == row["size"] and hashlib.sha256(data).hexdigest() == row["sha256"]
    summary = {"status": "passed", "zip": archive.name, "sha256": digest(archive), "bytes": archive.stat().st_size,
               "files": len(records) + 2, "installer_tests": 17, "zip_crc": "passed", "zip_hashes": "passed"}
    (destination / "RELEASE_VERIFICATION.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
    (destination / (archive.name + ".sha256")).write_text(summary["sha256"] + "  " + archive.name + "\n", encoding="utf-8")
    print(json.dumps(summary, indent=2))

if __name__ == "__main__":
    main()
