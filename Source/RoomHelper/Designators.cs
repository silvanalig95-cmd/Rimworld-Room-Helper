using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RoomHelper
{
    // Shared helpers for the Room Helper designators.
    internal static class DesignatorUtil
    {
        // Loads a texture, trying an optional fallback path and finally the always-
        // present BadTex, so a designator button is never null (which would throw
        // while drawing the command).
        public static Texture2D Icon(string path, string fallbackPath = null)
        {
            Texture2D tex = path.NullOrEmpty() ? null : ContentFinder<Texture2D>.Get(path, false);
            if (tex == null && !fallbackPath.NullOrEmpty())
            {
                tex = ContentFinder<Texture2D>.Get(fallbackPath, false);
            }
            return tex != null ? tex : BaseContent.BadTex;
        }

        // All room templates in menu order.
        public static IEnumerable<RoomTemplateDef> Templates =>
            DefDatabase<RoomTemplateDef>.AllDefs.OrderBy(d => d.order).ThenBy(d => d.label);
    }

    // Architect-menu button that opens a float menu of room templates and, once one
    // is chosen, hands control to a Designator_PlanRoom for that template. Referenced
    // from XML via DesignationCategoryDef.specialDesignatorClasses, so it needs a
    // parameterless constructor.
    public class Designator_PlanRoomMenu : Designator
    {
        public Designator_PlanRoomMenu()
        {
            defaultLabel = "Plan room";
            defaultDesc = "Pick a room type, then drag out an area. Room Helper lays down the walls, door, floor and furniture as blueprints, and your colonists build it. You decide the type; the mod handles the layout.";
            icon = DesignatorUtil.Icon("RoomHelper/UI/PlanRoom");
            useMouseIcon = true;
        }

        public override AcceptanceReport CanDesignateCell(IntVec3 loc) => false;

        public override void ProcessInput(Event ev)
        {
            var options = new List<FloatMenuOption>();
            foreach (RoomTemplateDef template in DesignatorUtil.Templates)
            {
                RoomTemplateDef local = template;
                options.Add(new FloatMenuOption(local.label.CapitalizeFirst(), () =>
                {
                    Find.DesignatorManager.Select(new Designator_PlanRoom(local));
                }));
            }
            if (options.Count == 0)
            {
                options.Add(new FloatMenuOption("(no room templates loaded)", null));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }
    }

    // Architect-menu button that opens the same room list but hands control to a
    // Designator_AutoPlaceRoom, which finds a spot for the player.
    public class Designator_AutoPlaceRoomMenu : Designator
    {
        public Designator_AutoPlaceRoomMenu()
        {
            defaultLabel = "Auto-place room";
            defaultDesc = "Pick a room type, then click a rough spot. Room Helper finds the nearest empty, buildable area that fits and lays the room out there for your colonists to build. Let them decide where it goes.";
            icon = DesignatorUtil.Icon("RoomHelper/UI/AutoRoom");
            useMouseIcon = true;
        }

        public override AcceptanceReport CanDesignateCell(IntVec3 loc) => false;

        public override void ProcessInput(Event ev)
        {
            var options = new List<FloatMenuOption>();
            foreach (RoomTemplateDef template in DesignatorUtil.Templates)
            {
                RoomTemplateDef local = template;
                options.Add(new FloatMenuOption(local.label.CapitalizeFirst(), () =>
                {
                    Find.DesignatorManager.Select(new Designator_AutoPlaceRoom(local));
                }));
            }
            if (options.Count == 0)
            {
                options.Add(new FloatMenuOption("(no room templates loaded)", null));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }
    }

    // Architect-menu button that opens the base architect's control panel, where the
    // player approves or dismisses the rooms their colonists have proposed.
    public class Designator_BaseArchitect : Designator
    {
        public Designator_BaseArchitect()
        {
            defaultLabel = "Base architect";
            defaultDesc = "Let your colonists plan the base. They work out what the colony is short of, pick a good spot for it — digging into rock or building in the open, whichever the map favours — and propose it for your approval.";
            icon = DesignatorUtil.Icon("RoomHelper/UI/Architect", "RoomHelper/UI/AutoRoom");
            useMouseIcon = false;
        }

        public override AcceptanceReport CanDesignateCell(IntVec3 loc) => false;

        public override void ProcessInput(Event ev)
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                return;
            }
            Find.WindowStack.Add(new Dialog_BaseArchitect(map));
        }
    }

    // Drag-out-an-area designator bound to one template. Placing works on the bounding
    // rectangle of whatever the player drags.
    public class Designator_PlanRoom : Designator
    {
        private readonly RoomTemplateDef template;

        public Designator_PlanRoom(RoomTemplateDef template)
        {
            this.template = template;
            defaultLabel = template.label.CapitalizeFirst();
            defaultDesc = "Drag out the outer footprint of the room. Walls sit on the edge you drag.";
            icon = DesignatorUtil.Icon(template.uiIconPath, "RoomHelper/UI/PlanRoom");
            useMouseIcon = true;
        }

        // Designators box-drag by default in RimWorld 1.6; overriding DesignateMultiCell
        // gives us the whole dragged rectangle at once (a plain click is a 1-cell drag).
        public override AcceptanceReport CanDesignateCell(IntVec3 loc)
        {
            Map map = Find.CurrentMap;
            if (map == null || !loc.InBounds(map) || loc.Fogged(map))
            {
                return false;
            }
            return true;
        }

        public override void DesignateMultiCell(IEnumerable<IntVec3> cells)
        {
            List<IntVec3> list = cells.ToList();
            if (list.Count == 0)
            {
                return;
            }

            int minX = int.MaxValue, minZ = int.MaxValue, maxX = int.MinValue, maxZ = int.MinValue;
            foreach (IntVec3 c in list)
            {
                if (c.x < minX) minX = c.x;
                if (c.x > maxX) maxX = c.x;
                if (c.z < minZ) minZ = c.z;
                if (c.z > maxZ) maxZ = c.z;
            }

            int width = Mathf.Max(maxX - minX + 1, template.minWidth);
            int height = Mathf.Max(maxZ - minZ + 1, template.minHeight);
            // Keep the drag anchored at its lower-left corner, then grow to the minimum.
            CellRect rect = new CellRect(minX, minZ, width, height);
            RoomPlanner.PlaceRoom(rect, template, Find.CurrentMap);
        }
    }

    // Single-click designator bound to one template: finds a spot near the click and
    // lays the room out there.
    public class Designator_AutoPlaceRoom : Designator
    {
        private readonly RoomTemplateDef template;

        public Designator_AutoPlaceRoom(RoomTemplateDef template)
        {
            this.template = template;
            defaultLabel = template.label.CapitalizeFirst();
            defaultDesc = "Click a rough spot. Room Helper finds the nearest empty area that fits and builds the room there.";
            icon = DesignatorUtil.Icon(template.uiIconPath, "RoomHelper/UI/AutoRoom");
            useMouseIcon = true;
        }

        public override AcceptanceReport CanDesignateCell(IntVec3 loc)
        {
            Map map = Find.CurrentMap;
            if (map == null || !loc.InBounds(map) || loc.Fogged(map))
            {
                return false;
            }
            return true;
        }

        // One click (or a sloppy drag) places a single room. We take the average of the
        // gesture's cells as the "rough spot" and let the planner find the real location.
        public override void DesignateMultiCell(IEnumerable<IntVec3> cells)
        {
            Map map = Find.CurrentMap;
            int n = 0, sx = 0, sz = 0;
            foreach (IntVec3 c in cells)
            {
                sx += c.x;
                sz += c.z;
                n++;
            }
            if (n == 0)
            {
                return;
            }
            IntVec3 center = new IntVec3(sx / n, 0, sz / n);

            if (RoomPlanner.TryFindPlacement(center, template, map, out CellRect rect))
            {
                RoomPlanner.PlaceRoom(rect, template, map);
            }
            else
            {
                Messages.Message(
                    $"Room Helper could not find an open, buildable spot for a {template.label} near there. Try clearing some space or picking another area.",
                    MessageTypeDefOf.RejectInput, false);
            }
        }
    }
}
