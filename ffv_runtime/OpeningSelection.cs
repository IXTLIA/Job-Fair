using System;

namespace ProjectBlues.Runtime
{
    // Both native entry paths use the same catalog-specific startup decision.
    public static class OpeningSelection
    {
        public static string Select(RunMetadata run, ModeOpening mixed, RemixedOpening remixed)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            CampaignContract.ValidatePlayable(run);
            if (run.SchemaVersion == 1) return null;
            if (run.SchemaVersion == 2 && CampaignContract.IsMixedMode(run.Mode)) return mixed.MixedJson;
            if (run.SchemaVersion == 3 && CampaignContract.IsPlayableRemixedCatalog(run.CatalogId)) return remixed.Json;
            throw new InvalidOperationException("This run has no supported opening.");
        }
    }
}
