# Room Helper — a RimWorld mod

**Stop agonising over room layouts. Pick a room type, and let your colony handle the rest.**

Room Helper adds a **Room Helper** tab to the Architect menu. Choose a room — bedroom,
barracks, dining room, kitchen, hospital, prison cell, storage, rec room, workshop or
research lab — and either drag out an area or just click a rough spot. Room Helper lays
down the walls, a door, the flooring and the furniture as ordinary **blueprints**, and
your colonists build the whole thing with their normal construction jobs.

You decide *what* you need. The mod decides *how it's laid out* and *where it fits*, and
your colonists put it together — exactly the "delegate the layout to someone else"
workflow this was built for.

![Plan / Auto-place icons](Textures/RoomHelper/UI/PlanRoom.png)

---

## What it does

- **Plan room** — pick a room type, then drag out the footprint. Walls go on the edge
  you drag; the interior is floored and furnished automatically.
- **Auto-place room** — pick a room type, then click roughly where you'd like it. Room
  Helper searches outward for the nearest empty, buildable patch that fits and lays the
  room out there. This is the "let a colonist figure out where it should go" button.
- Everything placed is a **standard blueprint**, so it integrates with the base game:
  deconstruct, re-roof, recolour or rearrange it however you like afterwards.
- **No Harmony, no dependencies.** Safe to add or remove from a save at any time.

Because the mod only *plans*, your colonists still gather materials and build on their
own schedule, just like anything else you place from the Architect menu.

---

## Installing

### Option A — download this repo (easiest)
1. Click **Code ▸ Download ZIP** on GitHub (or clone the repo).
2. Make sure the folder contains `Assemblies/RoomHelper.dll` (CI builds and commits it;
   see below if it's missing).
3. Copy the whole folder into your RimWorld `Mods` directory:
   - **Windows:** `...\SteamLibrary\steamapps\common\RimWorld\Mods\`
   - **macOS:** `~/Library/Application Support/Steam/steamapps/common/RimWorld/Mods/` (or
     `RimWorld.app/Mods`)
   - **Linux:** `~/.steam/steam/steamapps/common/RimWorld/Mods/`
4. Launch RimWorld, open **Mods**, enable **Room Helper**, and restart.

### Option B — build it yourself
Requires the [.NET SDK](https://dotnet.microsoft.com/download) (8.0+):

```bash
dotnet build Source/RoomHelper/RoomHelper.csproj -c Release
```

This writes `Assemblies/RoomHelper.dll`. Then follow steps 3–4 above.

> **Target version:** RimWorld **1.6**. The C# builds against the
> [`Krafs.Rimworld.Ref`](https://www.nuget.org/packages/Krafs.Rimworld.Ref) reference
> assemblies (no RimWorld install needed to compile).

---

## Using it in-game

1. Open the **Architect** menu and choose the **Room Helper** tab.
2. Click **Plan room** or **Auto-place room**.
3. Pick a room type from the list.
4. **Plan room:** drag a rectangle for the room's outer footprint.
   **Auto-place room:** click a rough spot and let the mod find the exact location.
5. Blueprints appear. Your colonists build them as normal — assign construction work and
   make sure you have the materials.

Tips:
- Walls sit *on* the rectangle you drag, so a 7×7 drag gives you a 5×5 interior.
- Auto-place only uses open, buildable ground and won't overwrite existing buildings.
- **Prison cell:** it builds as a normal bedroom. After it's built, click the bed and
  tick *For prisoners* to convert the room into a cell.
- Anything the mod can't place (blocked ground, a bench from a DLC you don't own) is
  simply skipped — the rest of the room still goes down.

---

## Customising or adding rooms

Room layouts are plain XML data in
[`Defs/RoomTemplateDefs/RoomTemplates.xml`](Defs/RoomTemplateDefs/RoomTemplates.xml) —
no coding required. Each `RoomHelper.RoomTemplateDef` sets the default size, wall/door
materials, floor and a furniture list. A furniture entry looks like:

```xml
<li>
  <thingDef>Bed</thingDef>              <!-- primary thing to place -->
  <alternates><li>DoubleBed</li></alternates>  <!-- tried if the primary is missing -->
  <stuff>WoodLog</stuff>                <!-- optional material -->
  <placement>AlongWall</placement>      <!-- Corner | Center | AlongWall | AdjacentToLast | Free -->
  <count>1</count>
  <optional>true</optional>             <!-- skip silently if it can't be placed -->
</li>
```

`AdjacentToLast` places a piece next to the previous one facing it — that's how dining
chairs get pulled up around a table. Add your own `RoomHelper.RoomTemplateDef` blocks to
create entirely new room types; they show up in the menu automatically.

---

## How it's built

| Path | What's there |
|------|--------------|
| `About/About.xml` | Mod metadata |
| `Defs/DesignationCategoryDefs/` | The **Room Helper** Architect tab |
| `Defs/RoomTemplateDefs/` | The room layouts (data) |
| `Source/RoomHelper/` | C# source (designators + layout planner) |
| `Textures/RoomHelper/UI/` | Designator icons |
| `Assemblies/` | Compiled `RoomHelper.dll` (built by CI) |
| `.github/workflows/build.yml` | Compiles the mod and commits the DLL |

The planner (`Source/RoomHelper/RoomPlanner.cs`) converts a rectangle + a template into
wall/door/floor/furniture blueprints via `GenConstruct.PlaceBlueprintForBuild`. No game
methods are patched, so it's compatible with virtually everything.

> **Heads up:** this mod was written and reviewed carefully, and CI verifies that it
> compiles, but it hasn't yet been play-tested inside a live RimWorld session. Please try
> it on a throwaway save first and open an issue if anything misbehaves.

## License

Released under the [MIT License](LICENSE).
