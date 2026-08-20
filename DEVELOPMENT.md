# Euclid Development Guide

This document is for developers working on **Euclid**, an editor utility mod for **A Dance of Fire and Ice**.

`README.md` is user-facing. This file is intentionally about architecture, invariants, fragile integration points, debugging strategy, and release verification. It is **not** a changelog and should not accumulate lists of already-completed fixes.

## Target environment

- A Dance of Fire and Ice: **3.3.0**
- Unity: **6000.3.10f1**
- Unity Mod Manager
- C# / .NET Framework 4.8 (`net48`)
- Windows Steam installation

ADOFAI's editor classes are not a stable public API. Fields, methods, event storage, enum wrappers, and Unity hierarchy details may change between game versions.

Keep version-sensitive access concentrated in:

```text
GameCompat.cs
LevelEventCompat.cs
```

Do not scatter new reflection fallbacks through feature code when the compatibility layer can own them.

---

## High-level architecture

### Runtime / bootstrap

```text
Startup.cs
EuclidMod.cs
EuclidBehaviour.cs
```

`Startup.cs` is the UMM entry point and should remain small. Runtime helpers that require a `MonoBehaviour` are installed from here.

`EuclidMod.cs` owns UMM-facing state such as enable/disable, Options UI, settings persistence, and overlay configuration.

`EuclidBehaviour.cs` is the runtime coordinator. It owns high-level frame ordering, snapshot refresh, editor-map boundary detection, panel ticking, and scene-level point-pick ordering.

If a feature updates only after another click, tab switch, selection change, or delayed frame, inspect `EuclidBehaviour` and the relevant component's Unity execution phase first.

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

`EuclidPanel.Construction.cs` owns most Shape Info and shape-list UI.

`EuclidPanel.PointBinding.cs` owns endpoint source binding / pinning behavior.

`EuclidPanel.EndpointUi.cs` owns endpoint validation and other endpoint-specific UI behavior.

`EuclidPanel.UiFactory.cs` should be used for common rows, buttons, inputs, sliders, and style-preserving control creation.

### Geometry / snapping

```text
ConstructionShapeTool.cs
GuideLineTool.cs
CoordinateSnapTool.cs
Vector2d.cs
```

Keep geometry math out of UI callbacks when possible.

`CoordinateSnapTool.cs` is the central bridge between geometry results and editable ADOFAI event properties.

### Effect editing / visualization

```text
CameraFrameEditor.cs
CameraFrameSnapshot.cs
CameraFrameOverlay.cs
EffectOverlayVisual.cs
EffectOverlayCollection.cs
AllEffectMarkerOverlayV2.cs
AllEffectMarkerSettings.cs
```

Selected effect markers and background/all-effect markers have different responsibilities:

- selected effect: interactive, may contain pending edit state
- unselected effect: read-only visualization only

Do not make background marker enumeration mutate the selected-effect editing cache.

### PositionTrack synchronization

```text
CoordinateSnapTool.cs
PositionTrackFocusSync.cs
PositionTrackMarkerDragFocus.cs
PositionTrackSnapCommitSync.cs
PositionTrackAppliedSync.cs
CameraFrameEditor.cs
```

These files form one state flow. Changes to one of them should be reviewed against the others.

---

## Construction-shape invariants

Supported primitives are:

- Point
- Line
- Circle

Line geometry through `(x1, y1)` and `(x2, y2)`:

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

Manual coordinate edits detach provenance once a valid replacement coordinate is committed.

Pinning is live binding. An unpinned picked endpoint is a coordinate snapshot, not a permanent reference.

### Map-local state

Construction shapes are map-local editor state.

When another level is opened, Euclid must not carry shapes, selection, pending point-pick state, or shape snap state into the new map.

Map-boundary detection belongs in the runtime coordinator rather than individual shape features.

---

## Shape Info UI rules

Unity layout rebuilds can expose intermediate control states for one frame. Avoid solving this with per-frame geometry correction components.

The preferred rule is:

```text
create control
→ assign final LayoutElement values
→ assign final interactable state
→ assign final visual state
→ let Unity render
```

Do not use:

```text
create with temporary state
→ render frame
→ fix size/interactable state in LateUpdate
```

for ordinary Shape Info controls.

### P2 for Point shapes

P2 remains present in the layout for stable panel height, but its controls must be created disabled from the beginning when the shape type is Point.

Do not create P2 enabled and disable it in a later frame.

### Select / Pin endpoint buttons

Endpoint action buttons in the same row should receive their final size when created.

Do not size Pin by copying Select every frame. If button dimensions need to change, change the construction/layout definition so both are born with the same constraint.

### Status buttons

Buttons whose enabled/selected state changes during panel rebuilds should not visually interpolate between states. A nonzero Unity `ColorBlock.fadeDuration` can make a correct state transition look like a flicker.

Use immediate visual state changes for Euclid's frequently rebuilt controls.

### Color sliders

Slider handle dimensions should be defined directly on the handle RectTransform. Avoid relying on parent forced expansion and then correcting the handle every frame.

If changing slider row height, verify that the handle's vertical anchors remain centered and non-stretched.

---

## Coordinate spaces

Always know which coordinate space a value uses.

Euclid commonly uses:

- world space
- ADOFAI tile-unit offsets
- screen-space mouse coordinates
- Canvas-local coordinates

`GameCompat.GetTileSize()` is the conversion scale between tile-unit event offsets and world displacement.

For a tile-relative offset:

```text
world = referenceWorld + offsetTiles * tileSize
```

and when writing a snapped world point back:

```text
offsetTiles = (world - referenceWorld) / tileSize
```

The same `referenceWorld` must be used for display, hit testing, snap preview, dragging, and write-back.

---

## PositionTrack: required state model

`PositionTrack` is the most fragile effect because ADOFAI separates the stored property value from the displayed/applied floor transform.

### Raw offset vs effective offset

Never assume that stored `positionOffset` is currently active.

Define:

```text
rawOffset = stored positionOffset

effectiveOffset =
    rawOffset   if positionOffset property is enabled
    (0, 0)      if positionOffset property is disabled
```

Use the property's enabled/disabled state through `LevelEventCompat` rather than duplicating dictionary assumptions.

For an already-applied `relativeTo = ThisTile` PositionTrack:

```text
referenceWorld = displayedFloorWorld - effectiveOffset * tileSize
targetWorld    = referenceWorld + effectiveOffset * tileSize
```

This gives the required behavior when the property is disabled:

```text
effectiveOffset = (0, 0)
referenceWorld  = displayedFloorWorld
targetWorld     = displayedFloorWorld
```

The raw nonzero value may still be stored in the event. It must not be subtracted while the property is disabled.

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
1. Start from last applied reference/floor/offset.
2. Raw/effective offset changes while floor does not:
      keep referenceWorld fixed
      move target marker as preview
3. ADOFAI moves the floor:
      recompute referenceWorld from displayed floor - effectiveOffset
      accept this as the new applied state
4. Upstream path/track effect moves the floor without changing this PositionTrack offset:
      recompute reference from the new floor
```

Do not treat `rawOffset == (0,0)` as proof that a read is provisional. A genuine zero offset is valid data.

### Cache ownership

`CoordinateSnapTool` owns the selected PositionTrack reference cache.

External synchronization code should publish a complete applied state through the dedicated synchronization method instead of reflecting into individual private fields.

If more state becomes necessary, extend the explicit API rather than restoring field-by-field reflection writes.

### `relativeTo`

ADOFAI 3.3.0 may store enum-like values in wrappers such as:

```text
[0, "ThisTile"]
```

`LevelEventCompat` normalizes known raw representations. Feature code should consume the normalized semantic value.

For PositionTrack:

- `ThisTile`: requires applied/pending reference tracking
- `Start` / `FirstTile`: use start reference
- `End` / `LastTile`: use end reference

Do not apply the `ThisTile` cache model blindly to the other modes.

---

## PositionTrack edit boundaries

There are two different edit modes and they should stay different for performance.

### Scene-marker drag

Dragging the PositionTrack target marker may affect many following tiles. Reapplying the full event every mouse-move frame can become expensive.

Desired model:

```text
MouseDown
→ acquire the real positionOffset edit context / focus
→ save undo state once

MouseDrag
→ update raw positionOffset only
→ update marker preview
→ do NOT repeatedly rebuild the downstream floor path

MouseUp
→ end the real ADOFAI edit context
→ allow one host commit / floor rebuild
→ synchronize applied marker state after floor movement
```

Do not convert marker drag back to full `ApplyPropertiesToRealEvents()` on every frame unless performance has been explicitly re-evaluated.

### Programmatic snap

A snap is a discrete edit, not a continuous drag.

When committing through the host inspector path, ordering matters:

```text
focus old value
→ write new raw value
→ synchronize inspector text
→ end edit / invoke host commit
→ wait for applied floor state
```

Focusing only after the raw value was already replaced can cause ADOFAI to see no meaningful edit boundary.

If the native event inspector is hidden by Euclid, reuse the native input's existing end-edit callback when possible. Keep direct `ApplyPropertiesToRealEvents()` as a compatibility fallback, not a second independent state model.

### Avoid multiple competing commit mechanisms

Before adding a new PositionTrack helper, determine whether the operation is:

- continuous preview
- discrete programmatic commit
- host inspector text edit
- external/upstream floor movement
- property enabled/disabled transition

Route it into the existing state flow. Do not add another component that independently guesses when PositionTrack became applied.

---

## Snapping invariants

`CoordinateSnapTool` is the source of truth for coordinate-target conversion.

When snap mode is active and the selected construction shape changes, snapping may immediately target the newly selected shape. Selection-driven snap logic should be edge-triggered by the selection change, not repeatedly applied every frame.

A failed or temporarily unavailable target during editor rebuild should not permanently consume the selection change if a retry on the next stable frame is required.

Snap code must preserve:

```text
same reference used by marker
same reference used by world → property conversion
same property-enabled semantics used by actual event application
```

If a marker moves correctly but the real tile does not, inspect the commit boundary rather than adjusting marker math.

If the real tile moves correctly but the reference marker jumps in the opposite direction, inspect applied-state synchronization rather than adding another positional compensation.

---

## Effect overlays

### Selected effect

The selected effect marker is the interactive representation and may reflect a pending edit.

### All-effect/background mode

Unselected markers are read-only.

They must:

- remain visible independently of whether an event inspector row is selected
- not mutate selected-event edit caches
- use current event enabled/disabled semantics
- project smoothly with camera movement
- avoid expensive editor mutation or floor application during rendering

For an unselected, already-applied PositionTrack with `ThisTile`, use the applied floor and effective offset. Do not reuse the selected event's pending-edit cache.

Rendering and data collection should be separated conceptually:

```text
event state / world points
        ↓
cheap world → screen projection
        ↓
render
```

Do not intentionally throttle screen projection to a visibly low refresh rate if the world state itself is stable.

---

## Editing `LevelEvent`

A raw dictionary write is not necessarily a complete editor edit.

A typical discrete mutation can involve:

```text
SaveState
→ write raw property
→ update property enabled/disabled state if intended
→ commit/apply through the correct host path
→ mark unsaved
→ refresh property text / event panel when needed
```

Not every edit should blindly force `disabled[key] = false`.

If an operation is specifically editing a disabled property's value without enabling it, preserve the disabled state. If the UI action semantically enables the property, do so explicitly.

### `LevelEventCompat.cs`

Use for:

- raw property access
- normalized enum/wrapper values
- property enabled/disabled checks
- storage compatibility

### `GameCompat.cs`

Use for:

- editor panels
- selected event access
- floor lookup
- tile size
- save/apply/refresh calls
- version-dependent/private-member access

---

## Unity update-order guidance

A large class of editor bugs is caused by observing a half-updated state across `Update`, `LateUpdate`, `OnGUI`, Canvas layout, and ADOFAI's own callbacks.

Before adding a timer or extra delayed frame, identify:

```text
Who writes the model?
Who updates the floor transform?
Who refreshes the inspector?
Who reads the marker state?
Which Unity phase does each one run in?
```

Prefer one clear state transition over several independent components correcting each other after the fact.

Use delayed/next-frame logic only when the host editor genuinely completes work asynchronously across frames.

---

## Build

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

Alternate game path:

```bat
BUILD_RELEASE.cmd "D:\SteamLibrary\steamapps\common\A Dance of Fire and Ice"
```

Direct build:

```bat
dotnet build Euclid.csproj -c Release "-p:GameDir=C:\Program Files (x86)\Steam\steamapps\common\A Dance of Fire and Ice"
```

The repository does not contain the game's Managed assemblies, so source-only environments cannot fully verify compilation against ADOFAI types.

Always run a real local build after changes that touch C# control flow, reflection signatures, Unity UI types, or ADOFAI members.

---

## Project validation

Before release:

```powershell
scripts\check_project.ps1
```

The script is useful for source-level validation but is not a substitute for compiling and launching the actual editor.

When changing the mod version, update both:

```text
Info.json
Euclid.csproj
```

---

## Debugging checklist by symptom

### Marker is correct, actual tile is wrong

Inspect:

```text
CameraFrameEditor.cs
PositionTrackMarkerDragFocus.cs
PositionTrackSnapCommitSync.cs
GameCompat apply/refresh methods
```

This is usually a commit/application problem, not marker geometry.

### Actual tile is correct, reference marker jumps opposite

Inspect:

```text
CoordinateSnapTool PositionTrack applied cache
PositionTrackFocusSync.cs
property enabled/disabled state
effective offset vs raw offset
```

### Bug appears only after snap → immediate drag

Inspect whether snap application has actually reached the floor transform before drag establishes its baseline.

Do not compensate by shifting the reference marker manually.

### Bug appears only when positionOffset is toggled off/on

Verify that all calculations use `effectiveOffset`, while raw storage remains unchanged.

### Shape Info control changes size for one frame

Inspect control creation constraints and parent `LayoutGroup` settings.

Do not add a LateUpdate size copier unless the control truly must be created dynamically after layout.

### Button flashes enabled/disabled during panel rebuild

Inspect:

- initial `interactable` value
- initial surface/text color
- `ColorBlock.fadeDuration`
- whether the control is created once in its final state or corrected later

### Shapes remain after opening another map

Inspect `EuclidBehaviour` map-boundary identity detection and `EuclidPanel.HandleEditorMapChanged()`.

---

## PositionTrack regression matrix

Any nontrivial change to PositionTrack should be tested against at least these cases:

```text
[ ] ThisTile, nonzero offset, enabled
[ ] ThisTile, zero offset, enabled
[ ] ThisTile, nonzero raw offset, disabled
[ ] toggle positionOffset ON → OFF
[ ] toggle positionOffset OFF → ON
[ ] inspector text edit → focus loss
[ ] marker drag → release
[ ] snap → applied tile movement
[ ] snap → immediately drag marker
[ ] snap enabled → select another construction shape
[ ] upstream/path movement while PositionTrack offset is unchanged
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

A visually plausible target marker alone is not sufficient.

---

## Shape UI regression matrix

After changing Shape Info or panel rebuilding logic:

```text
[ ] Point → Line → Circle → Point repeatedly
[ ] P2 is disabled immediately for Point with no one-frame flash
[ ] Select and Pin button geometry stays constant
[ ] color slider handles do not vertically stretch during rebuild
[ ] add shape does not flash action-button state
[ ] delete shape does not flash action-button state
[ ] intersection button state is correct immediately
[ ] snap button state is correct immediately
[ ] point pick from tile works
[ ] point pick from drawn Point works
[ ] manual X/Y edit detaches source correctly
[ ] Pin follows source movement
[ ] unpinned snapshot does not silently follow source
[ ] opening another map clears construction state
```

---

## ADOFAI update workflow

When a new ADOFAI version breaks Euclid:

1. Reproduce on the new version.
2. Check UMM logs.
3. Inspect `Assembly-CSharp.dll` with ILSpy.
4. Check `GameCompat.cs` and `LevelEventCompat.cs` first.
5. Verify event serialization shapes such as enum wrappers and disabled-property representation.
6. Verify editor hierarchy assumptions used to locate native inspector controls.
7. Compile against the new Managed assemblies.
8. Run the PositionTrack and Shape UI regression matrices.
9. Update the relevant `PORTING-*.md` notes.

Useful types:

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

- Treat current source code, not old discussion notes, as the source of truth.
- Keep geometry math separate from Unity UI where practical.
- Keep ADOFAI-version-specific reflection in compatibility/integration layers.
- Use one coordinate origin consistently for marker rendering and write-back.
- Distinguish raw event data from effective/applied state.
- Do not infer edit state from a numeric value such as `(0,0)` alone.
- Do not make read-only background overlays mutate editing state.
- Do not reintroduce per-frame UI size correction when the final layout can be defined at creation time.
- Avoid multiple independent PositionTrack state machines.
- Preserve deferred commit during continuous PositionTrack marker drag for performance.
- Compile locally after C# changes; runtime tests are required for editor timing behavior.

---

## Release checklist

```text
[ ] Update Info.json version
[ ] Update Euclid.csproj version
[ ] Run scripts/check_project.ps1
[ ] Build Release successfully against the target ADOFAI install
[ ] Launch ADOFAI with intended runtime dependencies only
[ ] Verify Euclid tab/panels
[ ] Run Shape UI regression matrix
[ ] Verify intersections and snapping
[ ] Run PositionTrack regression matrix
[ ] Verify Move Camera interaction
[ ] Verify all-effect overlay mode
[ ] Verify map change clears map-local construction state
[ ] Verify localization
[ ] Verify UMM options persistence
[ ] Install generated dist/Euclid-<version>.zip through UMM
[ ] Tag release and attach the UMM ZIP
```
