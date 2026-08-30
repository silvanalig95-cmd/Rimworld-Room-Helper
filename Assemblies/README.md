# Assemblies

`RoomHelper.dll` is compiled from [`../Source/RoomHelper`](../Source/RoomHelper)
and placed here. The GitHub Actions **Build mod** workflow builds it automatically
and commits it back to the branch, so a checkout of this repo is a ready-to-run mod.

To build it yourself instead:

```bash
dotnet build Source/RoomHelper/RoomHelper.csproj -c Release
```

RimWorld loads every `.dll` in this folder at startup.
