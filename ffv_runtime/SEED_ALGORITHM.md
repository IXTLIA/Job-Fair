# Portable starter-job seed contract

Version: `starter-jobs-sha256-v1`.

`SeedLogic.cs` and `ffv_randomizer/ffv_randomizer/runtime_seed.py` are original implementations of the same portable contract. They do not change the existing static startup compiler or install anything. In particular, seed `20261011` under this new version intentionally differs from the older Python `Random.sample` trial.

Seed input is an unsigned 64-bit integer written with ASCII decimal digits, allowing surrounding .NET whitespace and leading zeroes. Empty input, signs, separators, non-ASCII digits and overflow are rejected. Canonical display uses invariant decimal with no leading zeroes. Python uses the same whitespace set as .NET `String.Trim`, avoiding Python's additional U+001C–U+001F stripping behavior. Parsing an invalid seed reports failure with zero as the out/result value; callers must check the success flag.

New random seeds consume eight cryptographic random bytes interpreted explicitly in little-endian order. Selection is deterministic and independent of input job order:

1. Validate positive, distinct signed 32-bit job IDs; reject duplicates. Sort ascending. Require `1 <= count <= number of jobs`.
2. For every ID, hash UTF-8 text `ProjectBlues|starter-jobs|1|{canonicalSeed}|{invariantJobId}` with SHA-256.
3. Rank the unsigned digest bytes lexicographically, breaking an exact digest tie by ID. Return the first `count` IDs in ranking order.

The algorithm uses no language-specific pseudo-random generator, current culture or platform byte order. Its explicit version allows future randomizer algorithms to coexist without silently changing this starter-job mapping.

For job IDs 2 through 22 (the current 21-job catalog):

| Canonical seed | First four IDs |
| --- | --- |
| `0` | `4, 15, 3, 12` |
| `1` | `12, 2, 11, 14` |
| `20261011` | `4, 9, 13, 22` |
| `18446744073709551615` | `10, 21, 7, 16` |

## Validation

From the project root:

```powershell
& .\ffv_runtime\tests\run_seed_probe.ps1
```

This compiles only the portable C# source and console fixture using local Roslyn, references the embedded .NET 6.0.7 framework libraries, and executes with the installed .NET 6 patch runtime via `dotnet`. The fixture verifies all four frozen vectors, decimal/overflow/Unicode parsing, input-order invariance, ranked-prefix consistency, Arabic current-culture independence, invalid IDs/counts, and a cryptographic seed round trip. Its executable/results remain in `tests/build`; it executes no Unity or game code.

Then, from `ffv_randomizer`:

```powershell
& .\.venv\Scripts\python.exe -m unittest discover -s tests -p test_runtime_seed.py -v
```

Six Python tests passed, including direct comparison against the separately compiled C# output for every frozen vector and an explicit eight-byte little-endian entropy check. The cross-language test is skipped if the local C# result artifact is absent; run the C# fixture first for the complete check. These tests do not prove any in-game menu or save behavior.
