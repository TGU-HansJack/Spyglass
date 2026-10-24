# Compatibility regression runner

See the repository's [compatibility report](../../COMPATIBILITY-FORK.md) for coverage and limits.

Build against Vintage Story 1.22.3, then run once per installed game version:

```powershell
dotnet build tests/CompatibilityTests/CompatibilityTests.csproj -c Release -p:VINTAGE_STORY=E:/MVL/Release/1.22.3
dotnet tests/CompatibilityTests/bin/Release/net10.0/CompatibilityTests.dll <game-directory> <fork-dll> [optional-mod-dll-directory]
```

The optional directory supplies Scribe.dll, Scribe.Core.dll, Gui.dll, WalkableEntities.dll and their dependencies from the audited modpack. These binaries are not redistributed. With them, the runner performs 53 assertions. Without them, it skips the five Scribe/Walkable checks.

This uses actual game assemblies and Harmony with mock APIs and selected uninitialized engine objects. It checks patch installation and isolated behavior without opening a world or rendering frames.
