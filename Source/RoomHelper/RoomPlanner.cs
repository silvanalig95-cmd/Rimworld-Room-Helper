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
    // ordinary Construct / Mine jobs  no custom jobs or Harmony patches needed.
    //
    // The work is split into a shell pass (walls + door) and an interior pass
    // (flooring + furniture) so the base architect can run them at different times:
    // a room carved into rock can't be furnished until the rock has actually been
    // mined out, which may be hours of colonist work after the plan is approved.
    public static class RoomPlanner
    {
        // Tally of what a pass placed, used to build a player-facing message.
        public struct PlanResult
        {
            public int walls;
            public int doors;
            public int floors;
            public int furniture;

            public int Total => walls + doors + floors + furniture;

            public void Add(PlanResult other)
            {
                walls += other.walls;
                doors += other.doors;
                floors += other.floors;
                furniture += other.furniture;
            }
        }

        // Manual entry point used by the Architect-menu designators: lay out the
        // whole room in one go and tell the player what happened.
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

            IntVec3 doorCell = ChooseDoorCell(rect, map);
            PlanResult result = PlaceShell(rect, template, map, new[] { doorCell });
            result.Add(PlaceInterior(rect, template, map));
            AddToHomeArea(rect, map);

            AnnounceResult(result, template);
        }

        // Walls around the perimeter, with a door at each of <doorCells>. Cells that
        // already hold something (including natural rock, which serves as the wall of a
        // mountain room) are skipped, so this is safe to call repeatedly as a room gets
        // dug out.
        //
        // More than one door matters once rooms share walls: besides the way in from
        // outside, each wall shared with a neighbouring room gets a connecting door, so
        // the base reads as one building instead of a terrace of sheds.
        public static PlanResult PlaceShell(CellRect rect, RoomTemplateDef template, Map map,
            ICollection<IntVec3> doorCells)
        {
            var result = new PlanResult();
            Faction faction = Faction.OfPlayer;

            ThingDef wallDef = ThingDefOf.Wall;
            ThingDef wallStuff = ResolveStuff(template.wallStuff, wallDef);

            foreach (IntVec3 cell in rect.EdgeCells)
            {
                if (doorCells.Contains(cell))
                {
                    continue;
                }
                if (TryPlaceBuilding(wallDef, cell, Rot4.North, wallStuff, map, faction))
                {
                    result.walls++;
                }
            }

            ThingDef doorDef = ThingDefOf.Door;
            ThingDef doorStuff = ResolveStuff(template.doorStuff, doorDef);
            foreach (IntVec3 cell in doorCells)
            {
                bool onEdge = cell.x == rect.minX || cell.x == rect.maxX
                              || cell.z == rect.minZ || cell.z == rect.maxZ;
                if (!onEdge || !cell.InBounds(map))
                {
                    continue;
                }
                // A neighbouring room planned earlier may already have queued a wall
                // here. Cancelling an un-built blueprint costs nothing and is what lets
                // two rooms planned at different times still end up connected.
                ClearWallBlueprintAt(map, cell);
                if (TryPlaceBuilding(doorDef, cell, Rot4.North, doorStuff, map, faction))
                {
                    result.doors++;
                }
            }

            return result;
        }

        // Cancels an un-built building blueprint at <cell> so a door can go there.
        // Only blueprints are touched  anything already constructed is left alone.
        private static void ClearWallBlueprintAt(Map map, IntVec3 cell)
        {
            Thing[] snapshot = map.thingGrid.ThingsListAtFast(cell).ToArray();
            foreach (Thing t in snapshot)
            {
                if (t.Destroyed)
                {
                    continue;
                }
                // Floor blueprints build a TerrainDef; leave those be.
                if (t.def.IsBlueprint && t.def.entityDefToBuild is ThingDef)
                {
                    t.Destroy(DestroyMode.Cancel);
                }
            }
        }

        // Flooring and furniture inside the room. Assumes the interior is already
        // clear (the architect waits for mining to finish before calling this).
        public static PlanResult PlaceInterior(CellRect rect, RoomTemplateDef template, Map map)
        {
            var result = new PlanResult();
            Faction faction = Faction.OfPlayer;
            CellRect interior = rect.ContractedBy(1);
            var occupied = new HashSet<IntVec3>();

            // Furniture first, so footprint checks see bare ground rather than our own
            // floor blueprints.
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

                ThingDef stuff = ResolveStuff(spec.stuff != null ? new List<string> { spec.stuff } : null, def);

                int placed = 0;
                foreach (var pair in CandidatePlacements(spec, def, interior, lastPlaced))
                {
                    if (placed >= spec.count)
                    {
                        break;
                    }

                    IntVec3 center = pair.Item1;
                    Rot4 rot = pair.Item2;

                    CellRect footprint = GenAdj.OccupiedRect(center, rot, def.size);
                    if (!FootprintFits(footprint, interior, map, occupied, def))
                    {
                        continue;
                    }
                    // Let the game have the final say  it also checks interaction
                    // cells, edge areas and anything else we haven't thought of.
                    if (!GenConstruct.CanPlaceBlueprintAt(def, center, rot, map, false, null, null, stuff).Accepted)
                    {
                        continue;
                    }

                    GenConstruct.PlaceBlueprintForBuild(def, center, map, rot, faction, stuff);
                    foreach (IntVec3 c in footprint)
                    {
                        occupied.Add(c);
                    }
                    lastPlaced = footprint;
                    placed++;
                    result.furniture++;
                }
            }

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

            return result;
        }

        // Marks the room as part of the colony's home area so colonists clean, repair
        // and fight fires there.
        public static void AddToHomeArea(CellRect rect, Map map)
        {
            Area home = map.areaManager.Home;
            if (home == null)
            {
                return;
            }
            foreach (IntVec3 c in rect)
            {
                if (c.InBounds(map))
                {
                    home[c] = true;
                }
            }
        }

        // Tries to find an appropriate empty spot for a default-sized room near
        // <near>, spiralling outward. Used by the manual "auto-place" designator;
        // the base architect uses SiteScorer instead, which also weighs adjacency
        // and mountain-vs-open.
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

        // True if the thing must be built from a material (stuff). RimWorld 1.6 has no
        // ThingDef.MadeFromStuff; a def is stuffable when it declares stuff categories.
        private static bool IsStuffable(BuildableDef d)
        {
            return d != null && d.stuffCategories != null && d.stuffCategories.Count > 0;
        }

        // Picks the first listed material that exists and is a valid stuff; otherwise
        // falls back to the game's default material for the thing.
        public static ThingDef ResolveStuff(List<string> candidates, ThingDef forDef)
        {
            if (!IsStuffable(forDef))
            {
                return null;
            }
            if (candidates != null)
            {
                foreach (string name in candidates)
                {
                    ThingDef stuff = DefDatabase<ThingDef>.GetNamedSilentFail(name);
                    if (stuff != null && stuff.IsStuff)
                    {
                        return stuff;
                    }
                }
            }
            return GenStuff.DefaultStuffFor(forDef);
        }

        // Places a single 1-cell building blueprint if the cell is clear.
        private static bool TryPlaceBuilding(ThingDef def, IntVec3 cell, Rot4 rot, ThingDef stuff, Map map,
            Faction faction)
        {
            if (!CellClearForBuilding(cell, map))
            {
                return false;
            }
            if (!TerrainSupports(cell, map, TerrainAffordanceDefOf.Heavy))
            {
                return false;
            }

            GenConstruct.PlaceBlueprintForBuild(def, cell, map, rot, faction, stuff);
            return true;
        }

        private static bool TryPlaceFloor(TerrainDef floor, IntVec3 cell, Map map, Faction faction)
        {
            if (!cell.InBounds(map) || cell.Fogged(map))
            {
                return false;
            }
            if (cell.GetEdifice(map) != null)
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
            // Skip cells that already have a floor blueprint waiting.
            List<Thing> things = map.thingGrid.ThingsListAtFast(cell);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i].def.IsBlueprint && things[i].def.entityDefToBuild == floor)
                {
                    return false;
                }
            }

            GenConstruct.PlaceBlueprintForBuild(floor, cell, map, Rot4.North, faction, null);
            return true;
        }

        // A cell is clear for a new building if it is on the map, revealed, has no
        // edifice (wall/rock/existing building) and no blueprint/frame/impassable
        // thing already sitting on it.
        public static bool CellClearForBuilding(IntVec3 cell, Map map)
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
        // completely free of edifices/blueprints.
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
        // outward neighbour is open ground so colonists can actually reach it.
        public static IntVec3 ChooseDoorCell(CellRect rect, Map map)
        {
            IntVec3 best = new IntVec3(rect.CenterCell.x, 0, rect.minZ);
            int bestScore = int.MinValue;

            foreach (IntVec3 cell in rect.EdgeCells)
            {
                if (IsCorner(cell, rect))
                {
                    continue;
                }
                IntVec3 outward = OutwardCell(cell, rect);
                int score = 0;
                if (outward.InBounds(map))
                {
                    if (outward.Standable(map))
                    {
                        score += 100;
                    }
                    // A door into solid rock is useless; strongly prefer otherwise.
                    else if (outward.GetEdifice(map) is Mineable)
                    {
                        score -= 60;
                    }
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
        private static IEnumerable<System.Tuple<IntVec3, Rot4>> CandidatePlacements(FurnitureSpec spec, ThingDef def,
            CellRect interior, CellRect? lastPlaced)
        {
            Rot4 baseRot = def.rotatable ? new Rot4(spec.rotation) : Rot4.North;

            if (spec.placement == RoomPlacement.AdjacentToLast && lastPlaced.HasValue)
            {
                foreach (var pair in AdjacentPlacements(lastPlaced.Value))
                {
                    yield return pair;
                }
            }
            else if (spec.placement == RoomPlacement.Corner)
            {
                foreach (IntVec3 corner in CornerCells(interior))
                {
                    yield return System.Tuple.Create(corner, baseRot);
                }
            }
            else if (spec.placement == RoomPlacement.Center)
            {
                foreach (IntVec3 c in interior.Cells.OrderBy(c => (c - interior.CenterCell).LengthHorizontalSquared))
                {
                    yield return System.Tuple.Create(c, baseRot);
                }
            }
            else if (spec.placement == RoomPlacement.AlongWall)
            {
                foreach (var pair in AlongWallPlacements(interior))
                {
                    yield return pair;
                }
            }
            else
            {
                foreach (IntVec3 c in interior)
                {
                    yield return System.Tuple.Create(c, baseRot);
                }
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
        private static IEnumerable<System.Tuple<IntVec3, Rot4>> AlongWallPlacements(CellRect interior)
        {
            foreach (IntVec3 c in interior)
            {
                if (c.z == interior.maxZ)
                {
                    yield return System.Tuple.Create(c, Rot4.South);
                }
                else if (c.z == interior.minZ)
                {
                    yield return System.Tuple.Create(c, Rot4.North);
                }
                else if (c.x == interior.minX)
                {
                    yield return System.Tuple.Create(c, Rot4.East);
                }
                else if (c.x == interior.maxX)
                {
                    yield return System.Tuple.Create(c, Rot4.West);
                }
            }
        }

        // Cells orthogonally adjacent to the last-placed footprint, each facing back
        // toward it (chairs pulled up to a table).
        private static IEnumerable<System.Tuple<IntVec3, Rot4>> AdjacentPlacements(CellRect last)
        {
            for (int x = last.minX; x <= last.maxX; x++)
            {
                yield return System.Tuple.Create(new IntVec3(x, 0, last.maxZ + 1), Rot4.South);
                yield return System.Tuple.Create(new IntVec3(x, 0, last.minZ - 1), Rot4.North);
            }
            for (int z = last.minZ; z <= last.maxZ; z++)
            {
                yield return System.Tuple.Create(new IntVec3(last.maxX + 1, 0, z), Rot4.West);
                yield return System.Tuple.Create(new IntVec3(last.minX - 1, 0, z), Rot4.East);
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
