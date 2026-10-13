// Numeric, frozen regular-enemy loot contract. Native rolls remain native.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace ProjectBlues.Runtime
{
    public sealed class EnemyLootCatalog
    {
        public const string Scope = "native-occupied-slots-v1";
        public const string Prefix = "native_loot/";
        private readonly JsonDocument document;
        private readonly Dictionary<int, JsonElement> monsters = new Dictionary<int, JsonElement>();
        private readonly Dictionary<string, JsonElement> slots = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        private static EnemyLootCatalog shared;
        public static EnemyLootCatalog Shared => shared ?? (shared = new EnemyLootCatalog());
        public int PlacementCount => slots.Count;
        public JsonElement SupplementalBossExperience => document.RootElement.GetProperty("supplemental_boss_experience");

        private EnemyLootCatalog()
        {
            document = JsonDocument.Parse(NativeEnemyLootData.Json);
            var root = document.RootElement;
            if (root.GetProperty("schema_version").GetInt32() != 1
                || root.GetProperty("scope").GetString() != Scope
                || root.GetProperty("catalog_id").GetString() != "ff5-enemy-loot-v1")
                throw new ArgumentException("Unsupported native loot catalog.");
            foreach (var monster in root.GetProperty("monsters").EnumerateArray())
            {
                int id = monster.GetProperty("monster_id").GetInt32();
                if (id <= 0 || !monsters.TryAdd(id, monster)
                    || monster.GetProperty("drops").GetArrayLength() != 8
                    || monster.GetProperty("steals").GetArrayLength() != 4)
                    throw new ArgumentException("Invalid native loot monster.");
            }
            foreach (var slot in root.GetProperty("slots").EnumerateArray())
            {
                string id = slot.GetProperty("location_id").GetString();
                int monster = slot.GetProperty("monster_id").GetInt32();
                string kind = slot.GetProperty("kind").GetString();
                int index = slot.GetProperty("slot").GetInt32();
                int content = slot.GetProperty("native_content_id").GetInt32();
                if (!monsters.TryGetValue(monster, out var row) || (kind != "drop" && kind != "steal")
                    || index < 1 || index > (kind == "drop" ? 8 : 4) || content <= 0 || IsCritical(content)
                    || id != Location(monster, kind, index) || !slots.TryAdd(id, slot)
                    || content != (kind == "drop" ? row.GetProperty("drops")[index - 1][0].GetInt32()
                                                   : row.GetProperty("steals")[index - 1].GetInt32()))
                    throw new ArgumentException("Invalid occupied native loot slot.");
            }
            if (monsters.Count != 238 || slots.Count != 555)
                throw new ArgumentException("Native loot catalog coverage changed.");
        }
        public static bool IsCritical(int content) => content >= 33 && content <= 37;
        public static string Location(int monster, string kind, int index)
            => Prefix + monster.ToString(CultureInfo.InvariantCulture) + "/" + kind + "/" + index.ToString(CultureInfo.InvariantCulture);
        public static bool Applies(RunMetadata run) => CampaignContract.UsesExpandedRewards(run)
            && run.Settings.LootScope == Scope;
        public bool TryMonster(int id, out JsonElement monster) => monsters.TryGetValue(id, out monster);
        public static void Validate(RunMetadata run) => AddExpected(run, new HashSet<string>(StringComparer.Ordinal));
        public static void AddExpected(RunMetadata run, HashSet<string> expected)
        {
            if (!Applies(run)) return;
            var catalog = Shared;
            var nativeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var assignedCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var pair in catalog.slots)
            {
                if (!expected.Add(pair.Key)) throw new ArgumentException("Duplicate native loot location.");
                var placement = run.FindPlacement(pair.Key);
                if (placement == null || placement.RewardKind != "loot" || placement.Quantity != 1
                    || !Int32.TryParse(placement.RewardId, NumberStyles.None, CultureInfo.InvariantCulture, out int content)
                    || content <= 0 || IsCritical(content) || content.ToString(CultureInfo.InvariantCulture) != placement.RewardId)
                    throw new ArgumentException("Missing or invalid native loot placement.");
                string kind = pair.Value.GetProperty("kind").GetString();
                Count(nativeCounts, kind + "/" + pair.Value.GetProperty("native_content_id").GetInt32());
                Count(assignedCounts, kind + "/" + content);
            }
            if (nativeCounts.Count != assignedCounts.Count) throw new ArgumentException("Native loot pools changed.");
            foreach (var pair in nativeCounts)
                if (!assignedCounts.TryGetValue(pair.Key, out int count) || count != pair.Value)
                    throw new ArgumentException("Native drop and steal contents must retain their separate multisets.");
            foreach (var placement in run.Placements)
                if (placement.RewardKind == "loot" && !catalog.slots.ContainsKey(placement.LocationId))
                    throw new ArgumentException("Unknown native loot location.");
        }
        private static void Count(Dictionary<string, int> values, string key)
        { values.TryGetValue(key, out int count); values[key] = count + 1; }
        public static int Resolve(RunMetadata run, int monster, string kind, int slot, int original)
        {
            if (original == 0) return 0;
            var placement = run.FindPlacement(Location(monster, kind, slot));
            if (placement == null || placement.RewardKind != "loot" || placement.Quantity != 1)
                throw new ArgumentException("Occupied enemy loot slot is missing its frozen reward.");
            return Int32.Parse(placement.RewardId, CultureInfo.InvariantCulture);
        }
    }
}
