// Immutable shop identities and stock pools. No native or Unity dependencies.
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace ProjectBlues.Runtime
{
    public sealed class ShopCatalog
    {
        private static ShopCatalog shared;
        internal static ShopCatalog Shared => shared ?? (shared = new ShopCatalog());
        private readonly JsonElement catalog;
        public JsonElement Shops => catalog.GetProperty("shops");
        public JsonElement Slots => catalog.GetProperty("slots");
        public JsonElement Products => catalog.GetProperty("products");
        public JsonElement Contents => catalog.GetProperty("contents");

        public ShopCatalog()
        {
            using (var document = JsonDocument.Parse(NativeShopData.CatalogJson))
                catalog = document.RootElement.Clone();
            if (catalog.GetProperty("catalog_id").GetString() != CampaignContract.ShopCatalog)
                throw new ArgumentException("Embedded shop catalog identity mismatch.");
        }

        public static bool Applies(RunMetadata run) => run != null && ((run.SchemaVersion == 2
            && (CampaignContract.IsMixedShopCatalog(run.CatalogId) || run.CatalogId == CampaignContract.RemixedJobSliceCatalog))
            || CampaignContract.UsesWorldTravel(run));

        public static void AddExpected(RunMetadata run, HashSet<string> expected)
        {
            if (!Applies(run)) return;
            var original = new List<string>();
            var shuffled = new List<string>();
            var seen = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
            var placements = new Dictionary<string, RewardPlacement>(StringComparer.Ordinal);
            foreach (var placement in run.Placements) placements.Add(placement.LocationId, placement);
            foreach (var slot in Shared.Slots.EnumerateArray())
            {
                string id = slot.GetProperty("id").GetString();
                if (!expected.Add(id)) throw new ArgumentException("Duplicate shop placement identity.");
                if (!placements.TryGetValue(id, out var placement) || placement.RewardKind != "shop" || placement.Quantity != 1)
                    throw new ArgumentException("Shop stock placement is missing or has invalid kind/quantity.");
                string prefix = slot.GetProperty("world").GetInt32() + ":" + slot.GetProperty("shop_type").GetString() + ":";
                original.Add(prefix + slot.GetProperty("content_id").GetInt32());
                shuffled.Add(prefix + placement.NumericRewardId);
                string shopId = slot.GetProperty("shop_id").GetString();
                if (!seen.TryGetValue(shopId, out var contents))
                    seen.Add(shopId, contents = new HashSet<int>());
                if (!contents.Add(placement.NumericRewardId))
                    throw new ArgumentException("Shop contains duplicate content; native selection would be ambiguous.");
            }
            original.Sort(StringComparer.Ordinal); shuffled.Sort(StringComparer.Ordinal);
            for (int i = 0; i < original.Count; i++)
                if (original[i] != shuffled[i])
                    throw new ArgumentException("Shop stock changed a world's original type-specific pool.");
        }

        public static void ValidateRun(RunMetadata run)
        {
            if (Applies(run)) AddExpected(run, new HashSet<string>(StringComparer.Ordinal));
        }

        public JsonElement Product(int id)
        {
            foreach (var product in Products.EnumerateArray())
                if (product.GetProperty("id").GetInt32() == id) return product;
            throw new InvalidOperationException("Uncatalogued native shop product.");
        }

        public JsonElement Content(int id)
        {
            if (Contents.TryGetProperty(id.ToString(System.Globalization.CultureInfo.InvariantCulture), out var content)) return content;
            throw new InvalidOperationException("Uncatalogued shop content.");
        }

        public bool IsRetailGroup(int group)
        {
            foreach (var product in Products.EnumerateArray())
                if (product.GetProperty("group_id").GetInt32() == group) return true;
            return false;
        }

        public bool TryResolve(int map, int group, bool merged, out JsonElement result)
        {
            result = default;
            foreach (var shop in Shops.EnumerateArray())
            {
                if (shop.GetProperty("map_id").GetInt32() != map || shop.GetProperty("group_id").GetInt32() != group) continue;
                if ((shop.GetProperty("world").GetInt32() == 3) != merged) continue;
                if (result.ValueKind != JsonValueKind.Undefined)
                    throw new InvalidOperationException("Ambiguous native shop/world identity.");
                result = shop;
            }
            return result.ValueKind != JsonValueKind.Undefined;
        }

        public bool TryResolveInWorld(int map, int group, int world, out JsonElement result)
        {
            if (world < 1 || world > 3) throw new ArgumentException("Unknown shop world.");
            result = default;
            foreach (var shop in Shops.EnumerateArray())
            {
                if (shop.GetProperty("map_id").GetInt32() != map || shop.GetProperty("group_id").GetInt32() != group
                    || shop.GetProperty("world").GetInt32() != world) continue;
                if (result.ValueKind != JsonValueKind.Undefined) throw new InvalidOperationException("Ambiguous native shop/world identity.");
                result = shop;
            }
            return result.ValueKind != JsonValueKind.Undefined;
        }
    }
}
