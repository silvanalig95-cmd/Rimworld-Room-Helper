using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RoomHelper
{
    // The base architect. Runs quietly on every map, works out what the colony is
    // missing, picks a good spot for it, and proposes it to the player. Approved
    // rooms are then laid out incrementally as ground becomes available.
    //
    // MapComponents are instantiated automatically for every map, so this needs no
    // def and no Harmony patch.
    public class BaseArchitect : MapComponent
    {
        private List<PlannedRoom> rooms = new List<PlannedRoom>();
        private int nextCheckTick = -1;

        private const int MaterializeInterval = 250;   // ~4 in-game seconds
        private const int TicksPerHour = 2500;

        public BaseArchitect(Map map) : base(map)
        {
        }

        public List<PlannedRoom> Rooms => rooms;

        public IEnumerable<PlannedRoom> Proposals =>
            rooms.Where(r => r.state == RoomPlanState.Proposed);

        public int PendingProposalCount => rooms.Count(r => r.state == RoomPlanState.Proposed);

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref rooms, "rooms", LookMode.Deep);
            Scribe_Values.Look(ref nextCheckTick, "nextCheckTick", -1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && rooms == null)
            {
                rooms = new List<PlannedRoom>();
            }
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();

            RoomHelperSettings settings = RoomHelperMod.Settings;
            if (!settings.architectEnabled || !map.IsPlayerHome)
            {
                return;
            }

            int now = Find.TickManager.TicksGame;

            // Advance approved rooms as their ground frees up.
            if (now % MaterializeInterval == 0)
            {
                for (int i = 0; i < rooms.Count; i++)
                {
                    rooms[i].Materialize(map, this);
                }
            }

            // Periodically ask what the colony is missing.
            if (nextCheckTick < 0)
            {
                nextCheckTick = now + Mathf.RoundToInt(settings.checkIntervalHours * TicksPerHour);
                return;
            }
            if (now >= nextCheckTick)
            {
                nextCheckTick = now + Mathf.RoundToInt(settings.checkIntervalHours * TicksPerHour);
                TryProposeSomething();
            }
        }

        // Picks the colony's most pressing unmet need and proposes a room for it.
        public bool TryProposeSomething()
        {
            RoomHelperSettings settings = RoomHelperMod.Settings;
            if (PendingProposalCount >= settings.maxPendingProposals)
            {
                return false;
            }

            List<RoomNeed> needs = ColonyNeeds.Assess(map, this);
            foreach (RoomNeed need in needs)
            {
                if (TryProposeRoom(need))
                {
                    return true;
                }
            }
            return false;
        }

        // Proposes one specific room, if a decent site exists for it.
        public bool TryProposeRoom(RoomNeed need)
        {
            if (!SiteScorer.TryFindSite(map, need.template, this, out SiteScorer.SiteCandidate site))
            {
                return false;
            }

            IntVec3 door = RoomPlanner.ChooseDoorCell(site.rect, map);
            var room = new PlannedRoom(need.template, site.rect, door, site.mountain, site.cellsToMine, need.reason);
            rooms.Add(room);

            if (RoomHelperMod.Settings.autoApprove)
            {
                Approve(room);
                Messages.Message(
                    $"Base architect started a {room.Label} ({need.reason})",
                    new LookTargets(site.rect.CenterCell, map), MessageTypeDefOf.TaskCompletion, false);
            }
            else
            {
                SendProposalLetter(room, need);
            }
            return true;
        }

        private void SendProposalLetter(PlannedRoom room, RoomNeed need)
        {
            string where = room.mountain
                ? $"carved into the rock ({room.cellsToMine} cells to mine out)"
                : "on open ground";

            string planner = ColonyArchitect.PlannerName(map);
            string opening = planner.NullOrEmpty()
                ? $"Your colonists have drawn up plans for a {room.Label}."
                : $"{planner} has drawn up plans for a {room.Label}.";

            string text =
                opening + "\n\n" +
                $"Why: {need.reason}\n" +
                $"Where: {where}, {SiteDescription(room)}.\n" +
                $"Size: {room.Rect.Width} x {room.Rect.Height}.\n\n" +
                "The proposed outline is marked on the map. Open the Base architect window " +
                "(Architect menu → Room Helper) to approve, move or dismiss it.";

            Find.LetterStack.ReceiveLetter(
                $"Proposed: {room.Label}",
                text,
                LetterDefOf.NeutralEvent,
                new LookTargets(room.Rect.CenterCell, map));
        }

        private string SiteDescription(PlannedRoom room)
        {
            IntVec3 core = SiteScorer.BaseCore(map, this);
            int dist = Mathf.RoundToInt(IntVec3Utility.DistanceTo(room.Rect.CenterCell, core));
            return $"about {dist} cells from the centre of your base";
        }

        // ------------------------------------------------------------ player actions

        public void Approve(PlannedRoom room)
        {
            room.state = RoomPlanState.Approved;
            room.Materialize(map, this);
        }

        public void Reject(PlannedRoom room)
        {
            room.CancelDesignations(map);
            rooms.Remove(room);
        }

        // Re-rolls the location of a proposal, keeping the room type. Handy when the
        // architect's pick is legal but you'd rather it went elsewhere.
        public bool Relocate(PlannedRoom room)
        {
            RoomTemplateDef template = room.Template;
            if (template == null)
            {
                return false;
            }

            // Temporarily drop it from the list so its own footprint doesn't block
            // the search for a new one.
            rooms.Remove(room);

            if (!SiteScorer.TryFindSite(map, template, this, out SiteScorer.SiteCandidate site)
                || site.rect == room.Rect)
            {
                rooms.Add(room);
                return false;
            }

            IntVec3 door = RoomPlanner.ChooseDoorCell(site.rect, map);
            var replacement = new PlannedRoom(template, site.rect, door, site.mountain, site.cellsToMine, room.reason);
            rooms.Add(replacement);
            return true;
        }

        // ------------------------------------------------------------------ queries

        public int CountRoomsOfTemplate(RoomTemplateDef template)
        {
            return rooms.Count(r => r.Template == template);
        }

        public bool AnyPlannedRoomContains(IntVec3 cell)
        {
            for (int i = 0; i < rooms.Count; i++)
            {
                if (rooms[i].Rect.Contains(cell))
                {
                    return true;
                }
            }
            return false;
        }

        public bool AnyPlannedRoomOverlaps(CellRect rect)
        {
            for (int i = 0; i < rooms.Count; i++)
            {
                if (rooms[i].Rect.Overlaps(rect))
                {
                    return true;
                }
            }
            return false;
        }

        // Decides whether <rect> can coexist with the rooms already planned, and
        // reports how many wall cells it would share with them.
        //
        // Sharing a wall is not merely tolerated, it's the goal: two rooms flush
        // against each other is what turns a scattering of huts into a building. What
        // is rejected is a room whose wall would intrude on another room's interior,
        // and a room sitting exactly one cell away  that leaves a dead strip nobody
        // can use or reach.
        public bool CanPlaceRectAmongRooms(CellRect rect, out int sharedWallCells)
        {
            sharedWallCells = 0;
            CellRect interior = rect.ContractedBy(1);

            for (int i = 0; i < rooms.Count; i++)
            {
                CellRect other = rooms[i].Rect;

                if (rect.Overlaps(other))
                {
                    // Neither room's wall band may sit inside the other's interior.
                    if (rect.Overlaps(other.ContractedBy(1)) || other.Overlaps(interior))
                    {
                        sharedWallCells = 0;
                        return false;
                    }
                    sharedWallCells += IntersectionArea(rect, other);
                }
                else if (rect.ExpandedBy(1).Overlaps(other))
                {
                    sharedWallCells = 0;
                    return false;
                }
            }
            return true;
        }

        // Number of cells two rectangles have in common (0 if they don't touch).
        public static int IntersectionArea(CellRect a, CellRect b)
        {
            int x0 = Mathf.Max(a.minX, b.minX);
            int x1 = Mathf.Min(a.maxX, b.maxX);
            int z0 = Mathf.Max(a.minZ, b.minZ);
            int z1 = Mathf.Min(a.maxZ, b.maxZ);
            if (x1 < x0 || z1 < z0)
            {
                return 0;
            }
            return (x1 - x0 + 1) * (z1 - z0 + 1);
        }

        // Total contribution of every planned room toward the given set of thing
        // defNames, used so the architect doesn't re-propose a room it already planned.
        public int PlannedProvisionOf(List<string> satisfiedBy)
        {
            if (satisfiedBy == null || satisfiedBy.Count == 0)
            {
                return 0;
            }

            int total = 0;
            for (int i = 0; i < rooms.Count; i++)
            {
                RoomTemplateDef t = rooms[i].Template;
                if (t?.needCheck == null)
                {
                    continue;
                }
                string provided = t.needCheck.ProvidedThing;
                if (!provided.NullOrEmpty() && satisfiedBy.Contains(provided))
                {
                    total += t.needCheck.provides;
                }
            }
            return total;
        }

        // Distance from <from> to the closest planned room built from the named
        // template, or -1 if there is no such room yet.
        public float DistanceToNearestRoomOfTemplate(string templateDefName, IntVec3 from)
        {
            float best = -1f;
            for (int i = 0; i < rooms.Count; i++)
            {
                RoomTemplateDef t = rooms[i].Template;
                if (t == null || t.defName != templateDefName)
                {
                    continue;
                }
                float d = IntVec3Utility.DistanceTo(rooms[i].Rect.CenterCell, from);
                if (best < 0f || d < best)
                {
                    best = d;
                }
            }
            return best;
        }

        // ------------------------------------------------------------------ drawing

        // Outline proposals on the map so the player can see what they're approving.
        public override void MapComponentUpdate()
        {
            base.MapComponentUpdate();
            if (!RoomHelperMod.Settings.architectEnabled)
            {
                return;
            }

            for (int i = 0; i < rooms.Count; i++)
            {
                PlannedRoom room = rooms[i];
                if (room.state != RoomPlanState.Proposed)
                {
                    continue;
                }
                Color color = room.mountain ? new Color(0.9f, 0.7f, 0.2f) : new Color(0.3f, 0.8f, 1f);
                GenDraw.DrawFieldEdges(room.Rect.Cells.ToList(), color);
            }
        }
    }
}
