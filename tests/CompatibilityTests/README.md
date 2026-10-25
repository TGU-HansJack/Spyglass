# Compatibility regression runner

See the repository's [compatibility report](../../COMPATIBILITY-FORK.md) for coverage and limits.

Build against Vintage Story 1.22.3, then run once per installed game version:

```powershell
dotnet build tests/CompatibilityTests/CompatibilityTests.csproj -c Release -p:VINTAGE_STORY=E:/MVL/Release/1.22.3
dotnet tests/CompatibilityTests/bin/Release/net10.0/CompatibilityTests.dll <game-directory> <fork-dll> [optional-mod-dll-directory]
```

The optional directory supplies Scribe.dll, Scribe.Core.dll, Gui.dll, WalkableEntities.dll and their dependencies from the audited modpack. These binaries are not redistributed. With them, the runner performs 776 assertions. Without them, it performs 771 assertions and skips the five Scribe/Walkable checks.

This uses actual game assemblies and Harmony with mock APIs and selected uninitialized engine objects. It checks patch installation and isolated behavior without opening a world or rendering frames.

For production Cairo lens previews and 90 drawing assertions, put the game's Lib directory on PATH and use `--lens-preview` as the third argument. This loads the actual item and glass textures and writes transparent PNG overlays to `artifacts/lens-preview`; these are isolated overlays, not in-game screenshots. The F4 regression intercepts the drawing helper to check callback routing; it does not simulate a live game frame.

The main runner expands and resolves both production lens recipes with the game recipe API against a mock collectible registry. It checks 121 metal/glass combinations, ingredient rejection, original model texture paths and metal durability entries. Run from the repository root.

The GPU optics runner performs 104 assertions after the irregular runoff update, including variable trail widths, non-fixed water paths, pauses and irregular trail lifetimes.
