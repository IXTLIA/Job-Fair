"""Guided installer UI. All game launch actions remain with the player."""
import argparse
import json
import os
from pathlib import Path
import queue
import sys
import tempfile
import threading
import tkinter as tk
from tkinter import filedialog, messagebox, ttk

from core import prepare, install, restore, VERSION, startup_payload, validate_game, digest
from installations import discover_installations

RESOURCES = Path(getattr(sys, "_MEIPASS", Path(__file__).parent)) / "payload"

class Installer:
    def __init__(self, root, discovery=True):
        self.root = root
        self.busy = False
        self.events = queue.Queue()
        root.title("Job Fair " + VERSION + " — Tester Setup")
        root.geometry("780x560")
        root.minsize(720, 520)
        panel = ttk.Frame(root, padding=18)
        panel.pack(fill="both", expand=True)
        ttk.Label(panel, text="Job Fair 0.9.5 tester release", font=("Segoe UI", 18, "bold")).pack(anchor="w")
        ttk.Label(panel, text="Requires Windows x64 and Steam FFV build 20265087. Close FFV first.\n"
                  "Setup downloads the verified BepInEx and Magicite releases (about 35 MB).\n"
                  "Replaced files are backed up. Your saves and seed selection are preserved.", wraplength=720).pack(anchor="w", pady=12)
        ttk.Label(panel, text="FFV installation folder").pack(anchor="w")
        row = ttk.Frame(panel)
        row.pack(fill="x", pady=5)
        self.game = tk.StringVar()
        self.entry = ttk.Entry(row, textvariable=self.game)
        self.entry.pack(side="left", fill="x", expand=True)
        self.browse = ttk.Button(row, text="Browse…", command=self.choose)
        self.browse.pack(side="left", padx=5)
        if discovery:
            found = discover_installations()
            if found:
                self.game.set(str(found[0]))
        buttons = ttk.Frame(panel)
        buttons.pack(fill="x", pady=12)
        self.install_button = ttk.Button(buttons, text="Validate and install", command=self.start_install)
        self.install_button.pack(side="left")
        self.restore_button = ttk.Button(buttons, text="Restore a backup / remove", command=self.start_restore)
        self.restore_button.pack(side="left", padx=10)
        self.output = tk.Text(panel, wrap="word", height=12, state="disabled")
        self.output.pack(fill="both", expand=True)
        self.log("Extract the whole ZIP first. Keep the launcher folder together.\n"
                 "After setup, use JobFairLauncher.exe to choose a mode and apply a seed.\n"
                 "Then launch FFV yourself through Steam. First launch may take several minutes\n"
                 "while BepInEx downloads Unity libraries and generates its local assemblies.\n"
                 "Back up your FFV saves before playtesting; this installer does not change saves.")
        root.protocol("WM_DELETE_WINDOW", self.close)
        root.after(100, self.poll)

    def log(self, text):
        self.output.configure(state="normal")
        self.output.insert("end", text + "\n")
        self.output.see("end")
        self.output.configure(state="disabled")

    def choose(self):
        folder = filedialog.askdirectory(title="Choose FINAL FANTASY V PR inside steamapps/common")
        if folder:
            self.game.set(folder)

    def run(self, work):
        self.busy = True
        for widget in (self.entry, self.browse, self.install_button, self.restore_button):
            widget.configure(state="disabled")
        def worker():
            try:
                work(lambda text: self.events.put(("log", text)))
                self.events.put(("done", "Finished. See the details above."))
            except Exception as error:
                self.events.put(("error", str(error)))
        threading.Thread(target=worker, daemon=False).start()

    def start_install(self):
        game = self.game.get()
        def work(log):
            folder, payload = prepare(Path(game), RESOURCES, log)
            base = Path(os.environ.get("LOCALAPPDATA", Path.home() / "AppData/Local")) / "JobFair/Installer/backups"
            receipt = install(folder, payload, base, log)
            log("Keep this backup for removal: " + str(receipt))
            log("Ready for seed selection in the included launcher, then launch through Steam.")
        self.run(work)

    def start_restore(self):
        base = Path(os.environ.get("LOCALAPPDATA", Path.home() / "AppData/Local")) / "JobFair/Installer/backups"
        path = filedialog.askopenfilename(title="Choose the restore.json for this installation (newest first)",
                                          initialdir=str(base), filetypes=[("Backup journal", "restore.json")])
        if path and messagebox.askyesno("Restore backup", "Restore the files recorded in this backup and remove its added files?\n\nSaves, seed settings, generated cache and logs are retained."):
            self.run(lambda log: restore(Path(path), log))

    def poll(self):
        try:
            while True:
                kind, value = self.events.get_nowait()
                self.log(value)
                if kind != "log":
                    self.busy = False
                    for widget in (self.entry, self.browse, self.install_button, self.restore_button):
                        widget.configure(state="normal")
                    if kind == "error":
                        messagebox.showerror("Setup stopped", value)
        except queue.Empty:
            pass
        self.root.after(100, self.poll)

    def close(self):
        if self.busy:
            messagebox.showinfo("Setup is working", "Please let setup finish before closing this window.")
        else:
            self.root.destroy()

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--self-check", action="store_true")
    parser.add_argument("--verify-game", type=Path, help="Read-only startup reconstruction check; never installs.")
    parser.add_argument("--report", type=Path)
    parser.add_argument("--rehearse-install", action="store_true", help="With --self-check and --verify-game: install/restore only in a disposable fixture.")
    args = parser.parse_args()
    if args.self_check:
        manifest = json.loads((RESOURCES / "payload.json").read_text(encoding="utf-8"))
        for row in manifest["files"]:
            if digest(RESOURCES / row["source"]) != row["sha256"]:
                raise ValueError("Payload check failed.")
        root = tk.Tk()
        root.withdraw()
        Installer(root, discovery=False)
        root.update_idletasks()
        root.destroy()
        report = {"status": "passed", "frozen": bool(getattr(sys, "frozen", False)), "ui": "passed", "version": VERSION}
        if args.verify_game:
            result = startup_payload(validate_game(args.verify_game), manifest["patches"])
            report["startup_outputs"] = len(result)
        if args.rehearse_install:
            if not args.verify_game or not args.report:
                raise ValueError("Rehearsal requires --verify-game and --report.")
            from verify_clean_install import rehearse
            report["rehearsal"] = rehearse(RESOURCES, args.verify_game, args.report.with_suffix(".rehearsal.json"), lambda _: None)
        if args.report:
            args.report.write_text(json.dumps(report, indent=2), encoding="utf-8")
        return
    root = tk.Tk()
    Installer(root)
    root.mainloop()

if __name__ == "__main__":
    main()
