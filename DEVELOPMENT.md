# Euclid Development Guide

This document is for developers working on **Euclid**, an editor utility mod for **A Dance of Fire and Ice**.

`README.md` is user-facing. This file documents current architecture, invariants, fragile integration points, debugging strategy, and release verification. It is intentionally not a changelog.

## Target environment

- A Dance of Fire and Ice: **3.3.0**
- Unity: **6000.3.10f1**
- Unity Mod Manager
- C# / .NET Framework 4.8 (`net48`)
- Windows Steam installation

ADOFAI editor classes are not a stable public API. Fields, methods, event storage, enum wrappers, coroutine signatures, and Unity hierarchy details may change between game versions.

Keep version-sensitive access concentrated in integration/compatibility code. In particular:

```text
GameCompat.cs
LevelEventCompat.cs
EditorLevelLoadPatch.cs
```

Do not scatter new reflection or Harmony assumptions through feature code when one integration layer can own them.

---

## High-level architecture

### Runtime / bootstrap

```text
Startup.cs
EuclidMod.cs
EuclidBehaviour.cs
EditorLevelLoadPatch.cs
```

`Startup.cs` is the UMM entry point and should remain small. It initializes the mod and installs runtime hooks.

`EuclidMod.cs` owns UMM-facing state such as enable/disable, options UI, settings persistence, localization-facing configuration, and effect-overlay palettes.

`EuclidBehaviour.cs` is the frame coordinator. It owns high-level update ordering, snapshot refresh, panel ticking, scene-level point-pick priority, and fallback map-boundary observation.

`EditorLevelLoadPatch.cs` owns the authoritative editor-level load hook. ADOFAI can reuse editor, `levelData`, panel, and floor objects across loads, and a short loading interval can complete between Euclid `Update` calls. For this reason, map-local Euclid state is cleared from the real `scnEditor.OpenLevelCo` load path rather than relying only on polling identities.

### Panel / construction UI

```text
EuclidPanel.cs
EuclidPanel.Construction.cs
EuclidPanel.EndpointUi.cs
EuclidPanel.PointBinding.cs
EuclidPanel.Interaction.cs
EuclidPanel.UiFactory.cs
EuclidPanel.Style.cs
```

`EuclidPanel.Construction.cs` owns Shape Info, shape-list UI, endpoint position picking, and construction selection controls.

`EuclidPanel.PointBinding.cs` owns endpoint source binding and Pin behavior.

`EuclidPanel.EndpointUi.cs` owns endpoint validation and endpoint-specific UI behavior.

`EuclidPanel.UiFactory.cs` should be used for common rows, buttons, inputs, sliders, and style-preserving control creation.

### Geometry / snapping

```text
ConstructionShapeTool.cs
GuideLineTool.cs
CoordinateSnapTool.cs
Vector2d.cs
```

Keep geometry math out of UI callbacks when possible.

`CoordinateSnapTool.cs` is the central bridge between construction geometry and editable ADOFAI event properties.

### Effect editing / visualization

```text
CameraFrameEditor.cs
CameraFrameSnapshot.cs
CameraFrameOverlay.cs
CameraFrameTransformHandles.cs
EffectOverlayVisual.cs
EffectOverlayCollection.cs
AllEffectMarkerOverlayV2.cs
AllEffectMarkerSettings.cs
```

Selected effect markers and all-effect/background markers have different responsibilities:

- selected effect: interactive and may reflect pending edit state
- unselected effect: read-only visualization only

Do not let background marker enumeration mutate selected-effect edit caches.

### PositionTrack synchronization

```text
CoordinateSnapTool.cs
PositionTrackFocusSync.cs
PositionTrackMarkerDragFocus.cs
PositionTrackSnapCommitSync.cs
PositionTrackAppliedSync.cs
CameraFrameEditor.cs
```

These files form one state flow. Changes to one should be reviewed against the others.

---

## Editor level lifecycle and map-local state

Construction shapes are intentionally **not serialized into `.adofai` files**. They are temporary editor-local helper state.

When a different level is actually loaded, Euclid must clear:

```text
construction shapes
construction selection
pending endpoint pick
endpoint binding state
selected-shape snap state
PositionTrack temporary reference state
construction overlay/UI cache
```

The primary boundary is `scnEditor.OpenLevelCo`, patched by `EditorLevelLoadPatch`.

Important distinctions:

```text
Open another .adofai        -> clear map-local Euclid state
Reload the same .adofai     -> clear map-local Euclid state
Save / Save As              -> keep construction state
Open file picker then cancel-> keep construction state
```

Do not patch the no-argument file-picker entry point merely because it is named `OpenLevel`; clearing before a path is committed would erase state when the picker is cancelled.

`EuclidBehaviour` may retain lightweight loading/path/object checks as fallbacks for teardown or compatibility, but they must not be treated as the authoritative map-load signal.

If map-local state survives a level load, inspect in this order:

```text
Startup.cs installs EditorLevelLoadPatch
EditorLevelLoadPatch finds OpenLevelCo overload(s)
UMM log reports installed load hook(s)
load hook runs for the selected path
HandleEditorMapChanged clears panel-local state
ConstructionShapeTool.ClearAll clears model state
```

---

## Construction-shape invariants

Supported primitives:

- Point
- Line
- Circle

Line through `(x1, y1)` and `(x2, y2)`:

```text
a = (y2 - y1) / (x2 - x1)
b = y1 - a*x1
```

Line angle `θ` is an undirected orientation normalized into `[0°, 180°)`.

Circle semantics:

```text
P1 = center
P2 = point on circumference
r  = distance(P1, P2)
```

### Point provenance

An endpoint can originate from:

- an ADOFAI tile
- another construction Point
- manual coordinate input

Manual coordinate editing detaches source provenance after a valid replacement coordinate is committed.

An unpinned picked endpoint is a coordinate snapshot. Pin is a live binding.

### Endpoint Select state machine

The Select button is a toggle for one pending endpoint pick.

Required behavior:

```text
Select P1 while idle       -> arm P1
Select P1 again            -> cancel pending pick
Select P2 while P1 armed   -> switch pending pick to P2
successful tile/point pick -> clear pending pick
manual X/Y edit            -> clear pending pick for that endpoint
map load                    -> clear pending pick
```

The selected visual must represent this pending state immediately. Do not maintain a separate UI-only latch.

### Pin

Pin follows the selected source object in real time. Turning Pin off converts the current position back into a snapshot. If an unpinned source later moves or is renumbered, Euclid should stop claiming the endpoint is still attached while preserving the saved coordinates.

---

## Shape Info UI rules

Unity layout rebuilds can expose intermediate control states for one frame. Prefer creating controls directly in their final state.

```text
create control
-> assign final LayoutElement values
-> assign final interactable state
-> assign final visual state
-> let Unity render
```

Avoid ordinary UI fixes that depend on:

```text
create temporary state
-> render frame
-> repair size/interactable state in LateUpdate
```

### P2 for Point shapes

P2 remains present for stable panel height, but Point does not consume it. P2 controls must be created disabled immediately, not corrected a frame later.

### Select / Pin button geometry

Endpoint action buttons in the same row should receive their final widths at construction time. Do not copy dimensions every frame.

### Status buttons

Frequently rebuilt Euclid controls should change selected/disabled visuals immediately. A nonzero `ColorBlock.fadeDuration` can make a correct rebuild look like flicker.

### Color sliders

Define slider handle size directly on the handle RectTransform. Avoid parent stretching followed by per-frame correction.

---

## Coordinate spaces

Always know which coordinate space a value uses.

Euclid commonly uses:

- world space
- ADOFAI tile-unit offsets
- screen-space mouse coordinates
- IMGUI coordinates
- Canvas-local coordinates

`GameCompat.GetTileSize()` converts between tile-unit event offsets and world displacement.

```text
world = referenceWorld + offsetTiles * tileSize

offsetTiles = (world - referenceWorld) / tileSize
```

The same `referenceWorld` must be used for display, hit testing, snap preview, dragging, and write-back.

---

## Supported effect overlays

Current supported effect families:

```text
MoveCamera / CameraMove
MoveTrack
PositionTrack
FreeRoam / FreeRoamRemove
```

`MoveDecorations` is deliberately unsupported by coordinate markers and construction-shape snapping.

Decoration movement can target multiple decorations through tags and depends on placement/reference state, previous positions, and timing. A single marker is not a reliable representation of that event. Do not re-add decoration markers through generic vector-property fallback logic.

Per-effect default palette roles are currently separated by effect family. All-effect/background rendering must use the same support scope as selected-effect coordinate tools.

---

## PositionTrack: required state model

`PositionTrack` is the most fragile supported effect because ADOFAI separates the stored property value from the displayed/applied floor transform.

### Raw offset vs effective offset

Never assume stored `positionOffset` is active.

```text
rawOffset = stored positionOffset

effectiveOffset =
    rawOffset   if positionOffset is enabled
    (0, 0)      if positionOffset is disabled
```

Use `LevelEventCompat` for the enabled/disabled state.

For an already-applied `relativeTo = ThisTile` PositionTrack:

```text
referenceWorld = displayedFloorWorld - effectiveOffset * tileSize
targetWorld    = referenceWorld + effectiveOffset * tileSize
```

When `positionOffset` is disabled:

```text
effectiveOffset = (0, 0)
referenceWorld  = displayedFloorWorld
targetWorld     = displayedFloorWorld
```

A nonzero raw value may still remain stored. Never subtract the raw value while the property is disabled.

### Pending edit vs applied state

ADOFAI can expose a new raw offset before the real floor transform moves.

The marker state must distinguish:

```text
A. applied state
B. pending raw/effective edit
C. applied floor catch-up
```

Required behavior:

```text
1. Start from the last applied reference/floor/offset.
2. Raw/effective offset changes while floor does not:
      keep referenceWorld fixed
      move target marker as preview
3. ADOFAI moves the floor:
      recompute referenceWorld from displayed floor - effectiveOffset
      accept this as the new applied state
4. Upstream/path movement changes the floor with this offset unchanged:
      recompute reference from the new floor
```

Do not infer provisional state from `rawOffset == (0,0)`. Zero is valid data.

### Cache ownership

`CoordinateSnapTool` owns the selected PositionTrack reference cache. Synchronizers should publish complete applied state through the dedicated API rather than reflecting into individual cache fields.

### `relativeTo`

ADOFAI 3.3.0 may store enum-like values in wrappers such as:

```text
[0, "ThisTile"]
```

`LevelEventCompat` normalizes known raw representations.

- `ThisTile`: applied/pending reference tracking
- `Start` / `FirstTile`: start reference
- `End` / `LastTile`: end reference

Do not apply the `ThisTile` cache model blindly to other relative modes.

---

## PositionTrack edit boundaries

### Scene-marker drag

Dragging a PositionTrack marker can affect many downstream tiles. Do not rebuild the full path on every mouse-move frame.

Desired flow:

```text
MouseDown
-> acquire real positionOffset edit context
-> save undo state once

MouseDrag
-> update raw positionOffset
-> update preview
-> do not repeatedly rebuild downstream floor path

MouseUp
-> end host edit context
-> allow one host commit/floor rebuild
-> synchronize applied marker state
```

### Programmatic snap

Snap is a discrete edit.

```text
focus old value
-> write new raw value
-> synchronize inspector text
-> end edit / invoke host commit
-> wait for applied floor state
```

When the native event inspector is hidden by Euclid, reuse its existing end-edit callback when possible. Keep direct `ApplyPropertiesToRealEvents()` as compatibility fallback, not a parallel state machine.

---

## Camera frame editing

For a selected MoveCamera event, Euclid can directly manipulate:

```text
center handle  -> position
corner handles -> uniform zoom
rotation handle-> rotation
```

If zoom or rotation is disabled/inherited, the first manipulation starts from the currently effective value and enables/writes the edited property through the normal event-edit path.

One continuous drag should create one undo boundary, not one per frame.

---

## All-effect/background markers

Unselected markers are read-only. They must:

- remain independent of inspector focus
- not mutate selected-event edit caches
- respect property enabled/disabled state
- project smoothly while the editor camera moves
- avoid editor mutation during rendering
- exclude unsupported effects such as MoveDecorations

For an unselected already-applied PositionTrack with `ThisTile`, derive the marker from the applied floor and effective offset, not the selected event's pending cache.

Separate data collection from projection/rendering:

```text
event state / world points
        ↓
cheap world -> screen projection
        ↓
render
```

---

## Editing `LevelEvent`

A raw dictionary write is not necessarily a complete editor edit.

A typical discrete mutation may require:

```text
SaveState
-> write raw property
-> update enabled/disabled state if intended
-> commit/apply through host path
-> mark unsaved
-> refresh property text/event panel when needed
```

Do not blindly force every edited property enabled. Preserve disabled state when the operation is only updating stored value; enable explicitly when the UI action semantically edits an active property.

### `LevelEventCompat.cs`

Use for:

- raw property access
- normalized enum/wrapper values
- property enabled/disabled checks
- storage compatibility

### `GameCompat.cs`

Use for:

- editor panels
- selected event/floor access
- floor lookup
- tile size
- save/apply/refresh calls
- version-dependent/private members

---

## Unity update-order guidance

Before adding a timer or delayed frame, identify:

```text
Who writes the model?
Who updates the floor transform?
Who refreshes the inspector?
Who reads the marker state?
Which Unity phase does each run in?
```

Prefer one explicit state transition over several components correcting each other after the fact. Use next-frame logic only when the host genuinely completes work asynchronously.

Scene point picking currently has priority over reading the editor's resulting tile selection so a visible construction Point can win when it overlaps a tile.

---

## Build and project validation

Project target:

```text
net48
```

Default game path:

```text
C:\Program Files (x86)\Steam\steamapps\common\A Dance of Fire and Ice
```

Build:

```bat
BUILD_RELEASE.cmd
```

Alternate path:

```bat
BUILD_RELEASE.cmd "D:\SteamLibrary\steamapps\common\A Dance of Fire and Ice"
```

Direct build:

```bat
dotnet build Euclid.csproj -c Release "-p:GameDir=C:\Program Files (x86)\Steam\steamapps\common\A Dance of Fire and Ice"
```

Release builds create:

```text
dist/Euclid-<version>.zip
```

The repository does not contain the game's Managed assemblies, so source-only environments cannot fully verify compilation against ADOFAI types.

Before release run:

```powershell
scripts\check_project.ps1
```

When changing the mod version, update both:

```text
Info.json
Euclid.csproj
```

---

## Debugging checklist by symptom

### Marker is correct, actual tile is wrong

Inspect the event commit/application path rather than marker geometry:

```text
CameraFrameEditor.cs
PositionTrackMarkerDragFocus.cs
PositionTrackSnapCommitSync.cs
GameCompat apply/refresh helpers
```

### Actual tile is correct, reference marker jumps opposite

Inspect:

```text
CoordinateSnapTool PositionTrack applied cache
PositionTrackFocusSync.cs
property enabled/disabled state
effective offset vs raw offset
```

### Shape Info control changes size for one frame

Inspect initial `LayoutElement`, parent `LayoutGroup`, interactable state, and button `ColorBlock.fadeDuration`. Do not add a LateUpdate size copier unless the control truly appears dynamically after layout.

### Shapes remain after opening another map

Inspect `EditorLevelLoadPatch` and the `OpenLevelCo` hook first. Only after that inspect `EuclidBehaviour` fallback detection and `EuclidPanel.HandleEditorMapChanged()`.

### Opening the file picker clears shapes before a file is chosen

The hook is too early. The reset must happen on the concrete load path, not the no-argument picker entry point.

### Saving a new map clears shapes

The reset is incorrectly coupled to path creation/change. Save/Save As is not a level load and must preserve construction state.

---

## PositionTrack regression matrix

Any nontrivial PositionTrack change should cover:

```text
[ ] ThisTile, nonzero offset, enabled
[ ] ThisTile, zero offset, enabled
[ ] ThisTile, nonzero raw offset, disabled
[ ] positionOffset ON -> OFF
[ ] positionOffset OFF -> ON
[ ] inspector text edit -> focus loss
[ ] marker drag -> release
[ ] snap -> applied tile movement
[ ] snap -> immediately drag marker
[ ] snap enabled -> select another construction shape
[ ] upstream/path movement while offset is unchanged
[ ] select another PositionTrack event
[ ] delete selected event
[ ] Start/FirstTile relative mode
[ ] End/LastTile relative mode
[ ] all-effect markers ON with selected event
[ ] all-effect markers ON with no selected event
```

For `ThisTile`, verify all three independently:

```text
actual floor position
tile/reference marker
target/position marker
```

---

## Shape / lifecycle regression matrix

After changing construction UI, point picking, or map lifecycle:

```text
[ ] Point -> Line -> Circle -> Point repeatedly
[ ] P2 disabled immediately for Point
[ ] Select/Pin geometry stays constant
[ ] Select P1 once arms pick
[ ] Select P1 again cancels pick
[ ] Select P2 while P1 is armed switches to P2
[ ] point pick from tile works
[ ] point pick from drawn Point works
[ ] successful pick clears pending state
[ ] manual X/Y edit clears pending state and detaches source
[ ] Pin follows source movement
[ ] unpinned snapshot does not silently follow source
[ ] color slider handles do not stretch during rebuild
[ ] intersection/snap button states are correct immediately
[ ] unsaved map -> Save As keeps shapes
[ ] unsaved map -> open saved map clears shapes
[ ] saved map A -> saved map B clears shapes
[ ] reload same saved map clears shapes
[ ] open-file dialog cancel keeps shapes
[ ] map load clears pending pick and snap state
```

---

## Effect overlay regression matrix

```text
[ ] MoveCamera selected marker and camera frame
[ ] MoveTrack marker
[ ] PositionTrack marker
[ ] FreeRoam / FreeRoamRemove marker
[ ] MoveDecorations produces no coordinate marker
[ ] MoveDecorations is not a construction snap target
[ ] all-effect mode excludes MoveDecorations
[ ] per-effect colors persist after restart
[ ] map load leaves no old overlay state
[ ] large map with many markers remains usable while pan/zooming
```

---

## ADOFAI update workflow

When a new game version breaks Euclid:

1. Reproduce on the new version.
2. Check UMM logs.
3. Inspect `Assembly-CSharp.dll` with ILSpy.
4. Check `GameCompat.cs`, `LevelEventCompat.cs`, and Harmony patch targets first.
5. Verify `OpenLevelCo` or its replacement load path.
6. Verify event serialization wrappers and disabled-property representation.
7. Verify editor hierarchy assumptions used for native inspector controls.
8. Compile against the new Managed assemblies.
9. Run PositionTrack, Shape/lifecycle, and effect-overlay regression matrices.
10. Update the relevant `PORTING-*.md` notes.

Useful types include:

```text
scnEditor
InspectorPanel
scrFloor
ADOFAI.LevelEvent
LevelEventType
RDString
```

---

## Maintenance rules

- Treat current source code as the source of truth.
- Keep geometry separate from Unity UI where practical.
- Keep game-version-specific reflection/Harmony assumptions in integration layers.
- Use one coordinate origin consistently for display and write-back.
- Distinguish raw event data from effective/applied state.
- Do not infer edit state from a numeric value such as `(0,0)` alone.
- Do not make read-only overlays mutate editing state.
- Do not reintroduce per-frame UI correction when final layout can be defined at creation.
- Avoid multiple independent PositionTrack state machines.
- Preserve deferred commit during continuous PositionTrack marker drag.
- Keep map reset tied to actual level loading, not Save As or file-picker opening.
- Compile locally after C# changes; runtime tests are required for editor timing behavior.

---

## Release checklist

```text
[ ] Update Info.json version
[ ] Update Euclid.csproj version
[ ] Run scripts/check_project.ps1
[ ] Build Release against target ADOFAI install
[ ] Launch with intended runtime dependencies only
[ ] Verify Euclid tab/panels
[ ] Run Shape/lifecycle regression matrix
[ ] Verify intersections and snapping
[ ] Run PositionTrack regression matrix
[ ] Verify MoveCamera position/zoom/rotation handles and undo
[ ] Run effect overlay regression matrix
[ ] Verify localization
[ ] Verify UMM options persistence
[ ] Install generated dist/Euclid-<version>.zip through UMM
[ ] Re-test the installed ZIP rather than only the development DLL
[ ] Tag the verified commit
[ ] Create release and attach the UMM ZIP
```
