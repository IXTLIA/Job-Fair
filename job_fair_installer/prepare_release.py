"""Build exact runtime payload and copy/insert startup patches; never installs."""
import base64
import json
from pathlib import Path
import shutil
import sys
from importlib.metadata import distributions

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "job_fair_launcher"))
from core import sha, digest, apply_delta, MAGICITE, BUNDLES

def delta(base, data):
    width = 24
    lookup = {}
    for i in range(len(base) - width + 1):
        lookup.setdefault(base[i:i + width], i)
    ops, literal = [], bytearray()
    pos = 0
    while pos < len(data):
        at = lookup.get(data[pos:pos + width]) if pos + width <= len(data) else None
        if at is None:
            literal.append(data[pos])
            pos += 1
            continue
        if literal:
            ops.append(["insert", base64.b64encode(literal).decode()])
            literal.clear()
        length = width
        while at + length < len(base) and pos + length < len(data) and base[at + length] == data[pos + length]:
            length += 1
        ops.append(["copy", at, length])
        pos += length
    if literal:
        ops.append(["insert", base64.b64encode(literal).decode()])
    result = {"base_sha256": sha(base), "sha256": sha(data), "ops": ops}
    assert apply_delta(base, result) == data
    return result

def main():
    import argparse
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path, help="New payload directory")
    parser.add_argument("--runtime", type=Path, default=ROOT / "ffv_runtime/build/JobFair.Runtime.dll")
    args = parser.parse_args()
    output = args.output.resolve()
    shutil.copytree(ROOT / "job_fair_installer/payload_template", output)
    manifest = json.loads((output / "payload.json").read_text(encoding="utf-8"))
    entry = next(row for row in manifest["files"] if row["destination"].endswith("JobFair.Runtime.dll"))
    runtime_target = output / entry["source"]
    runtime_target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(args.runtime, runtime_target)
    entry["sha256"] = digest(runtime_target)
    (output / "payload.json").write_text(json.dumps(manifest, separators=(",", ":")), encoding="utf-8")
    # Keep all dependency license files from the actual build environment.
    notices = output.parent / "notices"
    notices.mkdir()
    provenance = []
    for dist in distributions():
        found = []
        for file in dist.files or []:
            if any(token in file.name.lower() for token in ("license", "copying", "notice", "copyright")) and file.suffix.lower() not in (".py", ".pyc"):
                path = Path(dist.locate_file(file))
                if not path.is_file():
                    continue
                dest = notices / dist.metadata["Name"] / str(file).replace("../", "parent/")
                dest.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(path, dest)
                found.append(dest.relative_to(notices).as_posix())
        provenance.append({"name": dist.metadata["Name"], "version": dist.version, "license_files": found,
                           "license": dist.metadata.get("License-Expression") or dist.metadata.get("License"),
                           "homepage": dist.metadata.get("Home-page")})
    (notices / "components.json").write_text(json.dumps(provenance, indent=2), encoding="utf-8")
    print(json.dumps({"payload_files": len(manifest["files"]), "patches": len(manifest["patches"]), "patch_bytes": (output / "payload.json").stat().st_size}))

if __name__ == "__main__":
    main()
