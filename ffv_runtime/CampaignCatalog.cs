// Immutable, source-pinned native slice; no Unity dependencies.
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProjectBlues.Runtime
{
    public sealed class CampaignCatalog
    {
        private readonly JsonElement catalog;
        private readonly string[] templates;
        public string CatalogId { get; }
        public bool TravelSlice => CampaignContract.IsWorldTravelCatalog(CatalogId);
        public bool JobSlice => CatalogId == CampaignContract.RemixedJobSliceCatalog || TravelSlice;
        public bool WorldPools => CatalogId == CampaignContract.WorldCatalog || CampaignContract.IsMixedShopCatalog(CatalogId) || JobSlice;
        public bool ExpandedRewards => CampaignContract.UsesExpandedRewards(CatalogId);
        public bool Fiesta => CampaignContract.IsFiesta(CatalogId);
        // The first four native Wind Shrine grants, in dispatch order. Job-slot
        // suffixes identify the original jobs, not the order of the story events.
        public static readonly IReadOnlyList<string> FiestaWindLocations = Array.AsReadOnly(new[] {
            "native_job_slot_07", "native_job_slot_03", "native_job_slot_02", "native_job_slot_05" });
        public static bool IsFiestaWindLocation(string id)
        {
            foreach (string slot in FiestaWindLocations) if (slot == id) return true;
            return false;
        }
        public OpeningScript[] Scripts { get; }
        public JsonElement Chests => catalog.GetProperty(WorldPools ? "container_bindings" : "wind_chests");
        public bool IsKnownScriptName(string name)
        {
            foreach (var script in catalog.GetProperty("scripts").EnumerateArray())
                if (name == script.GetProperty("script_name").GetString()) return true;
            return false;
        }
        public CampaignCatalog(string catalogId = CampaignContract.MixedCatalog)
        {
            if (catalogId != CampaignContract.MixedCatalog && catalogId != CampaignContract.WorldCatalog
                && !CampaignContract.IsMixedShopCatalog(catalogId) && catalogId != CampaignContract.RemixedJobSliceCatalog
                && !CampaignContract.IsWorldTravelCatalog(catalogId))
                throw new ArgumentException("Unknown native reward catalog.");
            CatalogId = catalogId;
            templates = WorldPools ? NativeWorldRewardData.ScriptJson : NativeRewardData.ScriptJson;
            using (var document = JsonDocument.Parse(WorldPools ? NativeWorldRewardData.CatalogJson : NativeRewardData.CatalogJson))
                catalog = document.RootElement.Clone();
            if (catalog.GetProperty("catalog_id").GetString() != (WorldPools ? CampaignContract.WorldCatalog : CatalogId))
                throw new ArgumentException("Embedded reward catalog identity mismatch.");
            Scripts = new OpeningScript[templates.Length];
            foreach (var script in catalog.GetProperty("scripts").EnumerateArray())
            {
                int index = script.GetProperty("script_index").GetInt32();
                string raw = templates[index];
                using (var hash = SHA256.Create())
                    if (Convert.ToHexString(hash.ComputeHash(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant()
                        != script.GetProperty("sha256").GetString())
                        throw new ArgumentException("Embedded native reward script hash mismatch.");
                Scripts[index] = new OpeningScript(raw);
            }
        }

        public void ValidateRun(RunMetadata run)
        {
            if (run == null || run.SchemaVersion == 1) return;
            // The explicit staged catalog can be inspected by portable probes.
            // Native adoption still calls ValidatePlayable before this method.
            if (TravelSlice) CampaignContract.ValidateWorldTravel(run);
            else if (JobSlice) CampaignContract.ValidateJobSliceDraft(run);
            else CampaignContract.ValidatePlayable(run);
            if (run.CatalogId != CatalogId) throw new ArgumentException("Run requires a different native catalog.");
            var expected = new HashSet<string>(StringComparer.Ordinal);
            if (JobSlice) RemixedJobCatalog.AddExpected(run, expected);
            else foreach (var slot in catalog.GetProperty("job_slots").EnumerateArray())
            {
                string id = slot.GetProperty("id").GetString();
                if (Fiesta && !IsFiestaWindLocation(id)) continue;
                expected.Add(id);
                var reward = Find(run, id);
                if (reward.RewardKind != "job") throw new ArgumentException("Native crystal slot requires a job.");
            }
            if (TravelSlice) WorldTravelState.AddExpected(run, expected);
            if (CampaignContract.UsesBossShuffle(run)) BossCatalog.AddExpected(run, expected);
            if (ExpandedRewards)
            {
                EventRewardCatalog.Shared.AddExpected(run, expected);
                EnemyLootCatalog.AddExpected(run, expected);
                ShopCatalog.AddExpected(run, expected);
                if (expected.Count != run.Placements.Count)
                    throw new ArgumentException("Expanded native placement table is incomplete or has extra locations.");
                return;
            }
            var pool = new List<string>();
            var actual = new List<string>();
            int fixedKarnakRows = 0;
            foreach (var chest in (WorldPools ? catalog.GetProperty("locations") : Chests).EnumerateArray())
            {
                string id = chest.GetProperty("id").GetString();
                expected.Add(id);
                var reward = Find(run, id);
                if (WorldPools)
                {
                    var native = chest.GetProperty("native_reward");
                    string world = chest.GetProperty("world").GetInt32() + ":";
                    string kind = native.GetProperty("kind").GetString();
                    string content = kind == "item" ? native.GetProperty("content_id").GetInt32().ToString() : "";
                    pool.Add(world + kind + ":" + content + ":" + native.GetProperty("quantity").GetInt32());
                    actual.Add(world + reward.RewardKind + ":" + reward.RewardId + ":" + reward.Quantity);
                    if (CampaignContract.UsesFixedKarnakRewards(CatalogId) && IsFixedKarnakLocation(id))
                    {
                        if (world != "1:" || reward.RewardKind != kind || reward.RewardId != content
                            || reward.Quantity != native.GetProperty("quantity").GetInt32())
                            throw new ArgumentException("Timed Karnak Castle chests must retain their native rewards.");
                        fixedKarnakRows++;
                    }
                }
                else
                {
                    if (reward.RewardKind != "item") throw new ArgumentException("This treasure slice permits ordinary items only.");
                    pool.Add(chest.GetProperty("content_id").GetInt32() + ":" + chest.GetProperty("quantity").GetInt32());
                    actual.Add(reward.RewardId + ":" + reward.Quantity);
                }
            }
            if (CampaignContract.UsesFixedKarnakRewards(CatalogId) && fixedKarnakRows != 15)
                throw new ArgumentException("Fixed Karnak treasure identities changed.");
            if (CampaignContract.IsMixedShopCatalog(CatalogId) || JobSlice) ShopCatalog.AddExpected(run, expected);
            if (expected.Count != run.Placements.Count) throw new ArgumentException("Native placement table is incomplete or has extra locations.");
            pool.Sort(StringComparer.Ordinal); actual.Sort(StringComparer.Ordinal);
            for (int i = 0; i < pool.Count; i++)
                if (pool[i] != actual[i]) throw new ArgumentException("Native treasure pool or quantity changed.");
        }

        // Source audit: the fifteen one-time castle containers have exactly
        // these identities in the frozen 260-row native world catalog. Keep
        // every saved row, and validate fixed stock separately from pool totals.
        internal static bool IsFixedKarnakLocation(string id)
        {
            for (int flag = 86; flag <= 100; flag++)
                if (id == "native_treasure_" + flag.ToString("D3", System.Globalization.CultureInfo.InvariantCulture)) return true;
            return false;
        }

        public static RewardPlacement Find(RunMetadata run, string id)
        {
            foreach (var reward in run.Placements) if (reward.LocationId == id) return reward;
            throw new ArgumentException("Placement missing for " + id);
        }

        public string RewriteScript(int index, RunMetadata run)
        {
            ValidateRun(run);
            if (run == null || run.SchemaVersion == 1) return templates[index];
            var root = JsonNode.Parse(templates[index]);
            foreach (var slot in catalog.GetProperty("job_slots").EnumerateArray())
            {
                if (slot.GetProperty("script_index").GetInt32() != index) continue;
                var command = root["Mnemonics"][slot.GetProperty("index").GetInt32()];
                if (JobSlice || (Fiesta && !IsFiestaWindLocation(slot.GetProperty("id").GetString())))
                {
                    // Preserve every instruction address, branch, label and story flag.
                    // Only the pinned vanilla job grant becomes a no-op.
                    Nop(command);
                }
                else
                {
                    int job = Find(run, slot.GetProperty("id").GetString()).NumericRewardId;
                    command["operands"]["sValues"][0] = Job(job).GetProperty("native_token").GetString();
                }
            }
            foreach (var binding in Script(index).GetProperty("message_bindings").EnumerateArray())
            {
                var command = root["Mnemonics"][binding.GetProperty("index").GetInt32()];
                if (Fiesta && (JobSlice || index != 1)) Nop(command);
                else command["operands"]["sValues"][0] = MessageKey(index, binding.GetProperty("index").GetInt32());
            }
            if (WorldPools)
                foreach (var slot in catalog.GetProperty("treasure_slots").EnumerateArray())
                {
                    if (slot.GetProperty("script_index").GetInt32() != index) continue;
                    var reward = Find(run, slot.GetProperty("id").GetString());
                    var command = root["Mnemonics"][slot.GetProperty("index").GetInt32()];
                    command["operands"]["iValues"][0] = NativeContentId(reward);
                    command["operands"]["iValues"][1] = reward.Quantity;
                    root["Mnemonics"][slot.GetProperty("message_index").GetInt32()]["operands"]["sValues"][0]
                        = "JOB_FAIR_TREASURE_" + slot.GetProperty("id").GetString();
                }
            return root.ToJsonString();
        }

        public static int NativeContentId(RewardPlacement reward)
        {
            if (reward.RewardKind == "item" || reward.RewardKind == "magic") return reward.NumericRewardId;
            if (reward.RewardKind == "gil") return 1;
            if (reward.RewardKind == "empty") return 0;
            throw new ArgumentException("Expected an ordinary treasure reward.");
        }

        public string Message(string key, RunMetadata run)
        {
            if (run == null || run.SchemaVersion == 1 || key == null) return null;
            if (WorldPools && key.StartsWith("JOB_FAIR_TREASURE_", StringComparison.Ordinal))
            {
                var reward = Find(run, key.Substring("JOB_FAIR_TREASURE_".Length));
                if (ExpandedRewards) return EventRewardCatalog.Shared.RewardText(reward);
                if (reward.RewardKind == "gil") return "Obtained " + reward.Quantity + " gil!";
                if (reward.RewardKind == "empty") return "It's empty.";
                if (reward.RewardKind != "item") throw new ArgumentException("Unexpected scripted treasure kind.");
                string name = catalog.GetProperty("item_names").GetProperty(reward.RewardId).GetString();
                return "Obtained " + name + (reward.Quantity == 1 ? "!" : " x" + reward.Quantity + "!");
            }
            if (!key.StartsWith("JOB_FAIR_CRYSTAL_", StringComparison.Ordinal)) return null;
            foreach (var script in catalog.GetProperty("scripts").EnumerateArray())
                foreach (var binding in script.GetProperty("message_bindings").EnumerateArray())
                {
                    if (key != MessageKey(script.GetProperty("script_index").GetInt32(), binding.GetProperty("index").GetInt32())) continue;
                    if (Fiesta)
                    {
                        if (JobSlice || script.GetProperty("script_index").GetInt32() != 1)
                            throw new ArgumentException("Fiesta suppressed crystal message cannot dispatch.");
                        var selected = new List<string>();
                        foreach (string id in FiestaWindLocations) selected.Add(JobName(Find(run, id).NumericRewardId));
                        return "<CT>" + selected[0] + "     " + selected[1] + "<P>" + Environment.NewLine
                            + selected[2] + "     " + selected[3];
                    }
                    if (JobSlice) return "Job crystals are hidden in treasures throughout the world.";
                    string text = binding.GetProperty("original_text").GetString();
                    var names = binding.GetProperty("original_names");
                    var slots = binding.GetProperty("slot_ids");
                    for (int i = 0; i < names.GetArrayLength(); i++) text = text.Replace(names[i].GetString(), "\u0001" + i + "\u0001");
                    for (int i = 0; i < names.GetArrayLength(); i++)
                        text = text.Replace("\u0001" + i + "\u0001", Job(Find(run, slots[i].GetString()).NumericRewardId).GetProperty("original_name").GetString());
                    // Native TSV Parse performs this before GetMessage returns.
                    return text.Replace("\\n", Environment.NewLine);
                }
            throw new ArgumentException("Unknown campaign message key.");
        }

        private JsonElement Script(int index)
        {
            foreach (var script in catalog.GetProperty("scripts").EnumerateArray())
                if (script.GetProperty("script_index").GetInt32() == index) return script;
            throw new ArgumentException("Unknown native script.");
        }
        private static void Nop(JsonNode command)
        {
            command["mnemonic"] = "Nop";
            for (int operand = 0; operand < 8; operand++)
            {
                command["operands"]["iValues"][operand] = 0;
                command["operands"]["rValues"][operand] = 0;
                command["operands"]["sValues"][operand] = "";
            }
        }
        private JsonElement Job(int id)
        {
            foreach (var job in catalog.GetProperty("job_slots").EnumerateArray())
                if (job.GetProperty("original_job_id").GetInt32() == id) return job;
            throw new ArgumentException("Unknown native job.");
        }
        public static string JobName(int id)
        {
            using (var source = JsonDocument.Parse(NativeRewardData.CatalogJson))
                foreach (var job in source.RootElement.GetProperty("job_slots").EnumerateArray())
                    if (job.GetProperty("original_job_id").GetInt32() == id)
                        return job.GetProperty("original_name").GetString();
            throw new ArgumentException("Unknown native job.");
        }
        private static string MessageKey(int script, int instruction) => "JOB_FAIR_CRYSTAL_" + script + "_" + instruction;
    }
}
