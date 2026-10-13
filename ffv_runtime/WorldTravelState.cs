// Versioned save state for the isolated reversible-world component.
// Destinations are catalog/code pins; saves never supply map coordinates.
using System;
using System.Collections.Generic;

namespace ProjectBlues.Runtime
{
    public sealed partial class WorldTravelState
    {
        public const int CurrentVersion = 1;
        public const string World2Key = "world_2_key";
        public const string World3Key = "world_3_key";
        public int Version { get; }
        public int ActiveWorld { get; }

        public WorldTravelState(int version, int activeWorld)
        {
            if (version != CurrentVersion || activeWorld < 1 || activeWorld > 3)
                throw new ArgumentException("Unsupported world-travel state.");
            Version = version; ActiveWorld = activeWorld;
        }

        public static WorldTravelState Initial => new WorldTravelState(CurrentVersion, 1);

        public IReadOnlyList<int> AvailableWorlds(IReadOnlyList<string> keys)
        {
            if (keys == null) throw new ArgumentNullException(nameof(keys));
            bool second = false, third = false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string key in keys)
            {
                if (!seen.Add(key)) throw new ArgumentException("Duplicate world key.");
                if (key == World2Key) second = true;
                else if (key == World3Key) third = true;
                else throw new ArgumentException("Unknown world key.");
            }
            var worlds = new List<int> { 1 };
            if (second) worlds.Add(2);
            // Each key independently opens its destination and is never spent.
            if (third) worlds.Add(3);
            return worlds.AsReadOnly();
        }

        public bool CanTravel(IReadOnlyList<string> keys, int targetWorld)
        {
            var worlds = AvailableWorlds(keys);
            if (targetWorld == ActiveWorld) return false;
            foreach (int world in worlds) if (world == targetWorld) return true;
            return false;
        }

        public void Validate(IReadOnlyList<string> keys)
        {
            foreach (int world in AvailableWorlds(keys)) if (world == ActiveWorld) return;
            throw new ArgumentException("Active world is not unlocked by the saved keys.");
        }

        public static bool CanEnter(RunMetadata run, int targetWorld)
            => CampaignContract.UsesWorldTravel(run)
                && ((CampaignContract.IsPlayableRemixedCatalog(run.CatalogId)
                     && targetWorld == run.WorldState.ActiveWorld)
                    || run.WorldState.CanTravel(run.AcquiredKeyIds, targetWorld));
    }
}
