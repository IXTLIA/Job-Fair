# Explicit data allowlist: no research, game assets, profiles or machine settings.
from pathlib import Path
from importlib.metadata import distribution
import sys

HERE = Path(SPECPATH)
ROOT = HERE.parents[1]
sys.path.insert(0, str(ROOT / "ffv_randomizer"))
from ffv_randomizer import resources
resources.verify_all()

datas = [(str(resources.DATA_ROOT / name), "ffv_randomizer/data")
         for name in (*resources.CATALOG_FILES, resources.MANIFEST_FILE)]
datas += [(str(HERE / "parity_vectors.json"), ".")]
datas += [(str(HERE / "boss_parity_vectors.json"), ".")]
datas += [(str(HERE / "rewards_parity_vectors.json"), ".")]
# Redistribute the actual build interpreter/bootloader license texts.
python_license = Path(sys.base_prefix) / "LICENSE.txt"
tk_license = Path(sys.base_prefix) / "tcl/tk8.6/license.terms"
pyinstaller_license = next(distribution("pyinstaller").locate_file(path)
    for path in distribution("pyinstaller").files if str(path).endswith("licenses/COPYING.txt"))
for path in (python_license, tk_license, pyinstaller_license):
    if not path.is_file():
        raise ValueError(f"Required bundled component license is missing: {path.name}")
datas += [(str(python_license), "notices/Python"), (str(tk_license), "notices/TclTk"),
          (str(pyinstaller_license), "notices/PyInstaller")]
datas += [(str(HERE / "notices" / name), "notices") for name in
          ("Tcl-license.terms", "OpenSSL-LICENSE.txt", "zlib-LICENSE.txt", "provenance.json")]

a = Analysis([str(HERE / "entrypoint.py")],
    pathex=[str(ROOT / "ffv_randomizer"), str(ROOT / "job_fair_launcher")],
    binaries=[], datas=datas, hiddenimports=[], hookspath=[], hooksconfig={},
    runtime_hooks=[], excludes=["ffv_randomizer.jobs", "ffv_randomizer.magicite", "ffv_randomizer.patches"],
    noarchive=False, optimize=0)
pyz = PYZ(a.pure)
exe = EXE(pyz, a.scripts, [], exclude_binaries=True, name="JobFairLauncher",
    debug=False, bootloader_ignore_signals=False, strip=False, upx=False,
    console=False, disable_windowed_traceback=False)
coll = COLLECT(exe, a.binaries, a.datas, strip=False, upx=False, name="JobFairLauncher")
