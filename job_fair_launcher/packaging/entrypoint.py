"""Portable launcher entry point; self-check never imports or opens the UI."""
from __future__ import annotations

import argparse
from contextlib import ExitStack
import json
import os
from pathlib import Path
import sys
import tempfile

if not getattr(sys, "frozen", False):
    _root = Path(__file__).resolve().parents[2]
    sys.path.insert(0, str(_root / "ffv_randomizer"))
    sys.path.insert(0, str(_root / "job_fair_launcher"))


def check_ui() -> dict:
    """Construct real controls, with all external I/O blocked, while withdrawn."""
    from unittest.mock import patch
    with tempfile.TemporaryDirectory(prefix="JobFair-ui-check-") as directory, ExitStack() as guards:
        guards.enter_context(patch.dict(os.environ, {"LOCALAPPDATA": directory, "APPDATA": directory}))
        import tkinter as tk
        import launcher
        import model
        import installations
        violations = []

        def forbidden(*args, **kwargs):
            violations.append("Forbidden external I/O")
            raise RuntimeError("UI diagnostic attempted a dialog, settings write or game action.")

        for name in ("atomic_write", "apply_next_run", "save_profile", "import_profile", "save_game_folder", "read_json"):
            guards.enter_context(patch.object(model, name, side_effect=forbidden))
        for name, value in (("load_game_folder", None), ("current_request", None),
                            ("list_profiles", ([], [])), ("generate_seed", 8)):
            guards.enter_context(patch.object(model, name, return_value=value))
        guards.enter_context(patch.object(installations, "discover_installations", return_value=[]))
        guards.enter_context(patch.object(launcher.messagebox, "_show", side_effect=forbidden))
        for name in ("askdirectory", "askopenfilename"):
            guards.enter_context(patch.object(launcher.filedialog, name, side_effect=forbidden))
        window = tk.Tk()
        window.withdraw()
        window.report_callback_exception = lambda kind, value, traceback: violations.append(str(value))
        checked = []
        try:
            ui = launcher.Launcher(window)
            for mode in (model.campaign.MIXED, model.campaign.REMIXED, model.campaign.MIXED_FIESTA, model.campaign.REMIXED_FIESTA):
                ui.mode.set(mode)
                window.update_idletasks()
                width, height = window.winfo_reqwidth(), window.winfo_reqheight()
                if (window.state() != "withdrawn" or window.winfo_ismapped()
                        or not 1 < width <= launcher.MINIMUM_SIZE[0] or not 1 < height <= launcher.MINIMUM_SIZE[1]
                        or window.minsize() != launcher.MINIMUM_SIZE):
                    raise ValueError("Launcher controls do not fit the withdrawn minimum-size window.")
                if not ui.jobs.get() or not ui.apply_button.instate(["disabled"]):
                    raise ValueError("The offline launcher preview or Apply state is incorrect.")
                checked.append({"mode": mode, "state": window.state(), "requested_size": [width, height]})
        finally:
            window.destroy()
        if violations:
            raise ValueError("Launcher callback failed or attempted external I/O during the UI diagnostic.")
        if list(Path(directory).iterdir()):
            raise ValueError("UI diagnostic wrote files in its isolated data directory.")
        return {"status": "passed", "minimum_size": list(launcher.MINIMUM_SIZE), "modes": checked,
                "dialogs": 0, "settings_writes": 0, "game_actions": 0}


def self_check(*, include_ui: bool = False) -> dict:
    from ffv_randomizer import campaign, resources
    hashes = resources.verify_all()
    vectors = json.loads(Path(__file__).with_name("parity_vectors.json").read_bytes())
    vectors += json.loads(Path(__file__).with_name("boss_parity_vectors.json").read_bytes())
    vectors += json.loads(Path(__file__).with_name("rewards_parity_vectors.json").read_bytes())
    checked = []
    for vector in vectors:
        request = campaign.build_request(vector["seed"], vector["mode"], catalog_id=vector["catalog_id"])
        campaign.validate_request(request)
        if request["placement_hash"] != vector["placement_hash"]:
            raise ValueError(f"Placement parity failed for {vector['mode']} seed {vector['seed']}.")
        checked.append({**vector, "placement_count": len(request["placements"])})
    report = {"status": "passed", "runtime_required": "Job Fair 0.9.5 (separate installation)",
              "frozen": bool(getattr(sys, "frozen", False)), "catalog_sha256": hashes,
              "verified_vectors": checked}
    if include_ui:
        report["ui_check"] = check_ui()
    return report


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Job Fair launcher")
    parser.add_argument("--self-check", "--smoke-test", action="store_true", dest="check",
                        help="Verify bundled catalogs and all four modes offline; no UI or settings writes.")
    parser.add_argument("--report-json", type=Path,
                        help="With --self-check, write the result to this explicit diagnostic file.")
    parser.add_argument("--check-ui", action="store_true",
                        help="With --self-check, also verify real Tk controls in a hidden window without external I/O.")
    args = parser.parse_args(argv)
    if args.report_json is not None and not args.check:
        parser.error("--report-json requires --self-check or --smoke-test")
    if args.check_ui and not args.check:
        parser.error("--check-ui requires --self-check or --smoke-test")
    if args.check:
        try:
            report = self_check(include_ui=args.check_ui)
            result = 0
        except Exception as error:
            report = {"status": "failed", "error": str(error)}
            result = 1
        text = json.dumps(report, indent=2) + "\n"
        if sys.stdout is not None:
            print(text, end="")
        if args.report_json is not None:
            args.report_json.write_text(text, encoding="utf-8")
        return result
    import tkinter as tk
    from launcher import Launcher
    window = tk.Tk()
    Launcher(window)
    window.mainloop()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
