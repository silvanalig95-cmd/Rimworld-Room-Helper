using RimWorld;
using UnityEngine;
using Verse;

namespace RoomHelper
{
    // Works out which colonist is doing the planning. The base architect isn't an
    // abstract system so much as a job somebody in the colony has taken on: the one
    // with the best head for it draws up the plans, and how good they are shows.
    public static class ColonyArchitect
    {
        // The colonist best suited to planning: the strongest combination of
        // Intellectual (working out what the colony needs) and Construction
        // (knowing what will actually build).
        public static Pawn FindPlanner(Map map)
        {
            if (map == null)
            {
                return null;
            }

            Pawn best = null;
            int bestScore = -1;

            foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
            {
                if (p.skills == null)
                {
                    continue;
                }
                int score = SkillLevel(p, SkillDefOf.Intellectual) + SkillLevel(p, SkillDefOf.Construction);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = p;
                }
            }
            return best;
        }

        public static int SkillLevel(Pawn p, SkillDef def)
        {
            if (p?.skills == null || def == null)
            {
                return 0;
            }
            SkillRecord record = p.skills.GetSkill(def);
            return record != null ? record.Level : 0;
        }

        // 0..1 measure of how good this colony's plans are. Drives how generously
        // rooms are sized: a skilled planner lays out roomier quarters, a colony of
        // beginners throws up something merely adequate.
        public static float PlanQuality(Map map)
        {
            Pawn planner = FindPlanner(map);
            if (planner == null)
            {
                return 0.5f;
            }
            int combined = SkillLevel(planner, SkillDefOf.Intellectual)
                           + SkillLevel(planner, SkillDefOf.Construction);
            return Mathf.Clamp01(combined / 28f);
        }

        // Extra cells added to each side of a planned room, 0..2, from plan quality.
        public static int SizeBonus(Map map)
        {
            return Mathf.FloorToInt(PlanQuality(map) * 2.99f);
        }

        // "Marcus has drawn up plans" / "Your colonists have drawn up plans".
        public static string PlannerName(Map map)
        {
            Pawn planner = FindPlanner(map);
            return planner != null ? planner.LabelShort : null;
        }
    }
}
