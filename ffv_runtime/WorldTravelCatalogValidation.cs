// Catalog-backed validation stays separate from the portable save-state model.
using System;
using System.Collections.Generic;

namespace ProjectBlues.Runtime
{
    public sealed partial class WorldTravelState
    {
        public static void AddExpected(RunMetadata run, HashSet<string> expected)
        {
            CampaignContract.ValidateWorldTravel(run);
            if (expected == null) throw new ArgumentNullException(nameof(expected));
            var occupied = new HashSet<string>(StringComparer.Ordinal);
            foreach (var reward in run.Placements)
                if (reward.RewardKind == "job")
                    occupied.Add(reward.LocationId.Substring("remixed_job/".Length));
            int keys = 0;
            foreach (var reward in run.Placements)
            {
                if (reward.RewardKind != "key") continue;
                const string prefix = "remixed_key/";
                if (!reward.LocationId.StartsWith(prefix, StringComparison.Ordinal))
                    throw new ArgumentException("World-key location lacks its bonus namespace.");
                string ordinary = reward.LocationId.Substring(prefix.Length);
                if (!RemixedJobCatalog.Shared.TryGetCandidate(ordinary, out var candidate)
                    || !occupied.Add(ordinary) || !expected.Add(reward.LocationId))
                    throw new ArgumentException("Unknown, overlapping or duplicate world-key candidate.");
                int world = candidate.GetProperty("world").GetInt32();
                if ((reward.RewardId == World2Key && world != 1)
                    || (reward.RewardId == World3Key && world != 1 && world != 2))
                    throw new ArgumentException("World key is outside its permitted source worlds.");
                var baseReward = run.FindPlacement(ordinary);
                if (!CampaignContract.IsOrdinaryReward(baseReward))
                    throw new ArgumentException("World key must preserve its ordinary container reward.");
                keys++;
            }
            if (keys != 2) throw new ArgumentException("Two world-key bonuses are required.");
        }
    }
}
