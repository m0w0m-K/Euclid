# ADOFAI 3.3.0 compatibility notes

Euclid 1.0.0 is developed as a standalone Unity Mod Manager mod against ADOFAI 3.3.0. The project references assemblies from the installed game instead of bundling copied or decompiled game binaries.

## Reference root

Default game root:

```text
C:\Program Files (x86)\Steam\steamapps\common\A Dance of Fire and Ice
```

Managed assemblies:

```text
A Dance of Fire and Ice_Data\Managed\
```

UMM assemblies used by the project:

```text
A Dance of Fire and Ice_Data\Managed\UnityModManager\UnityModManager.dll
A Dance of Fire and Ice_Data\Managed\UnityModManager\0Harmony.dll
```

`Euclid.csproj` accepts `GameDir` or `ADOFAI_DIR`, so another Steam library does not require editing every `<HintPath>`.

## Compatibility boundaries

ADOFAI editor internals are not a stable mod API. Keep version-sensitive assumptions concentrated in:

- `GameCompat.cs`: `scnEditor`, inspector panels, selected events/floors, SaveState/apply/refresh calls, editor object lookup
- `LevelEventCompat.cs`: raw `LevelEvent` values, enum/wrapper normalization, property enabled/disabled state
- `EditorLevelLoadPatch.cs`: Harmony discovery of the concrete editor level-load coroutine

Feature code should use these integration points rather than adding unrelated reflection or patch assumptions directly into UI/geometry code.

## Editor level loading

Construction shapes are map-local helper state and are not serialized into `.adofai` files.

ADOFAI 3.3.0 can reuse editor objects across level changes, and `isLoading` may transition completely between Euclid frames. Therefore polling `levelData`, floor identity, path identity, or loading state is not sufficient as the primary load boundary.

Euclid patches the concrete `scnEditor.OpenLevelCo` load path and clears map-local state when an actual level load starts.

Expected behavior:

```text
Open another saved map      -> clear construction state
Reload the same map         -> clear construction state
Save / Save As              -> preserve construction state
Open file picker and cancel -> preserve construction state
```

When porting, verify that `OpenLevelCo` still exists or locate the replacement method that receives the committed path and performs the real editor load. Do not move the reset to an earlier no-argument file-picker method unless cancellation semantics are handled explicitly.

## Event write sequence used by Euclid

`CameraFrameEditor` is a useful reference for a normal discrete event edit:

1. obtain `scnEditor.instance`
2. optionally save one undo state
3. write through `LevelEventCompat`
4. change enabled/disabled state only when the edit semantically requires it
5. apply or commit through the host path
6. mark the editor dirty/unsaved
7. refresh property text/event panel when required

PositionTrack marker dragging is intentionally different: it previews raw offset changes during drag and defers the expensive host/floor commit until the edit boundary.

## Supported overlay scope

Euclid 1.0.0 supports coordinate visualization/editing for:

```text
MoveCamera / CameraMove
MoveTrack
PositionTrack
FreeRoam / FreeRoamRemove
```

`MoveDecorations` is intentionally excluded from coordinate markers and construction snapping because one event may affect multiple tagged decorations with different placement/reference state. A generic vector-property fallback must not silently reintroduce it.

## Porting to a newer game version

Use this order:

1. point `GameDir` at the updated game and run a Release build
2. fix compile-time API changes
3. launch with UMM logging visible and confirm game/Unity versions
4. verify `EditorLevelLoadPatch` finds the actual level-load method
5. verify open/reload/Save As/file-picker-cancel lifecycle behavior
6. verify Euclid tab creation and tab selection/unselection
7. verify tile selection and endpoint Select toggle behavior
8. verify construction rendering order (`world/tiles < Euclid overlays < editor UI`)
9. verify supported event reads and MoveDecorations exclusion
10. verify event writes, undo, unsaved state, and inspector refresh
11. run PositionTrack enabled/disabled and applied-state tests
12. verify camera frame position/zoom/rotation handles
13. verify all-effect markers, colors, UMM options, and localization
14. run `scripts/check_project.ps1`
15. build and install the generated UMM ZIP for a final runtime test

## Dependency policy

Euclid remains standalone apart from Unity Mod Manager and Harmony as supplied with the UMM installation. It does not require EditorTabLib, an external localization mod, or NuGet runtime packages.

EditorTabLib remains useful as historical sample code for custom editor tabs, but its archived repository should not be restored as a required runtime dependency.

## Release packaging

`Info.json` and `Euclid.csproj` must carry the same version.

A Release build creates:

```text
dist/Euclid-<version>.zip
└─ Euclid/
   ├─ Euclid.dll
   └─ Info.json
```

The source repository cannot substitute for a real build against the target ADOFAI Managed assemblies. Before tagging a release, install the generated ZIP through UMM and re-run the map lifecycle, construction, snapping, PositionTrack, camera-frame, and overlay smoke tests.
