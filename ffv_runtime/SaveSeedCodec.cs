// Original, side-effect-free embedding in the native UserData JSON string.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace ProjectBlues.Runtime
{
    public static class SaveSeedCodec
    {
        public const string ReservedKey = "__job_fair";
        private static readonly HashSet<string> MetadataFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "schema_version", "seed", "generator", "run_id", "bartz_name", "starting_job_ids"
        };
        private static readonly HashSet<string> CampaignFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "schema_version", "seed", "generator", "run_id", "bartz_name", "starting_job_ids",
            "mode", "settings", "catalog_id", "placement_hash", "placements", "claimed_location_ids", "acquired_key_ids"
        };
        private static readonly HashSet<string> TravelFields = new HashSet<string>(CampaignFields, StringComparer.Ordinal)
        { "world_state" };

        public static string Attach(string nativeJson, RunMetadata metadata)
        {
            if (metadata == null) throw new ArgumentNullException(nameof(metadata));
            using (JsonDocument document = ParseObject(nativeJson))
            {
                foreach (JsonProperty property in document.RootElement.EnumerateObject())
                    if (property.NameEquals(ReservedKey))
                        throw new ArgumentException("Native JSON already contains reserved Job Fair metadata.");
                return WriteObject(document.RootElement, metadata, false);
            }
        }

        public static string Extract(string input, out RunMetadata metadata)
        {
            metadata = null;
            using (JsonDocument document = ParseObject(input))
            {
                bool found = false;
                JsonElement value = default;
                foreach (JsonProperty property in document.RootElement.EnumerateObject())
                {
                    if (!property.NameEquals(ReservedKey)) continue;
                    if (found) throw new ArgumentException("Duplicate Job Fair metadata property.");
                    found = true;
                    value = property.Value;
                }
                if (!found) return input;
                // Publish metadata only after the full extraction succeeds.
                RunMetadata parsed = ReadMetadata(value);
                string clean = WriteObject(document.RootElement, null, true);
                metadata = parsed;
                return clean;
            }
        }

        private static JsonDocument ParseObject(string input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            JsonDocument document = JsonDocument.Parse(input);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                document.Dispose();
                throw new ArgumentException("Native UserData JSON must be an object.");
            }
            return document;
        }

        private static RunMetadata ReadMetadata(JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("Job Fair metadata must be an object.");
            var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (JsonProperty property in value.EnumerateObject())
            {
                if (!fields.TryAdd(property.Name, property.Value))
                    throw new ArgumentException("Duplicate Job Fair metadata field: " + property.Name);
            }
            if (!fields.ContainsKey("schema_version")) throw new ArgumentException("Missing Job Fair schema.");
            int schema;
            if (fields["schema_version"].ValueKind != JsonValueKind.Number ||
                !fields["schema_version"].TryGetInt32(out schema))
                throw new ArgumentException("Job Fair schema must be an integer.");
            if (schema != 1 && schema != 2 && schema != 3) throw new ArgumentException("Unsupported Job Fair metadata schema.");
            HashSet<string> required = schema == 1 ? MetadataFields : schema == 2 ? CampaignFields : TravelFields;
            if (fields.Count != required.Count) throw new ArgumentException("Job Fair metadata fields are missing or unknown.");
            foreach (string name in fields.Keys)
                if (!required.Contains(name)) throw new ArgumentException("Unrecognized Job Fair metadata field: " + name);
            if (schema == 2 || schema == 3)
                return new RunMetadata(schema, ReadString(fields["seed"]), ReadString(fields["generator"]),
                    ReadString(fields["run_id"]), ReadString(fields["bartz_name"]), ReadString(fields["mode"]),
                    ReadSettings(fields["settings"]), ReadString(fields["catalog_id"]), ReadJobs(fields["starting_job_ids"]),
                    ReadPlacements(fields["placements"]), ReadString(fields["placement_hash"]),
                    ReadStrings(fields["claimed_location_ids"]), ReadStrings(fields["acquired_key_ids"]),
                    schema == 3 ? ReadWorldState(fields["world_state"]) : null);
            JsonElement jobs = fields["starting_job_ids"];
            if (jobs.ValueKind != JsonValueKind.Array || jobs.GetArrayLength() != 4)
                throw new ArgumentException("Exactly four starting job IDs are required.");
            int[] ids = new int[4];
            int index = 0;
            foreach (JsonElement job in jobs.EnumerateArray())
            {
                int id;
                if (job.ValueKind != JsonValueKind.Number || !job.TryGetInt32(out id))
                    throw new ArgumentException("Starting job IDs must be integers.");
                ids[index++] = id;
            }
            return new RunMetadata(schema, ReadString(fields["seed"]), ReadString(fields["generator"]),
                ReadString(fields["run_id"]), ReadString(fields["bartz_name"]), ids);
        }

        internal static string ReadString(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.String)
                throw new ArgumentException("Job Fair metadata text fields must be strings.");
            return element.GetString();
        }

        internal static Dictionary<string, JsonElement> ReadFields(JsonElement value, params string[] names)
        {
            if (value.ValueKind != JsonValueKind.Object) throw new ArgumentException("Expected a Job Fair configuration object.");
            var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            var required = new HashSet<string>(names, StringComparer.Ordinal);
            foreach (JsonProperty property in value.EnumerateObject())
                if (!required.Contains(property.Name) || !fields.TryAdd(property.Name, property.Value))
                    throw new ArgumentException("Unknown or duplicate Job Fair field: " + property.Name);
            if (fields.Count != required.Count) throw new ArgumentException("Missing Job Fair configuration field.");
            return fields;
        }

        internal static int ReadInteger(JsonElement value)
        {
            int result;
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out result))
                throw new ArgumentException("Job Fair integer field must be an integer.");
            return result;
        }

        internal static int[] ReadJobs(JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > 4)
                throw new ArgumentException("Invalid starting job array.");
            var result = new int[value.GetArrayLength()];
            int index = 0;
            foreach (JsonElement entry in value.EnumerateArray()) result[index++] = ReadInteger(entry);
            return result;
        }

        private static string[] ReadStrings(JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > 4096)
                throw new ArgumentException("Invalid claim/key state array.");
            var result = new string[value.GetArrayLength()];
            int index = 0;
            foreach (JsonElement entry in value.EnumerateArray()) result[index++] = ReadString(entry);
            return result;
        }

        internal static RunSettings ReadSettings(JsonElement value)
        {
            JsonElement ignored;
            bool shops = value.ValueKind == JsonValueKind.Object && value.TryGetProperty("shop_scope", out ignored);
            bool bosses = value.ValueKind == JsonValueKind.Object && value.TryGetProperty("boss_scope", out ignored);
            bool expanded = value.ValueKind == JsonValueKind.Object
                && (value.TryGetProperty("loot_scope", out ignored) || value.TryGetProperty("boss_experience", out ignored));
            Dictionary<string, JsonElement> fields = expanded ? ReadFields(value,
                "job_rewards", "key_items", "treasure", "treasure_scope", "world_access", "shop_scope", "shop_prices",
                "boss_scope", "boss_scaling", "loot_scope", "boss_experience") : bosses ? ReadFields(value,
                "job_rewards", "key_items", "treasure", "treasure_scope", "world_access", "shop_scope", "shop_prices", "boss_scope", "boss_scaling") : shops ? ReadFields(value,
                "job_rewards", "key_items", "treasure", "treasure_scope", "world_access", "shop_scope", "shop_prices")
                : ReadFields(value, "job_rewards", "key_items", "treasure", "treasure_scope", "world_access");
            return new RunSettings(ReadString(fields["job_rewards"]), ReadString(fields["key_items"]),
                ReadString(fields["treasure"]), ReadString(fields["treasure_scope"]), ReadString(fields["world_access"]),
                shops ? ReadString(fields["shop_scope"]) : null, shops ? ReadString(fields["shop_prices"]) : null,
                bosses ? ReadString(fields["boss_scope"]) : null, bosses ? ReadString(fields["boss_scaling"]) : null,
                expanded ? ReadString(fields["loot_scope"]) : null, expanded ? ReadString(fields["boss_experience"]) : null);
        }

        internal static RewardPlacement[] ReadPlacements(JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > 4096)
                throw new ArgumentException("Invalid placement array.");
            var result = new RewardPlacement[value.GetArrayLength()];
            int index = 0;
            foreach (JsonElement entry in value.EnumerateArray())
            {
                Dictionary<string, JsonElement> fields = ReadFields(entry, "location_id", "reward_kind", "reward_id", "quantity");
                result[index++] = new RewardPlacement(ReadString(fields["location_id"]), ReadString(fields["reward_kind"]),
                    ReadString(fields["reward_id"]), ReadInteger(fields["quantity"]));
            }
            return result;
        }

        internal static WorldTravelState ReadWorldState(JsonElement value)
        {
            var fields = ReadFields(value, "version", "active_world");
            return new WorldTravelState(ReadInteger(fields["version"]), ReadInteger(fields["active_world"]));
        }

        private static string WriteObject(JsonElement original, RunMetadata metadata, bool removeMetadata)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(stream))
                {
                    writer.WriteStartObject();
                    foreach (JsonProperty property in original.EnumerateObject())
                    {
                        if (removeMetadata && property.NameEquals(ReservedKey)) continue;
                        // Preserve duplicate native keys, original number lexemes
                        // and nested JSON values; never collapse into a dictionary.
                        writer.WritePropertyName(property.Name);
                        writer.WriteRawValue(property.Value.GetRawText(), skipInputValidation: true);
                    }
                    if (metadata != null)
                    {
                        writer.WritePropertyName(ReservedKey);
                        writer.WriteStartObject();
                        writer.WriteNumber("schema_version", metadata.SchemaVersion);
                        writer.WriteString("seed", metadata.Seed);
                        writer.WriteString("generator", metadata.Generator);
                        writer.WriteString("run_id", metadata.RunId);
                        writer.WriteString("bartz_name", metadata.BartzName);
                        writer.WriteStartArray("starting_job_ids");
                        foreach (int job in metadata.StartingJobs) writer.WriteNumberValue(job);
                        writer.WriteEndArray();
                        if (metadata.SchemaVersion == 2 || metadata.SchemaVersion == 3)
                        {
                            writer.WriteString("mode", metadata.Mode);
                            WriteSettings(writer, metadata.Settings);
                            writer.WriteString("catalog_id", metadata.CatalogId);
                            writer.WriteString("placement_hash", metadata.PlacementHash);
                            writer.WriteStartArray("placements");
                            foreach (RewardPlacement placement in metadata.Placements)
                            {
                                writer.WriteStartObject();
                                writer.WriteString("location_id", placement.LocationId);
                                writer.WriteString("reward_kind", placement.RewardKind);
                                writer.WriteString("reward_id", placement.RewardId);
                                writer.WriteNumber("quantity", placement.Quantity);
                                writer.WriteEndObject();
                            }
                            writer.WriteEndArray();
                            writer.WriteStartArray("claimed_location_ids");
                            foreach (string claim in metadata.ClaimedLocationIds) writer.WriteStringValue(claim);
                            writer.WriteEndArray();
                            writer.WriteStartArray("acquired_key_ids");
                            foreach (string key in metadata.AcquiredKeyIds) writer.WriteStringValue(key);
                            writer.WriteEndArray();
                            if (metadata.SchemaVersion == 3)
                            {
                                writer.WriteStartObject("world_state");
                                writer.WriteNumber("version", metadata.WorldState.Version);
                                writer.WriteNumber("active_world", metadata.WorldState.ActiveWorld);
                                writer.WriteEndObject();
                            }
                        }
                        writer.WriteEndObject();
                    }
                    writer.WriteEndObject();
                }
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static void WriteSettings(Utf8JsonWriter writer, RunSettings settings)
        {
            writer.WriteStartObject("settings");
            writer.WriteString("job_rewards", settings.JobRewards);
            writer.WriteString("key_items", settings.KeyItems);
            writer.WriteString("treasure", settings.Treasure);
            writer.WriteString("treasure_scope", settings.TreasureScope);
            writer.WriteString("world_access", settings.WorldAccess);
            if (settings.ShopScope != null)
            {
                writer.WriteString("shop_scope", settings.ShopScope);
                writer.WriteString("shop_prices", settings.ShopPrices);
            }
            if (settings.BossScope != null)
            {
                writer.WriteString("boss_scope", settings.BossScope);
                writer.WriteString("boss_scaling", settings.BossScaling);
            }
            if (settings.LootScope != null) writer.WriteString("loot_scope", settings.LootScope);
            if (settings.BossExperience != null) writer.WriteString("boss_experience", settings.BossExperience);
            writer.WriteEndObject();
        }
    }
}
