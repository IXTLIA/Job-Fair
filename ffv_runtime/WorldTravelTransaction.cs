// Portable ownership protocol. Native destinations come only from the pinned catalog.
using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProjectBlues.Runtime
{
    internal sealed class WorldTravelAnchor
    {
        internal int World, Map, Point, X, Y, Vehicle, View, DirectionIndex;
        internal bool Background;
        internal static WorldTravelAnchor ForWorld(int world)
        {
            using (var sha = SHA256.Create())
                if (Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(NativeWorldTravelData.CatalogJson))).ToLowerInvariant()
                    != NativeWorldTravelData.CatalogJsonSha256)
                    throw new InvalidOperationException("World travel catalog differs from its source pin.");
            using (var doc = JsonDocument.Parse(NativeWorldTravelData.CatalogJson))
            {
                if (doc.RootElement.GetProperty("catalog_id").GetString() != CampaignContract.WorldTravelCatalog)
                    throw new InvalidOperationException("Wrong world travel catalog.");
                foreach (var row in doc.RootElement.GetProperty("anchors").EnumerateArray())
                {
                    if (row.GetProperty("world").GetInt32() != world) continue;
                    var a = new WorldTravelAnchor { World = world, Map = row.GetProperty("map_id").GetInt32(),
                        Point = row.GetProperty("point_id").GetInt32(), X = row.GetProperty("x").GetInt32(), Y = row.GetProperty("y").GetInt32(),
                        Vehicle = row.GetProperty("vehicle_id").GetInt32(), View = row.GetProperty("expected_view").GetInt32(),
                        Background = row.GetProperty("board_background").GetBoolean() };
                    var command = row.GetProperty("set_vehicle_command"); var values = command.GetProperty("operands").GetProperty("iValues");
                    if (command.GetProperty("mnemonic").GetString() != "SetVehicle" || values.GetArrayLength() != 8
                        || values[0].GetInt32() != a.Vehicle || values[1].GetInt32() != a.Map || values[2].GetInt32() != a.X
                        || values[3].GetInt32() != a.Y || values[4].GetInt32() != 0 || values[5].GetInt32() != 3
                        || values[6].GetInt32() != 0 || values[7].GetInt32() != 0 || WorldFromMap(a.Map) != world)
                        throw new InvalidOperationException("World arrival differs from its pinned transport operation.");
                    a.DirectionIndex = values[5].GetInt32(); return a;
                }
            }
            throw new ArgumentException("Unknown world destination.", nameof(world));
        }
        // Surface/underwater boundaries share owned world identity, but this
        // slice only dispatches the Warp Shard from the three surface maps.
        internal static int WorldFromMap(int map) => map == 1 ? 1 : map == 12 || map == 18 ? 2 : map == 19 || map == 29 ? 3 : 0;
        internal static bool IsSurfaceMap(int map) => map == 1 || map == 12 || map == 19;
    }

    internal sealed class WorldTravelTransaction
    {
        internal enum Stage { Menu, Dispatched, AwaitingControl, Failed, Committed, Cancelled }
        internal readonly RunMetadata Run;
        internal readonly int SourceMap;
        internal readonly long FieldIdentity;
        internal long StartedAt { get; private set; }
        internal Stage Phase { get; private set; }
        internal WorldTravelAnchor Target { get; private set; }
        internal RunMetadata ArrivedRun { get; private set; }
        internal WorldTravelTransaction(RunMetadata run, int sourceMap, long fieldIdentity, long now)
        {
            if (!CampaignContract.UsesWorldTravel(run) || !WorldTravelAnchor.IsSurfaceMap(sourceMap)
                || WorldTravelAnchor.WorldFromMap(sourceMap) != run.WorldState.ActiveWorld
                || fieldIdentity == 0) throw new InvalidOperationException("Travel requires an owned overworld context.");
            Run = run; SourceMap = sourceMap; FieldIdentity = fieldIdentity; StartedAt = now; Phase = Stage.Menu;
        }
        internal void RequireOwner(RunMetadata run, long fieldIdentity)
        {
            if (!Object.ReferenceEquals(Run, run) || FieldIdentity != fieldIdentity || Phase == Stage.Failed)
                throw new InvalidOperationException("World travel ownership changed.");
        }
        internal void Dispatch(int targetWorld, long now)
        {
            if (Phase != Stage.Menu || !WorldTravelState.CanEnter(Run, targetWorld))
                throw new InvalidOperationException("This world is current or its key has not been found.");
            Target = WorldTravelAnchor.ForWorld(targetWorld);
            ArrivedRun = Run.WithActiveWorld(targetWorld); // Allocate before native mutation.
            StartedAt = now;
            Phase = Stage.Dispatched;
        }
        internal void Arrive(int actualMap)
        {
            if (Phase != Stage.Dispatched || Target.Map != actualMap)
                throw new InvalidOperationException("Native arrival was unexpected.");
            Phase = Stage.AwaitingControl;
        }
        internal RunMetadata Commit(int map, int vehicle, int stayMap, int view, float x, float y, bool nativeReady)
        {
            if (Phase != Stage.AwaitingControl || !nativeReady || map != Target.Map || vehicle != Target.Vehicle
                || stayMap != Target.Map || view != Target.View || !Near(x, Target.X) || !Near(y, Target.Y))
                throw new InvalidOperationException("Native arrival and transport were not verified.");
            Phase = Stage.Committed; return ArrivedRun;
        }
        internal static bool Near(float actual, int expected) => !Single.IsNaN(actual) && !Single.IsInfinity(actual) && Math.Abs(actual - expected) < 0.05f;
        internal void Cancel()
        {
            if (Phase != Stage.Menu) throw new InvalidOperationException("A dispatched transition cannot be discarded.");
            Phase = Stage.Cancelled;
        }
        internal void Fail() { Phase = Stage.Failed; }
    }
}
