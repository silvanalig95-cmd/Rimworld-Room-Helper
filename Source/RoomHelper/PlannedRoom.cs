using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RoomHelper
{
    public enum RoomPlanState
    {
        // Waiting for the player to say yes or no.
        Proposed,
        // Player approved it; the architect is laying it out as ground becomes free.
        Approved,
        // Everything that can be placed has been placed.
        Done
    }

    // One room the base architect has planned. Persisted with the save so an
    // approved room survives quitting mid-build.
    //
    // Materialisation is deliberately incremental. A room carved into a mountain
    // can't be furnished the moment it's approved: the rock has to be mined out
    // first, which is real colonist work. So the architect re-runs Materialize
    // periodically and places each part as soon as its ground is actually free.
    public class PlannedRoom : IExposable
    {
        private string templateDefName;
        private int minX, minZ, width, height;
        private IntVec3 doorCell;

        public RoomPlanState state = RoomPlanState.Proposed;
        public bool mountain;
        public int cellsToMine;
        public string reason = string.Empty;

        // Set once the interior pass has run, so furniture is laid out exactly once.
        private bool interiorDone;

        private RoomTemplateDef templateCache;

        public PlannedRoom()
        {
        }

        public PlannedRoom(RoomTemplateDef template, CellRect rect, IntVec3 doorCell, bool mountain,
            int cellsToMine, string reason)
        {
            templateDefName = template.defName;
            templateCache = template;
            minX = rect.minX;
            minZ = rect.minZ;
            width = rect.Width;
            height = rect.Height;
            this.doorCell = doorCell;
            this.mountain = mountain;
            this.cellsToMine = cellsToMine;
            this.reason = reason ?? string.Empty;
        }

        public CellRect Rect => new CellRect(minX, minZ, width, height);

        public CellRect Interior => Rect.ContractedBy(1);

        public IntVec3 DoorCell => doorCell;

        public RoomTemplateDef Template
        {
            get
            {
                if (templateCache == null && !templateDefName.NullOrEmpty())
                {
                    templateCache = DefDatabase<RoomTemplateDef>.GetNamedSilentFail(templateDefName);
                }
                return templateCache;
            }
        }

        public string Label => Template != null ? Template.label : "room";

        public void ExposeData()
        {
            Scribe_Values.Look(ref templateDefName, "templateDefName");
            Scribe_Values.Look(ref minX, "minX");
            Scribe_Values.Look(ref minZ, "minZ");
            Scribe_Values.Look(ref width, "width");
            Scribe_Values.Look(ref height, "height");
            Scribe_Values.Look(ref doorCell, "doorCell");
            Scribe_Values.Look(ref state, "state", RoomPlanState.Proposed);
            Scribe_Values.Look(ref mountain, "mountain");
            Scribe_Values.Look(ref cellsToMine, "cellsToMine");
            Scribe_Values.Look(ref interiorDone, "interiorDone");
            Scribe_Values.Look(ref reason, "reason");
        }

        // Advances construction as far as the current state of the ground allows.
        // Safe to call repeatedly; every step checks before it places.
        public void Materialize(Map map)
        {
            if (Template == null || state != RoomPlanState.Approved)
            {
                return;
            }

            // 1. Mine out anything in the way. Perimeter rock is left standing  in a
            //    mountain room the natural rock *is* the wall, which is the whole point
            //    of digging in.
            DesignateMining(map);

            // 2. Walls and door, wherever the ground is free. Cells still holding rock
            //    are simply skipped and picked up on a later pass.
            RoomPlanner.PlaceShell(Rect, Template, map, doorCell);

            // 3. Flooring and furniture, but only once the whole interior is actually
            //    clear  otherwise furniture would crowd into whatever corner happened
            //    to be mined out first.
            if (!interiorDone && InteriorIsClear(map))
            {
                RoomPlanner.PlaceInterior(Rect, Template, map);
                RoomPlanner.AddToHomeArea(Rect, map);
                interiorDone = true;
            }

            if (interiorDone && ShellIsSettled(map))
            {
                state = RoomPlanState.Done;
            }
        }

        // Queues mining for interior rock and for the doorway. Perimeter rock stays
        // as the room's natural wall.
        private void DesignateMining(Map map)
        {
            if (!RoomHelperMod.Settings.allowMining)
            {
                return;
            }

            CellRect interior = Interior;
            foreach (IntVec3 c in Rect)
            {
                bool isInterior = interior.Contains(c);
                if (!isInterior && c != doorCell)
                {
                    continue;
                }
                TryDesignateMine(map, c);
            }
        }

        private static void TryDesignateMine(Map map, IntVec3 c)
        {
            if (!c.InBounds(map))
            {
                return;
            }
            if (!(c.GetEdifice(map) is Mineable))
            {
                return;
            }
            if (map.designationManager.DesignationAt(c, DesignationDefOf.Mine) != null)
            {
                return;
            }
            map.designationManager.AddDesignation(new Designation(c, DesignationDefOf.Mine, null));
        }

        // True when nothing solid is left inside the room.
        public bool InteriorIsClear(Map map)
        {
            foreach (IntVec3 c in Interior)
            {
                if (!c.InBounds(map))
                {
                    return false;
                }
                if (c.GetEdifice(map) != null)
                {
                    return false;
                }
            }
            return true;
        }

        // True when every perimeter cell either holds something solid (a wall, natural
        // rock, the door) or has a blueprint/frame on the way, i.e. there is nothing
        // left for us to place.
        private bool ShellIsSettled(Map map)
        {
            foreach (IntVec3 c in Rect.EdgeCells)
            {
                if (!c.InBounds(map))
                {
                    continue;
                }
                if (c.GetEdifice(map) != null)
                {
                    continue;
                }
                if (HasBlueprintOrFrame(map, c))
                {
                    continue;
                }
                return false;
            }
            return true;
        }

        private static bool HasBlueprintOrFrame(Map map, IntVec3 c)
        {
            List<Thing> things = map.thingGrid.ThingsListAtFast(c);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i].def.IsBlueprint || things[i].def.IsFrame)
                {
                    return true;
                }
            }
            return false;
        }

        // Removes any mining designations this plan added, used when the player
        // rejects or cancels a room.
        public void CancelDesignations(Map map)
        {
            foreach (IntVec3 c in Rect)
            {
                if (!c.InBounds(map))
                {
                    continue;
                }
                Designation d = map.designationManager.DesignationAt(c, DesignationDefOf.Mine);
                if (d != null)
                {
                    map.designationManager.RemoveDesignation(d);
                }
            }
        }
    }
}
