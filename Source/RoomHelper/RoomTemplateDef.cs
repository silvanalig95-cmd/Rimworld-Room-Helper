using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RoomHelper
{
    // A blueprint recipe for a whole room. Loaded from XML as
    // <RoomHelper.RoomTemplateDef> ... </RoomHelper.RoomTemplateDef>.
    //
    // The player never has to think about the layout: they pick a template (or the
    // base architect picks one for them) and Room Helper works out the size, the
    // position and the interior arrangement, then lays down blueprints. Ordinary
    // colonists build it with their normal construction jobs.
    public class RoomTemplateDef : Def
    {
        // Icon shown in the room picker menu, relative to a Textures folder
        // (e.g. "RoomHelper/UI/Bedroom"). Optional.
        public string uiIconPath;

        // Smallest area (in cells, walls included) the player is allowed to plan.
        public int minWidth = 5;
        public int minHeight = 5;

        // Size used when auto-placing, or when the player just clicks a single cell.
        public int defaultWidth = 7;
        public int defaultHeight = 7;

        // Candidate wall materials (stuff defNames) tried in order. First one that
        // exists and is a valid stuff is used; otherwise the game's default wall
        // material is chosen.
        public List<string> wallStuff;

        // Candidate door materials (stuff defNames), same fallback behaviour.
        public List<string> doorStuff;

        // Floor terrain defName laid across the interior, e.g. "WoodPlankFloor".
        // Optional  leave empty to skip flooring.
        public string floorDef;

        // Furniture / production benches / lights to lay out inside the room.
        public List<FurnitureSpec> furniture = new List<FurnitureSpec>();

        // Sort order in the room picker menu (lower shows first).
        public int order = 0;

        // ------------------------------------------------------- base architect data

        // When the colony wants another one of these. Null = never auto-proposed;
        // the room stays available for manual placement.
        public RoomNeedCheck needCheck;

        // Whether the base architect may propose this room on its own. Rooms that
        // overlap another room's job (barracks vs bedroom) set this false so the
        // architect doesn't propose both for the same need.
        public bool autoPropose = true;

        // Templates this room likes to sit next to, by defName. Each nearby match
        // pulls candidate sites toward it during scoring (kitchen near the freezer,
        // dining room near the kitchen).
        public List<string> nearTemplates;

        // May the architect carve this room into rock? Some rooms (solar-dependent,
        // or ones the player wants above ground) can opt out.
        public bool allowMountain = true;

        // May the architect place this room in the open? Set false for rooms that
        // only make sense dug in.
        public bool allowOpenGround = true;
    }
}
