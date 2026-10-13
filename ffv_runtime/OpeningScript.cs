// Portable content identity for an externally hash-verified startup script.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace ProjectBlues.Runtime
{
    public sealed class OpeningScript
    {
        private readonly JsonElement verified;
        private readonly Instruction[] instructions;
        public int InstructionCount => instructions.Length;

        public OpeningScript(string verifiedJson)
        {
            if (verifiedJson == null) throw new ArgumentNullException(nameof(verifiedJson));
            try
            {
                using (JsonDocument document = JsonDocument.Parse(verifiedJson))
                {
                    if (document.RootElement.ValueKind != JsonValueKind.Object
                        || !UniqueProperties(document.RootElement))
                        throw new ArgumentException("Verified startup must be an object with unique JSON properties.");
                    verified = document.RootElement.Clone();
                }
                JsonElement sequence = verified.GetProperty("Mnemonics");
                if (sequence.ValueKind != JsonValueKind.Array || sequence.GetArrayLength() == 0)
                    throw new ArgumentException("Verified startup requires an instruction array.");
                instructions = new Instruction[sequence.GetArrayLength()];
                for (int index = 0; index < instructions.Length; index++)
                    instructions[index] = ReadInstruction(sequence[index]);
            }
            catch (Exception exception) when (exception is JsonException
                || exception is InvalidOperationException || exception is FormatException
                || exception is KeyNotFoundException || exception is OverflowException
                || exception is ArgumentException)
            {
                throw new ArgumentException("Invalid verified startup JSON.", nameof(verifiedJson), exception);
            }
        }

        public bool Matches(string candidate)
        {
            if (candidate == null) return false;
            try
            {
                using (JsonDocument document = JsonDocument.Parse(candidate))
                    return Equivalent(verified, document.RootElement);
            }
            catch (JsonException) { return false; }
        }

        public bool MatchesInstruction(int index, string label, string mnemonic, int type,
                                       int[] integers, double[] reals, string[] strings)
        {
            if (index < 0 || index >= instructions.Length
                || integers == null || reals == null || strings == null) return false;
            Instruction expected = instructions[index];
            if (!String.Equals(expected.Label, label, StringComparison.Ordinal)
                || !String.Equals(expected.Mnemonic, mnemonic, StringComparison.Ordinal)
                || expected.Type != type || integers.Length != expected.Integers.Length
                || reals.Length != expected.Reals.Length || strings.Length != expected.Strings.Length)
                return false;
            for (int slot = 0; slot < integers.Length; slot++)
                if (integers[slot] != expected.Integers[slot]) return false;
            for (int slot = 0; slot < reals.Length; slot++)
                if (reals[slot] != expected.Reals[slot]) return false;
            for (int slot = 0; slot < strings.Length; slot++)
                if (!String.Equals(strings[slot], expected.Strings[slot], StringComparison.Ordinal)) return false;
            return true;
        }

        private static bool UniqueProperties(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonProperty property in element.EnumerateObject())
                    if (!names.Add(property.Name) || !UniqueProperties(property.Value)) return false;
            }
            else if (element.ValueKind == JsonValueKind.Array)
                foreach (JsonElement item in element.EnumerateArray())
                    if (!UniqueProperties(item)) return false;
            return true;
        }

        private static bool Equivalent(JsonElement left, JsonElement right)
        {
            if (left.ValueKind != right.ValueKind) return false;
            switch (left.ValueKind)
            {
                case JsonValueKind.Object:
                    var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                    foreach (JsonProperty property in right.EnumerateObject())
                        if (!properties.TryAdd(property.Name, property.Value)) return false;
                    int count = 0;
                    foreach (JsonProperty property in left.EnumerateObject())
                    {
                        JsonElement value;
                        if (!properties.TryGetValue(property.Name, out value)
                            || !Equivalent(property.Value, value)) return false;
                        count++;
                    }
                    return count == properties.Count;
                case JsonValueKind.Array:
                    if (left.GetArrayLength() != right.GetArrayLength()) return false;
                    for (int index = 0; index < left.GetArrayLength(); index++)
                        if (!Equivalent(left[index], right[index])) return false;
                    return true;
                case JsonValueKind.Number:
                    return NumbersEqual(left.GetRawText(), right.GetRawText());
                case JsonValueKind.String:
                    return String.Equals(left.GetString(), right.GetString(), StringComparison.Ordinal);
                case JsonValueKind.True:
                case JsonValueKind.False:
                case JsonValueKind.Null:
                    return true;
                default:
                    return false;
            }
        }

        private static bool NumbersEqual(string left, string right)
        {
            if (String.Equals(left, right, StringComparison.Ordinal)) return true;
            string normalizedLeft, normalizedRight;
            return NormalizeNumber(left, out normalizedLeft)
                && NormalizeNumber(right, out normalizedRight)
                && String.Equals(normalizedLeft, normalizedRight, StringComparison.Ordinal);
        }

        // Exact decimal lexemes avoid Decimal/Double rounding a changed operand
        // into an expected value. Unsupported exponent overflow fails closed.
        private static bool NormalizeNumber(string text, out string normalized)
        {
            normalized = null;
            bool negative = text[0] == '-';
            if (negative) text = text.Substring(1);
            int exponentIndex = text.IndexOf('e');
            if (exponentIndex < 0) exponentIndex = text.IndexOf('E');
            string mantissa = exponentIndex < 0 ? text : text.Substring(0, exponentIndex);
            int point = mantissa.IndexOf('.');
            int fraction = point < 0 ? 0 : mantissa.Length - point - 1;
            string digits = mantissa.Replace(".", "").TrimStart('0');
            if (digits.Length == 0) { normalized = "0"; return true; }
            long exponent = 0;
            if (exponentIndex >= 0 && !Int64.TryParse(text.Substring(exponentIndex + 1),
                    NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent)) return false;
            string significant = digits.TrimEnd('0');
            int trailing = digits.Length - significant.Length;
            try { exponent = checked(exponent - fraction + trailing); }
            catch (OverflowException) { return false; }
            normalized = (negative ? "-" : "") + significant + "e"
                + exponent.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        private static Instruction ReadInstruction(JsonElement element)
        {
            var result = new Instruction();
            result.Label = RequiredString(element.GetProperty("label"));
            result.Mnemonic = RequiredString(element.GetProperty("mnemonic"));
            result.Type = element.GetProperty("type").GetInt32();
            JsonElement operands = element.GetProperty("operands");
            JsonElement integers = operands.GetProperty("iValues");
            JsonElement reals = operands.GetProperty("rValues");
            JsonElement strings = operands.GetProperty("sValues");
            result.Integers = new int[integers.GetArrayLength()];
            result.Reals = new double[reals.GetArrayLength()];
            result.Strings = new string[strings.GetArrayLength()];
            for (int slot = 0; slot < result.Integers.Length; slot++)
                result.Integers[slot] = integers[slot].GetInt32();
            for (int slot = 0; slot < result.Reals.Length; slot++)
            {
                double value = reals[slot].GetDouble();
                if (Double.IsNaN(value) || Double.IsInfinity(value))
                    throw new ArgumentException("Verified native real operands must be finite.");
                result.Reals[slot] = value;
            }
            for (int slot = 0; slot < result.Strings.Length; slot++)
                result.Strings[slot] = RequiredString(strings[slot]);
            return result;
        }

        private static string RequiredString(JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.String)
                throw new ArgumentException("Verified instruction text must be a string.");
            return value.GetString();
        }

        private sealed class Instruction
        {
            internal string Label, Mnemonic;
            internal int Type;
            internal int[] Integers;
            internal double[] Reals;
            internal string[] Strings;
        }
    }
}
