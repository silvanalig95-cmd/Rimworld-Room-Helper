using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RoomHelper
{
    // Looks at the colony and works out which rooms it is missing. This is the
    // "what does this base need next?" half of the base architect; SiteScorer is the
    // "where should it go?" half.
    public static class ColonyNeeds
    {
        // All unmet needs, highest score first. Only templates with a <needCheck>
        // and autoPropose=true are considered.
        public static List<RoomNeed> Assess(Map map, BaseArchitect architect)
        {
            var needs = new List<RoomNeed>();
            if (map == null)
            {
                return needs;
            }

            int colonists = map.mapPawns.FreeColonistsSpawnedCount;
            if (colonists <= 0)
            {
                return needs;
            }

            foreach (RoomTemplateDef template in DefDatabase<RoomTemplateDef>.AllDefs)
            {
                RoomNeedCheck check = template.needCheck;
                if (check == null || !template.autoPropose || !check.AppliesTo(colonists))
                {
                    continue;
                }

                // Don't run away building the same room type forever.
                if (architect != null && architect.CountRoomsOfTemplate(template) >= check.maxRooms)
                {
                    continue;
                }

                int desired = check.DesiredCount(colonists);
                if (desired <= 0)
                {
                    continue;
                }

                int have = CountExisting(map, check, architect);
                int deficit = desired - have;
                if (deficit <= 0)
                {
                    continue;
                }

                needs.Add(new RoomNeed
                {
                    template = template,
                    deficit = deficit,
                    // Weight by priority, but let a large shortfall outrank a small
                    // one of similar priority.
                    score = check.priority + deficit * 5f,
                    reason = DescribeNeed(template, check, have, desired, colonists)
                });
            }

            return needs.OrderByDescending(n => n.score).ToList();
        }

        // How many of the need-satisfying things the colony effectively has:
        // things already built, plus anything the architect has already planned or
        // queued (so it doesn't propose five bedrooms while the first is still going up).
        private static int CountExisting(Map map, RoomNeedCheck check, BaseArchitect architect)
        {
            int count = 0;

            if (check.satisfiedBy != null)
            {
                foreach (string defName in check.satisfiedBy)
                {
                    ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                    if (def == null)
                    {
                        continue;
                    }

                    // Built buildings owned by the player. Anything standing inside a
                    // room the architect planned is counted via the plan instead, so
                    // it isn't double counted once construction finishes.
                    List<Building> built = map.listerBuildings.AllBuildingsColonistOfDef(def).ToList();
                    foreach (Building b in built)
                    {
                        if (architect == null || !architect.AnyPlannedRoomContains(b.Position))
                        {
                            count++;
                        }
                    }

                    // Blueprints and frames the player placed by hand also count, so
                    // manual building suppresses proposals too.
                    count += CountUnbuilt(map, def, architect);
                }
            }

            // Rooms the architect has proposed, approved or finished.
            if (architect != null)
            {
                count += architect.PlannedProvisionOf(check.satisfiedBy);
            }

            return count;
        }

        // Blueprints / frames for <def> that aren't inside a planned room.
        private static int CountUnbuilt(Map map, ThingDef def, BaseArchitect architect)
        {
            int count = 0;
            if (def.blueprintDef != null)
            {
                foreach (Thing t in map.listerThings.ThingsOfDef(def.blueprintDef))
                {
                    if (architect == null || !architect.AnyPlannedRoomContains(t.Position))
                    {
                        count++;
                    }
                }
            }
            if (def.frameDef != null)
            {
                foreach (Thing t in map.listerThings.ThingsOfDef(def.frameDef))
                {
                    if (architect == null || !architect.AnyPlannedRoomContains(t.Position))
                    {
                        count++;
                    }
                }
            }
            return count;
        }

        private static string DescribeNeed(RoomTemplateDef template, RoomNeedCheck check,
            int have, int desired, int colonists)
        {
            if (check.perColonists > 0f)
            {
                return $"{colonists} colonists need {desired} (you have {have}).";
            }
            return $"The colony has {have} of {desired}.";
        }
    }
}
