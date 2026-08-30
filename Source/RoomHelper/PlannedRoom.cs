using System.Collections.Generic;
using RimWorld;
using UnityEngine;
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

        // Set once power has been traced to the room.
        private bool powerDone;

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
            Scribe_Values.Look(ref powerDone, "powerDone");
            Scribe_Values.Look(ref reason, "reason");
        }

        // Advances construction as far as the current state of the ground allows.
        // Safe to call repeatedly; every step checks before it places.
        public void Materialize(Map map, BaseArchitect architect)
        {
            if (Template == null || state != RoomPlanState.Approved)
            {
                return;
            }

            // Alongside the way in from outside, every wall shared with a neighbouring
            // room gets a door, so you can walk the base without stepping outdoors.
            List<IntVec3> doors = ConnectingDoorCells(architect);
            doors.Add(doorCell);

            // 1. Mine out anything in the way: the interior, and every doorway.
            //    Perimeter rock is left standing  in a mountain room the natural rock
            //    *is* the wall, which is the whole point of digging in.
            DesignateMining(map, doors);

            // 2. Walls and doors, wherever the ground is free. Cells still holding rock
            //    are simply skipped and picked up on a later pass.
            RoomPlanner.PlaceShell(Rect, Template, map, doors);

            // 3. Flooring and furniture, but only once the whole interior is actually
            //    clear  otherwise furniture would crowd into whatever corner happened
            //    to be mined out first.
            if (!interiorDone && InteriorIsClear(map))
            {
                RoomPlanner.PlaceInterior(Rect, Template, map);
                RoomPlanner.AddToHomeArea(Rect, map);
                interiorDone = true;
            }

            // 4. Run power to it, once there's a room to power. Retried until the
            //    colony actually has a grid to connect to.
            if (interiorDone && !powerDone && PowerPlanner.ColonyHasGrid(map))
            {
                PowerPlanner.ConnectToGrid(Rect, map);
                powerDone = true;
            }

            if (interiorDone && ShellIsSettled(map))
            {
                state = RoomPlanState.Done;
            }
        }

        // One door per wall shared with another planned room, placed in the middle of
        // the shared stretch so it links the two interiors.
        public List<IntVec3> ConnectingDoorCells(BaseArchitect architect)
        {
            var doors = new List<IntVec3>();
            if (architect == null)
            {
                return doors;
            }

            CellRect mine = Rect;
            CellRect myInterior = Interior;

            foreach (PlannedRoom other in architect.Rooms)
            {
                if (other == this || other.Template == null)
                {
                    continue;
                }
                if (!mine.Overlaps(other.Rect))
                {
                    continue;
                }
                if (TrySharedDoor(mine, myInterior, other.Rect, other.Interior, out IntVec3 door))
                {
                    doors.Add(door);
                }
            }
            return doors;
        }

        // Where two rectangles share a single line of wall, returns the midpoint of the
        // stretch over which both interiors actually meet  a door anywhere else on the
        // shared line would open into a corner rather than into the next room.
        private static bool TrySharedDoor(CellRect a, CellRect aIn, CellRect b, CellRect bIn,
            out IntVec3 door)
        {
            door = IntVec3.Invalid;

            int x0 = Mathf.Max(a.minX, b.minX);
            int x1 = Mathf.Min(a.maxX, b.maxX);
            int z0 = Mathf.Max(a.minZ, b.minZ);
            int z1 = Mathf.Min(a.maxZ, b.maxZ);
            if (x1 < x0 || z1 < z0)
            {
                return false;
            }

            if (x0 == x1 && z1 > z0)
            {
                // Vertical shared wall.
                int lo = Mathf.Max(aIn.minZ, bIn.minZ);
                int hi = Mathf.Min(aIn.maxZ, bIn.maxZ);
                if (hi < lo)
                {
                    return false;
                }
                door = new IntVec3(x0, 0, (lo + hi) / 2);
                return true;
            }

            if (z0 == z1 && x1 > x0)
            {
                // Horizontal shared wall.
                int lo = Mathf.Max(aIn.minX, bIn.minX);
                int hi = Mathf.Min(aIn.maxX, bIn.maxX);
                if (hi < lo)
                {
                    return false;
                }
                door = new IntVec3((lo + hi) / 2, 0, z0);
                return true;
            }

            // Touching only at a corner  nothing to connect.
            return false;
        }

        // Queues mining for interior rock and for the doorway. Perimeter rock stays
        // as the room's natural wall.
        private void DesignateMining(Map map, List<IntVec3> doors)
        {
            if (!RoomHelperMod.Settings.allowMining)
            {
                return;
            }

            CellRect interior = Interior;
            foreach (IntVec3 c in Rect)
            {
                if (!interior.Contains(c) && !doors.Contains(c))
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
