# 0.9.5 publication checks

- 65 generator tests, 23 launcher/export tests, 17 installer tests passed.
- Launcher parity: 40 vectors across four modes; hidden UI checks passed.
- Runtime compiled from this curated source checkout to SHA-256
  `9154e7dd41e7c2b774c76665cf80364c2f77870e902a4845b1bb278d3d920996`,
  exactly matching the installed 0.9.5 runtime.
- Launcher and installer portable EXEs built from the public checkout.
- Frozen installer reconstructed both startup overrides to approved hashes,
  downloaded both pinned upstream releases, installed 242 payload files in a
  disposable Steam-shaped fixture, and restored its original inventory exactly.
- Release ZIP CRC and per-file hashes passed. The public ZIP adds MIT licensing
  and current contributor-facing documentation; it has a new checksum compared
  with the earlier local-only tester package.
- Staged source bytes matched their working files; private paths, credentials,
  native game binaries, saves and local research were excluded.

Native map refresh, controller hardware, boss defense/EXP and complete campaign
acceptance remain pending. Clean-machine first launch is also a tester task.
No game launch, seed application, save changes or owner game writes were performed
for publication. Offline checks do not establish native gameplay behavior.
