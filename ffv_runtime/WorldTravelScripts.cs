// Pure, source-pinned Remixed exits, entry layouts and story-transition handling.
// These edits preserve instruction addresses and never write world story flags.
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProjectBlues.Runtime
{
    public sealed class WorldTravelScripts
    {
        private static WorldTravelScripts shared;
        public static WorldTravelScripts Shared => shared ?? (shared = new WorldTravelScripts());
        public OpeningScript[] Scripts { get; }
        private readonly Dictionary<int, JsonElement> exits = new Dictionary<int, JsonElement>();
        private readonly Dictionary<int, JsonElement> departures = new Dictionary<int, JsonElement>();
        private readonly Dictionary<int, JsonElement> conversions = new Dictionary<int, JsonElement>();
        private readonly Dictionary<int, JsonElement> nativeEvents = new Dictionary<int, JsonElement>();
        private readonly HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);

        public WorldTravelScripts()
        {
            if (Hash(NativeWorldTravelData.CatalogJson) != NativeWorldTravelData.CatalogJsonSha256)
                throw new ArgumentException("Embedded world travel catalog hash mismatch.");
            using (var document = JsonDocument.Parse(NativeWorldTravelData.CatalogJson))
            {
                JsonElement root = document.RootElement;
                if (root.GetProperty("catalog_id").GetString() != NativeWorldTravelData.CatalogId)
                    throw new ArgumentException("World travel catalog identity mismatch.");
                JsonElement records = root.GetProperty("scripts");
                Scripts = new OpeningScript[NativeWorldTravelData.ScriptJson.Length];
                if (records.GetArrayLength() != Scripts.Length) throw new ArgumentException("Travel script count mismatch.");
                foreach (var record in records.EnumerateArray())
                {
                    int index = record.GetProperty("script_index").GetInt32();
                    if (index < 0 || index >= Scripts.Length || Scripts[index] != null
                        || Hash(NativeWorldTravelData.ScriptJson[index]) != record.GetProperty("program_sha256").GetString())
                        throw new ArgumentException("Travel program pin mismatch.");
                    Scripts[index] = new OpeningScript(NativeWorldTravelData.ScriptJson[index]);
                }
                foreach (var record in root.GetProperty("shared_exits").EnumerateArray())
                {
                    int index = record.GetProperty("script_index").GetInt32();
                    if (!exits.TryAdd(index, record.Clone()) || !names.Add(record.GetProperty("name").GetString()))
                        throw new ArgumentException("Duplicate shared exit.");
                }
                foreach (var record in root.GetProperty("native_departures").EnumerateArray())
                {
                    int index = record.GetProperty("script_index").GetInt32();
                    if (exits.ContainsKey(index) || !departures.TryAdd(index, record.Clone())
                        || !names.Add(record.GetProperty("name").GetString()))
                        throw new ArgumentException("Duplicate native departure.");
                }
                foreach (var record in root.GetProperty("transport_conversions").EnumerateArray())
                {
                    int index = record.GetProperty("script_index").GetInt32();
                    if (exits.ContainsKey(index) || departures.ContainsKey(index) || !conversions.TryAdd(index, record.Clone())
                        || !names.Add(record.GetProperty("name").GetString()))
                        throw new ArgumentException("Duplicate transport conversion.");
                }
                foreach (var record in root.GetProperty("native_events").EnumerateArray())
                {
                    int index = record.GetProperty("script_index").GetInt32();
                    if (exits.ContainsKey(index) || departures.ContainsKey(index) || conversions.ContainsKey(index)
                        || !nativeEvents.TryAdd(index, record.Clone()))
                        throw new ArgumentException("Duplicate native world event.");
                    // Native Kuzar defines distinct sc_e_0420 programs on two
                    // maps; full program identities and map guards disambiguate.
                    names.Add(record.GetProperty("name").GetString());
                    using (var program = JsonDocument.Parse(NativeWorldTravelData.ScriptJson[index]))
                    {
                        var commands = program.RootElement.GetProperty("Mnemonics");
                        foreach (var edit in record.GetProperty("edits").EnumerateArray())
                            if (JsonSerializer.Serialize(commands[edit.GetProperty("index").GetInt32()])
                                != JsonSerializer.Serialize(edit.GetProperty("original")))
                                throw new ArgumentException("Native event edit no longer matches its pinned command.");
                    }
                }
                if (exits.Count != 21 || departures.Count != 1 || conversions.Count != 2 || nativeEvents.Count < 12)
                    throw new ArgumentException("Travel route guard count mismatch.");
            }
        }

        public bool HandlesScript(int index) => exits.ContainsKey(index) || departures.ContainsKey(index) || conversions.ContainsKey(index) || nativeEvents.ContainsKey(index);
        public bool IsKnownScriptName(string name) => name != null && names.Contains(name);

        public string BlockedReason(int index)
        {
            if (!departures.TryGetValue(index, out var record)) return null;
            return record.GetProperty("policy").GetString() == "throw_unexpected_late_continuation"
                ? null : "Use the Warp Shard to travel between unlocked worlds.";
        }

        public string RewriteScript(int index, int currentMapId, int activeWorld)
        {
            if (!HandlesScript(index)) throw new ArgumentOutOfRangeException(nameof(index), "Program is evidence only, not a handled travel script.");
            if (activeWorld < 1 || activeWorld > 3) throw new ArgumentOutOfRangeException(nameof(activeWorld));
            if (nativeEvents.TryGetValue(index, out var nativeEvent))
                return RewriteNativeEvent(index, currentMapId, activeWorld, nativeEvent);
            int changedIndex;
            int secondaryNopIndex = -1;
            string mnemonic;
            int target = 0;
            if (exits.TryGetValue(index, out var record))
            {
                if (record.GetProperty("map_id").GetInt32() != currentMapId
                    || (activeWorld != record.GetProperty("original_world").GetInt32() && activeWorld != 3))
                    throw new InvalidOperationException("Shared exit does not belong to the saved active world or current map.");
                changedIndex = record.GetProperty("branch_index").GetInt32();
                mnemonic = activeWorld == 3 ? "Jump" : "Nop";
                target = record.GetProperty("merged_branch_target").GetInt32();
            }
            else if (conversions.TryGetValue(index, out record))
            {
                if (!record.GetProperty("destinations").TryGetProperty(activeWorld.ToString(), out var destination)
                    || destination.GetProperty("map_id").GetInt32() != currentMapId)
                    throw new InvalidOperationException("Transport conversion does not belong to the saved active world or map.");
                changedIndex = record.GetProperty("replace_index").GetInt32();
                mnemonic = "Jump";
                target = destination.GetProperty("jump_target").GetInt32();
                if (activeWorld == 1) secondaryNopIndex = record.GetProperty("world1_secondary_nop_index").GetInt32();
            }
            else
            {
                record = departures[index];
                if (record.GetProperty("map_id").GetInt32() != currentMapId
                    || record.GetProperty("original_world").GetInt32() != activeWorld)
                    throw new InvalidOperationException("Native departure arrived from an unexpected map or world.");
                if (record.GetProperty("policy").GetString() == "throw_unexpected_late_continuation")
                    throw new InvalidOperationException("Unexpected native boss-victory continuation cannot be adopted by world travel.");
                changedIndex = record.GetProperty("replace_index").GetInt32();
                mnemonic = "Exit";
            }
            using (var document = JsonDocument.Parse(NativeWorldTravelData.ScriptJson[index]))
            using (var output = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(output))
                {
                    writer.WriteStartObject();
                    foreach (var property in document.RootElement.EnumerateObject())
                    {
                        writer.WritePropertyName(property.Name);
                        if (property.Name != "Mnemonics") { property.Value.WriteTo(writer); continue; }
                        writer.WriteStartArray();
                        int instruction = 0;
                        foreach (var command in property.Value.EnumerateArray())
                        {
                            if (instruction == changedIndex) WriteControl(writer, command, mnemonic, target);
                            else if (instruction == secondaryNopIndex) WriteControl(writer, command, "Nop", 0);
                            else command.WriteTo(writer);
                            instruction++;
                        }
                        writer.WriteEndArray();
                    }
                    writer.WriteEndObject();
                }
                return Encoding.UTF8.GetString(output.ToArray());
            }
        }

        private static string RewriteNativeEvent(int index, int map, int world, JsonElement record)
        {
            bool allowed = false;
            foreach (var value in record.GetProperty("allowed_active_worlds").EnumerateArray())
                if (value.GetInt32() == world) allowed = true;
            if (!allowed || record.GetProperty("map_id").GetInt32() != map)
                throw new InvalidOperationException("Native world event does not belong to the owned world or current map.");
            var edits = new Dictionary<int, JsonElement>();
            foreach (var edit in record.GetProperty("edits").EnumerateArray())
                foreach (var value in edit.GetProperty("worlds").EnumerateArray())
                    if (value.GetInt32() == world && !edits.TryAdd(edit.GetProperty("index").GetInt32(), edit.GetProperty("replacement")))
                        throw new InvalidOperationException("Native world event has overlapping edits.");
            using (var document = JsonDocument.Parse(NativeWorldTravelData.ScriptJson[index]))
            using (var output = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(output))
                {
                    writer.WriteStartObject();
                    foreach (var property in document.RootElement.EnumerateObject())
                    {
                        writer.WritePropertyName(property.Name);
                        if (property.Name != "Mnemonics") { property.Value.WriteTo(writer); continue; }
                        writer.WriteStartArray();
                        int instruction = 0;
                        foreach (var command in property.Value.EnumerateArray())
                        {
                            if (edits.TryGetValue(instruction++, out var replacement)) replacement.WriteTo(writer);
                            else command.WriteTo(writer);
                        }
                        writer.WriteEndArray();
                    }
                    writer.WriteEndObject();
                }
                return Encoding.UTF8.GetString(output.ToArray());
            }
        }

        private static void WriteControl(Utf8JsonWriter writer, JsonElement original, string mnemonic, int target)
        {
            writer.WriteStartObject();
            foreach (var property in original.EnumerateObject())
            {
                writer.WritePropertyName(property.Name);
                if (property.Name == "mnemonic") writer.WriteStringValue(mnemonic);
                else if (property.Name == "operands")
                {
                    writer.WriteStartObject();
                    writer.WriteStartArray("iValues");
                    if (mnemonic != "Nop") for (int slot = 0; slot < 8; slot++) writer.WriteNumberValue(slot == 0 && mnemonic == "Jump" ? target : 0);
                    writer.WriteEndArray();
                    writer.WriteStartArray("rValues");
                    if (mnemonic != "Nop") for (int slot = 0; slot < 8; slot++) writer.WriteNumberValue(0);
                    writer.WriteEndArray();
                    writer.WriteStartArray("sValues");
                    if (mnemonic != "Nop") for (int slot = 0; slot < 8; slot++) writer.WriteStringValue("");
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }
                else property.Value.WriteTo(writer);
            }
            writer.WriteEndObject();
        }

        private static string Hash(string value)
        {
            using (var hash = SHA256.Create())
                return Convert.ToHexString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
        }
    }
}
