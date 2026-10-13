// Finite event grants share their original world reward multisets with treasure.
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProjectBlues.Runtime
{
    public sealed class EventRewardCatalog
    {
        public static readonly EventRewardCatalog Shared = new EventRewardCatalog();
        private readonly JsonElement catalog;
        private readonly HashSet<int> magic = new HashSet<int>();
        public OpeningScript[] Scripts { get; }

        public EventRewardCatalog()
        {
            using (var document = JsonDocument.Parse(NativeEventRewardData.CatalogJson)) catalog = document.RootElement.Clone();
            if (catalog.GetProperty("catalog_id").GetString() != "world-event-pools-v1")
                throw new ArgumentException("Unknown event reward catalog.");
            foreach (var id in catalog.GetProperty("magic_content_ids").EnumerateArray()) magic.Add(id.GetInt32());
            Scripts = new OpeningScript[NativeEventRewardData.ScriptJson.Length];
            foreach (var script in catalog.GetProperty("scripts").EnumerateArray())
            {
                int index = script.GetProperty("script_index").GetInt32();
                string raw = NativeEventRewardData.ScriptJson[index];
                using (var hash = SHA256.Create())
                    if (Convert.ToHexString(hash.ComputeHash(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant()
                        != script.GetProperty("sha256").GetString())
                        throw new ArgumentException("Event reward source hash mismatch.");
                Scripts[index] = new OpeningScript(raw);
            }
        }

        public bool IsKnownScriptName(string name)
        {
            foreach (var script in catalog.GetProperty("scripts").EnumerateArray())
                if (name == script.GetProperty("script_name").GetString()) return true;
            return false;
        }

        public bool IsKelbScript(int index) => Script(index).GetProperty("script_name").GetString() == "sc_e_0236";

        // Validates ALL ordinary + event locations, including exact fixed Karnak
        // rows. The caller adds jobs/shops/loot/boss assignments separately.
        public void AddExpected(RunMetadata run, HashSet<string> expected)
        {
            if (!CampaignContract.UsesExpandedRewards(run)) throw new ArgumentException("Expanded reward catalog required.");
            var original = new List<string>();
            var actual = new List<string>();
            int count = 0, fixedCount = 0;
            foreach (string group in new[] { "ordinary_locations", "locations" })
                foreach (var row in catalog.GetProperty(group).EnumerateArray())
                {
                    string id = row.GetProperty("id").GetString();
                    if (!expected.Add(id)) throw new ArgumentException("Duplicate expanded reward location.");
                    var reward = CampaignCatalog.Find(run, id);
                    var native = row.GetProperty("native_reward");
                    int content = native.GetProperty("content_id").GetInt32();
                    string kind = native.GetProperty("kind").GetString();
                    bool fixedKarnak = CampaignCatalog.IsFixedKarnakLocation(id);
                    if (!fixedKarnak && magic.Contains(content)) kind = "magic";
                    string nativeId = kind == "gil" ? "" : content.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    string value = kind + ":" + nativeId + ":" + native.GetProperty("quantity").GetInt32();
                    string placed = reward.RewardKind + ":" + reward.RewardId + ":" + reward.Quantity;
                    string world = row.GetProperty("world").GetInt32() + ":";
                    original.Add(world + value); actual.Add(world + placed);
                    if (fixedKarnak)
                    {
                        if (world != "1:" || placed != value) throw new ArgumentException("Fixed Karnak event pool reward changed.");
                        fixedCount++;
                    }
                    count++;
                }
            if (count != 295 || fixedCount != 15) throw new ArgumentException("Expanded reward source count changed.");
            original.Sort(StringComparer.Ordinal); actual.Sort(StringComparer.Ordinal);
            for (int i = 0; i < original.Count; i++)
                if (original[i] != actual[i]) throw new ArgumentException("Expanded world reward multiset changed.");
        }

        public static RewardPlacement BossReward(RunMetadata run, int summonItem)
        {
            if (!CampaignContract.UsesExpandedRewards(run)) return null;
            if (summonItem < 33 || summonItem > 35) throw new ArgumentException("Unknown boss summon reward.");
            return CampaignCatalog.Find(run, "native_event_reward_boss_summon_" + summonItem);
        }

        public string RewardName(RewardPlacement reward)
        {
            if (reward.RewardKind == "gil") return reward.Quantity + " gil";
            if (reward.RewardKind != "item" && reward.RewardKind != "magic") throw new ArgumentException("Unsupported event reward kind.");
            string name = catalog.GetProperty("content_names").GetProperty(reward.RewardId).GetString();
            return name + (reward.Quantity == 1 ? "" : " x" + reward.Quantity);
        }

        // A complete acquisition sentence, also used by expanded ordinary caches.
        public string RewardText(RewardPlacement reward) => "Obtained " + RewardName(reward) + "!";

        public string Message(string key, RunMetadata run)
        {
            if (!CampaignContract.UsesExpandedRewards(run) || key == null || !key.StartsWith("JOB_FAIR_EVENT_", StringComparison.Ordinal)) return null;
            foreach (var script in catalog.GetProperty("scripts").EnumerateArray())
                foreach (var binding in script.GetProperty("message_bindings").EnumerateArray())
                    for (int stage = 0; stage < binding.GetProperty("location_ids").GetArrayLength(); stage++)
                    {
                        if (key != MessageKey(script.GetProperty("script_index").GetInt32(), binding.GetProperty("index").GetInt32(), stage)) continue;
                        var reward = CampaignCatalog.Find(run, binding.GetProperty("location_ids")[stage].GetString());
                        return binding.GetProperty("template").GetString().Replace("{reward}", RewardName(reward))
                            .Replace("{obtained}", RewardText(reward)).Replace("\\n", Environment.NewLine);
                    }
            throw new ArgumentException("Unknown event reward message key.");
        }

        public string RewriteScript(int index, RunMetadata run, int kelbStage = 0)
        {
            if (!CampaignContract.UsesExpandedRewards(run)) return NativeEventRewardData.ScriptJson[index];
            var script = Script(index);
            if (kelbStage < 0 || kelbStage > 2) throw new ArgumentException("Invalid Kelb reward visit.");
            var program = JsonNode.Parse(NativeEventRewardData.ScriptJson[index]);
            var commands = program["Mnemonics"].AsArray();
            foreach (var binding in script.GetProperty("reward_bindings").EnumerateArray())
            {
                var ids = binding.GetProperty("location_ids");
                var reward = CampaignCatalog.Find(run, ids[ids.GetArrayLength() == 1 ? 0 : kelbStage].GetString());
                var integers = commands[binding.GetProperty("index").GetInt32()]["operands"]["iValues"].AsArray();
                integers[0] = CampaignCatalog.NativeContentId(reward);
                integers[1] = reward.Quantity;
            }
            foreach (var binding in script.GetProperty("message_bindings").EnumerateArray())
            {
                int command = binding.GetProperty("index").GetInt32();
                int stage = binding.GetProperty("location_ids").GetArrayLength() == 1 ? 0 : kelbStage;
                commands[command]["operands"]["sValues"][binding.GetProperty("slot").GetInt32()] = MessageKey(index, command, stage);
            }
            return program.ToJsonString();
        }

        private static string MessageKey(int script, int command, int stage) => "JOB_FAIR_EVENT_" + script + "_" + command + "_" + stage;
        private JsonElement Script(int index)
        {
            foreach (var script in catalog.GetProperty("scripts").EnumerateArray())
                if (script.GetProperty("script_index").GetInt32() == index) return script;
            throw new ArgumentException("Unknown native event script.");
        }
    }
}
