// Replaces only the current execution's native instruction objects. Never edits
// the cached source array or any cached Mnemonic/Operands instance in place.
using System;
using System.Collections.Generic;
using System.Text.Json;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Last.Interpreter;
using NativeFormat = Last.Interpreter.Instructions.Format;

namespace ProjectBlues.Runtime
{
    internal static class NativeProgram
    {
        private sealed class OwnedReplacement
        {
            internal Il2CppReferenceArray<NativeFormat.Mnemonic> Program;
            internal OpeningScript Matcher;
            internal string Context;
        }
        // One retained replacement per native core, rather than one per scene
        // visit. Holding the array also prevents a recycled pointer from being
        // mistaken for an execution already transformed by another hook.
        private static readonly Dictionary<IntPtr, OwnedReplacement> Owned = new Dictionary<IntPtr, OwnedReplacement>();

        internal static bool IsOwnedReplacement(Integrator interpreter, string context)
        {
            if (interpreter == null || interpreter.mainCore == null) return false;
            Core core = interpreter.mainCore;
            OwnedReplacement owned;
            if (!Owned.TryGetValue(core.Pointer, out owned) || core.mnemonics == null
                || core.mnemonics.Pointer != owned.Program.Pointer) return false;
            if (interpreter.processing == null || interpreter.processing.Pointer != owned.Program.Pointer
                || owned.Context != context || !Matches(core, owned.Matcher))
                throw new InvalidOperationException("An owned native story program changed before dispatch.");
            return true;
        }

        internal static bool Matches(Core core, OpeningScript expected)
            => core != null && Matches(core.mnemonics, expected);

        private static bool Matches(Il2CppReferenceArray<NativeFormat.Mnemonic> program, OpeningScript expected)
        {
            if (program == null || expected == null || program.Length != expected.InstructionCount) return false;
            for (int index = 0; index < program.Length; index++)
            {
                var instruction = program[index];
                if (instruction == null || instruction.operands == null) return false;
                var operands = instruction.operands;
                if (operands.iValues == null || operands.rValues == null || operands.sValues == null) return false;
                var integers = new int[operands.iValues.Length];
                var reals = new double[operands.rValues.Length];
                var strings = new string[operands.sValues.Length];
                for (int slot = 0; slot < integers.Length; slot++) integers[slot] = operands.iValues[slot];
                for (int slot = 0; slot < reals.Length; slot++) reals[slot] = operands.rValues[slot];
                for (int slot = 0; slot < strings.Length; slot++) strings[slot] = operands.sValues[slot];
                if (!expected.MatchesInstruction(index, instruction.label, instruction.mnemonic,
                        instruction.type, integers, reals, strings)) return false;
            }
            return true;
        }

        // Call only from a successful Integrator.Execute postfix, before any
        // command dispatch. Native Execute itself calls Entry(m,0,0,len-1) then
        // Ready; see the retained pinned-build disassembly in the startup audit.
        internal static void ReplaceBeforeDispatch(Integrator interpreter, OpeningScript expected,
                                                    string replacementJson, string ownershipContext = null)
        {
            if (interpreter == null || interpreter.mainCore == null)
                throw new InvalidOperationException("No native main core exists for this replacement.");
            Core core = interpreter.mainCore;
            var original = core.mnemonics;
            if (!Matches(original, expected) || interpreter.processing == null
                || interpreter.processing.Pointer != original.Pointer)
                throw new InvalidOperationException("The native program differs from the verified source.");
            if (!core.IsReady || core.pc != 0 || core.programCounter == null
                || core.programCounter.clockCount != 0 || core.programCounter.addressRange == null
                || core.programCounter.addressRange.min != 0
                || core.programCounter.addressRange.max != original.Length - 1)
                throw new InvalidOperationException("Native replacement requires the untouched Execute entry state.");

            var replacementMatcher = new OpeningScript(replacementJson);
            var replacement = Compile(replacementJson);
            if (!Matches(replacement, replacementMatcher))
                throw new InvalidOperationException("Native replacement allocation did not preserve its source.");
            int originalCoreId = core.coreID;
            if (ownershipContext != null)
                Owned[core.Pointer] = new OwnedReplacement { Program = replacement,
                    Matcher = replacementMatcher, Context = ownershipContext };

            // All allocations/validation above finish before either active pointer
            // changes. The old array and its operand arrays remain unchanged for
            // subsequent scene entries and another save's different placement.
            core.Entry(replacement, 0, 0, replacement.Length - 1);
            interpreter.processing = replacement;
            core.Ready();
            if (core.coreID != originalCoreId || core.pc != 0 || !core.IsReady
                || core.programCounter.addressRange.max != replacement.Length - 1
                || core.mnemonics.Pointer != replacement.Pointer
                || interpreter.processing.Pointer != replacement.Pointer
                || !Matches(core, replacementMatcher))
                throw new InvalidOperationException("Native program replacement failed its entry-state checks.");
        }

        // Native ChangeScript calls Entry and leaves the core Awake; its caller
        // controls resumption. It bypasses Execute, and calling Ready here would
        // change the native lifecycle. Swap only the two active array pointers,
        // preserving the existing counter, range, core ID and status register.
        internal static void ReplaceAfterChangeScript(Integrator interpreter, OpeningScript expected,
                                                       string replacementJson, string ownershipContext)
        {
            if (interpreter == null || interpreter.mainCore == null)
                throw new InvalidOperationException("No native main core exists after ChangeScript.");
            Core core = interpreter.mainCore;
            var original = core.mnemonics;
            if (!Matches(original, expected) || interpreter.processing == null
                || interpreter.processing.Pointer != original.Pointer)
                throw new InvalidOperationException("Changed native story program differs from its verified source.");
            if (core.statusRegister == null || !core.statusRegister.IsAwake() || core.IsReady
                || core.pc != 0 || core.programCounter == null || core.programCounter.clockCount != 0
                || core.programCounter.addressRange == null || core.programCounter.addressRange.min != 0
                || core.programCounter.addressRange.max != original.Length - 1)
                throw new InvalidOperationException("Native story replacement requires untouched ChangeScript Entry state.");

            var replacementMatcher = new OpeningScript(replacementJson);
            var replacement = Compile(replacementJson);
            if (replacement.Length != original.Length || !Matches(replacement, replacementMatcher))
                throw new InvalidOperationException("Changed story replacement altered instruction layout or allocation.");
            int originalCoreId = core.coreID;
            var originalCounter = core.programCounter;
            var originalStatus = core.statusRegister;
            // Allocate the ownership record and dictionary slot before touching
            // either native execution pointer. All source arrays stay immutable.
            Owned[core.Pointer] = new OwnedReplacement { Program = replacement,
                Matcher = replacementMatcher, Context = ownershipContext };
            core.mnemonics = replacement;
            interpreter.processing = replacement;
            if (core.coreID != originalCoreId || core.pc != 0 || core.IsReady
                || core.programCounter.Pointer != originalCounter.Pointer
                || core.statusRegister.Pointer != originalStatus.Pointer || !core.statusRegister.IsAwake()
                || core.programCounter.clockCount != 0 || core.programCounter.addressRange.min != 0
                || core.programCounter.addressRange.max != replacement.Length - 1
                || core.mnemonics.Pointer != replacement.Pointer || interpreter.processing.Pointer != replacement.Pointer
                || !Matches(core, replacementMatcher))
                throw new InvalidOperationException("Native story replacement changed its Entry lifecycle.");
        }

        private static Il2CppReferenceArray<NativeFormat.Mnemonic> Compile(string json)
        {
            using (var document = JsonDocument.Parse(json))
            {
                var sequence = document.RootElement.GetProperty("Mnemonics");
                var result = new Il2CppReferenceArray<NativeFormat.Mnemonic>(sequence.GetArrayLength());
                for (int index = 0; index < sequence.GetArrayLength(); index++)
                {
                    var instruction = sequence[index];
                    var operands = instruction.GetProperty("operands");
                    var integers = operands.GetProperty("iValues");
                    var reals = operands.GetProperty("rValues");
                    var strings = operands.GetProperty("sValues");
                    var nativeOperands = new NativeFormat.Operands
                    {
                        iValues = new Il2CppStructArray<int>(integers.GetArrayLength()),
                        rValues = new Il2CppStructArray<double>(reals.GetArrayLength()),
                        sValues = new Il2CppStringArray(strings.GetArrayLength())
                    };
                    for (int slot = 0; slot < integers.GetArrayLength(); slot++) nativeOperands.iValues[slot] = integers[slot].GetInt32();
                    for (int slot = 0; slot < reals.GetArrayLength(); slot++) nativeOperands.rValues[slot] = reals[slot].GetDouble();
                    for (int slot = 0; slot < strings.GetArrayLength(); slot++) nativeOperands.sValues[slot] = strings[slot].GetString();
                    result[index] = new NativeFormat.Mnemonic
                    {
                        label = instruction.GetProperty("label").GetString(),
                        mnemonic = instruction.GetProperty("mnemonic").GetString(),
                        type = instruction.GetProperty("type").GetInt32(),
                        operands = nativeOperands
                    };
                }
                return result;
            }
        }
    }
}
