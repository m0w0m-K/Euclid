# Euclid Development Guide

This document is for developers working on **Euclid**, an editor utility mod for **A Dance of Fire and Ice**.

The public-facing `README.md` is intended for mod users. This file focuses on architecture, build flow, editor integration, geometry, snapping, compatibility, and maintenance.

## Target environment

- A Dance of Fire and Ice: **3.3.0**
- Unity: **6000.3.10f1**
- Unity Mod Manager
- C# / .NET Framework 4.8 (`net48`)
- Windows Steam installation

ADOFAI's classes in `Assembly-CSharp.dll` are not a stable public modding API. Fields, methods, enum values, event internals, and editor UI hierarchy may change between game versions.

For version-sensitive code, keep compatibility logic concentrated in `GameCompat.cs` and `LevelEventCompat.cs` whenever practical.

## Repository structure

The main source files are currently kept at the repository root.

```text
Euclid/
├─ Startup.cs
├─ EuclidMod.cs
├─ EuclidBehaviour.cs
│
├─ EuclidPanel.cs
├─ EuclidPanel.Construction.cs
├─ EuclidPanel.Interaction.cs
├─ EuclidPanel.Style.cs
├─ EuclidPanel.UiFactory.cs
│
├─ ConstructionShapeTool.cs
├─ ConstructionShapeCanvasOverlay.cs
├─ GuideLineTool.cs
├─ CoordinateSnapTool.cs
│
├─ CameraFrameEditor.cs
├─ CameraFrameSnapshot.cs
├─ CameraFrameOverlay.cs
├─ EffectOverlayVisual.cs
│
├─ GameCompat.cs
├─ LevelEventCompat.cs
├─ TileSelectionOrderTracker.cs
├─ MeasureSnapshot.cs
├─ Vector2d.cs
│
├─ Localization/
├─ scripts/
│  └─ check_project.ps1
│
├─ Info.json
├─ Euclid.csproj
├─ BUILD_RELEASE.cmd
├─ README.md
├─ DEVELOPMENT.md
├─ ADOFAI_MODDING_GUIDE.md
└─ PORTING-3.3.0.md
```

## Runtime entry and lifecycle

### `Startup.cs`

The Unity Mod Manager entry point is intentionally small.

`Info.json` points to:

```text
Euclid.Startup.Load
```

The startup class should only hand initialization to the main mod class instead of accumulating feature code.

### `EuclidMod.cs`

Handles the UMM-facing part of the mod:

- load/bootstrap
- enable/disable state
- UMM Options UI
- settings persistence
- color configuration

`Settings.json` is runtime state and should not be committed.

### `EuclidBehaviour.cs`

Acts as the runtime coordinator.

It is responsible for the per-frame flow between the editor, Euclid panel, construction tools, overlays, and input handling.

When debugging a feature that updates correctly only after selecting another tile, changing a tab, or reopening a panel, start here and check the refresh order.

## Editor panel architecture

Euclid integrates directly into the ADOFAI editor instead of requiring EditorTabLib.

### `EuclidPanel.cs`

Owns the high-level editor panel lifecycle:

- locate ADOFAI editor UI
- create the Euclid tab
- create/open/close the main panel
- create the floating Shape Info panel
- keep panel placement synchronized with the game editor UI

The Euclid tab clones a built-in ADOFAI inspector tab where possible so its sprite, tint, spacing, and selected/unselected appearance match the native UI.

### `EuclidPanel.UiFactory.cs`

Reusable Unity UI construction helpers.

Use this file when adding buttons, labels, input fields, rows, scroll areas, sliders, and cloned controls.

Do not manually duplicate UI setup in multiple feature files if a reusable builder can handle it.

### `EuclidPanel.Style.cs`

Captures native editor style and contains presentation-related helpers.

### `EuclidPanel.Interaction.cs`

Coordinates editor state with Euclid panel state.

This is a useful starting point when a UI control displays stale values or selection state is not synchronized with ADOFAI.

### `EuclidPanel.Construction.cs`

Owns most construction-shape UI:

- shape list
- shape type switching
- P1/P2 editors
- tile/drawn-point picking
- shape name
- shape color editor
- geometry information
- detail panel content

The geometry information currently shown in Shape Info is:

- Line: slope `a`, y-intercept `b`, angle `θ`
- Circle: radius `r`

For a line through `(x1, y1)` and `(x2, y2)`:

```text
a = (y2 - y1) / (x2 - x1)
b = y1 - a*x1
```

`θ` represents the undirected line orientation, normalized into `[0°, 180°)`.

For a vertical line:

```text
a = ∞
b = —
θ = 90°
```

For a circle, P1 is the center and P2 is a point on the circumference:

```text
r = distance(P1, P2)
```

## Construction geometry

### `ConstructionShapeTool.cs`

This is the primary state and geometry layer for construction objects.

Supported construction primitives:

- Point
- Line
- Circle

The code still contains compatibility handling for `PerpendicularBisector`, but the construction UI normalizes that shape to a regular Line when appropriate.

Responsibilities include:

- create/delete/select shapes
- names
- visibility
- colors
- P1/P2 state
- geometry calculations
- intersection calculations
- snap candidates

Keep calculations here or in a dedicated geometry layer rather than in UI callbacks.

### Point provenance

A construction endpoint may originate from:

- a selected ADOFAI tile
- a previously drawn construction point
- direct coordinate input

Direct coordinate edits clear tile/point provenance once the new value is valid.

### Point vs line/circle P2

The detail panel keeps the P2 section visible even for Point shapes, but disables it. This preserves a stable panel height while switching between Point, Line, and Circle.

## Construction rendering

### `ConstructionShapeCanvasOverlay.cs`

Construction geometry is rendered on a dedicated Canvas instead of relying entirely on IMGUI.

The intended visual stack is:

```text
ADOFAI world / tiles
        ↓
Euclid construction + effect overlay
        ↓
ADOFAI editor UI
```

This prevents construction lines and effect markers from drawing over inspector panels.

Do not assume `GUI.depth` can solve Canvas ordering. IMGUI and Unity UI Canvas do not share the same ordering system.

Some mouse hit-testing still happens through `OnGUI` where consuming the GUI event before normal editor handling is useful.

## Guide lines

### `GuideLineTool.cs`

Handles temporary guide geometry and state.

Guide functionality includes lines defined from selected tiles and custom anchor/direction data.

Guide-line math uses a parametric representation conceptually equivalent to:

```text
P(t) = A + tD
```

where:

- `A` is the anchor
- `D` is the direction
- `t` is the line parameter

This representation is convenient for projection and forward/backward stepping.

## Coordinate snapping

### `CoordinateSnapTool.cs`

This is the main bridge between geometric snap results and mutable ADOFAI event data.

Start here when:

- a snap edits the wrong event property
- an effect marker has the wrong origin
- world coordinates and tile-unit coordinates are mixed up
- dragging/snap preview differs from the value eventually stored in the event

### Coordinate spaces

Be explicit about coordinate space.

Euclid commonly works with:

- world-space coordinates
- ADOFAI tile-unit offsets
- screen-space mouse coordinates
- Canvas-local coordinates

`GameCompat.GetTileSize()` is used when converting tile-unit event properties to world displacement.

Avoid passing a `Vector2` between layers without knowing which coordinate system it represents.

## Position-like event targets

Several editor effects expose a `positionOffset`-like coordinate.

Euclid wraps those through a `CoordinateTarget` so UI/snapping code does not need to know every event's storage format.

Currently important examples include:

- Move Track
- Position Track
- Free Roam
- related position-offset events found through metadata/raw event data

### Position Track reference point

`PositionTrack` is special because its `positionOffset` is relative to a tile reference.

For `relativeTo = ThisTile`, Euclid needs the tile position **before the currently focused PositionTrack event is applied**.

The editor's current floor transform may already include the focused PositionTrack displacement. Therefore, using `floor.transform.position` directly as the reference can double-count the active event.

The reference used by Euclid is conceptually:

```text
referenceWorld = currentDisplayedFloorPosition
               - currentPositionOffset * tileSize
```

Then the event target becomes:

```text
targetWorld = referenceWorld
            + positionOffset * tileSize
```

This same reference point must be used for both:

- the tile/reference marker drawn in the effect overlay
- conversion from a snapped world-space point back into `positionOffset`

If those use different origins, the marker may look correct while the saved event coordinate is wrong.

For `relativeTo = Start` or `End`, the corresponding origin tile is used directly.

When modifying PositionTrack behavior, check both:

```text
TryGetPositionOffsetTarget(...)
GetPositionOffsetReferencePoint(...)
```

and verify the overlay path through:

```text
TryGetFocusedEffectVisual(...)
```

## Effect overlays

### `EffectOverlayVisual.cs`

Small data structure describing effect visualization.

### `ConstructionShapeCanvasOverlay.cs`

Also renders effect markers below the normal editor UI.

Effect palettes are configurable in UMM Options.

Different visual concepts use separate configured colors, such as:

- tile/reference marker
- target-position marker
- segment between them
- effect-name label

### Camera visualization

Camera-specific logic is split across:

- `CameraFrameSnapshot.cs`
- `CameraFrameEditor.cs`
- `CameraFrameOverlay.cs`

`CameraFrameSnapshot` reads/interprets selected Move Camera state.

`CameraFrameEditor` writes edited camera data back to the ADOFAI event.

`CameraFrameOverlay` handles camera frame visualization and interaction.

## Editing `LevelEvent`

Changing a raw event value is usually not sufficient by itself.

ADOFAI editor mutations may also require:

```text
SaveState
    ↓
write raw property
    ↓
clear disabled/inherited state if necessary
    ↓
ApplyPropertiesToRealEvents
    ↓
mark unsaved changes
    ↓
refresh inspector/property text
```

### `LevelEventCompat.cs`

Wraps access to event raw values.

Use it instead of scattering assumptions about `LevelEvent`'s internal storage throughout feature code.

### `GameCompat.cs`

Contains version-sensitive interactions with ADOFAI editor internals.

If an ADOFAI update breaks Euclid, inspect this file first.

Examples of things that belong here:

- locating editor panels
- accessing selected events
- finding floors
- reading tile size
- invoking editor refresh/save behavior
- reflection fallbacks for renamed/private members

## Tile selection tracking

### `TileSelectionOrderTracker.cs`

Tracks selection/click order because Euclid frequently distinguishes the first selected tile from the last selected tile.

When tile-based point selection behaves incorrectly after multi-select operations, inspect this class and the construction-panel selection path together.

## Localization

Localization resources live in:

```text
Localization/*.lang
```

They are UTF-8 tab-separated key/value files embedded into `Euclid.dll` by the project file.

Current language files should contain the same key set as `en.lang`.

Unknown languages and missing translations fall back to English.

When adding a localization key:

1. Add it to `en.lang`.
2. Add the same key to every other `.lang` file.
3. Run `scripts/check_project.ps1`.

## Settings

UMM Options are managed by `EuclidMod.cs`.

Current settings include overlay visibility and color palettes.

Runtime configuration is persisted to `Settings.json` and intentionally excluded by `.gitignore`.

When adding a setting, consider migration/default behavior so existing user settings remain valid.

## Build

The project targets:

```text
net48
```

The project references DLLs directly from the installed ADOFAI directory instead of bundling the game's assemblies.

Default game path:

```text
C:\Program Files (x86)\Steam\steamapps\common\A Dance of Fire and Ice
```

Build with:

```bat
BUILD_RELEASE.cmd
```

An alternate game path can be supplied:

```bat
BUILD_RELEASE.cmd "D:\SteamLibrary\steamapps\common\A Dance of Fire and Ice"
```

Or directly:

```bat
dotnet build Euclid.csproj -c Release "-p:GameDir=C:\Program Files (x86)\Steam\steamapps\common\A Dance of Fire and Ice"
```

## Release package

A successful Release build creates the normal UMM package in `dist`.

```text
Euclid-<version>.zip
└─ Euclid/
   ├─ Info.json
   └─ Euclid.dll
```

The UMM release ZIP is the file intended for users.

Do not distribute the source ZIP as the UMM installer.

GitHub automatically generates source archives for tagged releases, so a separate manually created source ZIP is normally unnecessary once the source is committed to the repository.

## Project validation

Before a release, run:

```powershell
scripts\check_project.ps1
```

The checker is intended to catch source-level mistakes such as:

- invalid `Info.json`
- invalid project XML
- version mismatch between `Info.json` and `Euclid.csproj`
- accidental external dependencies
- old branding
- localization-key mismatch
- accidentally restored obsolete files

This is not a replacement for compiling and testing in ADOFAI.

## Version updates

When changing the mod version, update both:

```text
Info.json
Euclid.csproj
```

The Release MSBuild target derives the generated ZIP name from the project version.

## ADOFAI update workflow

When a new ADOFAI version breaks the mod, use this order:

1. Confirm the failure on the new game version.
2. Check UMM loading/log output first.
3. Inspect `Assembly-CSharp.dll` with ILSpy.
4. Check `GameCompat.cs` and `LevelEventCompat.cs` for changed members.
5. Check editor hierarchy assumptions used by `EuclidPanel`.
6. Check event schemas/properties used by `CoordinateSnapTool` and camera code.
7. Compile against the new game's Managed assemblies.
8. Test panel creation, shape editing, snapping, camera overlay, effect overlay, localization, and settings persistence.
9. Update compatibility notes in `PORTING-*.md`.

Do not patch random feature files with duplicated reflection workarounds if the incompatibility can be isolated in the compatibility layer.

## Reverse engineering

The main game assembly to inspect is:

```text
A Dance of Fire and Ice_Data\Managed\Assembly-CSharp.dll
```

Useful types include:

```text
scnEditor
InspectorPanel
scrFloor
ADOFAI.LevelEvent
LevelEventType
RDString
```

Useful tool:

- ILSpy: https://github.com/icsharpcode/ILSpy

Search not only for class names, but also:

- strings visible in the editor
- event property names
- enum names
- fields referenced by nearby editor code

## External modding references

General references used while developing Euclid:

- Unity Mod Manager: https://github.com/newman55/unity-mod-manager
- UMM mod creation Wiki: https://github.com/newman55/unity-mod-manager/wiki/How-to-create-a-mod-for-unity-game
- FLOWERs ADOFAI Mod Development Guide: https://github.com/FLOWERs-Modding/ADOFAI-Mod-Development-Guide
- AdofaiModTemplate: https://github.com/PizzaLovers007/AdofaiModTemplate
- AdofaiTweaks: https://github.com/PizzaLovers007/AdofaiTweaks
- JipperOverlayer: https://github.com/adofaiex/JipperOverlayer
- Harmony: https://github.com/pardeike/Harmony
- EditorTabLib: https://github.com/tjwogud/EditorTabLib

EditorTabLib is useful as a historical/reference implementation, but Euclid does not require it at runtime.

## Where to start for common changes

If the request is about...

### Shape Info UI

Start with:

```text
EuclidPanel.Construction.cs
```

### Shape calculations/intersections

Start with:

```text
ConstructionShapeTool.cs
Vector2d.cs
```

### Shape/effect visual drawing

Start with:

```text
ConstructionShapeCanvasOverlay.cs
```

### Effect coordinate snapping

Start with:

```text
CoordinateSnapTool.cs
CameraFrameEditor.cs
LevelEventCompat.cs
```

### Wrong tile/effect reference position

Start with:

```text
CoordinateSnapTool.GetPositionOffsetReferencePoint
CoordinateSnapTool.TryGetPositionOffsetTarget
CoordinateSnapTool.TryGetFocusedEffectVisual
```

### ADOFAI update compatibility

Start with:

```text
GameCompat.cs
LevelEventCompat.cs
PORTING-3.3.0.md
```

### Tab/panel styling or placement

Start with:

```text
EuclidPanel.cs
EuclidPanel.UiFactory.cs
EuclidPanel.Style.cs
```

### Input priority

Start with:

```text
EuclidBehaviour.cs
EuclidPanel.Interaction.cs
TileSelectionOrderTracker.cs
```

## Maintenance rules

A few rules keep the project easier to modify:

- Keep ADOFAI-version-specific reflection in compatibility files.
- Keep geometry math separate from Unity UI code.
- Use one reference origin consistently for display, snap preview, and event write-back.
- Do not restore obsolete fallback implementations unless there is a verified need.
- Prefer cloning/capturing native ADOFAI styling instead of hardcoding a parallel visual system.
- Keep overlays on a Canvas below the editor UI when they should not cover panels.
- Treat current source code as the source of truth; old README changelog notes may describe behavior that has since been replaced.
- Compile and test against the actual installed ADOFAI version before publishing a release.

## Release checklist

Before publishing a GitHub release:

```text
[ ] Update Info.json version
[ ] Update Euclid.csproj version
[ ] Run scripts/check_project.ps1
[ ] Build Release successfully
[ ] Launch ADOFAI with only required runtime dependencies
[ ] Verify Euclid tab opens/closes
[ ] Verify Point / Line / Circle editing
[ ] Verify shape geometry information
[ ] Verify color editing
[ ] Verify intersection generation
[ ] Verify snapping
[ ] Verify PositionTrack reference marker and offset write-back
[ ] Verify Move Camera frame
[ ] Verify effect overlays
[ ] Verify localization
[ ] Verify UMM options persist
[ ] Install generated dist/Euclid-<version>.zip through UMM
[ ] Commit source
[ ] Tag the release
[ ] Attach the UMM ZIP to GitHub Release
```
