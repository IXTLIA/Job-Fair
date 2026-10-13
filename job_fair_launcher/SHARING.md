# Launcher packaging

Job Fair original code is MIT licensed. See [CONTRIBUTING.md](../CONTRIBUTING.md) for source setup, tests and portable builds. The standalone launcher needs the matching runtime; the full release ZIP also includes a guided installer.

`tools/export_source.py` creates a smaller launcher-only source export. It is not the complete mod repository. Its explicit allowlist excludes local profiles, saves, game files and research. Builds retain third-party notices and catalog hashes.
