using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RoomHelper
{
    // Turns a rectangle + a RoomTemplateDef into a set of construction blueprints
    // (walls, a door, flooring and furniture). Everything placed here is a normal
    // blueprint owned by the player faction, so vanilla colonists build it with their
    // ordinary Construct / SmoothFloor jobs  no custom jobs or Harmony patches needed.
    public static class RoomPlanner
    {
        // Result of planning a room, used only to build a player-facing message.
        public struct PlanResult
        {
            public int walls;
            public int doors;
            public int floors;
            public int furniture;
            public int skippedFurniture;

            public int Total => walls + doors + floors + furniture;
        }

        // Main entry point. Lays out a room inside <rect> using <template> and pops a
        // message telling the player what got queued for construction.
        public static void PlaceRoom(CellRect rect, RoomTemplateDef template, Map map)
        {
            if (map == null || template == null)
            {
                return;
            }

            rect = rect.ClipInsideMap(map);
            if (rect.Width < 3 || rect.Height < 3)
            {
                Messages.Message("Room Helper: that area is too small for a room (needs at least 3x3).",
                    MessageTypeDefOf.RejectInput, false);
                return;
            }

            Faction faction = Faction.OfPlayer;
            var occupiedBuildings = new HashSet<IntVec3>();
            var result = new PlanResult();

            // --- 1. Walls + door around the perimeter ------------------------------
            ThingDef wallDef = ThingDefOf.Wall;
            ThingDef wallStuff = ResolveStuff(template.wallStuff, wallDef);

            IntVec3 doorCell = ChooseDoorCell(rect, map);
            foreach (IntVec3 cell in rect.EdgeCells)
            {
                if (cell == doorCell)
                {
                    continue;
                }
                if (TryPlaceBuilding(wallDef, cell, Rot4.North, wallStuff, map, faction, occupiedBuildings, requireHeavy: true))
                {
                    result.walls++;
                }
            }

            ThingDef doorDef = ThingDefOf.Door;
            ThingDef doorStuff = ResolveStuff(template.doorStuff, doorDef);
            if (TryPlaceBuilding(doorDef, doorCell, Rot4.North, doorStuff, map, faction, occupiedBuildings, requireHeavy: true))
            {
                result.doors++;
            }

            CellRect interior = rect.ContractedBy(1);

            // --- 2. Furniture (buildings on the interior) --------------------------
            //     Done before flooring so footprint checks see bare ground, not our
            //     own floor blueprints.
            CellRect? lastPlaced = null;
            foreach (FurnitureSpec spec in template.furniture)
            {
                ThingDef def = ResolveThing(spec);
                if (def == null)
                {
                    if (!spec.optional)
                    {
                        Log.Warning($"[Room Helper] Template '{template.defName}' wants a piece that could not be resolved: '{spec.thingDef}'.");
                    }
                    continue;
                }

                ThingDef stuff = def.MadeFromStuff
                    ? ResolveStuff(spec.stuff != null ? new List<string> { spec.stuff } : null, def)
                    : null;

                int placed = 0;
                foreach ((IntVec3 center, Rot4 rot) in CandidatePlacements(spec, def, interior, lastPlaced))
                {
                    if (placed >= spec.count)
                    {
                        break;
                    }

                    CellRect footprint = GenAdj.OccupiedRect(center, rot, def.size);
                    if (!FootprintFits(footprint, interior, map, occupiedBuildings, def))
                    {
                        continue;
                    }

                    GenConstruct.PlaceBlueprintForBuild(def, center, map, rot, faction, stuff);
                    foreach (IntVec3 c in footprint)
                    {
                        occupiedBuildings.Add(c);
                    }
                    lastPlaced = footprint;
                    placed++;
                    result.furniture++;
                }

                if (placed < spec.count && !spec.optional)
                {
                    result.skippedFurniture += spec.count - placed;
                }
            }

            // --- 3. Flooring across the interior -----------------------------------
            TerrainDef floor = template.floorDef.NullOrEmpty()
                ? null
                : DefDatabase<TerrainDef>.GetNamedSilentFail(template.floorDef);
            if (floor != null)
            {
                foreach (IntVec3 cell in interior)
                {
                    if (TryPlaceFloor(floor, cell, map, faction))
                    {
                        result.floors++;
                    }
                }
            }

            AnnounceResult(result, template);
        }

        // Tries to find an appropriate empty spot for a default-sized room near
        // <near>, spiralling outward. This is the "let a colonist decide where it
        // goes" behaviour: the player points at a rough area and the mod picks the
        // exact location that actually fits.
        public static bool TryFindPlacement(IntVec3 near, RoomTemplateDef template, Map map, out CellRect rect)
        {
            int w = Mathf.Max(template.defaultWidth, template.minWidth);
            int h = Mathf.Max(template.defaultHeight, template.minHeight);
            rect = default;

            const int maxRadius = 30;
            for (int radius = 0; radius <= maxRadius; radius++)
            {
                foreach (IntVec3 offset in RingOffsets(radius))
                {
                    IntVec3 origin = near + offset;
                    CellRect candidate = new CellRect(origin.x - w / 2, origin.z - h / 2, w, h);
                    if (IsRectClearForRoom(candidate, map))
                    {
                        rect = candidate;
                        return true;
                    }
                }
            }
            return false;
        }

        // ------------------------------------------------------------------ helpers

        private static void AnnounceResult(PlanResult r, RoomTemplateDef template)
        {
            if (r.Total == 0)
            {
                Messages.Message(
                    $"Room Helper could not place any blueprints for the {template.label} here  the ground may be blocked or unbuildable.",
                    MessageTypeDefOf.RejectInput, false);
                return;
            }

            string msg = $"Room Helper queued a {template.label}: {r.walls} walls, {r.doors} door, {r.furniture} furnishings"
                         + (r.floors > 0 ? $", {r.floors} floor tiles" : string.Empty)
                         + ". Your colonists will build it.";
            Messages.Message(msg, MessageTypeDefOf.TaskCompletion, false);
        }

        private static ThingDef ResolveThing(FurnitureSpec spec)
        {
            foreach (string name in spec.CandidateDefNames())
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(name);
                if (def != null)
                {
                    return def;
                }
            }
            return null;
        }

        // Picks the first listed material that exists and is actually a stuff usable
        // for <forDef>; otherwise falls back to the game's default material.
        private static ThingDef ResolveStuff(List<string> candidates, ThingDef forDef)
        {
            if (forDef == null || !forDef.MadeFromStuff)
            {
                return null;
            }
            if (candidates != null)
            {
                foreach (string name in candidates)
                {
                    ThingDef stuff = DefDatabase<ThingDef>.GetNamedSilentFail(name);
                    if (stuff != null && stuff.IsStuff && stuff.stuffProps != null
                        && GenStuff.AllowedStuffsFor(forDef).Contains(stuff))
                    {
                        return stuff;
                    }
                }
            }
            return GenStuff.DefaultStuffFor(forDef);
        }

        // Places a single building blueprint if the footprint (here always 1 cell for
        // walls/doors) is clear. Returns true if a blueprint was created.
        private static bool TryPlaceBuilding(ThingDef def, IntVec3 cell, Rot4 rot, ThingDef stuff, Map map,
            Faction faction, HashSet<IntVec3> occupied, bool requireHeavy)
        {
            if (!CellClearForBuilding(cell, map) || occupied.Contains(cell))
            {
                return false;
            }
            if (requireHeavy && !TerrainSupports(cell, map, TerrainAffordanceDefOf.Heavy))
            {
                return false;
            }

            GenConstruct.PlaceBlueprintForBuild(def, cell, map, rot, faction, stuff);
            occupied.Add(cell);
            return true;
        }

        private static bool TryPlaceFloor(TerrainDef floor, IntVec3 cell, Map map, Faction faction)
        {
            if (!cell.InBounds(map) || cell.Fogged(map))
            {
                return false;
            }
            TerrainDef current = cell.GetTerrain(map);
            if (current == null || current == floor)
            {
                return false;
            }
            // Don't try to floor over rock, water or other non-buildable ground. The
            // affordance check below also rejects water (it lacks the needed support).
            if (current.passability == Traversability.Impassable)
            {
                return false;
            }
            if (floor.terrainAffordanceNeeded != null
                && (current.affordances == null || !current.affordances.Contains(floor.terrainAffordanceNeeded)))
            {
                return false;
            }

            GenConstruct.PlaceBlueprintForBuild(floor, cell, map, Rot4.North, faction, null);
            return true;
        }

        // A cell is clear for a new building if it is on the map, revealed, has no
        // edifice (wall/rock/existing building) and no blueprint/frame/impassable
        // thing already sitting on it.
        private static bool CellClearForBuilding(IntVec3 cell, Map map)
        {
            if (!cell.InBounds(map) || cell.Fogged(map))
            {
                return false;
            }
            if (cell.GetEdifice(map) != null)
            {
                return false;
            }
            List<Thing> things = map.thingGrid.ThingsListAtFast(cell);
            for (int i = 0; i < things.Count; i++)
            {
                ThingDef d = things[i].def;
                if (d.IsBlueprint || d.IsFrame || d.passability == Traversability.Impassable)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool FootprintFits(CellRect footprint, CellRect interior, Map map,
            HashSet<IntVec3> occupied, ThingDef def)
        {
            foreach (IntVec3 c in footprint)
            {
                if (!interior.Contains(c) || occupied.Contains(c))
                {
                    return false;
                }
                if (!CellClearForBuilding(c, map))
                {
                    return false;
                }
                if (def.terrainAffordanceNeeded != null && !TerrainSupports(c, map, def.terrainAffordanceNeeded))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool TerrainSupports(IntVec3 cell, Map map, TerrainAffordanceDef affordance)
        {
            TerrainDef t = cell.GetTerrain(map);
            return t != null && t.affordances != null && t.affordances.Contains(affordance);
        }

        // Whole rectangle must be on-map, revealed, on heavy-buildable ground and
        // completely free of edifices/blueprints. Used by auto-placement.
        private static bool IsRectClearForRoom(CellRect rect, Map map)
        {
            if (rect.minX < 0 || rect.minZ < 0 || rect.maxX >= map.Size.x || rect.maxZ >= map.Size.z)
            {
                return false;
            }
            foreach (IntVec3 c in rect)
            {
                if (c.Fogged(map) || !CellClearForBuilding(c, map) || !TerrainSupports(c, map, TerrainAffordanceDefOf.Heavy))
                {
                    return false;
                }
            }
            return true;
        }

        // Picks a perimeter cell (never a corner) for the door, preferring one whose
        // outward neighbour is standable open ground so colonists can actually reach it.
        private static IntVec3 ChooseDoorCell(CellRect rect, Map map)
        {
            IntVec3 fallback = new IntVec3(rect.CenterCell.x, 0, rect.minZ);
            IntVec3 best = fallback;
            int bestScore = int.MinValue;

            foreach (IntVec3 cell in rect.EdgeCells)
            {
                if (IsCorner(cell, rect))
                {
                    continue;
                }
                IntVec3 outward = OutwardCell(cell, rect);
                int score = 0;
                if (outward.InBounds(map) && outward.Standable(map))
                {
                    score += 100;
                }
                // Prefer the south edge so doors tend to face "downward" toward the base.
                if (cell.z == rect.minZ)
                {
                    score += 10;
                }
                if (score > bestScore)
                {
                    bestScore = score;
                    best = cell;
                }
            }
            return best;
        }

        private static bool IsCorner(IntVec3 c, CellRect rect)
        {
            return (c.x == rect.minX || c.x == rect.maxX) && (c.z == rect.minZ || c.z == rect.maxZ);
        }

        // The cell just outside the rectangle from an edge cell.
        private static IntVec3 OutwardCell(IntVec3 c, CellRect rect)
        {
            if (c.z == rect.minZ)
            {
                return c + IntVec3.South;
            }
            if (c.z == rect.maxZ)
            {
                return c + IntVec3.North;
            }
            if (c.x == rect.minX)
            {
                return c + IntVec3.West;
            }
            return c + IntVec3.East;
        }

        // Yields candidate (center, rotation) pairs for a furniture piece, ordered by
        // the spec's placement strategy.
        private static IEnumerable<(IntVec3, Rot4)> CandidatePlacements(FurnitureSpec spec, ThingDef def,
            CellRect interior, CellRect? lastPlaced)
        {
            Rot4 baseRot = def.rotatable ? new Rot4(spec.rotation) : Rot4.North;

            switch (spec.placement)
            {
                case RoomPlacement.AdjacentToLast when lastPlaced.HasValue:
                    foreach (var pair in AdjacentPlacements(lastPlaced.Value, interior, def))
                    {
                        yield return pair;
                    }
                    break;

                case RoomPlacement.Corner:
                    foreach (IntVec3 corner in CornerCells(interior))
                    {
                        yield return (corner, baseRot);
                    }
                    break;

                case RoomPlacement.Center:
                    foreach (IntVec3 c in interior.Cells.OrderBy(c => (c - interior.CenterCell).LengthHorizontalSquared))
                    {
                        yield return (c, baseRot);
                    }
                    break;

                case RoomPlacement.AlongWall:
                    foreach (var pair in AlongWallPlacements(interior, def))
                    {
                        yield return pair;
                    }
                    break;

                default:
                    foreach (IntVec3 c in interior)
                    {
                        yield return (c, baseRot);
                    }
                    break;
            }
        }

        private static IEnumerable<IntVec3> CornerCells(CellRect r)
        {
            yield return new IntVec3(r.minX, 0, r.maxZ);
            yield return new IntVec3(r.maxX, 0, r.maxZ);
            yield return new IntVec3(r.minX, 0, r.minZ);
            yield return new IntVec3(r.maxX, 0, r.minZ);
        }

        // Interior-edge cells with a rotation facing away from the nearest wall, so
        // beds/benches sit with their backs to the wall.
        private static IEnumerable<(IntVec3, Rot4)> AlongWallPlacements(CellRect interior, ThingDef def)
        {
            foreach (IntVec3 c in interior)
            {
                if (c.z == interior.maxZ)
                {
                    yield return (c, Rot4.South);
                }
                else if (c.z == interior.minZ)
                {
                    yield return (c, Rot4.North);
                }
                else if (c.x == interior.minX)
                {
                    yield return (c, Rot4.East);
                }
                else if (c.x == interior.maxX)
                {
                    yield return (c, Rot4.West);
                }
            }
        }

        // Cells orthogonally adjacent to the last-placed footprint, each facing back
        // toward it (chairs pulled up to a table).
        private static IEnumerable<(IntVec3, Rot4)> AdjacentPlacements(CellRect last, CellRect interior, ThingDef def)
        {
            for (int x = last.minX; x <= last.maxX; x++)
            {
                yield return (new IntVec3(x, 0, last.maxZ + 1), Rot4.South);
                yield return (new IntVec3(x, 0, last.minZ - 1), Rot4.North);
            }
            for (int z = last.minZ; z <= last.maxZ; z++)
            {
                yield return (new IntVec3(last.maxX + 1, 0, z), Rot4.West);
                yield return (new IntVec3(last.minX - 1, 0, z), Rot4.East);
            }
        }

        // Cells on the ring exactly <radius> away (Chebyshev) from the origin.
        private static IEnumerable<IntVec3> RingOffsets(int radius)
        {
            if (radius == 0)
            {
                yield return IntVec3.Zero;
                yield break;
            }
            for (int x = -radius; x <= radius; x++)
            {
                for (int z = -radius; z <= radius; z++)
                {
                    if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) == radius)
                    {
                        yield return new IntVec3(x, 0, z);
                    }
                }
            }
        }
    }
}
