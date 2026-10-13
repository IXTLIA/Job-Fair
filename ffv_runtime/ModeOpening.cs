// Original mode-aware startup transformer. Native source evidence is recorded in
// research/ff5/startup/MIXED_OPENING_AUDIT.md and mixed_opening_sources.json.
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProjectBlues.Runtime
{
    public sealed class ModeOpening
    {
        public const string Version = "mixed-wind-entrance-v1";
        public const int MixedMapId = 202;
        public const int MixedPointId = 1;
        public const int FirstStartingGrantIndex = 10;
        public const int StartingGrantCount = 4;
        public const string LegacyOpeningSha256 =
            "6b507e3ff62f9d78b2708bac5f2f74a706de8237ad5ea1fd25c546abbdf75e70";

        // Flag 4 remains set after the final meteor departure; later meteor
        // progression also requires it.
        // Flags 14 (Wind entrance), 15 (Wing Raptor), and 16 (Wind crystal/menu)
        // deliberately remain untouched so the native opening dungeon still runs.
        private static readonly int[] CompletedIntroFlags =
            { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13 };

        public string MixedJson { get; }
        public OpeningScript MixedScript { get; }

        public ModeOpening(string verifiedLegacyJson)
        {
            if (verifiedLegacyJson == null) throw new ArgumentNullException(nameof(verifiedLegacyJson));
            using (var hash = SHA256.Create())
            {
                string actual = Convert.ToHexString(hash.ComputeHash(Encoding.UTF8.GetBytes(verifiedLegacyJson)))
                    .ToLowerInvariant();
                if (!String.Equals(actual, LegacyOpeningSha256, StringComparison.Ordinal))
                    throw new ArgumentException("Mixed opening requires the exact verified revision 2 source.", nameof(verifiedLegacyJson));
            }
            using (JsonDocument source = JsonDocument.Parse(verifiedLegacyJson))
            using (var output = new MemoryStream())
            {
                var sequence = source.RootElement.GetProperty("Mnemonics");
                if (sequence.GetArrayLength() != 17)
                    throw new ArgumentException("Unexpected legacy startup instruction count.", nameof(verifiedLegacyJson));
                using (var writer = new Utf8JsonWriter(output))
                {
                    writer.WriteStartObject();
                    foreach (var property in source.RootElement.EnumerateObject())
                    {
                        writer.WritePropertyName(property.Name);
                        if (property.Name == "Mnemonics") WriteMixedSequence(writer, sequence);
                        else if (property.Name == "Segments")
                        {
                            writer.WriteStartArray();
                            writer.WriteStartObject();
                            writer.WriteString("Label", "Main");
                            writer.WriteNumber("EntryPoint", 0);
                            writer.WriteNumber("Count", 32);
                            writer.WriteEndObject();
                            writer.WriteEndArray();
                        }
                        else property.Value.WriteTo(writer);
                    }
                    writer.WriteEndObject();
                }
                MixedJson = Encoding.UTF8.GetString(output.ToArray());
            }
            MixedScript = new OpeningScript(MixedJson);
        }

        private static void WriteMixedSequence(Utf8JsonWriter writer, JsonElement source)
        {
            writer.WriteStartArray();
            // Naming and party joins remain byte-equivalent at the instruction
            // level. The four original grant calls are lifecycle markers: the
            // Mixed runtime MUST consume each with Core.Next without releasing a
            // job, committing run metadata at the fourth marker. A zero-job mode
            // therefore never indexes its empty StartingJobs array.
            for (int index = 0; index < FirstStartingGrantIndex + StartingGrantCount; index++)
                source[index].WriteTo(writer);
            foreach (int flag in CompletedIntroFlags)
                WriteCommand(writer, "SetFlag", new[] { flag, 0, 0, 0, 0, 0, 0, 0 }, "ScenarioFlag1");
            // Pendant from the skipped Lenna meeting. This is native key state,
            // not a randomized key or an extra ordinary-treasure placement.
            WriteCommand(writer, "GetItem", new[] { 42, 1, 0, 0, 0, 0, 0, 0 }, "");
            // Native pirate-ship arrival south of Wind Shrine; no early airship,
            // boarding token, or invented vehicle-release flag is supplied.
            WriteCommand(writer, "SetVehicle", new[] { 2, 1, 182, 57, 0, 0, 0, 0 }, "");
            WriteCommand(writer, "ChangeMap", new[] { MixedMapId, MixedPointId, 0, 0, 0, 0, 0, 0 }, "");
            source[16].WriteTo(writer);
            writer.WriteEndArray();
        }

        private static void WriteCommand(Utf8JsonWriter writer, string mnemonic, int[] integers, string firstString)
        {
            writer.WriteStartObject();
            writer.WriteString("label", "");
            writer.WriteString("mnemonic", mnemonic);
            writer.WriteStartObject("operands");
            writer.WriteStartArray("iValues");
            foreach (int value in integers) writer.WriteNumberValue(value);
            writer.WriteEndArray();
            writer.WriteStartArray("rValues");
            for (int slot = 0; slot < 8; slot++) writer.WriteNumberValue(0);
            writer.WriteEndArray();
            writer.WriteStartArray("sValues");
            writer.WriteStringValue(firstString);
            for (int slot = 1; slot < 8; slot++) writer.WriteStringValue("");
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteNumber("type", 1);
            writer.WriteString("comment", "");
            writer.WriteEndObject();
        }
    }
}
