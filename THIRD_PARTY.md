# Third-party components

The MIT license at the repository root applies to Job Fair's original code.
It does not relicense third-party dependencies or underlying Final Fantasy V
material. Game assemblies, interop assemblies and native asset bundles are not
redistributed in this repository. Generated compatibility catalogs and native
reference identifiers support the user's separately installed game.

Setup downloads unchanged BepInEx v6.0.0-pre.2 and Magicite v2.2.0 archives from
their official GitHub releases, verifies exact hashes, and installs their notices.
These include BepInEx/Unity Doorstop (LGPL 2.1), Magicite/Syldra (MIT), and the
bundled .NET runtime notices. The committed installer payload template retains
those exact notice texts. It does not contain the upstream loader binaries.

The portable programs include Python, Tcl/Tk, PyInstaller and Python dependencies.
Their license files are collected during build and retained under `_internal/notices`.
UnityPy and its dependency notices accompany the installer. The launcher also
retains OpenSSL and zlib notices under `job_fair_launcher/packaging/notices`.

- https://github.com/BepInEx/BepInEx/releases/tag/v6.0.0-pre.2
- https://github.com/Silvris/Magicite/releases/tag/v2.2.0
- https://github.com/K0lb3/UnityPy

No Career Day source or SNES patch is incorporated. Job Fair is an independent
Pixel Remaster project inspired by Career Day's randomizer approach.
