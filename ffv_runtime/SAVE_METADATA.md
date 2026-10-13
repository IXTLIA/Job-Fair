# Portable embedded run metadata

`RunMetadata.cs` and `SaveSeedCodec.cs` implement an original, side-effect-free codec for the native **UserData JSON string**. They do not read or write files, access Unity, or alter the outer save-slot structure.

```csharp
RunMetadata run = RunMetadata.Create(seed, chosenName);
string savedUserData = SaveSeedCodec.Attach(nativeUserDataJson, run);
RunMetadata loadedRun;
string nativeUserData = SaveSeedCodec.Extract(savedUserData, out loadedRun);
```

The reserved top-level property is `__job_fair`:

```json
{
  "schema_version": 1,
  "seed": "20261011",
  "generator": "starter-jobs-sha256-v1",
  "run_id": "aef021114bc44591a83a25a1f486b8ec",
  "bartz_name": "Bartz",
  "starting_job_ids": [4, 9, 13, 22]
}
```

The example run ID is illustrative; `Create` uses a fresh GUID for every run, including repeated runs with the same seed. Metadata is immutable, including a cloned read-only job collection. Its public properties are `SchemaVersion`, `Seed`, `Generator`, `RunId`, `BartzName` and `StartingJobs`. `WithBartzName(name)` returns a validated replacement with the same seed, generator, run ID and jobs. The Tule rename session uses this after updating the native character name; it does not mutate the earlier descriptor.

Validation requires exactly the six schema fields with no duplicates or extras; supported schema and generator; canonical uint64 decimal seed; a 32-character lowercase hexadecimal GUID; a nonblank name of at most 24 Unicode scalar values with well-formed UTF-16; and exactly the four ordered job IDs produced by this generator for the current IDs 2–22. The 24-character storage bound does not establish the native naming UI's allowed length; the UI may impose a smaller limit.

`Attach` refuses any existing reserved property. `Extract` refuses duplicate reserved properties, malformed/unsupported metadata and seed/job disagreement rather than silently inventing a replacement seed. Escaped property spellings are decoded before duplicate checks. On extraction failure the out parameter remains null.

Native properties are enumerated in order and their JSON values written raw, preserving big integer and floating-point lexemes, Unicode, nested values, arrays, nulls and duplicate **nonreserved** keys. Native data is never collapsed into a dictionary. A nested `__job_fair` key is ordinary native data and remains untouched. When metadata exists, the root object's whitespace/property-name escaping can be normalized; native field values and order remain intact. Vanilla input without the top-level key returns the exact original string and null metadata.

Integration must clear prior run context for a vanilla load and avoid attaching metadata from a different active run. Corrupt/unsupported metadata requires an explicit load-failure path; the codec intentionally provides no fallback to a new seed. Actual native save/load hook timing and in-game acceptance remain separate work.

## Validation

From the project root:

```powershell
& .\ffv_runtime\tests\run_codec_probe.ps1
```

The console fixture compiled with the existing Roslyn C# 9 compiler against exact embedded .NET 6.0.7 libraries and ran with the installed .NET 6 patch runtime. **87 assertions passed**, covering native data preservation, vanilla passthrough, metadata round trip, distinct run IDs, immutable jobs, empty native objects, Unicode character limits, invalid input, duplicate/version/seed/job/GUID corruption, strict launcher config and renamed-metadata identity/round trip. No native save files or game code were accessed.

Results and source/output hashes: `tests/build/codec_probe_results.json`. Output remains under the ignored local `tests/build` directory.

Additional reference assemblies beyond the portable seed fixture are `System.Text.Json.dll`, `System.Text.Encodings.Web.dll`, `System.Memory.dll`, `System.Buffers.dll` and `System.IO.dll`, all from the existing game's `dotnet` directory. The fixture script records the complete compiler argument vector.
