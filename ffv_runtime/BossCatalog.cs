// Versioned boss identities, placement validation and integer scaling. No game dependencies.
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace ProjectBlues.Runtime
{
    public sealed class BossCatalog
    {
        public const string CatalogId = "ff5-bosses-v1";
        public const string ScalingVersion = "destination-budget-v1";
        private static BossCatalog shared;
        public static BossCatalog Shared => shared ?? (shared = new BossCatalog());
        private readonly JsonElement catalog;
        private readonly Dictionary<string, JsonElement> identities = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        private readonly Dictionary<int, JsonElement> formations = new Dictionary<int, JsonElement>();
        public JsonElement Bosses => catalog.GetProperty("bosses");

        public BossCatalog() : this(NativeBossData.CatalogJson) { }
        public BossCatalog(string json)
        {
            using (var document = JsonDocument.Parse(json)) catalog = document.RootElement.Clone();
            if (catalog.GetProperty("schema_version").GetInt32() != 1
                || catalog.GetProperty("catalog_id").GetString() != CatalogId
                || catalog.GetProperty("scaling_version").GetString() != ScalingVersion
                || Bosses.GetArrayLength() != 49)
                throw new ArgumentException("Boss catalog identity differs from the versioned 49-boss contract.");
            var ranks = new HashSet<int>(); int simple = 0, complex = 0;
            foreach (var boss in Bosses.EnumerateArray())
            {
                string id = boss.GetProperty("boss_id").GetString();
                identities.Add(id, boss);
                int rank = boss.GetProperty("reference_rank").GetInt32();
                if (rank < 1 || rank > 49 || !ranks.Add(rank)) throw new ArgumentException("Invalid boss reference rank.");
                string tier = boss.GetProperty("tier").GetString();
                if (tier == "simple") simple++; else if (tier == "complex") complex++;
                else throw new ArgumentException("Invalid boss tactical tier.");
                formations.Add(boss.GetProperty("formation_id").GetInt32(), boss);
                foreach (var alias in boss.GetProperty("formation_alias_ids").EnumerateArray()) formations.Add(alias.GetInt32(), boss);
                if (boss.GetProperty("effective_hp_budget").GetInt32() < 1)
                    throw new ArgumentException("Invalid boss health budget.");
                var componentIds = new HashSet<int>();
                foreach (var monster in boss.GetProperty("stats").EnumerateArray())
                    if (!componentIds.Add(monster.GetProperty("monster_id").GetInt32()) || monster.GetProperty("hp").GetInt32() < 1)
                        throw new ArgumentException("Invalid boss component stats.");
                foreach (var monster in boss.GetProperty("monster_ids").EnumerateArray())
                    if (!componentIds.Contains(monster.GetInt32())) throw new ArgumentException("Missing boss component stats.");
            }
            if (simple != 27 || complex != 22) throw new ArgumentException("Boss tactical pools changed.");
        }

        public JsonElement Get(string id)
        {
            if (!identities.TryGetValue(id, out var result)) throw new ArgumentException("Uncatalogued boss identity.");
            return result;
        }
        public bool TryFormation(int id, out JsonElement boss) => formations.TryGetValue(id, out boss);
        public static bool Applies(RunMetadata run) => CampaignContract.UsesBossShuffle(run);
        public static void Validate(RunMetadata run) => AddExpected(run, new HashSet<string>(StringComparer.Ordinal));
        public static void AddExpected(RunMetadata run, HashSet<string> expected)
        {
            if (!Applies(run)) return;
            var assigned = new HashSet<string>(StringComparer.Ordinal);
            foreach (var destination in Shared.Bosses.EnumerateArray())
            {
                string id = "native_boss/" + destination.GetProperty("boss_id").GetString();
                if (!expected.Add(id)) throw new ArgumentException("Duplicate boss location.");
                var placement = run.FindPlacement(id);
                if (placement == null || placement.RewardKind != "boss" || placement.Quantity != 1 || !assigned.Add(placement.RewardId))
                    throw new ArgumentException("Boss assignments must contain each boss exactly once.");
                var source = Shared.Get(placement.RewardId);
                if (CampaignContract.IsMixedMode(run.Mode)
                    && source.GetProperty("tier").GetString() != destination.GetProperty("tier").GetString())
                    throw new ArgumentException("Mixed boss assignment crossed tactical tiers.");
            }
            foreach (var placement in run.Placements)
                if (placement.RewardKind == "boss" && !expected.Contains(placement.LocationId))
                    throw new ArgumentException("Unknown boss placement location.");
        }

        public static JsonElement Monster(JsonElement encounter, int monsterId)
        {
            foreach (var monster in encounter.GetProperty("stats").EnumerateArray())
                if (monster.GetProperty("monster_id").GetInt32() == monsterId) return monster;
            throw new ArgumentException("Monster is outside the assigned encounter's audited forms and companions.");
        }
        public static bool HasMonster(JsonElement encounter, int monsterId)
        {
            foreach (var monster in encounter.GetProperty("monster_ids").EnumerateArray()) if (monster.GetInt32() == monsterId) return true;
            return false;
        }
        public static int ScaleInteger(int value, int numerator, int denominator, int minimum = 0, int maximum = Int32.MaxValue)
        {
            if (value < 0 || numerator < 0 || denominator <= 0 || minimum < 0 || maximum < minimum)
                throw new ArgumentException("Invalid boss scaling ratio.");
            long scaled = ((long)value * numerator + denominator / 2L) / denominator;
            return (int)Math.Max(minimum, Math.Min(maximum, scaled));
        }
        private static int Profile(JsonElement boss, string field)
        {
            int value = 0;
            var profileIds = new HashSet<int>();
            foreach (var id in boss.GetProperty("profile_monster_ids").EnumerateArray()) profileIds.Add(id.GetInt32());
            foreach (var monster in boss.GetProperty("stats").EnumerateArray())
            {
                if (!profileIds.Contains(monster.GetProperty("monster_id").GetInt32())) continue;
                int candidate = monster.GetProperty(field).GetInt32();
                if ((field == "defense" || field == "ability_defense") && candidate == 255) continue;
                value = Math.Max(value, candidate);
            }
            return value;
        }
        public static int ScaleDefense(int value, int numerator, int denominator)
        {
            if (value < 0 || value > 255 || numerator <= 0 || denominator <= 0)
                throw new ArgumentException("Invalid boss defense scaling ratio.");
            if (value == 0 || value == 255) return value;
            // Round value * sqrt(destination HP budget / source HP budget)
            // half-up using integer midpoint comparisons, never floating point.
            // The largest product is below 2^50 for positive Int32 budgets.
            long squared = 4L * value * value * numerator;
            int low = 0, high = 254;
            while (low < high)
            {
                int next = (low + high + 1) / 2;
                long midpoint = 2L * next - 1;
                if (squared >= midpoint * midpoint * denominator) low = next;
                else high = next - 1;
            }
            return low;
        }
        public static int ScaleStat(JsonElement source, JsonElement destination, int monsterId, string field)
        {
            int value = Monster(source, monsterId).GetProperty(field).GetInt32();
            if (field == "mp") return value;
            if (field == "hp") return ScaleInteger(value, destination.GetProperty("effective_hp_budget").GetInt32(),
                source.GetProperty("effective_hp_budget").GetInt32(), 1);
            bool defense = field == "defense" || field == "ability_defense";
            // Peak destination armor may belong to a temporary defensive form.
            // Native levels can also be puzzle values (Titan 1, Odin 2). Use
            // audited combat-work budgets, retaining the donor's own defenses.
            if (defense) return ScaleDefense(value, destination.GetProperty("effective_hp_budget").GetInt32(),
                source.GetProperty("effective_hp_budget").GetInt32());
            if (value == 0) return 0;
            return ScaleInteger(value, Profile(destination, field), Math.Max(1, Profile(source, field)), 1, 255);
        }
        public static int ScaleFixedDamage(JsonElement source, JsonElement destination, int damage)
        {
            if (damage < 0) throw new ArgumentOutOfRangeException(nameof(damage));
            if (damage == 0) return 0;
            return ScaleInteger(damage, Profile(destination, "lv"), Math.Max(1, Profile(source, "lv")), 1, 9999);
        }
        public static int ScaleHpThreshold(JsonElement source, JsonElement destination, int monsterId, int threshold)
        {
            int originalHp = Monster(source, monsterId).GetProperty("hp").GetInt32();
            if (threshold < 0 || threshold > originalHp) throw new ArgumentOutOfRangeException(nameof(threshold));
            // Values 1 and 2 test native death/survival sentinels. Scaling these
            // to zero prevents Sandworm/Gilgamesh/Necrophobe endings from firing.
            if (threshold <= 2) return threshold;
            int newHp = ScaleStat(source, destination, monsterId, "hp");
            int result = ScaleInteger(threshold, newHp, originalHp);
            // A non-full-health phase remains non-full-health even at the smallest budget.
            if (threshold < originalHp) result = Math.Min(result, newHp - 1);
            return result;
        }
    }
}
