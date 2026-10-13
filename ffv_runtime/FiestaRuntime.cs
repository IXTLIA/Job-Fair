// Fiesta grants use the four pinned Wind instructions or the existing owned
// Remixed starter transaction. Native job releases outside them stay disabled.
using System;
using System.Collections.Generic;
using Last.Interpreter;
using Last.Management;

namespace ProjectBlues.Runtime
{
    internal static class FiestaRuntime
    {
        private static Integrator wind;
        private static string context;
        internal static void Reset() { wind = null; context = null; }
        internal static string Context(RunMetadata run) => "fiesta-wind|" + run.RunId + "|" + run.PlacementHash;

        internal static void RegisterWind(Integrator interpreter, RunMetadata run, int script)
        {
            if (run.Mode != CampaignContract.MixedFiesta || script != 1) return;
            string key = Context(run);
            if (!NativeProgram.IsOwnedReplacement(interpreter, key))
                throw new InvalidOperationException("Fiesta Wind program was not replaced by its verified owner.");
            wind = interpreter;
            context = key;
        }

        internal static bool BeforeAddReleaseJob(RunMetadata run, int job)
        {
            if (run == null || run.Mode != CampaignContract.MixedFiesta) return true;
            if (job == 1) return true;
            if (job < 2 || job > 22) throw new InvalidOperationException("Unknown Fiesta job release.");
            if (SetupView.FatalError != null || RunSession.Loading != null
                || !Object.ReferenceEquals(RunSession.Active, run))
                throw new InvalidOperationException("Fiesta job grant has no active run.");
            if (wind == null || wind.mainCore == null || context != Context(run)) return false;
            int index = wind.mainCore.pc - 86;
            if (index < 0 || index >= CampaignCatalog.FiestaWindLocations.Count) return false;
            if (!NativeProgram.IsOwnedReplacement(wind, context)) return false;
            var expected = new HashSet<int> { 1 };
            for (int i = 0; i < index; i++)
                expected.Add(CampaignCatalog.Find(run, CampaignCatalog.FiestaWindLocations[i]).NumericRewardId);
            int selected = CampaignCatalog.Find(run, CampaignCatalog.FiestaWindLocations[index]).NumericRewardId;
            if (job != selected || !Released().SetEquals(expected))
                throw new InvalidOperationException("Fiesta Wind jobs disagree with the saved selection or grant order.");
            return true;
        }

        internal static void ValidateLoaded(RunMetadata run)
        {
            if (!CampaignContract.IsFiesta(run)) return;
            var allowed = new HashSet<int>(run.StartingJobs) { 1 };
            if (run.Mode == CampaignContract.MixedFiesta)
                foreach (string id in CampaignCatalog.FiestaWindLocations)
                    allowed.Add(CampaignCatalog.Find(run, id).NumericRewardId);
            var released = Released();
            if (!released.Contains(1) || !released.IsSubsetOf(allowed)
                || (run.Mode == CampaignContract.RemixedFiesta && !released.SetEquals(allowed)))
                throw new InvalidOperationException("Native jobs exceed or disagree with this Fiesta run's four jobs plus Freelancer.");
        }

        private static HashSet<int> Released()
        {
            var manager = UserDataManager.Instance();
            if (manager == null) throw new InvalidOperationException("Native Fiesta job list is unavailable.");
            var jobs = manager.GetReleasedJobsClone();
            if (jobs == null) throw new InvalidOperationException("Native Fiesta job list is unavailable.");
            var result = new HashSet<int>();
            for (int i = 0; i < jobs.Count; i++)
                if (jobs[i] == null || jobs[i].Id < 1 || jobs[i].Id > 22 || !result.Add(jobs[i].Id))
                    throw new InvalidOperationException("Native Fiesta job list is invalid.");
            return result;
        }
    }
}
