using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RoomHelper
{
    // A blueprint recipe for a whole room. Loaded from XML as
    // <RoomHelper.RoomTemplateDef> ... </RoomHelper.RoomTemplateDef>.
    //
    // The player never has to think about the layout: they pick a template and
    // either drag out an area or let the mod find a spot, and Room Helper lays down
    // wall / door / floor / furniture blueprints. Ordinary colonists then build it
    // with their normal construction jobs.
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
    }
}
