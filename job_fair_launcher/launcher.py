"""Visible Tk launcher. Execute manually with LaunchJobFair.ps1."""
from __future__ import annotations

import tkinter as tk
from pathlib import Path
from tkinter import filedialog, messagebox, ttk

import model
import installations

MINIMUM_SIZE = (720, 850)
INITIAL_GEOMETRY = "780x870"


class Launcher:
    def __init__(self, window: tk.Tk):
        self.window = window
        window.title("Job Fair — FFV Pixel Remaster")
        window.minsize(*MINIMUM_SIZE)
        window.geometry(INITIAL_GEOMETRY)
        frame = ttk.Frame(window, padding=24)
        frame.pack(fill="both", expand=True)
        frame.columnconfigure(0, weight=1)
        ttk.Label(frame, text="Job Fair", font=("Segoe UI", 22, "bold")).grid(sticky="w")
        ttk.Label(frame, text="Set up your next FFV Pixel Remaster run.", font=("Segoe UI", 10)).grid(sticky="w", pady=(2, 18))
        initial_error = None
        try:
            saved_folder = model.load_game_folder()
        except (ValueError, OSError) as error:
            saved_folder, initial_error = None, str(error)
        try:
            candidates = installations.discover_installations()
        except (ValueError, OSError):
            candidates = []
        chosen = saved_folder or (candidates[0] if len(candidates) == 1 else None)
        self.selected_folder = chosen
        self.game_folder = tk.StringVar(value=str(chosen) if chosen else "")
        folder_row = ttk.Frame(frame)
        folder_row.grid(sticky="ew", pady=(0, 14))
        folder_row.columnconfigure(0, weight=1)
        ttk.Label(folder_row, text="FFV installation folder").grid(row=0, column=0, sticky="w")
        self.folder_choices = ttk.Combobox(folder_row, textvariable=self.game_folder, state="readonly", width=1,
                                          values=list(dict.fromkeys(str(p) for p in ([chosen] if chosen else []) + candidates)))
        self.folder_choices.grid(row=1, column=0, sticky="ew", pady=4)
        self.folder_choices.bind("<<ComboboxSelected>>", lambda _: self.choose_folder(self.game_folder.get()))
        ttk.Button(folder_row, text="Browse…", command=self.browse_folder).grid(row=1, column=1, padx=(8, 0))
        ttk.Button(folder_row, text="Find Steam game", command=self.detect_folder).grid(row=1, column=2, padx=(8, 0))
        ttk.Label(frame, text="Mode").grid(sticky="w")
        self.mode = tk.StringVar(value=model.campaign.MIXED)
        ttk.Combobox(frame, textvariable=self.mode, values=model.campaign.MODES, state="readonly").grid(sticky="ew", pady=(2, 4))
        self.mode_description = tk.StringVar()
        ttk.Label(frame, textvariable=self.mode_description, wraplength=650).grid(sticky="w")

        seed_row = ttk.Frame(frame)
        seed_row.grid(sticky="ew", pady=(20, 6))
        seed_row.columnconfigure(0, weight=1)
        ttk.Label(seed_row, text="Seed").grid(row=0, column=0, sticky="w")
        try:
            selected = model.current_request(chosen)
        except (ValueError, OSError) as error:
            selected, initial_error = None, str(error)
        if selected is not None:
            self.mode.set(selected["mode"])
        self.seed = tk.StringVar(value=selected["seed"] if selected else str(model.generate_seed()))
        self.seed_entry = ttk.Entry(seed_row, textvariable=self.seed, font=("Consolas", 12))
        self.seed_entry.grid(row=1, column=0, sticky="ew", pady=4)
        ttk.Button(seed_row, text="Generate seed", command=self.generate).grid(row=1, column=1, padx=(10, 0))
        self.jobs = tk.StringVar()
        ttk.Label(frame, text="Job availability", font=("Segoe UI", 10, "bold")).grid(sticky="w", pady=(8, 4))
        ttk.Label(frame, textvariable=self.jobs, wraplength=650, font=("Segoe UI", 11)).grid(sticky="w")

        profile_frame = ttk.LabelFrame(frame, text="Seed profiles", padding=12)
        profile_frame.grid(sticky="ew", pady=20)
        profile_frame.columnconfigure(0, weight=1)
        self.profile_name = tk.StringVar()
        ttk.Label(profile_frame, text="Profile name").grid(row=0, column=0, sticky="w")
        ttk.Entry(profile_frame, textvariable=self.profile_name).grid(row=1, column=0, sticky="ew", pady=(3, 10))
        ttk.Button(profile_frame, text="Save profile", command=self.save).grid(row=1, column=1, padx=(10, 0), pady=(3, 10))
        self.saved = ttk.Combobox(profile_frame, state="readonly")
        self.saved.grid(row=2, column=0, sticky="ew")
        ttk.Button(profile_frame, text="Load profile", command=self.load).grid(row=2, column=1, padx=(10, 0))
        ttk.Button(profile_frame, text="Import profile…", command=self.import_profile).grid(row=3, column=0, sticky="w", pady=(8, 0))

        ttk.Label(frame, text="All modes shuffle treasures and event rewards within each world, shop stock by world and type, and regular enemy drops and steals. Bosses award EXP. Karnak Castle's timed chests keep their original rewards. Sealed Weapons and the Brave Blade/Chicken Knife choice stay fixed.", wraplength=650).grid(sticky="w", pady=(0, 14))
        self.apply_button = ttk.Button(frame, text="Apply for next New Game", command=self.apply)
        self.apply_button.grid(sticky="ew", ipady=6)
        ttk.Label(frame, text="Close FFV before applying. Then launch through Steam and select New Game. Use a fresh save slot for testing.", wraplength=650).grid(sticky="w", pady=10)
        self.status = tk.StringVar(value="Previous selection could not be read: " + initial_error if initial_error else
                                  ("Ready. Current rules are used automatically, including for saved profiles." if chosen else
                                   "Select your FFV folder with Browse, or choose a detected installation from the list."))
        self.status_label = ttk.Label(frame, textvariable=self.status, wraplength=650, justify="left")
        self.status_label.grid(sticky="w", pady=(3, 0))
        frame.bind("<Configure>", lambda event: self.status_label.configure(wraplength=min(650, max(200, event.width - 56))))
        self.seed.trace_add("write", lambda *_: self.update_preview())
        self.mode.trace_add("write", lambda *_: self.update_preview())
        self.refresh_profiles()
        self.update_preview()
        self.seed_entry.focus_set()

    def update_preview(self):
        descriptions = {model.campaign.MIXED:
            "Follow the story from Wind Shrine with a full party of Freelancers. "
            "Unlock 21 shuffled jobs at the crystals. Bosses shuffle within Simple/Complex tiers, "
            "with stats scaled to their location. Story and key progression stay normal.",
            model.campaign.REMIXED:
            "Explore with Bartz, Lenna, Faris and Krile, starting as Freelancers with four jobs unlocked. "
            "Find 17 hidden jobs and keys to Worlds 2 and 3. All 49 bosses shuffle in one pool, "
            "with stats scaled to their location. Dungeon bosses remain required.",
            model.campaign.MIXED_FIESTA:
            "Follow the story as Freelancers until Wind Shrine unlocks four distinct jobs. "
            "Freelancer stays available; no later jobs unlock. Party members may share jobs. "
            "Bosses shuffle within Simple/Complex tiers, with stats scaled to their location.",
            model.campaign.REMIXED_FIESTA:
            "Explore with four distinct jobs unlocked from the start, plus Freelancer. "
            "No later jobs unlock; party members may share jobs. Find keys to Worlds 2 and 3. "
            "All 49 bosses shuffle in one pool, with stats scaled to their location."}
        self.mode_description.set(descriptions.get(self.mode.get(), "Choose a mode."))
        try:
            jobs = model.preview(self.seed.get(), self.mode.get())
            prefix = ("Freelancers; Wind Shrine unlocks: " if self.mode.get() == model.campaign.MIXED_FIESTA else
                      "Freelancers; unlocked: " if model.campaign.base_mode(self.mode.get()) == model.campaign.REMIXED else "")
            self.jobs.set(prefix
                          + "  ·  ".join(name for _, name in jobs))
            self.apply_button.state(["!disabled"] if self.game_folder.get() else ["disabled"])
        except ValueError:
            self.jobs.set("Enter a decimal seed from 0 to 18446744073709551615.")
            self.apply_button.state(["disabled"])

    def generate(self):
        self.seed.set(str(model.generate_seed()))
        self.status.set("New seed generated. Apply it when ready.")

    def choose_folder(self, folder):
        try:
            path = model.save_game_folder(Path(folder))
        except (ValueError, OSError) as error:
            self.game_folder.set(str(self.selected_folder) if self.selected_folder else "")
            messagebox.showerror("Game folder could not be selected", str(error), parent=self.window)
            self.update_preview()
            return
        self.selected_folder = path
        self.game_folder.set(str(path))
        values = list(self.folder_choices["values"])
        self.folder_choices["values"] = list(dict.fromkeys([str(path), *values]))
        try:
            selected = model.current_request(path)
            if selected:
                self.mode.set(selected["mode"])
                self.seed.set(selected["seed"])
            self.status.set("Game folder saved. Apply a seed after installing the matching Job Fair runtime.")
        except (ValueError, OSError) as error:
            self.status.set("Game folder saved, but its previous seed configuration could not be read. "
                            "Choose a mode and seed, then Apply to replace it. " + str(error))
        self.update_preview()

    def browse_folder(self):
        folder = filedialog.askdirectory(parent=self.window, title="Select the folder containing FINAL FANTASY V.exe",
                                         initialdir=self.game_folder.get() or None, mustexist=True)
        if folder:
            self.choose_folder(folder)

    def detect_folder(self):
        try:
            candidates = installations.discover_installations()
            self.folder_choices["values"] = [str(path) for path in candidates]
            if len(candidates) == 1:
                self.choose_folder(candidates[0])
            elif candidates:
                self.status.set("Several FFV installations were found. Choose one from the folder list.")
            else:
                self.status.set("FFV was not found in Steam libraries. Use Browse to select its installation folder.")
        except (ValueError, OSError) as error:
            messagebox.showerror("Steam search failed", str(error), parent=self.window)

    def import_profile(self):
        filename = filedialog.askopenfilename(parent=self.window, title="Import a Job Fair seed profile",
                                             filetypes=[("Job Fair profile", "*.json")])
        if not filename:
            return
        try:
            path = model.import_profile(Path(filename))
            profile = model.load_profile(path)
            self.refresh_profiles()
            self.saved.set(profile["name"])
            self.load()
        except (ValueError, OSError) as error:
            messagebox.showerror("Profile was not imported", str(error), parent=self.window)

    def refresh_profiles(self):
        self.profiles, errors = model.list_profiles()
        self.saved["values"] = [name for name, _ in self.profiles]
        if errors:
            self.status.set(f"{len(errors)} invalid profile(s) skipped. Existing files were preserved.")

    def save(self):
        try:
            name = self.profile_name.get().strip()
            if any(existing == name for existing, _ in self.profiles):
                if not messagebox.askyesno("Replace saved profile?", f"Replace the saved seed in ‘{name}’?", parent=self.window):
                    return
            model.save_profile(name, self.seed.get(), mode=self.mode.get())
            self.seed.set(model.seed_request(self.seed.get(), self.mode.get())["seed"])
            self.refresh_profiles()
            self.saved.set(name)
            self.status.set(f"Saved profile: {name}")
        except (ValueError, OSError) as error:
            messagebox.showerror("Profile was not saved", str(error), parent=self.window)

    def load(self):
        try:
            index = self.saved.current()
            if index < 0:
                raise ValueError("Choose a saved profile first.")
            profile = model.load_profile(self.profiles[index][1])
            self.profile_name.set(profile["name"])
            self.mode.set(profile["request"]["mode"])
            self.seed.set(profile["request"]["seed"])
            self.update_preview()
            self.status.set(f"Loaded {profile['name']}. Apply it to use for the next New Game.")
        except (ValueError, OSError) as error:
            messagebox.showerror("Profile was not loaded", str(error), parent=self.window)

    def apply(self):
        try:
            folder = Path(self.game_folder.get()) if self.game_folder.get() else None
            receipt = model.apply_next_run(self.seed.get(), game_root=folder, mode=self.mode.get())
            self.seed.set(receipt["request"]["seed"])
            self.status.set(f"Seed {self.seed.get()} applied. Launch FFV through Steam and select New Game.")
            if "receipt_warning" in receipt:
                messagebox.showwarning("Seed applied", "The seed was applied, but its local receipt could not be saved.\n" + receipt["receipt_warning"], parent=self.window)
        except (ValueError, OSError, TimeoutError, model.subprocess.SubprocessError) as error:
            messagebox.showerror("Seed was not applied", str(error), parent=self.window)


if __name__ == "__main__":
    root = tk.Tk()
    Launcher(root)
    root.mainloop()
