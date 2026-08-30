# Room Helper — a RimWorld mod

**Stop agonising over base layout. Let your colonists work out what to build and where,
and just say yes.**

Room Helper adds a **Room Helper** tab to the Architect menu with a **base architect**
that plans your colony for you, plus manual room tools for when you want to place
something yourself.

You decide *whether*. Your colonists decide *what*, *where* and *how it's arranged* —
which is the whole point if, like most of us, you'd rather not stare at an empty patch
of dirt wondering how big to make the bedroom.

![Base architect icon](Textures/RoomHelper/UI/Architect.png)

---

## The base architect

Left to itself, the architect quietly does what a good colony planner would:

1. **Works out what the colony is short of.** One bed per colonist, a dining table per
   six, a stove, hospital beds once you're past four colonists, storage, a rec room, a
   workshop, a research bench. It counts what you've already built *and* what's already
   planned, so it won't nag you about a bedroom that's half-finished.
2. **Picks a genuinely good spot.** Candidate sites are scored on compactness (keeping
   the base tight rather than sprawling), adjacency (the kitchen wants to be near the
   storage room and the dining room), overhead rock, and how much mining it would cost.
3. **Decides mountain or open ground per site.** Both are scored by the *same* function,
   so the map decides: it digs into rock where there's good rock to dig — free walls,
   overhead cover against drop pods — and builds in the open where there isn't. A slider
   lets you lean one way if you'd rather, but the default is "let the map decide."
4. **Proposes it to you.** You get a letter and a coloured outline on the map. Open the
   **Base architect** window to **Approve**, ask for **Elsewhere** (re-roll the location),
   or **Dismiss**.
5. **Builds it as the ground frees up.** Approve a room carved into a cliff and the
   architect designates the rock for mining, drops walls in as cells open up, and only
   lays out the furniture once the interior is genuinely clear — so you don't end up with
   a bed wedged into the one corner that got mined first.

Nothing happens without your say-so unless you want it to — there's a **"Build without
asking"** toggle if you'd rather it just get on with it.

## Manual tools

- **Plan room** — pick a room type, then drag out the footprint. Walls go on the edge
  you drag; the interior is floored and furnished automatically.
- **Auto-place room** — pick a room type, then click roughly where you'd like it. Room
  Helper searches outward for the nearest empty, buildable patch that fits.

Everything placed by either path is a **standard blueprint**, so it integrates with the
base game: deconstruct, re-roof, recolour or rearrange it however you like afterwards.
Your colonists still gather materials and build on their own schedule.

**No Harmony, no dependencies.** Safe to add or remove from a save at any time.

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

### Letting the architect run the base
1. Open the **Architect** menu → **Room Helper** tab → **Base architect**.
2. Leave **"Let colonists plan the base"** ticked. That's it.
3. Roughly once an in-game hour it checks the colony. When something's missing you get a
   letter and an outline on the map.
4. Open the window and hit **Approve**, **Elsewhere**, or **Dismiss**.
5. Blueprints (and mining orders, for a dug-in room) appear. Your colonists build them as
   normal — assign construction work and keep materials stocked.

Use **Plan something now** in that window if you don't want to wait for the next check.

### Placing a room yourself
1. **Room Helper** tab → **Plan room** or **Auto-place room**.
2. Pick a room type from the list.
3. **Plan room:** drag a rectangle for the room's outer footprint.
   **Auto-place room:** click a rough spot and let the mod find the exact location.

Tips:
- Walls sit *on* the rectangle you drag, so a 7×7 drag gives you a 5×5 interior.
- Blue outline = planned on open ground. Amber outline = to be carved out of rock.
- A mountain room leaves the surrounding rock standing as its walls — that's why it needs
  fewer materials but more mining.
- **Prison cell** and **barracks** are never auto-proposed (the architect can't know you
  intend to take prisoners, and barracks compete with bedrooms) — place those by hand.
- After a prison cell is built, click the bed and tick *For prisoners* to convert the room.
- Anything the mod can't place (blocked ground, a bench from a DLC you don't own) is
  simply skipped — the rest of the room still goes down.

### Settings
**Options → Mod settings → Room Helper** covers autonomy (**Build without asking**), how
often the colony is checked, how many proposals can queue up, whether mining is allowed,
and a **site preference** slider — left for open ground, right for mountain, centre (the
default) to let each site win on its own merits.

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

### Teaching the architect a new room

A template becomes something the architect will *propose on its own* by adding a
`<needCheck>` block — no code, just data:

```xml
<needCheck>
  <satisfiedBy><li>Bed</li><li>DoubleBed</li></satisfiedBy>  <!-- what counts -->
  <perColonists>1</perColonists>   <!-- want one per colonist -->
  <minimumCount>0</minimumCount>   <!-- ...and at least this many regardless -->
  <minColonists>0</minColonists>   <!-- ignore until the colony is this big -->
  <priority>100</priority>         <!-- higher wins when needs compete -->
  <maxRooms>12</maxRooms>          <!-- never auto-build more than this -->
  <provides>1</provides>           <!-- how many this room contributes -->
  <providesThing>Bed</providesThing>
</needCheck>
<nearTemplates><li>RH_DiningRoom</li></nearTemplates>  <!-- likes to sit near these -->
<allowMountain>true</allowMountain>
<allowOpenGround>true</allowOpenGround>
```

Set `<autoPropose>false</autoPropose>` to keep a room manual-only (as barracks and prison
cells are). Because needs are counted against what's *already built or planned*, a room
you place by hand quietly satisfies the same need.

---

## How it's built

| Path | What's there |
|------|--------------|
| `About/About.xml` | Mod metadata |
| `Defs/DesignationCategoryDefs/` | The **Room Helper** Architect tab |
| `Defs/RoomTemplateDefs/` | The room layouts and need rules (data) |
| `Source/RoomHelper/` | C# source (see below) |
| `Textures/RoomHelper/UI/` | Designator icons |
| `Assemblies/` | Compiled `RoomHelper.dll` (built by CI) |
| `.github/workflows/build.yml` | Compiles the mod and commits the DLL |

The C# splits into a planning half and an execution half:

| File | Responsibility |
|------|----------------|
| `ColonyNeeds.cs` | What is the colony short of? |
| `SiteScorer.cs` | Where should it go — and is this a mountain or open-ground site? |
| `BaseArchitect.cs` | `MapComponent` tying it together: check, propose, materialise |
| `PlannedRoom.cs` | One planned room; saves with the game, builds incrementally |
| `RoomPlanner.cs` | Rectangle + template → wall/door/floor/furniture blueprints |
| `Dialog_BaseArchitect.cs` | Approve / relocate / dismiss UI |
| `RoomHelperMod.cs` | Mod settings |

Everything is placed through `GenConstruct.PlaceBlueprintForBuild` and validated with
`GenConstruct.CanPlaceBlueprintAt`. No game methods are patched and there are no
dependencies, so it's compatible with virtually everything.

> **Heads up:** this mod was written and reviewed carefully, every RimWorld API call is
> verified against the 1.6 reference assemblies, and CI proves it compiles — but it has
> **not** been play-tested inside a live RimWorld session. Please try it on a throwaway
> save first and open an issue if anything misbehaves.

## License

Released under the [MIT License](LICENSE).
