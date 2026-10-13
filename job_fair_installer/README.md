# Guided installer

See [CONTRIBUTING.md](../CONTRIBUTING.md) for build and test commands. `core.py` handles validation, downloads, startup reconstruction, installation and restore. `installer.py` supplies the UI. `payload_template` contains startup patch recipes, configuration, keys and notices; `prepare_release.py` adds your compiled runtime and collects build dependency notices.

The installer never launches FFV, applies seeds or changes saves. Restore preflights tracked files and preserves later changed binaries by refusing conflicting restoration. Offline fixture tests do not establish native gameplay acceptance.
