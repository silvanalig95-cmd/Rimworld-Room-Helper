using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RoomHelper
{
    // Decides *where* a room should go. Candidate rectangles are generated around the
    // colony's centre of gravity and scored; the winner is proposed to the player.
    //
    // Both kinds of site are scored with the same function, so the map decides which
    // style wins: dug into rock where there is good rock to dig (overhead cover, no
    // wall materials needed, but mining labour), out in the open where there isn't.
    public static class SiteScorer
    {
        public struct SiteCandidate
        {
            public CellRect rect;
            public float score;
            public bool mountain;   // interior is mostly rock and must be mined out
            public int cellsToMine;
        }

        // Fraction of the rect that must be rock before we treat it as a mountain
        // room. Between this and OpenMaxRockFraction we reject the site: a room half
        // in and half out of a cliff is awkward to build and ugly to live in.
        private const float MountainMinRockFraction = 0.60f;
        private const float OpenMaxRockFraction = 0.10f;

        private const int SearchRadius = 45;
        private const int SearchStep = 2;
        private const int MaxCandidatesScored = 2500;

        // Finds the best site for <template>, or returns false if nowhere works.
        public static bool TryFindSite(Map map, RoomTemplateDef template, BaseArchitect architect,
            out SiteCandidate best)
        {
            best = default;
            if (map == null || template == null)
            {
                return false;
            }

            IntVec3 core = BaseCore(map, architect);

            // A better planner lays out roomier quarters.
            int bonus = ColonyArchitect.SizeBonus(map);
            int w = Mathf.Max(template.defaultWidth, template.minWidth) + bonus;
            int h = Mathf.Max(template.defaultHeight, template.minHeight) + bonus;

            bool found = false;
            int scored = 0;

            for (int radius = 0; radius <= SearchRadius && scored < MaxCandidatesScored; radius += SearchStep)
            {
                foreach (IntVec3 offset in RingOffsets(radius, SearchStep))
                {
                    if (scored >= MaxCandidatesScored)
                    {
                        break;
                    }

                    IntVec3 center = core + offset;
                    CellRect rect = new CellRect(center.x - w / 2, center.z - h / 2, w, h);

                    if (TryScoreSite(map, rect, template, architect, core, out SiteCandidate candidate))
                    {
                        scored++;
                        if (!found || candidate.score > best.score)
                        {
                            best = candidate;
                            found = true;
                        }
                    }
                }
            }

            return found;
        }

        // Scores one candidate rectangle. Returns false if the site is unusable.
        public static bool TryScoreSite(Map map, CellRect rect, RoomTemplateDef template,
            BaseArchitect architect, IntVec3 core, out SiteCandidate candidate)
        {
            candidate = default;

            if (rect.minX < 1 || rect.minZ < 1 || rect.maxX >= map.Size.x - 1 || rect.maxZ >= map.Size.z - 1)
            {
                return false;
            }

            // Rooms are allowed  encouraged  to sit flush against each other and
            // share a wall. What's rejected is a genuine overlap, or a one-cell gap.
            int sharedWallCells = 0;
            if (architect != null && !architect.CanPlaceRectAmongRooms(rect, out sharedWallCells))
            {
                return false;
            }

            int rockCells = 0;
            int thickRoofCells = 0;
            int total = 0;

            foreach (IntVec3 c in rect)
            {
                total++;

                if (!c.InBounds(map) || c.Fogged(map))
                {
                    return false;
                }

                TerrainDef terrain = c.GetTerrain(map);
                if (terrain == null || !terrain.affordances.Contains(TerrainAffordanceDefOf.Heavy))
                {
                    return false;
                }

                Building edifice = c.GetEdifice(map);
                if (edifice != null)
                {
                    // Natural rock is fine  we can mine it. Anything else (a player
                    // building, an ancient ruin) means hands off.
                    if (edifice is Mineable)
                    {
                        rockCells++;
                    }
                    else
                    {
                        return false;
                    }
                }

                // Existing blueprints/frames mean this ground is already spoken for.
                List<Thing> things = map.thingGrid.ThingsListAtFast(c);
                for (int i = 0; i < things.Count; i++)
                {
                    ThingDef d = things[i].def;
                    if (d.IsBlueprint || d.IsFrame)
                    {
                        return false;
                    }
                }

                RoofDef roof = map.roofGrid.RoofAt(c);
                if (roof != null && roof.isThickRoof)
                {
                    thickRoofCells++;
                }
            }

            if (total == 0)
            {
                return false;
            }

            float rockFraction = rockCells / (float)total;
            bool mountain;

            if (rockFraction >= MountainMinRockFraction)
            {
                mountain = true;
            }
            else if (rockFraction <= OpenMaxRockFraction)
            {
                mountain = false;
            }
            else
            {
                // Half in a cliff face  reject.
                return false;
            }

            if (mountain && !template.allowMountain)
            {
                return false;
            }
            if (!mountain && !template.allowOpenGround)
            {
                return false;
            }

            RoomHelperSettings settings = RoomHelperMod.Settings;
            if (mountain && !settings.allowMining)
            {
                return false;
            }

            // ------------------------------------------------------------- scoring
            float score = 100f;

            // Sharing a wall with what's already planned is the strongest signal that
            // this site is part of the base rather than merely near it. Weighted above
            // everything else so the architect grows one connected building.
            score += Mathf.Min(sharedWallCells, 14) * 6f;

            // Compactness: a base that sprawls is a base that walks.
            float distToCore = IntVec3Utility.DistanceTo(rect.CenterCell, core);
            score -= distToCore * 1.6f;

            // Adjacency: sit next to the rooms this one likes being next to.
            if (template.nearTemplates != null && architect != null)
            {
                foreach (string wanted in template.nearTemplates)
                {
                    float d = architect.DistanceToNearestRoomOfTemplate(wanted, rect.CenterCell);
                    if (d >= 0f)
                    {
                        // Big bonus for being close, fading out by ~20 cells.
                        score += Mathf.Max(0f, 45f - d * 2.2f);
                    }
                }
            }

            if (mountain)
            {
                // Overhead rock is genuinely valuable: drop pods can't land on it and
                // it insulates. But every rock cell is mining work before anyone can
                // move in, so a deep dig is penalised.
                float thickRoofFraction = thickRoofCells / (float)total;
                score += thickRoofFraction * 25f;
                score -= rockCells * 0.45f;
                score += settings.mountainBias * 25f;
            }
            else
            {
                score -= settings.mountainBias * 25f;
            }

            candidate = new SiteCandidate
            {
                rect = rect,
                score = score,
                mountain = mountain,
                cellsToMine = rockCells
            };
            return true;
        }

        // The colony's centre of gravity: the average position of what the player has
        // built, falling back to planned rooms, then to a colonist, then map centre.
        public static IntVec3 BaseCore(Map map, BaseArchitect architect)
        {
            long sx = 0, sz = 0;
            int n = 0;

            foreach (Building b in map.listerBuildings.allBuildingsColonist)
            {
                sx += b.Position.x;
                sz += b.Position.z;
                n++;
            }

            if (n == 0 && architect != null)
            {
                foreach (PlannedRoom room in architect.Rooms)
                {
                    IntVec3 c = room.Rect.CenterCell;
                    sx += c.x;
                    sz += c.z;
                    n++;
                }
            }

            if (n == 0)
            {
                List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
                if (colonists.Count > 0)
                {
                    return colonists[0].Position;
                }
                return map.Center;
            }

            return new IntVec3((int)(sx / n), 0, (int)(sz / n));
        }

        // Cells exactly <radius> away (Chebyshev), sampled every <step> cells.
        private static IEnumerable<IntVec3> RingOffsets(int radius, int step)
        {
            if (radius == 0)
            {
                yield return IntVec3.Zero;
                yield break;
            }
            for (int x = -radius; x <= radius; x += step)
            {
                for (int z = -radius; z <= radius; z += step)
                {
                    if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) >= radius - step + 1)
                    {
                        yield return new IntVec3(x, 0, z);
                    }
                }
            }
        }
    }
}
