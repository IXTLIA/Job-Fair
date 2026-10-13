// Source-pinned job-bonus candidates. This validates allocation, not world travel.
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProjectBlues.Runtime
{
    public sealed class RemixedJobCatalog
    {
        private static RemixedJobCatalog shared;
        public static RemixedJobCatalog Shared => shared ?? (shared = new RemixedJobCatalog());
        private readonly Dictionary<string, JsonElement> candidates = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        private readonly Dictionary<string, JsonElement> ordinary = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        public const string BonusPrefix = "remixed_job/";

        public RemixedJobCatalog()
        {
            using (var hash = SHA256.Create())
                if (Convert.ToHexString(hash.ComputeHash(Encoding.UTF8.GetBytes(NativeRemixedJobData.CatalogJson))).ToLowerInvariant()
                    != NativeRemixedJobData.CatalogJsonSha256)
                    throw new ArgumentException("Embedded Remixed job catalog hash mismatch.");
            using (var source = JsonDocument.Parse(NativeRemixedJobData.CatalogJson))
            {
                var root = source.RootElement;
                if (root.GetProperty("catalog_id").GetString() != CampaignContract.RemixedJobSliceCatalog)
                    throw new ArgumentException("Embedded Remixed job catalog identity mismatch.");
                var quotas = root.GetProperty("quotas");
                if (quotas.GetProperty("1").GetInt32() != 6 || quotas.GetProperty("2").GetInt32() != 6
                    || quotas.GetProperty("3").GetInt32() != 5)
                    throw new ArgumentException("Remixed job quota contract changed.");
                int[] counts = new int[4];
                foreach (var candidate in root.GetProperty("bonus_candidates").EnumerateArray())
                {
                    string id = candidate.GetProperty("id").GetString();
                    string baseId = candidate.GetProperty("ordinary_id").GetString();
                    int world = candidate.GetProperty("world").GetInt32();
                    if (world < 1 || world > 3 || id != BonusPrefix + baseId
                        || candidate.GetProperty("first_world").GetInt32() != world
                        || !candidate.GetProperty("job_bonus_eligible").GetBoolean()
                        || candidate.GetProperty("gate_eligible").GetBoolean()
                        || candidate.GetProperty("eligibility_scope").GetString() != "staged_native_story_only")
                        throw new ArgumentException("Invalid Remixed job candidate eligibility.");
                    if (!candidates.TryAdd(id, candidate.Clone()) || !ordinary.TryAdd(baseId, candidate.Clone()))
                        throw new ArgumentException("Duplicate Remixed job candidate.");
                    counts[world]++;
                }
                if (candidates.Count != 24 || counts[1] != 8 || counts[2] != 8 || counts[3] != 8)
                    throw new ArgumentException("Incomplete Remixed job candidate set.");
            }
        }

        public bool TryGetCandidate(string ordinaryId, out JsonElement candidate)
            => ordinary.TryGetValue(ordinaryId, out candidate);

        public static void AddExpected(RunMetadata run, HashSet<string> expected)
        {
            if (CampaignContract.UsesWorldTravel(run)) CampaignContract.ValidateWorldTravel(run);
            else CampaignContract.ValidateJobSliceDraft(run);
            int[] counts = new int[4];
            foreach (var reward in run.Placements)
            {
                if (reward.RewardKind != "job") continue;
                if (!Shared.candidates.TryGetValue(reward.LocationId, out var candidate)
                    || reward.Quantity != 1 || !expected.Add(reward.LocationId))
                    throw new ArgumentException("Invalid or duplicate Remixed job bonus.");
                var ordinaryReward = run.FindPlacement(candidate.GetProperty("ordinary_id").GetString());
                if (!CampaignContract.IsOrdinaryReward(ordinaryReward))
                    throw new ArgumentException("A job bonus must retain its ordinary container reward.");
                counts[candidate.GetProperty("world").GetInt32()]++;
            }
            if (CampaignContract.IsFiesta(run)
                ? counts[1] != 0 || counts[2] != 0 || counts[3] != 0
                : counts[1] != 6 || counts[2] != 6 || counts[3] != 5)
                throw new ArgumentException("Hidden jobs must be distributed six / six / five across the worlds.");
        }
    }
}
