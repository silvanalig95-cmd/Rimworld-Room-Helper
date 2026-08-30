using UnityEngine;
using Verse;

namespace RoomHelper
{
    public class RoomHelperSettings : ModSettings
    {
        // Master switch for the base architect. When off, Room Helper is just the
        // manual room-placing tools.
        public bool architectEnabled = true;

        // Skip the approval step and build proposals straight away. Off by default:
        // the architect proposes, you decide.
        public bool autoApprove = false;

        // In-game hours between "what does the colony need?" checks.
        public float checkIntervalHours = 1f;

        // How many un-answered proposals may be outstanding at once. Keeping this at
        // 1 stops the letter stack filling up while you're busy.
        public int maxPendingProposals = 1;

        // -1 strongly prefers open ground, +1 strongly prefers digging in, 0 lets the
        // map decide on the merits of each site.
        public float mountainBias = 0f;

        // Whether the architect may queue mining to carve rooms out of rock.
        public bool allowMining = true;

        // Whether the architect runs power conduits to each finished room. Only ever
        // acts once the colony actually has a grid to connect to.
        public bool planPower = true;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref architectEnabled, "architectEnabled", true);
            Scribe_Values.Look(ref autoApprove, "autoApprove", false);
            Scribe_Values.Look(ref checkIntervalHours, "checkIntervalHours", 1f);
            Scribe_Values.Look(ref maxPendingProposals, "maxPendingProposals", 1);
            Scribe_Values.Look(ref mountainBias, "mountainBias", 0f);
            Scribe_Values.Look(ref allowMining, "allowMining", true);
            Scribe_Values.Look(ref planPower, "planPower", true);
        }
    }

    public class RoomHelperMod : Mod
    {
        private static RoomHelperSettings settingsInt;

        public RoomHelperMod(ModContentPack content) : base(content)
        {
            settingsInt = GetSettings<RoomHelperSettings>();
        }

        // Never null, even if something odd happens with mod loading order.
        public static RoomHelperSettings Settings
        {
            get
            {
                if (settingsInt == null)
                {
                    settingsInt = new RoomHelperSettings();
                }
                return settingsInt;
            }
        }

        public override string SettingsCategory() => "Room Helper";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            RoomHelperSettings s = Settings;
            float y = inRect.y;
            float w = inRect.width;

            Widgets.CheckboxLabeled(new Rect(inRect.x, y, w, 26f),
                "Enable the base architect", ref s.architectEnabled);
            y += 30f;

            Widgets.CheckboxLabeled(new Rect(inRect.x, y, w, 26f),
                "Build proposals automatically (skip approval)", ref s.autoApprove);
            y += 30f;

            Widgets.CheckboxLabeled(new Rect(inRect.x, y, w, 26f),
                "Allow carving rooms into rock", ref s.allowMining);
            y += 30f;

            Widgets.CheckboxLabeled(new Rect(inRect.x, y, w, 26f),
                "Run power to finished rooms", ref s.planPower);
            y += 36f;

            Widgets.Label(new Rect(inRect.x, y, w, 24f),
                $"Check the colony every {s.checkIntervalHours:0.#} in-game hour(s)");
            y += 26f;
            s.checkIntervalHours = Widgets.HorizontalSlider(
                new Rect(inRect.x, y, w, 26f), s.checkIntervalHours, 0.25f, 12f, false, null, null, null, 0.25f);
            y += 36f;

            Widgets.Label(new Rect(inRect.x, y, w, 24f),
                $"Outstanding proposals allowed at once: {s.maxPendingProposals}");
            y += 26f;
            s.maxPendingProposals = Mathf.RoundToInt(Widgets.HorizontalSlider(
                new Rect(inRect.x, y, w, 26f), s.maxPendingProposals, 1f, 5f, false, null, null, null, 1f));
            y += 36f;

            Widgets.Label(new Rect(inRect.x, y, w, 24f), BiasLabel(s.mountainBias));
            y += 26f;
            s.mountainBias = Widgets.HorizontalSlider(
                new Rect(inRect.x, y, w, 26f), s.mountainBias, -1f, 1f, false, null, null, null, 0.1f);
            y += 36f;

            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(inRect.x, y, w, 60f),
                "At the neutral setting the architect scores every candidate site on its own merits and lets the map decide: it digs in where there is good rock to dig, and builds in the open where there isn't.");
            Text.Font = GameFont.Small;
        }

        private static string BiasLabel(float bias)
        {
            if (bias <= -0.6f) return "Site preference: strongly favour open ground";
            if (bias <= -0.2f) return "Site preference: lean toward open ground";
            if (bias < 0.2f) return "Site preference: let the map decide";
            if (bias < 0.6f) return "Site preference: lean toward mountain";
            return "Site preference: strongly favour mountain";
        }
    }
}
