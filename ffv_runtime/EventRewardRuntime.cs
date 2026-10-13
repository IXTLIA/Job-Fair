// Per-dispatch reward operands; native scripts retain all claim/cost behavior.
using System;
using Last.Interpreter;
using Last.Management;

namespace ProjectBlues.Runtime
{
    internal static class EventRewardRuntime
    {
        private static int KelbStage()
        {
            var data = DataStorage.Instance;
            if (data == null) throw new InvalidOperationException("Event reward storage is unavailable.");
            if (data.Get(DataStorage.Category.kScenarioFlag2, 3) == 0) return 0;
            if (data.Get(DataStorage.Category.kScenarioFlag2, 4) == 0) return 1;
            if (data.Get(DataStorage.Category.kScenarioFlag2, 5) == 0) return 2;
            throw new InvalidOperationException("The exhausted Kelb reward event must not execute again.");
        }

        internal static string Rewrite(string json, RunMetadata run)
        {
            if (!CampaignContract.UsesExpandedRewards(run) || json == null) return json;
            var catalog = EventRewardCatalog.Shared;
            for (int i = 0; i < catalog.Scripts.Length; i++)
                if (catalog.Scripts[i].Matches(json))
                    return catalog.RewriteScript(i, run, catalog.IsKelbScript(i) ? KelbStage() : 0);
            return json;
        }

        internal static bool TryPrepare(Integrator interpreter, RunMetadata run, bool changedScript = false, string scriptName = null)
        {
            if (!CampaignContract.UsesExpandedRewards(run)) return false;
            var catalog = EventRewardCatalog.Shared;
            bool known = catalog.IsKnownScriptName(scriptName);
            if (interpreter == null || interpreter.mainCore == null)
            {
                if (known) throw new InvalidOperationException("Known reward event has no native core.");
                return false;
            }
            if (changedScript && known && interpreter.scriptName != scriptName)
                throw new InvalidOperationException("Changed reward event name differs from its request.");
            // Only query the shared native owner registry for event scripts. A
            // travel script may own the same core using its own context.
            string context = "events|" + run.RunId + "|" + run.PlacementHash;
            if (known && NativeProgram.IsOwnedReplacement(interpreter, context)) return true;
            for (int i = 0; i < catalog.Scripts.Length; i++)
                if (NativeProgram.Matches(interpreter.mainCore, catalog.Scripts[i]))
                {
                    string rewritten = catalog.RewriteScript(i, run, catalog.IsKelbScript(i) ? KelbStage() : 0);
                    if (changedScript) NativeProgram.ReplaceAfterChangeScript(interpreter, catalog.Scripts[i], rewritten, context);
                    else NativeProgram.ReplaceBeforeDispatch(interpreter, catalog.Scripts[i], rewritten, context);
                    return true;
                }
            if (known) throw new InvalidOperationException("Known reward event differs from its pinned native source.");
            return false;
        }
    }
}
