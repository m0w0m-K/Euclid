# Euclid

Standalone Unity Mod Manager mod for the A Dance of Fire and Ice editor.

Current development target: ADOFAI 3.3.0 / Unity 6000.3.10f1 / .NET Framework 4.8.

Euclid does not require EditorTabLib or the old Localizations dependency. It creates and manages its own editor tab, UI, localization resources, construction geometry, snapping, and overlays.

## Main features

- editor-internal Euclid tab (`Å` icon)
- construction shapes: point, infinite line, circle
- P1/P2 picking from editor tiles and drawn construction points
- per-shape color and visibility
- guide-line and coordinate snapping helpers
- Move Camera frame visualization and position dragging
- focused effect markers for camera/track/free-roam related events
- UMM options for overlay visibility and colors
- built-in localization files for the languages currently supported by Euclid

## Project map

| File | Responsibility |
| --- | --- |
| `Startup.cs` | UMM entry point only |
| `EuclidMod.cs` | UMM callbacks, persistent options, logging, bootstrap |
| `EuclidBehaviour.cs` | per-frame runtime coordinator and input ordering |
| `EuclidPanel.cs` | tab/panel lifecycle and detached detail panel |
| `EuclidPanel.Construction.cs` | construction list, detail editor, point picking, inline RGBA/HEX editor |
| `EuclidPanel.Interaction.cs` | synchronization with ADOFAI editor state |
| `EuclidPanel.UiFactory.cs` | Unity UI creation and cloned native control styling |
| `EuclidPanel.Style.cs` | captured ADOFAI visual styles and text formatting |
| `ConstructionShapeTool.cs` | construction model and geometry |
| `ConstructionShapeCanvasOverlay.cs` | shape/effect rendering below ADOFAI editor UI |
| `GuideLineTool.cs` | guide geometry and state |
| `CoordinateSnapTool.cs` | snapping geometry to editable ADOFAI properties |
| `CameraFrameSnapshot.cs` | derives selected Move Camera state |
| `CameraFrameEditor.cs` | writes camera/event values back to the editor |
| `CameraFrameOverlay.cs` | camera-frame interaction and IMGUI hit handling |
| `MeasureSnapshot.cs` | internal selected-tile geometry snapshot used by editor tools |
| `TileSelectionOrderTracker.cs` | preserves editor tile click order |
| `GameCompat.cs` | reflection-based ADOFAI editor compatibility boundary |
| `LevelEventCompat.cs` | compatibility access to `LevelEvent` raw data |
| `EuclidText.cs` | embedded localization loading/fallback |
| `Localization/*.lang` | UTF-8 tab-separated localization resources |

For a more general explanation intended for future ADOFAI mods, read `ADOFAI_MODDING_GUIDE.md`.
For version-porting notes, read `PORTING-3.3.0.md`.

## Build

The project resolves game references from the installed ADOFAI directory. Default location:

```text
C:\Program Files (x86)\Steam\steamapps\common\A Dance of Fire and Ice
```

Command-line build:

```bat
dotnet build Euclid.csproj -c Release "-p:GameDir=C:\Program Files (x86)\Steam\steamapps\common\A Dance of Fire and Ice"
```

Or double-click `BUILD_RELEASE.cmd`. An alternate game directory can be passed as the first argument.

A successful Windows Release build creates:

```text
dist/
└─ Euclid/
   ├─ Euclid.dll
   └─ Info.json

dist/Euclid-0.7.62.zip
```

`dist/Euclid-0.7.62.zip` is the UMM installer package. Development no longer requires a script that directly overwrites the installed mod directory.

## Static project check

Before a release, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\check_project.ps1
```

The check verifies the UMM metadata, source/project version agreement, standalone dependency policy, localization key parity, current `Å` tab icon, and absence of old branding/dead bridge files.

## Important maintenance rules

1. Treat `Assembly-CSharp.dll` as a version-specific implementation detail, not a stable public API.
2. Put reflection and renamed/private editor-member handling in `GameCompat.cs` or `LevelEventCompat.cs` instead of scattering it through feature code.
3. Keep geometry/state separate from rendering and editor mutation. This is why Euclid has `ConstructionShapeTool`, `ConstructionShapeCanvasOverlay`, and `CoordinateSnapTool` as separate layers.
4. When changing an editor event, preserve undo/dirty/inspector refresh behavior. See `CameraFrameEditor.cs` for the sequence used by Euclid.
5. Do not copy game/Unity reference DLLs into the release archive. The project references them with `<Private>false>`.
6. When ADOFAI updates, compile against the new installed `Managed` directory first. Compiler errors and runtime reflection failures are the first compatibility checklist.

## Refactor note: 0.7.61

The source cleanup removed old paths that were no longer part of the live UI:

- removed the unused ADOFAI native `ColorField` reflection bridge
- removed legacy IMGUI-only snapshot/camera text helpers
- removed the old floating shape color-picker implementation and unused HEX fallback path; the current inline RGBA/HEX editor remains
- removed the unused legacy `GuideLineTool.DrawGui` path
- replaced the stale UI regression script with a project-level sanity check
- changed the development helper from direct build-and-install to release-package generation
- rewrote the documentation around the current standalone architecture

## UI/geometry note: 0.7.62

- reduced the vertical size of the inline RGBA slider handles
- extended construction-line rendering so lines remain visually continuous farther from P1
- Shape Info now shows `a`, `b`, and `θ` for lines and `r` for circles
- vertical lines display an undefined intercept instead of forcing a finite slope/intercept
