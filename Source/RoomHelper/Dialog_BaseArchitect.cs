using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RoomHelper
{
    // The base architect's control panel: what the colony still needs, what the
    // architect wants to build next, and what it's currently working on.
    public class Dialog_BaseArchitect : Window
    {
        private readonly Map map;
        private Vector2 scroll = Vector2.zero;

        private const float RowPadding = 6f;
        private const float ButtonWidth = 92f;
        private const float ButtonHeight = 28f;

        public Dialog_BaseArchitect(Map map)
        {
            this.map = map;
            doCloseX = true;
            doCloseButton = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = false;
            draggable = true;
            preventCameraMotion = false;
        }

        public override Vector2 InitialSize => new Vector2(640f, 620f);

        private BaseArchitect Architect => map?.GetComponent<BaseArchitect>();

        public override void DoWindowContents(Rect inRect)
        {
            BaseArchitect architect = Architect;
            if (architect == null)
            {
                Widgets.Label(inRect, "No base architect on this map.");
                return;
            }

            RoomHelperSettings settings = RoomHelperMod.Settings;
            float y = inRect.y;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 34f), "Base architect");
            Text.Font = GameFont.Small;
            y += 38f;

            // --- master switches ---------------------------------------------------
            Widgets.CheckboxLabeled(new Rect(inRect.x, y, inRect.width * 0.55f, 24f),
                "Let colonists plan the base", ref settings.architectEnabled);
            Widgets.CheckboxLabeled(new Rect(inRect.x + inRect.width * 0.57f, y, inRect.width * 0.43f, 24f),
                "Build without asking", ref settings.autoApprove);
            y += 30f;

            Widgets.DrawLineHorizontal(inRect.x, y, inRect.width);
            y += RowPadding;

            // --- what the colony is short of ---------------------------------------
            List<RoomNeed> needs = ColonyNeeds.Assess(map, architect);
            Text.Font = GameFont.Tiny;
            string needSummary = needs.Count == 0
                ? "Your colony isn't short of anything the architect knows how to build."
                : "Outstanding needs: " + string.Join(", ", needs.Take(4).Select(n => n.template.label).ToArray());
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 22f), needSummary);
            Text.Font = GameFont.Small;
            y += 26f;

            // --- action row ---------------------------------------------------------
            if (Widgets.ButtonText(new Rect(inRect.x, y, 150f, ButtonHeight), "Plan something now"))
            {
                if (!architect.TryProposeSomething())
                {
                    Messages.Message(
                        "The architect has nothing to propose right now  either the colony is well supplied, or there's no room to build.",
                        MessageTypeDefOf.RejectInput, false);
                }
            }
            y += ButtonHeight + RowPadding;

            Widgets.DrawLineHorizontal(inRect.x, y, inRect.width);
            y += RowPadding;

            // --- proposals + work in progress ---------------------------------------
            float listTop = y;
            float listHeight = inRect.height - listTop - 44f;
            Rect outRect = new Rect(inRect.x, listTop, inRect.width, listHeight);

            List<PlannedRoom> proposals = architect.Proposals.ToList();
            List<PlannedRoom> active = architect.Rooms
                .Where(r => r.state != RoomPlanState.Proposed).ToList();

            float viewHeight = proposals.Count * 74f + active.Count * 34f + 60f;
            Rect viewRect = new Rect(0f, 0f, outRect.width - 20f, viewHeight);

            Widgets.BeginScrollView(outRect, ref scroll, viewRect);
            float vy = 0f;

            if (proposals.Count == 0)
            {
                Widgets.Label(new Rect(0f, vy, viewRect.width, 24f), "No proposals waiting.");
                vy += 28f;
            }
            else
            {
                foreach (PlannedRoom room in proposals)
                {
                    vy = DrawProposal(architect, room, viewRect.width, vy);
                }
            }

            if (active.Count > 0)
            {
                vy += 8f;
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(0f, vy, viewRect.width, 22f), "Approved and under way");
                Text.Font = GameFont.Small;
                vy += 24f;

                foreach (PlannedRoom room in active)
                {
                    vy = DrawActiveRoom(architect, room, viewRect.width, vy);
                }
            }

            Widgets.EndScrollView();
        }

        private float DrawProposal(BaseArchitect architect, PlannedRoom room, float width, float vy)
        {
            Rect row = new Rect(0f, vy, width, 68f);
            Widgets.DrawMenuSection(row);

            Rect inner = row.ContractedBy(8f);
            float textWidth = inner.width - ButtonWidth - 8f;

            Widgets.Label(new Rect(inner.x, inner.y, textWidth, 22f), room.Label.CapitalizeFirst());

            Text.Font = GameFont.Tiny;
            string detail = room.mountain
                ? $"Carved into rock  {room.cellsToMine} cells to mine. {room.reason}"
                : $"On open ground. {room.reason}";
            Widgets.Label(new Rect(inner.x, inner.y + 22f, textWidth, 34f), detail);
            Text.Font = GameFont.Small;

            float bx = inner.x + inner.width - ButtonWidth;
            if (Widgets.ButtonText(new Rect(bx, inner.y, ButtonWidth, 24f), "Approve"))
            {
                architect.Approve(room);
            }
            if (Widgets.ButtonText(new Rect(bx, inner.y + 26f, ButtonWidth, 24f), "Elsewhere"))
            {
                if (!architect.Relocate(room))
                {
                    Messages.Message("The architect couldn't find a better spot for that room.",
                        MessageTypeDefOf.RejectInput, false);
                }
            }

            // A separate, narrower reject button under the label text.
            if (Widgets.ButtonText(new Rect(inner.x, inner.y + 42f, 80f, 22f), "Dismiss"))
            {
                architect.Reject(room);
            }

            // Clicking the row jumps the camera to the proposed site.
            if (Widgets.ButtonInvisible(new Rect(inner.x + 84f, inner.y + 42f, textWidth - 84f, 22f)))
            {
                CameraJumper.TryJump(room.Rect.CenterCell, map);
            }

            return vy + 74f;
        }

        private float DrawActiveRoom(BaseArchitect architect, PlannedRoom room, float width, float vy)
        {
            Rect row = new Rect(0f, vy, width, 28f);
            if (Mouse.IsOver(row))
            {
                Widgets.DrawHighlight(row);
            }

            string status = room.state == RoomPlanState.Done ? "laid out" : "building";
            Widgets.Label(new Rect(row.x + 4f, row.y, width - 180f, 24f),
                $"{room.Label.CapitalizeFirst()}  {status}");

            if (Widgets.ButtonText(new Rect(width - 170f, row.y, 76f, 24f), "Go to"))
            {
                CameraJumper.TryJump(room.Rect.CenterCell, map);
            }
            if (Widgets.ButtonText(new Rect(width - 88f, row.y, 84f, 24f), "Forget"))
            {
                architect.Reject(room);
            }

            return vy + 32f;
        }
    }
}
