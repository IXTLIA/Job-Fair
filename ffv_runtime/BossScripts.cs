// Rewrites only numeric AI health thresholds in a fresh text asset. Branch labels,
// commands, target selectors, attack tables and the cached original remain intact.
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProjectBlues.Runtime
{
    public static class BossScripts
    {
        public static string Hash(string text)
        {
            using (var sha = SHA256.Create()) return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        }

        public static string RewriteAi(string text, JsonElement source, JsonElement destination, int monsterId)
        {
            JsonElement evidence = default;
            foreach (var item in source.GetProperty("ai_scripts").EnumerateArray())
                if (item.GetProperty("monster_id").GetInt32() == monsterId) { evidence = item; break; }
            if (evidence.ValueKind == JsonValueKind.Undefined || Hash(text) != evidence.GetProperty("sha256").GetString())
                throw new InvalidOperationException("Boss AI text differs from the pinned native script.");
            using (var document = JsonDocument.Parse(text))
            using (var stream = new MemoryStream())
            {
                var edits = new Dictionary<int, int>();
                var instructions = document.RootElement.GetProperty("Mnemonics");
                for (int index = 0; index < instructions.GetArrayLength(); index++)
                {
                    var instruction = instructions[index];
                    string name = instruction.GetProperty("mnemonic").GetString();
                    if (name != "IsParameterValue") continue;
                    var values = instruction.GetProperty("operands").GetProperty("iValues");
                    if (values.GetArrayLength() < 5) throw new InvalidOperationException("Boss HP condition has an unexpected shape.");
                    if (values[2].GetInt32() != 2) continue; // Native HP parameter type.
                    int selector = values[0].GetInt32(), target = values[1].GetInt32();
                    // Type 0/ID 0 and type 2 formation selectors reference the
                    // executing monster in the audited native roster. Explicit
                    // IDs are retained and must remain inside the same encounter.
                    int referencedMonster = selector == 0 && target != 0 ? target : monsterId;
                    if (selector != 0 && selector != 2) throw new InvalidOperationException("Unaudited boss HP target selector.");
                    edits.Add(index, BossCatalog.ScaleHpThreshold(source, destination, referencedMonster, values[3].GetInt32()));
                }
                if (edits.Count == 0) return text;
                using (var writer = new Utf8JsonWriter(stream))
                {
                    writer.WriteStartObject();
                    foreach (var property in document.RootElement.EnumerateObject())
                    {
                        writer.WritePropertyName(property.Name);
                        if (property.Name != "Mnemonics") { property.Value.WriteTo(writer); continue; }
                        writer.WriteStartArray();
                        for (int index = 0; index < instructions.GetArrayLength(); index++)
                            if (edits.TryGetValue(index, out int value)) WriteInstruction(writer, instructions[index], value);
                            else instructions[index].WriteTo(writer);
                        writer.WriteEndArray();
                    }
                    writer.WriteEndObject(); writer.Flush();
                }
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static void WriteInstruction(Utf8JsonWriter writer, JsonElement instruction, int threshold)
        {
            writer.WriteStartObject();
            foreach (var property in instruction.EnumerateObject())
            {
                writer.WritePropertyName(property.Name);
                if (property.Name != "operands") { property.Value.WriteTo(writer); continue; }
                writer.WriteStartObject();
                foreach (var operand in property.Value.EnumerateObject())
                {
                    writer.WritePropertyName(operand.Name);
                    if (operand.Name != "iValues") { operand.Value.WriteTo(writer); continue; }
                    writer.WriteStartArray(); int index = 0;
                    foreach (var item in operand.Value.EnumerateArray())
                    { writer.WriteNumberValue(index == 3 ? threshold : item.GetInt32()); index++; }
                    writer.WriteEndArray();
                }
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
    }
}
