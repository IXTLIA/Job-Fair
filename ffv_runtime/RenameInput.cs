// Portable name editing; does not depend on Unity's stripped TextEditor class.
using System;
using System.Globalization;
using System.Text;

namespace ProjectBlues.Runtime
{
    public sealed class RenameInput
    {
        private const int MaximumElements = 6;
        private const int MaximumUtf16 = 24;
        private bool replaceInitial = true;

        public string Text { get; private set; }
        public string Validation { get; private set; }

        public RenameInput(string initial)
        {
            Text = initial ?? "";
            Validation = "";
            if (!IsWellFormedAndPrintable(Text) || !Fits(Text))
            {
                Text = "";
                Validation = "Choose a name with 1 to 6 visible characters.";
            }
        }

        public void Insert(string printableFrame)
        {
            if (String.IsNullOrEmpty(printableFrame)) return;
            // Validate before filtering so a control between two surrogate halves
            // cannot accidentally turn malformed input into an accepted pair.
            if (!IsWellFormedAndPrintable(printableFrame, true))
            {
                Validation = "That input contains an unsupported character.";
                return;
            }
            // Return/backspace belong to the caller's key handling, never text.
            var filtered = new StringBuilder();
            foreach (char character in printableFrame)
                if (character != '\r' && character != '\n' && character != '\b')
                    filtered.Append(character);
            string incoming = filtered.ToString();
            if (incoming.Length == 0) return;
            if (!IsWellFormedAndPrintable(incoming))
            {
                Validation = "That input contains an unsupported character.";
                return;
            }

            string working = replaceInitial ? "" : Text;
            int[] starts = StringInfo.ParseCombiningCharacters(incoming);
            bool accepted = false;
            bool limited = false;
            for (int index = 0; index < starts.Length; index++)
            {
                int end = index + 1 < starts.Length ? starts[index + 1] : incoming.Length;
                string candidate = working + incoming.Substring(starts[index], end - starts[index]);
                if (!Fits(candidate))
                {
                    limited = true;
                    break;
                }
                working = candidate;
                accepted = true;
            }
            if (accepted)
            {
                Text = working;
                replaceInitial = false;
            }
            Validation = limited ? "The name is full. Use up to 6 visible characters, or a shorter name." : "";
        }

        public void Backspace()
        {
            if (replaceInitial)
                Text = "";
            else if (Text.Length > 0)
            {
                int[] starts = StringInfo.ParseCombiningCharacters(Text);
                Text = Text.Substring(0, starts[starts.Length - 1]);
            }
            replaceInitial = false;
            Validation = "";
        }

        public void Clear()
        {
            Text = "";
            replaceInitial = false;
            Validation = "";
        }

        public bool TryConfirm(out string name)
        {
            name = null;
            string candidate = Text.Trim();
            if (candidate.Length == 0 || !IsWellFormedAndPrintable(candidate) || !Fits(candidate))
            {
                Validation = "Choose a name with 1 to 6 visible characters.";
                return false;
            }
            try
            {
                RunMetadata.ValidateName(candidate);
            }
            catch (ArgumentException)
            {
                Validation = "That name cannot be stored. Choose another name.";
                return false;
            }
            name = candidate;
            Text = candidate;
            Validation = "";
            return true;
        }

        private static bool Fits(string value)
        {
            return value.Length <= MaximumUtf16
                && StringInfo.ParseCombiningCharacters(value).Length <= MaximumElements;
        }

        private static bool IsWellFormedAndPrintable(string value, bool allowKeyControls = false)
        {
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (Char.IsControl(character)
                    && !(allowKeyControls && (character == '\r' || character == '\n' || character == '\b')))
                    return false;
                if (Char.IsHighSurrogate(character))
                {
                    if (index + 1 >= value.Length || !Char.IsLowSurrogate(value[index + 1])) return false;
                    index++;
                }
                else if (Char.IsLowSurrogate(character)) return false;
            }
            return true;
        }
    }
}
