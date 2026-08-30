using System.Collections.Generic;
using Verse;

namespace RoomHelper
{
    // Where a piece of furniture should be placed inside a planned room.
    public enum RoomPlacement
    {
        // A free interior corner (lamps, dressers, end tables).
        Corner,
        // As close to the middle of the room as it will fit (dining tables, benches).
        Center,
        // Hugging an interior wall (beds, shelves, work stations).
        AlongWall,
        // Directly next to the previous piece of furniture, facing it (chairs around a table).
        AdjacentToLast,
        // Anywhere it fits, scanning the interior row by row.
        Free
    }

    // One entry in a RoomTemplateDef's furniture list. Everything is resolved by
    // defName at run time with a silent fallback so a template never hard-crashes
    // if a def was renamed or lives in a DLC the player does not own.
    public class FurnitureSpec
    {
        // Primary thing to place (defName), e.g. "Bed".
        public string thingDef;

        // Optional fallbacks tried in order if <thingDef> does not exist.
        public List<string> alternates;

        // Optional stuff (material) defName. If null a sensible default is chosen.
        public string stuff;

        // How many of this piece to try to place.
        public int count = 1;

        // Placement strategy inside the room.
        public RoomPlacement placement = RoomPlacement.Free;

        // Rotation index 0..3 (North, East, South, West). Used as a starting hint;
        // some placements (AlongWall / AdjacentToLast) override it so the piece faces
        // the right way.
        public int rotation = 0;

        // If true (default) a piece that cannot be resolved or does not fit is quietly
        // skipped instead of producing a warning.
        public bool optional = true;

        // All candidate defNames in priority order.
        public IEnumerable<string> CandidateDefNames()
        {
            if (!thingDef.NullOrEmpty())
            {
                yield return thingDef;
            }
            if (alternates != null)
            {
                foreach (string a in alternates)
                {
                    if (!a.NullOrEmpty())
                    {
                        yield return a;
                    }
                }
            }
        }
    }
}
