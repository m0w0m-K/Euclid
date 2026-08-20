# ADOFAI 3.3.0 compatibility notes

Euclid is currently developed as a standalone UMM mod against ADOFAI 3.3.0. The project deliberately references the assemblies installed with the game instead of bundling copied/decompiled game binaries.

## Reference root

Default game root:

```text
C:\Program Files (x86)\Steam\steamapps\common\A Dance of Fire and Ice
```

Managed assemblies:

```text
A Dance of Fire and Ice_Data\Managed\
```

UMM assembly used by the project:

```text
A Dance of Fire and Ice_Data\Managed\UnityModManager\UnityModManager.dll
```

`Euclid.csproj` accepts `GameDir` or `ADOFAI_DIR`, so a different Steam library does not require editing every `<HintPath>`.

## Why `GameCompat.cs` exists

ADOFAI editor internals are not a stable mod API. Euclid therefore keeps version-sensitive access concentrated in two files:

- `GameCompat.cs`: `scnEditor`, inspector panels, selected events/floors, event panel refresh, SaveState/apply calls, editor object lookup
- `LevelEventCompat.cs`: access to `LevelEvent` raw dictionary/indexer values

Feature code should call these compatibility helpers rather than directly adding new reflection against private editor members.

## Event write sequence used by Euclid

`CameraFrameEditor.TrySetVectorProperty` is the reference implementation for changing an editor event without leaving the editor in an inconsistent state:

1. obtain `scnEditor.instance`
2. optionally save an undo state
3. write the `LevelEvent` property through `LevelEventCompat`
4. clear the property's disabled/inherited state when required
5. apply properties to real events
6. mark the editor dirty/unsaved
7. refresh property text and, when appropriate, the event panel

The exact method/member names may change in later ADOFAI versions; adapt the compatibility layer first.

## Porting to a newer game version

Use this order:

1. point `GameDir` at the updated game and run a Release build
2. fix compile-time API changes first
3. launch with UMM logging visible and check the `Euclid loaded` line for game/Unity versions
4. verify editor tab creation and tab selection/unselection
5. verify tile selection and point picking
6. verify construction rendering order (`world/tiles < Euclid overlays < editor UI`)
7. verify event reads (`MoveCamera`, track position/move, free roam)
8. verify event writes, undo, unsaved state, and inspector refresh
9. verify UMM options and localization
10. run `scripts/check_project.ps1` before packaging

## Dependency policy

Euclid 0.7.62 remains standalone apart from Unity Mod Manager itself. It intentionally does not require EditorTabLib or an external localization mod.

EditorTabLib is still useful as historical sample code for custom editor tabs, but its repository was archived in 2025. Euclid's internal-tab implementation should therefore be maintained independently rather than reintroducing it as a runtime dependency.
