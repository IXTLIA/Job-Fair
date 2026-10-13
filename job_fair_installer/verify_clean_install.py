"""Full download/install/restore rehearsal in a temporary Steam-shaped fixture."""
import json
from pathlib import Path
import shutil
import sys
import tempfile
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "job_fair_launcher"))
import core

def rehearse(resources, real_game, report_path, log=print):
    recipe = json.loads((resources / "payload.json").read_text(encoding="utf-8"))
    with tempfile.TemporaryDirectory(prefix="JobFair-clean-install-") as temp:
        root = Path(temp)
        game = root / "steamapps/common/FINAL FANTASY V PR"
        game.mkdir(parents=True)
        (root / "steamapps/appmanifest_1173810.acf").write_text('"AppState" { "appid" "1173810" "buildid" "20265087" "installdir" "FINAL FANTASY V PR" }')
        # This fixture cannot launch a game; only read-only compatibility inputs are copied.
        (game / "FINAL FANTASY V.exe").write_bytes(b"Non-executable test sentinel")
        for name in (*core.GAME_HASHES, *(core.BUNDLES + p["bundle"] for p in recipe["patches"])):
            dest = game / name
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(real_game / name, dest)
        def inventory():
            return {p.relative_to(game).as_posix(): core.digest(p) for p in game.rglob("*") if p.is_file()}
        before = inventory()
        validated, payload = core.prepare(game, resources, log)
        receipt = core.install(validated, payload, root / "backups", log)
        assert all(core.digest(game / p) == core.sha(data) for p, data in payload.items())
        assert all(core.digest(game / p) == value for p, value in before.items())
        installed = inventory()
        core.restore(receipt, log)
        assert inventory() == before
        result = {"status": "passed", "dependency_downloads_verified": len(core.DEPENDENCIES),
                  "payload_files": len(payload), "fixture_original_files": len(before),
                  "installed_files": len(installed), "restore_exact": True,
                  "native_game_launch": False, "owner_game_written": False}
        report_path.write_text(json.dumps(result, indent=2), encoding="utf-8")
        log(json.dumps(result))
    return result

if __name__ == "__main__":
    rehearse(*map(Path, sys.argv[1:]))
