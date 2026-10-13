# Contributing to Job Fair

Start from an issue or a focused change. Include the mode, seed and affected
route, plus tests and any native-playtest evidence. Keep fixes narrow and retain
existing save/run contracts. Do not claim native acceptance from unit tests.

## Python tools and tests

Use Windows x64 Python 3.13 (release builds used 3.13.15):

```powershell
py -3.13 -m venv ffv_randomizer/.venv
& ffv_randomizer/.venv/Scripts/python.exe -m pip install -r job_fair_launcher/packaging/build_requirements.txt -r job_fair_installer/build_requirements.txt
Push-Location ffv_randomizer
& ./.venv/Scripts/python.exe -m unittest discover -s tests
Pop-Location
& ffv_randomizer/.venv/Scripts/python.exe -m unittest discover -s job_fair_launcher/tests
& ffv_randomizer/.venv/Scripts/python.exe -m unittest discover -s job_fair_installer
& ffv_randomizer/.venv/Scripts/python.exe job_fair_launcher/packaging/entrypoint.py --self-check --check-ui
```

The catalogs and parity vectors are checked in, so these tests need no game
installation. Run the launcher source with `job_fair_launcher/LaunchJobFair.ps1`.
Applying a seed requires a supported game with the matching mod runtime.

## Build the runtime

Install the tester release into your own supported FFV copy, then launch through
Steam yourself once so BepInEx generates its local interop assemblies. Close FFV.
Install a C# compiler supporting C# 9 (the release used Visual Studio 2019 Build
Tools Roslyn). Pass your own paths:

```powershell
./ffv_runtime/build.ps1 -GameRoot 'D:/SteamLibrary/steamapps/common/FINAL FANTASY V PR' -Compiler 'C:/Program Files (x86)/Microsoft Visual Studio/2019/BuildTools/MSBuild/Current/Bin/Roslyn/csc.exe'
```

This creates `ffv_runtime/build/JobFair.Runtime.dll` and build evidence. It does
not install or launch anything. BepInEx, .NET and generated game references come
from your own installation. Never commit those binaries or extracted game assets.
The checked-in generated C# catalogs are part of the runtime source. Their header
references to historical research scripts describe provenance; the private research
workspace is not required to compile the current runtime.

Selected native-adapter probes under `ffv_runtime/tests` use local .NET references
and fake native objects. They require the same `-GameRoot`, `-Compiler` and a
compatible `-Dotnet` runtime. They do not launch FFV. The wider private extraction
and historical-probe suite is not shipped; these checks are not a full gameplay test.

## Build launcher and installer

```powershell
./job_fair_launcher/packaging/build_portable.ps1 -Python ./ffv_randomizer/.venv/Scripts/python.exe -OutputDirectory ./dist/launcher
& ffv_randomizer/.venv/Scripts/python.exe job_fair_installer/prepare_release.py ./dist/setup-input/payload
$env:JOBFAIR_SETUP_INPUT = (Resolve-Path ./dist/setup-input).Path
& ffv_randomizer/.venv/Scripts/python.exe -m PyInstaller --distpath ./dist/setup --workpath ./dist/setup-work job_fair_installer/JobFairSetup.spec
```

Installer preparation uses the committed startup patch template and your compiled
runtime. Its notices are collected from the actual build environment. Test the
standalone EXE with `--self-check --report <new-report.json>`. Adding
`--verify-game <FFV-folder>` reconstructs startup overrides read-only. Adding
`--rehearse-install` performs download/install/restore in a disposable fixture;
it does not write into the supplied real game folder or launch FFV.

Package the tested build using explicit inputs:

```powershell
& ffv_randomizer/.venv/Scripts/python.exe job_fair_installer/package_release.py ./dist/release --setup ./dist/setup/JobFairSetup --launcher ./dist/launcher/JobFairLauncher --verification ./dist/setup-check.json
```

Use new output folders. Modified runtime builds need a deliberately updated
`COMPATIBLE_RUNTIME_SHA256` in `job_fair_launcher/model.py` and the release
assembler's runtime pin, followed by fresh launcher/runtime parity checks. Do not
silently bypass compatibility checks. Versions, test counts and release notes
must describe the new build; the current assembler targets the 0.9.5 release.

## Contribution boundaries

Do not commit saves, personal seed profiles, credentials, installation journals,
logs, game assemblies, asset bundles, downloaded loader binaries or generated
interop. Keep third-party notices intact. Original contributions are provided
under this repository's MIT license. Game names and native reference material do
not grant rights to the underlying game.

When reporting a failure, use the issue template. For native bugs preserve the
log before restarting FFV and include the seed, mode, steps and observed behavior.
