from pathlib import Path
from PyInstaller.utils.hooks import collect_data_files, collect_dynamic_libs, collect_submodules
import os
import sys

HERE = Path(SPECPATH)
ROOT = HERE.parent
DATA = Path(os.environ['JOBFAIR_SETUP_INPUT'])
datas = [(str(DATA / 'payload'), 'payload'), (str(DATA / 'notices'), 'notices')]
datas += collect_data_files('UnityPy')
datas += [(str(Path(sys.base_prefix) / 'LICENSE.txt'), 'notices/Python'),
          (str(Path(sys.base_prefix) / 'tcl/tk8.6/license.terms'), 'notices/TclTk')]
datas += [(str(ROOT / 'job_fair_launcher/packaging/notices'), 'notices/LauncherRuntime')]
a = Analysis([str(HERE / 'installer.py')], pathex=[str(HERE), str(ROOT / 'job_fair_launcher')],
             binaries=collect_dynamic_libs('UnityPy'), datas=datas,
             hiddenimports=collect_submodules('UnityPy'), hookspath=[], hooksconfig={}, runtime_hooks=[],
             excludes=[], noarchive=False, optimize=0)
pyz = PYZ(a.pure)
exe = EXE(pyz, a.scripts, [], exclude_binaries=True, name='JobFairSetup', debug=False,
          bootloader_ignore_signals=False, strip=False, upx=False, console=False,
          disable_windowed_traceback=False)
coll = COLLECT(exe, a.binaries, a.datas, strip=False, upx=False, name='JobFairSetup')
