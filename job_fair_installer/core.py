"""Portable tester installer. No game execution, seed application or save writes."""
from __future__ import annotations
import base64
import configparser
import csv
import hashlib
import io
import json
import os
from pathlib import Path, PurePosixPath
import shutil
import stat
import subprocess
import urllib.request
import uuid
import zipfile

from installations import validate_game_identity

VERSION = "0.9.5"
GAME_HASHES = {
    "GameAssembly.dll": "355bb0eccd3cf6a2cb70d48f2329379eb98d4b5085971d46985154a882dcfb17",
    "FINAL FANTASY V_Data/il2cpp_data/Metadata/global-metadata.dat": "65df1ceec31f0f40bc4dc881606708aa65fd9cd4e3aa4237d4d4fcf498cf91e3",
}
MAGICITE = "FINAL FANTASY V_Data/StreamingAssets/Magicite/"
BUNDLES = "FINAL FANTASY V_Data/StreamingAssets/aa/StandaloneWindows64/"
DEPENDENCIES = (
    ("https://github.com/BepInEx/BepInEx/releases/download/v6.0.0-pre.2/BepInEx-Unity.IL2CPP-win-x64-6.0.0-pre.2.zip",
     "616ec7eb06cf11b2a0000e8fcef04d1b12bb58e84a2e0bdac9523234fc193ceb", 34146254),
    ("https://github.com/Silvris/Magicite/releases/download/v2.2.0/Magicite-2-2-0.zip",
     "7ba1b73f92638551daf4e6dc095bf7164fe093d29c26ce4dfba1ddc455c0f54c", 34442),
)

def sha(data):
    return hashlib.sha256(data).hexdigest()

def digest(path):
    with Path(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()

def safe_relative(name):
    p = PurePosixPath(name)
    if (not name or "\\" in name or ":" in name or p.is_absolute()
            or any(x in ("", ".", "..") or x.endswith((" ", ".")) for x in name.split("/"))
            or any(ord(c) < 32 for c in name)
            or any(x.split(".")[0].upper() in {"CON", "PRN", "AUX", "NUL", *(f"COM{i}" for i in range(10)), *(f"LPT{i}" for i in range(10))} for x in p.parts)):
        raise ValueError("Unsafe package path: " + name)
    return p

def target(root, name):
    root = Path(root).absolute()
    p = root.joinpath(*safe_relative(name).parts)
    check = p
    while True:
        if check.is_symlink() or (hasattr(check, "is_junction") and check.is_junction()):
            raise ValueError("Links/junctions are unsupported: " + str(check))
        if check == check.parent:
            break
        check = check.parent
    if not p.resolve().is_relative_to(root.resolve()):
        raise ValueError("Destination escapes its root.")
    return p

def game_closed():
    result = subprocess.run(["tasklist", "/FI", "IMAGENAME eq FINAL FANTASY V.exe", "/FO", "CSV", "/NH"],
                            capture_output=True, text=True, check=True,
                            creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
    if any(row and row[0].casefold() == "final fantasy v.exe" for row in csv.reader(io.StringIO(result.stdout))):
        raise ValueError("Close FINAL FANTASY V before installing or restoring.")

def validate_game(game):
    game = validate_game_identity(game)
    for name, expected in GAME_HASHES.items():
        if digest(target(game, name)) != expected:
            raise ValueError("Unsupported or changed game file: " + name)
    return game

def apply_delta(base, patch):
    if sha(base) != patch["base_sha256"]:
        raise ValueError("Startup source does not match supported FFV build.")
    chunks = []
    for op in patch["ops"]:
        if op[0] == "copy":
            _, start, length = op
            if start < 0 or length < 0 or start + length > len(base):
                raise ValueError("Invalid patch copy range.")
            chunks.append(base[start:start + length])
        elif op[0] == "insert":
            chunks.append(base64.b64decode(op[1], validate=True))
        else:
            raise ValueError("Unknown patch operation.")
    data = b"".join(chunks)
    if sha(data) != patch["sha256"]:
        raise ValueError("Startup patch verification failed.")
    return data

def startup_payload(game, patches):
    import UnityPy
    result = {}
    for patch in patches:
        bundle = target(game, BUNDLES + patch["bundle"])
        if digest(bundle) != patch["bundle_sha256"]:
            raise ValueError("Unsupported startup bundle: " + bundle.name)
        # Bytes avoid keeping the user's bundle file handle open after extraction.
        environment = UnityPy.load(bundle.read_bytes())
        pointer = environment.container[patch["container_path"]]
        reader = pointer.deref() if hasattr(pointer, "deref") else pointer
        data = reader.parse_as_object().m_Script
        if isinstance(data, str):
            data = data.encode("utf-8", "surrogateescape")
        result[patch["destination"]] = apply_delta(bytes(data), patch)
    return result

def read_archive(data):
    result = {}
    seen = set()
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        for entry in archive.infolist():
            name = entry.filename.rstrip("/")
            safe_relative(name)
            key = name.casefold()
            if key in seen or stat.S_ISLNK(entry.external_attr >> 16):
                raise ValueError("Duplicate or linked archive member.")
            seen.add(key)
            if not entry.is_dir():
                result[name] = archive.read(entry)
    return result

def prepare(game, resources, log=print):
    game_closed()
    game = validate_game(game)
    manifest = json.loads((resources / "payload.json").read_text(encoding="utf-8"))
    existing_magicite = target(game, "BepInEx/config/silvris.magicite.cfg")
    if existing_magicite.exists():
        config = configparser.ConfigParser(interpolation=None)
        config.read(existing_magicite, encoding="utf-8-sig")
        if (config.get("Magicite Paths", "Import Directory", fallback="%StreamingAssets%/Magicite").replace("\\", "/") != "%StreamingAssets%/Magicite"
                or config.getboolean("General", "Export Enabled", fallback=False)):
            raise ValueError("Magicite uses custom import/export settings. Use its default import folder with export disabled.")
    payload = {}
    for url, expected, size in DEPENDENCIES:
        log("Downloading " + url.rsplit("/", 1)[1])
        request = urllib.request.Request(url, headers={"User-Agent": "JobFair-Tester-Installer/0.9.5"})
        with urllib.request.urlopen(request, timeout=90) as response:
            data = response.read(size + 1)
        if len(data) != size or sha(data) != expected:
            raise ValueError("Loader download failed its size/SHA-256 check.")
        for name, content in read_archive(data).items():
            if name in payload:
                raise ValueError("Overlapping dependency paths.")
            payload[name] = content
    log("Preparing verified startup changes from your FFV files...")
    payload.update(startup_payload(game, manifest["patches"]))
    for entry in manifest["files"]:
        content = target(resources, entry["source"]).read_bytes()
        if sha(content) != entry["sha256"]:
            raise ValueError("Damaged installer payload: " + entry["source"])
        payload[entry["destination"]] = content
    # Keep already-expanded compatible user loader configuration.
    for name in ("BepInEx/config/BepInEx.cfg", "BepInEx/config/silvris.magicite.cfg"):
        existing = target(game, name)
        if existing.exists():
            payload.pop(name, None)
    # Unknown loaders must not be silently converted, since first launch can regenerate interop.
    for name in ("winhttp.dll", "BepInEx/core/BepInEx.Core.dll", "BepInEx/plugins/Magicite.dll", "BepInEx/plugins/Syldra.dll"):
        existing = target(game, name)
        if existing.exists() and (name not in payload or digest(existing) != sha(payload[name])):
            raise ValueError("A different loader is installed: " + name + ". Use a clean Steam installation.")
    # Refuse other Magicite override mods; their precedence can invalidate Job Fair.
    mods = target(game, MAGICITE.rstrip("/"))
    if mods.exists() and any(p.name != "ProjectBluesStartupTrial" for p in mods.iterdir()):
        raise ValueError("Other Magicite mods are present. Use a separate clean installation for testing.")
    return game, payload

def write_json(path, value):
    temp = path.with_suffix(".tmp")
    temp.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")
    os.replace(temp, path)

def install(game, payload, backup_root, log=print):
    """Journal before every write; activate loader last. Caller supplies verified payload."""
    game_closed()
    backup_root = Path(backup_root).absolute()
    if backup_root.resolve().is_relative_to(game.resolve()) or game.resolve().is_relative_to(backup_root.resolve()):
        raise ValueError("Backups must be outside the game folder.")
    for name in payload:
        p = target(game, name)
        if p.exists() and not p.is_file():
            raise ValueError("A destination is not a file: " + name)
    folder = backup_root / uuid.uuid4().hex
    folder.mkdir(parents=True, exist_ok=False)
    journal = {"format": 1, "version": VERSION, "game": str(game), "status": "prepared", "files": []}
    journal_path = folder / "restore.json"
    for name in sorted(payload, key=lambda x: (x == "winhttp.dll", x)):
        p = target(game, name)
        before = digest(p) if p.exists() else None
        after = sha(payload[name])
        if before == after:
            continue
        if before is not None:
            saved = target(folder / "original", name)
            saved.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(p, saved)
            if digest(saved) != before:
                raise ValueError("Backup verification failed: " + name)
        journal["files"].append({"path": name, "before": before, "after": after})
    write_json(journal_path, journal)
    game_closed()
    try:
        for row in journal["files"]:
            p = target(game, row["path"])
            if (digest(p) if p.exists() else None) != row["before"]:
                raise ValueError("Destination changed after backup: " + row["path"])
            p.parent.mkdir(parents=True, exist_ok=True)
            journal["status"] = "installing"
            write_json(journal_path, journal)
            temp = p.with_name(p.name + ".jobfair-" + uuid.uuid4().hex)
            try:
                with temp.open("xb") as out:
                    out.write(payload[row["path"]])
                    out.flush()
                    os.fsync(out.fileno())
                os.replace(temp, p)
            finally:
                if temp.exists():
                    temp.unlink()
            if digest(p) != row["after"]:
                raise ValueError("Installed file failed verification: " + row["path"])
        journal["status"] = "installed"
        write_json(journal_path, journal)
    except Exception as error:
        log("Installation stopped. Recovery journal: " + str(journal_path))
        raise RuntimeError(str(error) + "\nRecovery journal: " + str(journal_path)) from error
    log("Installed Job Fair " + VERSION + ". Backup: " + str(journal_path))
    return journal_path

def restore(journal_path, log=print):
    game_closed()
    journal_path = Path(journal_path)
    journal = json.loads(journal_path.read_text(encoding="utf-8"))
    if journal.get("format") != 1 or journal.get("status") == "restored":
        raise ValueError("Unsupported or already restored backup.")
    game = validate_game(Path(journal["game"]))
    # Validate every file before changing anything; preserve later updates and user changes.
    for row in journal["files"]:
        p = target(game, row["path"])
        current = digest(p) if p.exists() else None
        if current not in (row["before"], row["after"]) and row["path"].startswith("BepInEx/config/") and row["before"] is None:
            row["retain_generated_config"] = True
        elif current not in (row["before"], row["after"]):
            raise ValueError("File changed since installation; preserve it before restoring: " + str(p))
        if row["before"] is not None and digest(target(journal_path.parent / "original", row["path"])) != row["before"]:
            raise ValueError("Backup is missing or damaged: " + row["path"])
    for row in reversed(journal["files"]):
        if row.get("retain_generated_config"):
            continue
        p = target(game, row["path"])
        if row["before"] is None:
            if p.exists():
                p.unlink()
        else:
            shutil.copy2(target(journal_path.parent / "original", row["path"]), p)
            if digest(p) != row["before"]:
                raise ValueError("Restore verification failed.")
    journal["status"] = "restored"
    write_json(journal_path, journal)
    log("Restored replaced files and removed installer-added files. Generated logs/cache and saves were retained.")
