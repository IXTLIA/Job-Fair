using System;
using System.Collections.Generic;
using System.Text.Json;

namespace ProjectBlues.Runtime
{
    // Launcher configuration applies only to New Game. Loading uses saved metadata.
    public static class NextRunConfig
    {
        public const int MaximumLength = 1048576;
        public static NextRunRequest Parse(string json)
        {
            if (json == null || json.Length > MaximumLength)
                throw new ArgumentException("Missing or oversized Job Fair launcher configuration.");
            using (JsonDocument document = JsonDocument.Parse(json))
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    throw new ArgumentException("Job Fair launcher configuration must be an object.");
                JsonElement schemaValue;
                if (!document.RootElement.TryGetProperty("schema_version", out schemaValue))
                    throw new ArgumentException("Job Fair launcher schema is missing.");
                int schema = SaveSeedCodec.ReadInteger(schemaValue);
                if (schema == 1) return new NextRunRequest(ParseSeed(json));
                if (schema != 2 && schema != 3) throw new ArgumentException("Unsupported Job Fair launcher configuration version.");
                Dictionary<string, JsonElement> fields = SaveSeedCodec.ReadFields(document.RootElement,
                    "schema_version", "seed", "generator", "mode", "settings", "catalog_id", "starting_job_ids", "placements", "placement_hash");
                // Reuse the same stored-data validation as saves, including placement integrity.
                var metadata = new RunMetadata(schema, SaveSeedCodec.ReadString(fields["seed"]), SaveSeedCodec.ReadString(fields["generator"]),
                    "00000000000000000000000000000000", "Bartz", SaveSeedCodec.ReadString(fields["mode"]),
                    SaveSeedCodec.ReadSettings(fields["settings"]), SaveSeedCodec.ReadString(fields["catalog_id"]),
                    SaveSeedCodec.ReadJobs(fields["starting_job_ids"]), SaveSeedCodec.ReadPlacements(fields["placements"]),
                    SaveSeedCodec.ReadString(fields["placement_hash"]), Array.Empty<string>(), Array.Empty<string>(),
                    schema == 3 ? WorldTravelState.Initial : null);
                CampaignContract.ValidatePlayable(metadata);
                if (json.Length > 65536 && !CampaignContract.IsMixedShopCatalog(metadata.CatalogId)
                    && !CampaignContract.IsPlayableRemixedCatalog(metadata.CatalogId))
                    throw new ArgumentException("Legacy campaign launcher configuration is oversized.");
                return new NextRunRequest(metadata);
            }
        }

        public static ulong ParseSeed(string json)
        {
            if (json == null || json.Length > 4096)
                throw new ArgumentException("Missing or oversized Job Fair launcher configuration.");
            using (JsonDocument document = JsonDocument.Parse(json))
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    throw new ArgumentException("Job Fair launcher configuration must be an object.");
                var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                foreach (JsonProperty property in document.RootElement.EnumerateObject())
                    if (!fields.TryAdd(property.Name, property.Value))
                        throw new ArgumentException("Duplicate Job Fair configuration field.");
                if (fields.Count != 3 || !fields.ContainsKey("schema_version")
                    || !fields.ContainsKey("generator") || !fields.ContainsKey("seed"))
                    throw new ArgumentException("Unsupported Job Fair configuration fields.");
                int schema;
                if (fields["schema_version"].ValueKind != JsonValueKind.Number
                    || !fields["schema_version"].TryGetInt32(out schema) || schema != 1
                    || fields["generator"].ValueKind != JsonValueKind.String
                    || fields["generator"].GetString() != SeedLogic.AlgorithmVersion
                    || fields["seed"].ValueKind != JsonValueKind.String)
                    throw new ArgumentException("Unsupported Job Fair launcher configuration version.");
                string text = fields["seed"].GetString();
                ulong seed;
                if (!SeedLogic.TryParseSeed(text, out seed) || SeedLogic.CanonicalSeed(seed) != text)
                    throw new ArgumentException("The Job Fair seed must be canonical unsigned decimal text.");
                return seed;
            }
        }
    }

    public sealed class NextRunRequest
    {
        private readonly RunMetadata template;
        public int SchemaVersion { get; }
        public ulong Seed { get; }
        public string Generator { get; }
        public string Mode { get; }
        public string CatalogId { get; }
        public string PlacementHash { get; }
        public RunSettings Settings { get; }
        public IReadOnlyList<int> StartingJobs { get; }
        public IReadOnlyList<RewardPlacement> Placements { get; }

        internal NextRunRequest(ulong seed)
        {
            template = RunMetadata.Create(seed, "Bartz");
            SchemaVersion = 1; Seed = seed; Generator = template.Generator;
            Mode = template.Mode; Settings = template.Settings; CatalogId = template.CatalogId;
            PlacementHash = template.PlacementHash; StartingJobs = template.StartingJobs; Placements = template.Placements;
        }

        internal NextRunRequest(RunMetadata metadata)
        {
            template = metadata;
            ulong seed;
            if (!SeedLogic.TryParseSeed(metadata.Seed, out seed)) throw new ArgumentException("Invalid seed.");
            SchemaVersion = metadata.SchemaVersion; Seed = seed; Generator = metadata.Generator; Mode = metadata.Mode;
            Settings = metadata.Settings; CatalogId = metadata.CatalogId; PlacementHash = metadata.PlacementHash;
            StartingJobs = metadata.StartingJobs; Placements = metadata.Placements;
        }

        public RunMetadata CreateRun(string bartzName)
        {
            if (SchemaVersion == 1) return RunMetadata.Create(Seed, bartzName);
            return new RunMetadata(SchemaVersion, template.Seed, Generator, Guid.NewGuid().ToString("N"), bartzName,
                Mode, Settings, CatalogId, RunMetadata.Copy(StartingJobs), RunMetadata.Copy(Placements), PlacementHash,
                Array.Empty<string>(), Array.Empty<string>(), SchemaVersion == 3 ? WorldTravelState.Initial : null);
        }
    }
}
