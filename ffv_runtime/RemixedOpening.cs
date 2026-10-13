// Remixed's distinct startup reuses the accepted party/Wind entrance sequence.
// See research/ff5/startup/REMIXED_OPENING_AUDIT.md for source and terrain pins.
using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace ProjectBlues.Runtime
{
    public sealed class RemixedOpening
    {
        public const string Version = "remixed-wind-krile-v2";
        public const int InstructionCount = 34;
        public const int MapId = 202, PointId = 1;
        public const int AirshipId = 11, AirshipMapId = 1, AirshipX = 182, AirshipY = 53;
        public string Json { get; }
        public OpeningScript Script { get; }

        public RemixedOpening(string verifiedLegacyJson)
        {
            // ModeOpening verifies the complete revision 2 hash and preserves
            // party joins, four runtime grant markers, and native intro state.
            var mixed = new ModeOpening(verifiedLegacyJson);
            using (var source = JsonDocument.Parse(mixed.MixedJson))
            using (var output = new MemoryStream())
            {
                var sequence = source.RootElement.GetProperty("Mnemonics");
                if (sequence.GetArrayLength() != 32 || !IsOriginalShip(sequence[29]))
                    throw new InvalidOperationException("The accepted Wind opening source changed.");
                using (var writer = new Utf8JsonWriter(output))
                {
                    writer.WriteStartObject();
                    foreach (var property in source.RootElement.EnumerateObject())
                    {
                        writer.WritePropertyName(property.Name);
                        if (property.Name == "Segments")
                        {
                            writer.WriteStartArray();
                            foreach (var segment in property.Value.EnumerateArray())
                            {
                                writer.WriteStartObject();
                                foreach (var field in segment.EnumerateObject())
                                {
                                    writer.WritePropertyName(field.Name);
                                    if (field.Name == "Count") writer.WriteNumberValue(InstructionCount);
                                    else field.Value.WriteTo(writer);
                                }
                                writer.WriteEndObject();
                            }
                            writer.WriteEndArray();
                            continue;
                        }
                        if (property.Name != "Mnemonics") { property.Value.WriteTo(writer); continue; }
                        writer.WriteStartArray();
                        for (int index = 0; index < sequence.GetArrayLength(); index++)
                        {
                            if (index == 29) WriteAirship(writer, sequence[index]);
                            else sequence[index].WriteTo(writer);
                            if (index == 13)
                            {
                                // Native first JoinKrile transfers Galuf's record.
                                // Initialize him normally before using the two
                                // native party calls; never create a fifth member.
                                // The seeded grant markers remain at indexes10–13.
                                WritePartyCall(writer, sequence[8], "パーティ離脱：ガラフ");
                                WritePartyCall(writer, sequence[8], "パーティ加入：クルル");
                            }
                        }
                        writer.WriteEndArray();
                    }
                    writer.WriteEndObject();
                }
                Json = Encoding.UTF8.GetString(output.ToArray());
            }
            Script = new OpeningScript(Json);
        }

        private static void WritePartyCall(Utf8JsonWriter writer, JsonElement original, string nativeToken)
        {
            // Both commands are source-pinned native SysCalls with zero operands.
            // Preserve the accepted join command's complete shape and replace
            // only its first syscall token. This runs only in a new opening.
            if (original.GetProperty("mnemonic").GetString() != "SysCall"
                || original.GetProperty("operands").GetProperty("sValues")[0].GetString() != "パーティ加入：ガラフ")
                throw new InvalidOperationException("Native Galuf initialization source changed.");
            writer.WriteStartObject();
            foreach (var property in original.EnumerateObject())
            {
                writer.WritePropertyName(property.Name);
                if (property.Name != "operands") { property.Value.WriteTo(writer); continue; }
                writer.WriteStartObject();
                foreach (var operand in property.Value.EnumerateObject())
                {
                    writer.WritePropertyName(operand.Name);
                    if (operand.Name != "sValues") { operand.Value.WriteTo(writer); continue; }
                    writer.WriteStartArray();
                    int slot = 0;
                    foreach (var value in operand.Value.EnumerateArray())
                        if (slot++ == 0) writer.WriteStringValue(nativeToken); else value.WriteTo(writer);
                    writer.WriteEndArray();
                }
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }

        private static bool IsOriginalShip(JsonElement command)
        {
            if (command.GetProperty("mnemonic").GetString() != "SetVehicle") return false;
            var values = command.GetProperty("operands").GetProperty("iValues");
            int[] expected = { 2, 1, 182, 57, 0, 0, 0, 0 };
            if (values.GetArrayLength() != expected.Length) return false;
            for (int slot = 0; slot < expected.Length; slot++)
                if (values[slot].GetInt32() != expected[slot]) return false;
            return true;
        }

        private static void WriteAirship(Utf8JsonWriter writer, JsonElement original)
        {
            writer.WriteStartObject();
            foreach (var property in original.EnumerateObject())
            {
                writer.WritePropertyName(property.Name);
                if (property.Name != "operands") { property.Value.WriteTo(writer); continue; }
                writer.WriteStartObject();
                foreach (var operand in property.Value.EnumerateObject())
                {
                    writer.WritePropertyName(operand.Name);
                    if (operand.Name != "iValues") { operand.Value.WriteTo(writer); continue; }
                    writer.WriteStartArray();
                    // Native sc_e_0071_4[7] uses vehicle11, layer0 and direction3.
                    // Its Ronka ascent capability opens the native boss route.
                    // Only map/x/y select the verified Wind exterior ground tile.
                    foreach (int value in new[] { AirshipId, AirshipMapId, AirshipX, AirshipY, 0, 3, 0, 0 })
                        writer.WriteNumberValue(value);
                    writer.WriteEndArray();
                }
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
    }
}
