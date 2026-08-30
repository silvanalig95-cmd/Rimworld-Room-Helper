using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RoomHelper
{
    // Describes when the colony wants another room of a given type. Attached to a
    // RoomTemplateDef as <needCheck>...</needCheck> so new room types can declare
    // their own trigger conditions in XML without touching code.
    //
    // Semantics: the colony wants <desired> of the things listed in satisfiedBy.
    // Anything already built (or already planned) counts against that. If the colony
    // is short, the architect proposes a room of this type.
    public class RoomNeedCheck
    {
        // Things that satisfy this need, by defName. The first entry doubles as the
        // "what does this room provide" marker unless providesThing overrides it.
        public List<string> satisfiedBy = new List<string>();

        // Want one of the thing per this many colonists. 1 = one bed per colonist,
        // 8 = one dining table per eight colonists. 0 disables per-colonist scaling.
        public float perColonists = 0f;

        // Always want at least this many, regardless of colony size.
        public int minimumCount = 0;

        // Higher = proposed sooner when several needs compete.
        public float priority = 50f;

        // Never auto-propose more than this many rooms of this type.
        public int maxRooms = 8;

        // Don't consider this need until the colony has at least this many colonists.
        public int minColonists = 0;

        // How many of the satisfying thing a finished room of this type contributes.
        public int provides = 1;

        // Overrides which defName this room counts as providing. Defaults to
        // satisfiedBy[0]. Useful when a room satisfies a need it doesn't literally
        // contain the first-listed thing for.
        public string providesThing;

        public string ProvidedThing =>
            !providesThing.NullOrEmpty() ? providesThing
            : (satisfiedBy != null && satisfiedBy.Count > 0 ? satisfiedBy[0] : null);

        // How many of the thing the colony should have right now.
        public int DesiredCount(int colonists)
        {
            int desired = minimumCount;
            if (perColonists > 0f)
            {
                desired = Mathf.Max(desired, Mathf.CeilToInt(colonists / perColonists));
            }
            return desired;
        }

        // True if this need is even eligible for the current colony size.
        public bool AppliesTo(int colonists) => colonists >= minColonists;
    }

    // One unmet need, produced by ColonyNeeds.Assess.
    public struct RoomNeed
    {
        public RoomTemplateDef template;
        public int deficit;      // how many of the thing we're short
        public float score;      // priority-weighted, higher wins
        public string reason;    // shown to the player in the proposal
    }
}
