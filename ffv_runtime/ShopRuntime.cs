// Transform fresh native shop rows; master tables and cached stock remain native.
using System;
using System.Globalization;
using System.Text.Json;
using HarmonyLib;
using Last.Data;
using Last.Data.Master;
using Last.Interpreter;
using Last.Systems;
using NativeShopList = Il2CppSystem.Collections.Generic.List<Last.Data.ShopProductData>;
using ShopContentType = Last.Defaine.Content.ContentType;

namespace ProjectBlues.Runtime
{
    internal static class ShopRuntime
    {
        private static ShopCatalog catalog;
        // Holding the native list prevents pointer reuse while its UI layout is
        // owned. Only one shop can be open; the next request releases this state.
        private static NativeShopList ownedList;
        private static string ownedRunId, ownedPlacementHash;
        private static int ownedWorld;
        private static ShopContentType ownedContentType;
        internal static void Initialize() { catalog = ShopCatalog.Shared; ClearUiOwnership(); }
        internal static void ValidateRun(RunMetadata run) { ShopCatalog.ValidateRun(run); }

        private static void ClearUiOwnership()
        { ownedList = null; ownedRunId = null; ownedPlacementHash = null; ownedWorld = 0; }

        internal static bool TryGetContentType(NativeShopList list, out ShopContentType type)
        {
            type = default;
            var run = CampaignRuntime.EventRun;
            if (!ShopCatalog.Applies(run) || run.RunId != ownedRunId || run.PlacementHash != ownedPlacementHash
                || (CampaignContract.UsesWorldTravel(run) && run.WorldState.ActiveWorld != ownedWorld))
            { ClearUiOwnership(); return false; }
            if (list == null || ownedList == null || list.Pointer != ownedList.Pointer) return false;
            type = ownedContentType;
            return true;
        }

        internal static NativeShopList Prepare(int group, NativeShopList original)
        {
            ClearUiOwnership();
            var run = CampaignRuntime.EventRun;
            if (!ShopCatalog.Applies(run)) return original;
            if (RunSession.Loading != null) throw new InvalidOperationException("Shop interaction during unfinished load.");
            if (catalog == null) throw new InvalidOperationException("Shop catalog unavailable.");
            if (!catalog.IsRetailGroup(group)) return original; // Native inns and other services.
            var storage = DataStorage.Instance;
            if (storage == null) throw new InvalidOperationException("Shop world context unavailable.");
            JsonElement shop;
            bool resolved = CampaignContract.UsesWorldTravel(run)
                ? catalog.TryResolveInWorld(storage.currentMapId, group, run.WorldState.ActiveWorld, out shop)
                : catalog.TryResolve(storage.currentMapId, group, storage.Get(DataStorage.Category.kScenarioFlag1, 125) != 0, out shop);
            if (!resolved)
                throw new InvalidOperationException("Retail shop does not match a pinned map/group/world binding.");
            ShopCatalog.ValidateRun(run);
            var ids = shop.GetProperty("product_ids");
            var slots = shop.GetProperty("slot_ids");
            if (original == null || original.Count != ids.GetArrayLength() || ids.GetArrayLength() != slots.GetArrayLength())
                throw new InvalidOperationException("Native shop stock count changed.");
            var manager = MasterManager.Instance;
            if (manager == null) throw new InvalidOperationException("Shop master data unavailable.");
            var products = manager.GetList<Product>();
            var contents = manager.GetList<Content>();
            if (products == null || contents == null) throw new InvalidOperationException("Shop master tables unavailable.");
            var result = new NativeShopList();
            var seen = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < original.Count; i++)
            {
                var source = original[i];
                if (source == null || !seen.Add(source.ProductId))
                    throw new InvalidOperationException("Native shop contains a null or duplicate product row.");
                int index = -1;
                for (int j = 0; j < ids.GetArrayLength(); j++)
                    if (ids[j].GetInt32() == source.ProductId) index = j;
                if (index < 0 || !products.ContainsKey(source.ProductId))
                    throw new InvalidOperationException("Native shop product identity changed.");
                var pinned = catalog.Product(source.ProductId);
                var product = products[source.ProductId];
                int nativeContent = pinned.GetProperty("content_id").GetInt32();
                int coefficient = pinned.GetProperty("coefficient").GetInt32();
                int limit = pinned.GetProperty("purchase_limit").GetInt32();
                if (product == null || product.Id != source.ProductId || product.GroupId != group
                    || product.ContentId != nativeContent || product.Coefficient != coefficient || product.PurchaseLimit != limit)
                    throw new InvalidOperationException("Native product master row changed.");
                if (!contents.ContainsKey(nativeContent)) throw new InvalidOperationException("Native shop content missing.");
                ValidateContent(contents[nativeContent], catalog.Content(nativeContent));
                ValidateRow(source, source.ProductId, contents[nativeContent], coefficient, limit);
                var placement = run.FindPlacement(slots[index].GetString());
                if (placement == null || !contents.ContainsKey(placement.NumericRewardId))
                    throw new InvalidOperationException("Saved shop placement or target content missing.");
                var content = contents[placement.NumericRewardId];
                ValidateContent(content, catalog.Content(placement.NumericRewardId));
                // The native coefficient is an absolute addition. Retaining a
                // source discount could make a different cheap item negative.
                string csv = source.ProductId.ToString(CultureInfo.InvariantCulture) + ","
                    + content.Id.ToString(CultureInfo.InvariantCulture) + ","
                    + group.ToString(CultureInfo.InvariantCulture) + ",0," + limit.ToString(CultureInfo.InvariantCulture);
                var clone = new Product(csv);
                var replacement = ShopUtility.CreateProductData(clone, content);
                ValidateRow(replacement, source.ProductId, content, 0, limit);
                result.Add(replacement);
            }
            Plugin.Logger.LogInfo("Mixed shop prepared: " + shop.GetProperty("id").GetString() + ", seed " + run.Seed);
            // Native layout depends on the first row, including a special
            // throwing-item type. Preserve the native merchant's layout when
            // a shuffled weapon list starts with a throwing item.
            ownedContentType = ShopUtility.GetContentType(original);
            ownedRunId = run.RunId;
            ownedPlacementHash = run.PlacementHash;
            ownedWorld = shop.GetProperty("world").GetInt32();
            ownedList = result;
            return result;
        }

        private static void ValidateContent(Content content, JsonElement pinned)
        {
            if (content == null || content.Id != pinned.GetProperty("id").GetInt32()
                || content.TypeId != pinned.GetProperty("type_id").GetInt32()
                || content.TypeValue != pinned.GetProperty("type_value").GetInt32()
                || content.MesIdName != pinned.GetProperty("mes_id_name").GetString()
                || content.MesIdBattle != pinned.GetProperty("mes_id_battle").GetString()
                || content.MesIdDescription != pinned.GetProperty("mes_id_description").GetString()
                || content.IconId != pinned.GetProperty("icon_id").GetInt32())
                throw new InvalidOperationException("Native shop content master row changed.");
        }

        private static void ValidateRow(ShopProductData row, int productId, Content content, int coefficient, int limit)
        {
            var price = catalog.Content(content.Id).GetProperty("price_master");
            int buy = checked(price.GetProperty("buy").GetInt32() + coefficient);
            int sell = price.GetProperty("sell").GetInt32();
            if (buy <= 0 || row == null || row.ProductId != productId || row.ContentId != content.Id
                || (int)row.ContentType != content.TypeId || row.ContentValue != content.TypeValue
                || row.Buy != buy || row.Sell != sell || row.Limit != limit)
                throw new InvalidOperationException("Native shop row content, price, or limit changed.");
        }
    }

    [HarmonyPatch(typeof(ShopUtility), nameof(ShopUtility.GetProducts), new Type[] { typeof(int) })]
    internal static class ShopProductsPatch
    {
        private static void Postfix(int __0, ref NativeShopList __result)
        {
            try { __result = ShopRuntime.Prepare(__0, __result); }
            catch (Exception exception)
            {
                // Never expose the original stock after a randomized-run guard fails.
                __result = new NativeShopList();
                Plugin.Logger.LogError(exception);
                SetupView.Fail("Job Fair could not prepare this shop. Please close FFV and report the log.");
            }
        }
    }

    [HarmonyPatch(typeof(ShopUtility), nameof(ShopUtility.GetContentType), new Type[] { typeof(NativeShopList) })]
    internal static class ShopContentTypePatch
    {
        private static bool Prefix(NativeShopList __0, ref ShopContentType __result)
        {
            if (!ShopRuntime.TryGetContentType(__0, out var type)) return true;
            __result = type;
            return false;
        }
    }
}
