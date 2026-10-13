// Original immutable run identity. No Unity or filesystem dependencies.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ProjectBlues.Runtime
{
    public sealed class RunMetadata
    {
        public int SchemaVersion { get; }
        public string Seed { get; }
        public string Generator { get; }
        public string RunId { get; }
        public string BartzName { get; }
        public IReadOnlyList<int> StartingJobs { get; }
        public string Mode { get; }
        public RunSettings Settings { get; }
        public string CatalogId { get; }
        public string PlacementHash { get; }
        public IReadOnlyList<RewardPlacement> Placements { get; }
        public IReadOnlyList<string> ClaimedLocationIds { get; }
        public IReadOnlyList<string> AcquiredKeyIds { get; }
        public WorldTravelState WorldState { get; }

        public static RunMetadata CreateCampaign(ulong seed, string bartzName, string mode,
            string catalogId, RewardPlacement[] placements, int[] startingJobs)
        {
            return new RunMetadata(SeedLogic.CanonicalSeed(seed), CampaignContract.Generator,
                Guid.NewGuid().ToString("N"), bartzName, mode, RunSettings.ForCatalog(mode, catalogId),
                catalogId, startingJobs, placements, null, Array.Empty<string>(), Array.Empty<string>());
        }

        public static RunMetadata Create(ulong seed, string bartzName)
        {
            return new RunMetadata(1, SeedLogic.CanonicalSeed(seed), SeedLogic.AlgorithmVersion,
                Guid.NewGuid().ToString("N"), bartzName,
                SeedLogic.SelectStartingJobs(seed, NativeJobIds()));
        }

        public static RunMetadata CreateWorldTravel(ulong seed, string bartzName,
            RewardPlacement[] placements, int[] startingJobs)
        {
            return new RunMetadata(3, SeedLogic.CanonicalSeed(seed), CampaignContract.Generator,
                Guid.NewGuid().ToString("N"), bartzName, CampaignContract.RemixedCrystals,
                RunSettings.ForCatalog(CampaignContract.RemixedCrystals, CampaignContract.WorldTravelCatalog),
                CampaignContract.WorldTravelCatalog, startingJobs, placements, null,
                Array.Empty<string>(), Array.Empty<string>(), WorldTravelState.Initial);
        }

        public static RunMetadata CreateRemixedPlaytest(ulong seed, string bartzName,
            RewardPlacement[] placements, int[] startingJobs)
        {
            return new RunMetadata(3, SeedLogic.CanonicalSeed(seed), CampaignContract.Generator,
                Guid.NewGuid().ToString("N"), bartzName, CampaignContract.RemixedCrystals,
                RunSettings.ForCatalog(CampaignContract.RemixedCrystals, CampaignContract.RemixedPlaytestCatalog),
                CampaignContract.RemixedPlaytestCatalog, startingJobs, placements, null,
                Array.Empty<string>(), Array.Empty<string>(), WorldTravelState.Initial);
        }

        public RunMetadata WithBartzName(string name)
        {
            var jobs = new int[StartingJobs.Count];
            for (int index = 0; index < jobs.Length; index++) jobs[index] = StartingJobs[index];
            if (SchemaVersion == 2 || SchemaVersion == 3)
                return CopyCampaign(name, Copy(ClaimedLocationIds), Copy(AcquiredKeyIds));
            return new RunMetadata(SchemaVersion, Seed, Generator, RunId, name, jobs);
        }

        // Call only after the native grant succeeds. Repeated dispatch cannot create a second claim.
        public RunMetadata WithClaim(string locationId)
        {
            if (SchemaVersion != 2 && SchemaVersion != 3) throw new ArgumentException("Legacy runs do not have randomized claims.");
            if (IsClaimed(locationId)) return this;
            RewardPlacement placement = FindPlacement(locationId);
            if (placement == null) throw new ArgumentException("Cannot claim an unknown randomized location.");
            if (placement.RewardKind == "shop" || placement.RewardKind == "boss" || placement.RewardKind == "loot")
                throw new ArgumentException("Stock, boss and loot assignments are not reward claims.");
            var claims = new List<string>(ClaimedLocationIds) { locationId };
            var keys = new List<string>(AcquiredKeyIds);
            if (placement.RewardKind == "key") keys.Add(placement.RewardId);
            return CopyCampaign(BartzName, claims.ToArray(), keys.ToArray());
        }

        public bool IsClaimed(string locationId)
        {
            foreach (string id in ClaimedLocationIds) if (id == locationId) return true;
            return false;
        }

        public bool HasKey(string keyId)
        {
            foreach (string id in AcquiredKeyIds) if (id == keyId) return true;
            return false;
        }

        public RewardPlacement FindPlacement(string locationId)
        {
            foreach (RewardPlacement placement in Placements)
                if (placement.LocationId == locationId) return placement;
            return null;
        }

        private RunMetadata CopyCampaign(string name, string[] claims, string[] keys)
        {
            return new RunMetadata(SchemaVersion, Seed, Generator, RunId, name, Mode, Settings, CatalogId,
                Copy(StartingJobs), Copy(Placements), PlacementHash, claims, keys, WorldState);
        }

        // Publish this immutable snapshot only after the native adapter proves
        // arrival. Pending transitions must block saving and other mutations.
        public RunMetadata WithActiveWorld(int world)
        {
            if (!CampaignContract.UsesWorldTravel(this))
                throw new ArgumentException("This run has no reversible-world state.");
            if (world == WorldState.ActiveWorld) return this;
            if (!WorldState.CanTravel(AcquiredKeyIds, world)) throw new ArgumentException("Destination world is locked.");
            return new RunMetadata(3, Seed, Generator, RunId, BartzName, Mode, Settings, CatalogId,
                Copy(StartingJobs), Copy(Placements), PlacementHash, Copy(ClaimedLocationIds), Copy(AcquiredKeyIds),
                new WorldTravelState(WorldTravelState.CurrentVersion, world));
        }

        internal static T[] Copy<T>(IReadOnlyList<T> values)
        {
            var result = new T[values.Count];
            for (int index = 0; index < result.Length; index++) result[index] = values[index];
            return result;
        }

        internal RunMetadata(int schemaVersion, string seed, string generator, string runId,
                             string bartzName, int[] startingJobs)
        {
            if (schemaVersion != 1) throw new ArgumentException("Unsupported Job Fair metadata schema.");
            ulong parsed;
            if (!SeedLogic.TryParseSeed(seed, out parsed) || SeedLogic.CanonicalSeed(parsed) != seed)
                throw new ArgumentException("Metadata seed must be canonical uint64 decimal.");
            if (generator != SeedLogic.AlgorithmVersion)
                throw new ArgumentException("Unsupported Job Fair seed generator.");
            if (runId == null || runId.Length != 32)
                throw new ArgumentException("Run ID must be 32 lowercase hexadecimal characters.");
            foreach (char value in runId)
                if (!(value >= '0' && value <= '9') && !(value >= 'a' && value <= 'f'))
                    throw new ArgumentException("Run ID must be 32 lowercase hexadecimal characters.");
            Guid parsedGuid;
            if (!Guid.TryParseExact(runId, "N", out parsedGuid))
                throw new ArgumentException("Invalid run ID.");
            ValidateName(bartzName);
            int[] expected = SeedLogic.SelectStartingJobs(parsed, NativeJobIds());
            if (startingJobs == null || startingJobs.Length != expected.Length)
                throw new ArgumentException("Exactly four starting jobs are required.");
            for (int index = 0; index < expected.Length; index++)
                if (startingJobs[index] != expected[index])
                    throw new ArgumentException("Starting jobs disagree with the recorded seed/generator.");
            SchemaVersion = schemaVersion;
            Seed = seed;
            Generator = generator;
            RunId = runId;
            BartzName = bartzName;
            StartingJobs = Array.AsReadOnly((int[])startingJobs.Clone());
            Mode = "Legacy starter-only";
            CatalogId = "";
            PlacementHash = "";
            Placements = Array.AsReadOnly(Array.Empty<RewardPlacement>());
            ClaimedLocationIds = Array.AsReadOnly(Array.Empty<string>());
            AcquiredKeyIds = Array.AsReadOnly(Array.Empty<string>());
        }

        // Schema 2 validates the stored placement table; it never regenerates rewards from a seed.
        internal RunMetadata(string seed, string generator, string runId, string bartzName,
            string mode, RunSettings settings, string catalogId, int[] startingJobs,
            RewardPlacement[] placements, string placementHash, string[] claims, string[] keys)
            : this(2, seed, generator, runId, bartzName, mode, settings, catalogId, startingJobs,
                placements, placementHash, claims, keys, null) { }

        internal RunMetadata(int schemaVersion, string seed, string generator, string runId, string bartzName,
            string mode, RunSettings settings, string catalogId, int[] startingJobs,
            RewardPlacement[] placements, string placementHash, string[] claims, string[] keys, WorldTravelState worldState)
        {
            if ((schemaVersion != 2 && schemaVersion != 3)
                || (schemaVersion == 3) != CampaignContract.IsWorldTravelCatalog(catalogId)
                || (schemaVersion == 3 ? worldState == null : worldState != null))
                throw new ArgumentException("Campaign schema, catalog and world state disagree.");
            ulong parsed;
            if (!SeedLogic.TryParseSeed(seed, out parsed) || SeedLogic.CanonicalSeed(parsed) != seed)
                throw new ArgumentException("Metadata seed must be canonical uint64 decimal.");
            CampaignContract.ValidateSupported(generator, catalogId, mode);
            Guid parsedGuid;
            if (runId == null || runId.Length != 32 || !Guid.TryParseExact(runId, "N", out parsedGuid)
                || parsedGuid.ToString("N") != runId) throw new ArgumentException("Invalid run ID.");
            ValidateName(bartzName);
            RunSettings normalized = RunSettings.ForCatalog(mode, catalogId);
            if (settings == null || !settings.SameAs(normalized))
                throw new ArgumentException("Run settings disagree with the normalized mode settings.");
            if (startingJobs == null || startingJobs.Length != (CampaignContract.IsMixedMode(mode) ? 0 : 4))
                throw new ArgumentException("Starting job count disagrees with the campaign mode.");
            var jobs = new HashSet<int>();
            foreach (int id in startingJobs)
                if (id < 2 || id > 22 || !jobs.Add(id)) throw new ArgumentException("Invalid or duplicate starting job.");
            if (placements == null || placements.Length == 0 || placements.Length > 4096)
                throw new ArgumentException("Missing or oversized placement table.");
            var ordered = (RewardPlacement[])placements.Clone();
            Array.Sort(ordered, (left, right) => StringComparer.Ordinal.Compare(left?.LocationId, right?.LocationId));
            var locations = new Dictionary<string, RewardPlacement>(StringComparer.Ordinal);
            var possibleKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (RewardPlacement placement in ordered)
            {
                if (placement == null || !locations.TryAdd(placement.LocationId, placement))
                    throw new ArgumentException("Null or duplicate placement location.");
                if (placement.RewardKind == "job" && !jobs.Add(placement.NumericRewardId))
                    throw new ArgumentException("Duplicate job placement or starting-job overlap.");
                if (placement.RewardKind == "key" && (CampaignContract.IsMixedMode(mode) || !possibleKeys.Add(placement.RewardId)))
                    throw new ArgumentException("Unexpected or duplicate progression key.");
                if (placement.RewardKind == "boss" && !CampaignContract.UsesBossShuffle(catalogId))
                    throw new ArgumentException("This catalog does not permit boss assignments.");
                if ((placement.RewardKind == "loot" || placement.RewardKind == "magic")
                    && !CampaignContract.UsesExpandedRewards(catalogId))
                    throw new ArgumentException("This catalog does not permit expanded reward assignments.");
            }
            if (jobs.Count != (CampaignContract.IsFiesta(catalogId) ? 4 : 21))
                throw new ArgumentException("The campaign job count disagrees with its saved job policy.");
            string computedHash = CampaignContract.ComputePlacementHash(seed, generator, mode, settings,
                catalogId, startingJobs, ordered);
            if (placementHash != null && placementHash != computedHash)
                throw new ArgumentException("Stored placement hash does not match the immutable placement table.");
            string[] sortedClaims = ValidateState(claims, "claim");
            string[] sortedKeys = ValidateState(keys, "key");
            var expectedKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in sortedClaims)
            {
                RewardPlacement placement;
                if (!locations.TryGetValue(id, out placement)) throw new ArgumentException("Claim references an unknown location.");
                if (placement.RewardKind == "shop" || placement.RewardKind == "boss" || placement.RewardKind == "loot")
                    throw new ArgumentException("Stock, boss and loot assignments cannot be completed reward claims.");
                if (placement.RewardKind == "key") expectedKeys.Add(placement.RewardId);
            }
            if (!expectedKeys.SetEquals(sortedKeys)) throw new ArgumentException("Key possession disagrees with completed key claims.");
            SchemaVersion = schemaVersion;
            Seed = seed; Generator = generator; RunId = runId; BartzName = bartzName;
            Mode = mode; Settings = settings; CatalogId = catalogId; PlacementHash = computedHash;
            StartingJobs = Array.AsReadOnly((int[])startingJobs.Clone());
            Placements = Array.AsReadOnly(ordered);
            ClaimedLocationIds = Array.AsReadOnly(sortedClaims);
            AcquiredKeyIds = Array.AsReadOnly(sortedKeys);
            WorldState = worldState;
            if (catalogId == CampaignContract.RemixedJobSliceCatalog)
                CampaignContract.ValidateJobSliceDraft(this);
            if (schemaVersion == 3) CampaignContract.ValidateWorldTravel(this);
        }

        private static string[] ValidateState(string[] input, string label)
        {
            if (input == null || input.Length > 4096) throw new ArgumentException("Missing or oversized " + label + " state.");
            var unique = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string value in input)
            {
                CampaignContract.ValidateToken(value, label);
                if (!unique.Add(value)) throw new ArgumentException("Duplicate " + label + " state.");
            }
            string[] result = new string[unique.Count];
            unique.CopyTo(result);
            return result;
        }

        private static int[] NativeJobIds()
        {
            int[] ids = new int[21];
            for (int index = 0; index < ids.Length; index++) ids[index] = index + 2;
            return ids;
        }

        internal static void ValidateName(string name)
        {
            if (String.IsNullOrWhiteSpace(name)) throw new ArgumentException("Bartz name must not be empty.");
            int scalars = 0;
            for (int index = 0; index < name.Length; index++)
            {
                char character = name[index];
                if (Char.IsHighSurrogate(character))
                {
                    if (index + 1 >= name.Length || !Char.IsLowSurrogate(name[index + 1]))
                        throw new ArgumentException("Bartz name contains invalid Unicode.");
                    index++;
                }
                else if (Char.IsLowSurrogate(character))
                    throw new ArgumentException("Bartz name contains invalid Unicode.");
                if (++scalars > 24) throw new ArgumentException("Bartz name exceeds 24 Unicode characters.");
            }
        }
    }

    public sealed class RewardPlacement
    {
        public string LocationId { get; }
        public string RewardKind { get; }
        public string RewardId { get; }
        public int Quantity { get; }
        public int NumericRewardId { get; }

        public RewardPlacement(string locationId, string rewardKind, string rewardId, int quantity)
        {
            CampaignContract.ValidateToken(locationId, "location");
            int numeric = 0;
            if (rewardKind == "job" || rewardKind == "item" || rewardKind == "shop" || rewardKind == "magic" || rewardKind == "loot")
            {
                if (rewardId == null || !Int32.TryParse(rewardId, NumberStyles.None, CultureInfo.InvariantCulture, out numeric)
                    || numeric <= 0 || numeric.ToString(CultureInfo.InvariantCulture) != rewardId)
                    throw new ArgumentException("Job/item reward ID must be canonical positive decimal.");
                if (rewardKind == "job" && (numeric < 2 || numeric > 22 || quantity != 1))
                    throw new ArgumentException("Invalid job reward.");
                if (rewardKind == "item" && (quantity < 1 || quantity > 99)) throw new ArgumentException("Invalid item quantity.");
                if (rewardKind == "shop" && quantity != 1) throw new ArgumentException("Shop slots require quantity one.");
                if ((rewardKind == "magic" || rewardKind == "loot") && quantity != 1)
                    throw new ArgumentException("Magic unlocks and loot slots require quantity one.");
            }
            else if (rewardKind == "key")
            {
                CampaignContract.ValidateToken(rewardId, "key");
                if (quantity != 1) throw new ArgumentException("Key rewards must have quantity one.");
            }
            else if (rewardKind == "boss")
            {
                CampaignContract.ValidateToken(rewardId, "boss");
                if (quantity != 1 || !locationId.StartsWith("native_boss/", StringComparison.Ordinal))
                    throw new ArgumentException("Boss assignments require a native boss location and quantity one.");
            }
            else if (rewardKind == "gil")
            {
                if (rewardId != "" || quantity < 1 || quantity > 9999999) throw new ArgumentException("Invalid gil reward.");
            }
            else if (rewardKind == "empty")
            {
                if (rewardId != "" || quantity != 0) throw new ArgumentException("Invalid empty reward.");
            }
            else throw new ArgumentException("Unsupported randomized reward kind.");
            LocationId = locationId; RewardKind = rewardKind; RewardId = rewardId; Quantity = quantity; NumericRewardId = numeric;
        }
    }

    public sealed class RunSettings
    {
        public string JobRewards { get; }
        public string KeyItems { get; }
        public string Treasure { get; }
        public string TreasureScope { get; }
        public string WorldAccess { get; }
        public string ShopScope { get; }
        public string ShopPrices { get; }
        public string BossScope { get; }
        public string BossScaling { get; }
        public string LootScope { get; }
        public string BossExperience { get; }

        internal RunSettings(string jobRewards, string keyItems, string treasure, string treasureScope, string worldAccess,
            string shopScope = null, string shopPrices = null, string bossScope = null, string bossScaling = null,
            string lootScope = null, string bossExperience = null)
        {
            JobRewards = jobRewards; KeyItems = keyItems; Treasure = treasure;
            TreasureScope = treasureScope; WorldAccess = worldAccess;
            ShopScope = shopScope; ShopPrices = shopPrices;
            BossScope = bossScope; BossScaling = bossScaling;
            LootScope = lootScope; BossExperience = bossExperience;
        }

        public static RunSettings ForMode(string mode)
        {
            if (mode == CampaignContract.MixedCrystals)
                return new RunSettings("native-crystals", "vanilla", "randomized", "wind-shrine-v1", "story");
            if (mode == CampaignContract.RemixedCrystals)
                return new RunSettings("container-jobs", "randomized", "randomized", "contract-fixture-v1", "key-gated");
            throw new ArgumentException("Unsupported Job Fair mode.");
        }

        public static RunSettings ForCatalog(string mode, string catalogId)
        {
            if (CampaignContract.UsesExpandedRewards(catalogId))
            {
                if (mode != CampaignContract.ModeForExpandedCatalog(catalogId))
                    throw new ArgumentException("Expanded reward catalog and campaign mode disagree.");
                bool mixed = CampaignContract.IsMixedMode(mode), fiesta = CampaignContract.IsFiesta(catalogId);
                return new RunSettings(fiesta ? (mixed ? "fiesta-wind-v1" : "fiesta-start-v1")
                        : (mixed ? "native-crystals" : "container-job-bonuses-v1"),
                    mixed ? "vanilla" : "world-key-bonuses-v1", "randomized", "world-event-pools-v1",
                    mixed ? "story" : "reversible-worlds-v1", "world-type-pools-v1", "native-item-prices",
                    mixed ? "mixed-tiers-v1" : "remixed-all-v1", "destination-budget-v1",
                    "native-occupied-slots-v1", "destination-hp-v1");
            }
            if (CampaignContract.UsesBossShuffle(catalogId))
            {
                bool mixed = catalogId == CampaignContract.MixedBossCatalog;
                if (mode != (mixed ? CampaignContract.MixedCrystals : CampaignContract.RemixedCrystals))
                    throw new ArgumentException("Boss catalog and campaign mode disagree.");
                var baseSettings = ForCatalog(mode, mixed ? CampaignContract.FixedKarnakMixedCatalog : CampaignContract.FixedKarnakRemixedCatalog);
                return new RunSettings(baseSettings.JobRewards, baseSettings.KeyItems, baseSettings.Treasure,
                    baseSettings.TreasureScope, baseSettings.WorldAccess, baseSettings.ShopScope, baseSettings.ShopPrices,
                    mixed ? "mixed-tiers-v1" : "remixed-all-v1", "destination-budget-v1");
            }
            if (mode == CampaignContract.RemixedCrystals && CampaignContract.IsWorldTravelCatalog(catalogId))
                return new RunSettings("container-job-bonuses-v1", "world-key-bonuses-v1", "randomized",
                    CampaignContract.UsesFixedKarnakRewards(catalogId) ? CampaignContract.FixedKarnakTreasureScope : "world-pools-v2",
                    "reversible-worlds-v1", "world-type-pools-v1", "native-item-prices");
            if (mode == CampaignContract.RemixedCrystals && catalogId == CampaignContract.RemixedJobSliceCatalog)
                return new RunSettings("container-job-bonuses-v1", "vanilla", "randomized", "world-pools-v2", "story",
                    "world-type-pools-v1", "native-item-prices");
            if (mode == CampaignContract.MixedCrystals && CampaignContract.IsMixedShopCatalog(catalogId))
                return new RunSettings("native-crystals", "vanilla", "randomized",
                    CampaignContract.UsesFixedKarnakRewards(catalogId) ? CampaignContract.FixedKarnakTreasureScope : "world-pools-v2", "story",
                    "world-type-pools-v1", "native-item-prices");
            if (mode == CampaignContract.MixedCrystals && catalogId == CampaignContract.WorldCatalog)
                return new RunSettings("native-crystals", "vanilla", "randomized", "world-pools-v2", "story");
            return ForMode(mode);
        }

        internal bool SameAs(RunSettings other)
        {
            return JobRewards == other.JobRewards && KeyItems == other.KeyItems && Treasure == other.Treasure
                && TreasureScope == other.TreasureScope && WorldAccess == other.WorldAccess
                && ShopScope == other.ShopScope && ShopPrices == other.ShopPrices
                && BossScope == other.BossScope && BossScaling == other.BossScaling
                && LootScope == other.LootScope && BossExperience == other.BossExperience;
        }
    }

    public static class CampaignContract
    {
        public const string MixedCrystals = "Mixed Crystals";
        public const string RemixedCrystals = "Remixed Crystals";
        public const string MixedFiesta = "Mixed-Fiesta";
        public const string RemixedFiesta = "Remixed-Fiesta";
        public const string Generator = "job-fair-native-solver-v1";
        public const string MixedCatalog = "mixed-native-jobs-wind-treasure-v1";
        public const string WorldCatalog = "mixed-native-jobs-world-treasure-v2";
        public const string ShopCatalog = "mixed-world-treasure-shops-v3";
        public const string RemixedJobSliceCatalog = "remixed-job-crystals-slice-v1";
        public const string WorldTravelCatalog = "remixed-world-travel-slice-v1";
        public const string RemixedPlaytestCatalog = "remixed-crystals-playtest-v1";
        public const string FixedKarnakMixedCatalog = "mixed-world-treasure-shops-fixed-karnak-v1";
        public const string FixedKarnakRemixedCatalog = "remixed-crystals-fixed-karnak-v1";
        public const string MixedBossCatalog = "mixed-crystals-bosses-v1";
        public const string RemixedBossCatalog = "remixed-crystals-bosses-v1";
        public const string MixedRewardsCatalog = "mixed-crystals-rewards-v1";
        public const string RemixedRewardsCatalog = "remixed-crystals-rewards-v1";
        public const string MixedFiestaCatalog = "mixed-fiesta-rewards-v1";
        public const string RemixedFiestaCatalog = "remixed-fiesta-rewards-v1";
        public const string FixedKarnakTreasureScope = "world-pools-karnak-fixed-v1";
        // Offline contract tests only. Native integration must call ValidatePlayable before adoption.
        public const string RemixedFixtureCatalog = "remixed-contract-fixture-v1";

        public static void ValidateSupported(string generator, string catalogId, string mode)
        {
            if (generator != Generator) throw new ArgumentException("Unsupported Job Fair placement generator.");
            if (UsesExpandedRewards(catalogId))
            {
                if (mode != ModeForExpandedCatalog(catalogId))
                    throw new ArgumentException("Expanded reward catalog and mode disagree.");
                return;
            }
            if (!((mode == MixedCrystals && (catalogId == MixedCatalog || catalogId == WorldCatalog || IsMixedShopCatalog(catalogId)))
                || (mode == RemixedCrystals && (catalogId == RemixedFixtureCatalog || catalogId == RemixedJobSliceCatalog
                    || IsWorldTravelCatalog(catalogId)))))
                throw new ArgumentException("Unsupported Job Fair mode/catalog combination.");
        }

        public static void ValidatePlayable(RunMetadata metadata)
        {
            if (metadata == null) throw new ArgumentNullException(nameof(metadata));
            if (metadata.SchemaVersion == 1) return;
            ValidateSupported(metadata.Generator, metadata.CatalogId, metadata.Mode);
            if (UsesBossShuffle(metadata)) BossCatalog.Validate(metadata);
            if (IsPlayableRemixedCatalog(metadata.CatalogId))
            {
                ValidateWorldTravel(metadata);
                return;
            }
            if (!IsMixedMode(metadata.Mode))
                throw new ArgumentException("This historical Remixed draft is not available for native play.");
        }

        // Explicit isolated-probe entry only. This does not admit the draft to
        // NextRunConfig or normal save adoption. Native catalog validation must
        // additionally check exact candidates, world quotas and ordinary/shop pools.
        public static void ValidateJobSliceDraft(RunMetadata metadata)
        {
            if (metadata == null || metadata.SchemaVersion != 2 || metadata.Mode != RemixedCrystals
                || metadata.CatalogId != RemixedJobSliceCatalog || metadata.StartingJobs.Count != 4
                || !metadata.Settings.SameAs(RunSettings.ForCatalog(RemixedCrystals, RemixedJobSliceCatalog))
                || metadata.Placements.Count != 1113 || metadata.AcquiredKeyIds.Count != 0)
                throw new ArgumentException("Invalid isolated Remixed job-slice contract.");
            int bonuses = 0, ordinary = 0, shops = 0;
            foreach (var reward in metadata.Placements)
            {
                if (reward.LocationId.StartsWith("remixed_job/", StringComparison.Ordinal))
                {
                    if (reward.RewardKind != "job") throw new ArgumentException("Bonus locations require one job.");
                    bonuses++;
                }
                else if (reward.RewardKind == "shop") shops++;
                else if (reward.RewardKind == "item" || reward.RewardKind == "gil") ordinary++;
                else throw new ArgumentException("Unexpected job/key reward outside the isolated bonus table.");
            }
            if (bonuses != 17 || ordinary != 260 || shops != 836)
                throw new ArgumentException("Incomplete isolated Remixed job-slice pools.");
            foreach (string claim in metadata.ClaimedLocationIds)
                if (!claim.StartsWith("remixed_job/", StringComparison.Ordinal))
                    throw new ArgumentException("This draft stores only independent job-bonus claims.");
        }

        public static bool IsWorldTravelDraft(RunMetadata run)
            => run != null && run.SchemaVersion == 3 && run.Mode == RemixedCrystals
                && run.CatalogId == WorldTravelCatalog && run.WorldState != null;

        public static bool IsWorldTravelCatalog(string catalogId)
            => catalogId == WorldTravelCatalog || IsPlayableRemixedCatalog(catalogId);

        public static bool IsPlayableRemixedCatalog(string catalogId)
            => catalogId == RemixedPlaytestCatalog || catalogId == FixedKarnakRemixedCatalog || catalogId == RemixedBossCatalog
                || catalogId == RemixedRewardsCatalog || catalogId == RemixedFiestaCatalog;

        public static bool IsMixedShopCatalog(string catalogId)
            => catalogId == ShopCatalog || catalogId == FixedKarnakMixedCatalog || catalogId == MixedBossCatalog
                || catalogId == MixedRewardsCatalog || catalogId == MixedFiestaCatalog;

        public static bool UsesFixedKarnakRewards(string catalogId)
            => catalogId == FixedKarnakMixedCatalog || catalogId == FixedKarnakRemixedCatalog || UsesBossShuffle(catalogId);

        public static bool UsesBossShuffle(string catalogId)
            => catalogId == MixedBossCatalog || catalogId == RemixedBossCatalog || UsesExpandedRewards(catalogId);

        public static bool UsesBossShuffle(RunMetadata run)
            => run != null && run.SchemaVersion != 1 && UsesBossShuffle(run.CatalogId);

        public static bool UsesWorldTravel(RunMetadata run)
            => run != null && run.SchemaVersion == 3 && IsRemixedMode(run.Mode)
                && IsWorldTravelCatalog(run.CatalogId) && run.WorldState != null;

        public static bool IsMixedMode(string mode) => mode == MixedCrystals || mode == MixedFiesta;
        public static bool IsRemixedMode(string mode) => mode == RemixedCrystals || mode == RemixedFiesta;
        public static bool IsFiesta(string catalogId) => catalogId == MixedFiestaCatalog || catalogId == RemixedFiestaCatalog;
        public static bool IsFiesta(RunMetadata run) => run != null && IsFiesta(run.CatalogId);
        public static bool UsesExpandedRewards(string catalogId)
            => catalogId == MixedRewardsCatalog || catalogId == RemixedRewardsCatalog || IsFiesta(catalogId);
        public static bool UsesExpandedRewards(RunMetadata run) => run != null && UsesExpandedRewards(run.CatalogId);
        public static string ModeForExpandedCatalog(string catalogId)
        {
            switch (catalogId)
            {
                case MixedRewardsCatalog: return MixedCrystals;
                case RemixedRewardsCatalog: return RemixedCrystals;
                case MixedFiestaCatalog: return MixedFiesta;
                case RemixedFiestaCatalog: return RemixedFiesta;
                default: throw new ArgumentException("Unknown expanded reward catalog.");
            }
        }

        public static bool IsOrdinaryReward(RewardPlacement reward)
            => reward != null && (reward.RewardKind == "item" || reward.RewardKind == "gil" || reward.RewardKind == "magic");

        private static void ValidateExpandedTravel(RunMetadata run)
        {
            if (!UsesWorldTravel(run) || run.StartingJobs.Count != 4
                || !run.Settings.SameAs(RunSettings.ForCatalog(run.Mode, run.CatalogId)))
                throw new ArgumentException("Invalid expanded world-travel contract.");
            var physical = new HashSet<string>(StringComparer.Ordinal);
            var keys = new HashSet<string>(StringComparer.Ordinal);
            int jobs = 0, ordinary = 0, shops = 0, bosses = 0;
            foreach (var reward in run.Placements)
            {
                string baseId = null;
                if (reward.LocationId.StartsWith("remixed_job/", StringComparison.Ordinal))
                {
                    if (reward.RewardKind != "job") throw new ArgumentException("Job bonus kind changed.");
                    baseId = reward.LocationId.Substring("remixed_job/".Length); jobs++;
                }
                else if (reward.LocationId.StartsWith("remixed_key/", StringComparison.Ordinal))
                {
                    if (reward.RewardKind != "key" || !keys.Add(reward.RewardId))
                        throw new ArgumentException("Invalid world-key bonus.");
                    baseId = reward.LocationId.Substring("remixed_key/".Length);
                }
                else if (reward.RewardKind == "shop") shops++;
                else if (reward.RewardKind == "boss") bosses++;
                else if (reward.RewardKind == "loot") { }
                else if (IsOrdinaryReward(reward)
                    && (reward.LocationId.StartsWith("native_treasure_", StringComparison.Ordinal)
                        || reward.LocationId.StartsWith("native_cache_", StringComparison.Ordinal))) ordinary++;
                else if (IsOrdinaryReward(reward) && reward.LocationId.StartsWith("native_event_reward_", StringComparison.Ordinal)) { }
                else throw new ArgumentException("Unknown expanded reward placement.");
                if (baseId != null && (!physical.Add(baseId) || !IsOrdinaryReward(run.FindPlacement(baseId))))
                    throw new ArgumentException("Bonus must retain a distinct ordinary container.");
            }
            if (jobs != (IsFiesta(run) ? 0 : 17) || ordinary != 260 || shops != 836 || bosses != 49
                || !keys.SetEquals(new[] { WorldTravelState.World2Key, WorldTravelState.World3Key }))
                throw new ArgumentException("Expanded world-travel pool sizes disagree.");
            foreach (string claim in run.ClaimedLocationIds)
                if (!claim.StartsWith("remixed_job/", StringComparison.Ordinal)
                    && !claim.StartsWith("remixed_key/", StringComparison.Ordinal)
                    && !claim.StartsWith("native_event_reward_", StringComparison.Ordinal))
                    throw new ArgumentException("Unrecognized independent reward claim.");
            run.WorldState.Validate(run.AcquiredKeyIds);
        }

        // Structural contract only. The source-pinned catalog additionally
        // validates candidate bindings, world restrictions and exact pools.
        public static void ValidateWorldTravelDraft(RunMetadata run)
        {
            if (!IsWorldTravelDraft(run)) throw new ArgumentException("Invalid isolated world-travel contract.");
            ValidateWorldTravel(run);
        }

        public static void ValidateWorldTravel(RunMetadata run)
        {
            if (UsesExpandedRewards(run)) { ValidateExpandedTravel(run); return; }
            if (!UsesWorldTravel(run) || run.StartingJobs.Count != 4 || run.Placements.Count != (UsesBossShuffle(run) ? 1164 : 1115)
                || !run.Settings.SameAs(RunSettings.ForCatalog(RemixedCrystals, run.CatalogId)))
                throw new ArgumentException("Invalid world-travel contract.");
            var physical = new HashSet<string>(StringComparer.Ordinal);
            var keys = new HashSet<string>(StringComparer.Ordinal);
            int jobs = 0, ordinary = 0, shops = 0;
            foreach (var reward in run.Placements)
            {
                if (reward.RewardKind == "boss" && UsesBossShuffle(run)) continue;
                string baseId = null;
                if (reward.LocationId.StartsWith("remixed_job/", StringComparison.Ordinal))
                {
                    if (reward.RewardKind != "job") throw new ArgumentException("Job bonus kind changed.");
                    baseId = reward.LocationId.Substring("remixed_job/".Length); jobs++;
                }
                else if (reward.LocationId.StartsWith("remixed_key/", StringComparison.Ordinal))
                {
                    if (reward.RewardKind != "key" || reward.Quantity != 1 || !keys.Add(reward.RewardId))
                        throw new ArgumentException("World keys require distinct single bonus rewards.");
                    baseId = reward.LocationId.Substring("remixed_key/".Length);
                }
                else if (reward.RewardKind == "shop") shops++;
                else if (reward.RewardKind == "item" || reward.RewardKind == "gil") ordinary++;
                else throw new ArgumentException("Unexpected reward outside world-travel bonus locations.");
                if (baseId != null)
                {
                    var baseReward = run.FindPlacement(baseId);
                    if (!physical.Add(baseId) || baseReward == null
                        || (baseReward.RewardKind != "item" && baseReward.RewardKind != "gil"))
                        throw new ArgumentException("Each bonus requires a distinct preserved ordinary container.");
                }
            }
            if (jobs != 17 || ordinary != 260 || shops != 836
                || !keys.SetEquals(new[] { WorldTravelState.World2Key, WorldTravelState.World3Key }))
                throw new ArgumentException("World-travel reward pools changed.");
            foreach (string claim in run.ClaimedLocationIds)
                if (!claim.StartsWith("remixed_job/", StringComparison.Ordinal)
                    && !claim.StartsWith("remixed_key/", StringComparison.Ordinal))
                    throw new ArgumentException("Only job and world-key bonuses have independent claims.");
            run.WorldState.Validate(run.AcquiredKeyIds);
        }

        internal static void ValidateToken(string value, string label)
        {
            if (String.IsNullOrEmpty(value) || value.Length > 200) throw new ArgumentException("Invalid " + label + " identifier.");
            foreach (char character in value)
                if (!(character >= 'a' && character <= 'z') && !(character >= 'A' && character <= 'Z')
                    && !(character >= '0' && character <= '9') && character != '-' && character != '_'
                    && character != '.' && character != '/' && character != ':')
                    throw new ArgumentException("Invalid " + label + " identifier.");
        }

        public static string ComputePlacementHash(string seed, string generator, string mode, RunSettings settings,
            string catalogId, IReadOnlyList<int> startingJobs, IReadOnlyList<RewardPlacement> placements)
        {
            var rows = new List<RewardPlacement>(placements);
            rows.Sort((left, right) => StringComparer.Ordinal.Compare(left.LocationId, right.LocationId));
            var text = new StringBuilder("JobFair|placements|1\n");
            text.Append("seed|").Append(seed).Append('\n');
            text.Append("generator|").Append(generator).Append('\n');
            text.Append("mode|").Append(mode).Append('\n');
            text.Append("catalog|").Append(catalogId).Append('\n');
            text.Append("settings|").Append(settings.JobRewards).Append('|').Append(settings.KeyItems).Append('|')
                .Append(settings.Treasure).Append('|').Append(settings.TreasureScope).Append('|').Append(settings.WorldAccess).Append('\n');
            if (settings.ShopScope != null)
                text.Append("shops|").Append(settings.ShopScope).Append('|').Append(settings.ShopPrices).Append('\n');
            if (settings.BossScope != null)
                text.Append("bosses|").Append(settings.BossScope).Append('|').Append(settings.BossScaling).Append('\n');
            if (settings.LootScope != null)
                text.Append("loot|").Append(settings.LootScope).Append('\n');
            if (settings.BossExperience != null)
                text.Append("boss-experience|").Append(settings.BossExperience).Append('\n');
            text.Append("starters|");
            for (int index = 0; index < startingJobs.Count; index++)
            {
                if (index != 0) text.Append(',');
                text.Append(startingJobs[index].ToString(CultureInfo.InvariantCulture));
            }
            text.Append('\n');
            foreach (RewardPlacement placement in rows)
                text.Append("placement|").Append(placement.LocationId).Append('|').Append(placement.RewardKind).Append('|')
                    .Append(placement.RewardId).Append('|').Append(placement.Quantity.ToString(CultureInfo.InvariantCulture)).Append('\n');
            using (SHA256 hash = SHA256.Create())
            {
                byte[] digest = hash.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()));
                var result = new StringBuilder(64);
                foreach (byte value in digest) result.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                return result.ToString();
            }
        }
    }
}
