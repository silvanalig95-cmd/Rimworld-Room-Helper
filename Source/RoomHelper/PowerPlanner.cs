using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RoomHelper
{
    // Runs power to a finished room. A room full of unpowered lamps and a cold stove
    // is the most obvious sign a base was laid out by something that wasn't thinking
    // about the whole colony, so once a room's interior is down the architect traces
    // a conduit back to whatever grid the colony already has.
    //
    // Deliberately conservative: if the colony has no electricity at all yet, this
    // does nothing rather than proposing a generator the player hasn't researched or
    // can't afford.
    public static class PowerPlanner
    {
        // Anything that is, or carries, a power grid. Order doesn't matter  we take
        // whichever instance is physically nearest.
        private static readonly string[] GridDefNames =
        {
            "PowerConduit",
            "HiddenConduit",
            "Battery",
            "SolarGenerator",
            "WindTurbine",
            "WoodFiredGenerator",
            "ChemfuelPoweredGenerator",
            "GeothermalGenerator",
            "WatermillGenerator",
            "ToxifierGenerator",
            "BioferriteGenerator"
        };

        // Traces a conduit from inside <rect> to the nearest bit of existing grid.
        // Returns how many conduit blueprints were placed (0 if there's no grid yet,
        // or the room is already connected).
        public static int ConnectToGrid(CellRect rect, Map map)
        {
            if (map == null || !RoomHelperMod.Settings.planPower)
            {
                return 0;
            }

            ThingDef conduit = DefDatabase<ThingDef>.GetNamedSilentFail("PowerConduit");
            if (conduit == null)
            {
                return 0;
            }

            IntVec3 from = rect.CenterCell;
            if (!TryFindNearestGridCell(map, from, out IntVec3 target))
            {
                // No electricity anywhere yet  nothing to connect to.
                return 0;
            }

            // Already reaching us? Then there's nothing to do.
            if (rect.ExpandedBy(1).Contains(target))
            {
                return 0;
            }

            int placed = 0;
            Faction faction = Faction.OfPlayer;

            foreach (IntVec3 cell in PathCells(from, target))
            {
                if (!cell.InBounds(map) || cell.Fogged(map))
                {
                    continue;
                }
                if (HasConduit(map, cell, conduit))
                {
                    continue;
                }
                if (!GenConstruct.CanPlaceBlueprintAt(conduit, cell, Rot4.North, map, false, null, null, null).Accepted)
                {
                    continue;
                }

                GenConstruct.PlaceBlueprintForBuild(conduit, cell, map, Rot4.North, faction, null);
                placed++;
            }

            return placed;
        }

        // True if the colony has any powered infrastructure at all.
        public static bool ColonyHasGrid(Map map)
        {
            return map != null && TryFindNearestGridCell(map, map.Center, out _);
        }

        private static bool TryFindNearestGridCell(Map map, IntVec3 from, out IntVec3 nearest)
        {
            nearest = IntVec3.Invalid;
            float bestDist = float.MaxValue;
            bool found = false;

            foreach (string defName in GridDefNames)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                if (def == null)
                {
                    continue;
                }

                List<Thing> things = map.listerThings.ThingsOfDef(def);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing t = things[i];
                    if (t.Faction != Faction.OfPlayer)
                    {
                        continue;
                    }
                    float d = IntVec3Utility.DistanceTo(t.Position, from);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        nearest = t.Position;
                        found = true;
                    }
                }
            }

            return found;
        }

        private static bool HasConduit(Map map, IntVec3 cell, ThingDef conduit)
        {
            List<Thing> things = map.thingGrid.ThingsListAtFast(cell);
            for (int i = 0; i < things.Count; i++)
            {
                Thing t = things[i];
                if (t.def == conduit)
                {
                    return true;
                }
                // A blueprint or frame for one counts too  don't queue it twice.
                if ((t.def.IsBlueprint || t.def.IsFrame) && t.def.entityDefToBuild == conduit)
                {
                    return true;
                }
            }
            return false;
        }

        // An L-shaped run: along x, then along z. Simple, predictable, and easy for the
        // player to reroute by hand afterwards.
        private static IEnumerable<IntVec3> PathCells(IntVec3 from, IntVec3 to)
        {
            int x = from.x;
            int z = from.z;

            int stepX = to.x > x ? 1 : -1;
            while (x != to.x)
            {
                yield return new IntVec3(x, 0, z);
                x += stepX;
            }

            int stepZ = to.z > z ? 1 : -1;
            while (z != to.z)
            {
                yield return new IntVec3(x, 0, z);
                z += stepZ;
            }

            yield return new IntVec3(x, 0, z);
        }
    }
}
