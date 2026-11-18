using System;
using System.Collections;
using ADOFAI;
using UnityEngine;

namespace Euclid
{
    // ADOFAI can change a stored floor-moving positionOffset before the real floor transform is
    // necessarily rebuilt. PositionTrack (ThisTile) and FreeRoam both use this pattern. This
    // component observes the editor's actual text-input focus and tells CoordinateSnapTool only
    // when a state is known to be applied.
    //
    // Important distinction:
    //   raw offset       = value stored in positionOffset
    //   effective offset = raw offset when the property is enabled, otherwise zero
    // The marker cache always stores the last APPLIED effective offset. That same model handles
    // ordinary text edits, Euclid marker drags, snapping, and the property's own on/off toggle.
    internal sealed class PositionTrackFocusSync : MonoBehaviour
    {
        private const float ChangeToleranceSqr = 0.00000001f;

        private LevelEvent trackedEvent;
        private int trackedFloor;
        private int trackedEditorEventIndex = -1;
        private bool hasLastObservedState;
        private Vector2 lastObservedFloorWorld;
        private Vector2 lastObservedRawOffset;
        private Vector2 lastObservedEffectiveOffset;
        private bool inputWasFocused;

        // Once raw positionOffset changes while a real inspector field has focus, the displayed
        // floor is still the previously applied state. Hold that state until focus is gone and the
        // floor catches up instead of deriving a new origin from the pending raw value.
        private bool awaitingFocusedEditApply;
        private Vector2 heldZeroReference;
        private Vector2 heldAppliedEffectiveOffset;
        private Vector2 heldAppliedFloorWorld;

        internal static void Install()
        {
            var behaviour = EuclidMod.Behaviour;
            if (behaviour == null || behaviour.GetComponent<PositionTrackFocusSync>() != null)
            {
                return;
            }

            behaviour.gameObject.AddComponent<PositionTrackFocusSync>();
        }

        private void LateUpdate()
        {
            if (!EuclidMod.Enabled)
            {
                ResetTracking();
                return;
            }

            var editor = scnEditor.instance;
            var panel = GameCompat.GetLevelEventsPanel(editor);
            var ev = GameCompat.GetSelectedEvent(panel);
            if (editor == null || !UsesAppliedFloorPositionOffset(ev) ||
                !IsThisTileRelative(ev) || !TryGetPositionOffset(ev, out var rawOffset))
            {
                ResetTracking();
                return;
            }

            var referenceFloor = ev.floor;
            if (!TryGetFloorWorld(editor, referenceFloor, out var floorWorld))
            {
                return;
            }

            var tileSize = Mathf.Max(GameCompat.GetTileSize(), 0.000001f);
            var effectiveOffset = LevelEventCompat.IsPropertyEnabled(ev, "positionOffset")
                ? rawOffset
                : Vector2.zero;
            var editorEventIndex = GetEditorEventIndex(editor, ev);
            var inputFocused = IsTextInputFocused();

            if (!IsSameLogicalEvent(ev, referenceFloor, editorEventIndex))
            {
                BeginTracking(
                    ev,
                    referenceFloor,
                    editorEventIndex,
                    floorWorld,
                    rawOffset,
                    effectiveOffset,
                    inputFocused);
                return;
            }

            // ADOFAI may replace the LevelEvent object while applying an inspector edit. Preserve
            // the held applied state across that replacement instead of letting object identity make
            // CoordinateSnapTool initialize from a half-applied frame.
            if (!ReferenceEquals(trackedEvent, ev) && awaitingFocusedEditApply)
            {
                CoordinateSnapTool.SyncPositionTrackAppliedState(
                    ev,
                    referenceFloor,
                    tileSize,
                    heldZeroReference,
                    heldAppliedEffectiveOffset,
                    heldAppliedFloorWorld);
            }

            trackedEvent = ev;
            trackedFloor = referenceFloor;
            if (editorEventIndex >= 0)
            {
                trackedEditorEventIndex = editorEventIndex;
            }

            var rawChanged = hasLastObservedState &&
                (rawOffset - lastObservedRawOffset).sqrMagnitude > ChangeToleranceSqr;
            var floorChanged = hasLastObservedState &&
                (floorWorld - lastObservedFloorWorld).sqrMagnitude > ChangeToleranceSqr;

            if (inputFocused && rawChanged && !awaitingFocusedEditApply)
            {
                // Capture the actual state from immediately before the focused edit. This is the
                // only offset that was known to be applied to heldAppliedFloorWorld.
                awaitingFocusedEditApply = true;
                heldAppliedEffectiveOffset = lastObservedEffectiveOffset;
                heldAppliedFloorWorld = lastObservedFloorWorld;
                heldZeroReference = heldAppliedFloorWorld - heldAppliedEffectiveOffset * tileSize;
            }

            if (awaitingFocusedEditApply)
            {
                if (inputFocused)
                {
                    CoordinateSnapTool.SyncPositionTrackAppliedState(
                        ev,
                        referenceFloor,
                        tileSize,
                        heldZeroReference,
                        heldAppliedEffectiveOffset,
                        heldAppliedFloorWorld);
                }
                else if ((effectiveOffset - heldAppliedEffectiveOffset).sqrMagnitude <= ChangeToleranceSqr)
                {
                    // The edit ended at the same effective value it started with. There is no new
                    // applied state to wait for.
                    CoordinateSnapTool.SyncPositionTrackAppliedState(
                        ev,
                        referenceFloor,
                        tileSize,
                        heldZeroReference,
                        heldAppliedEffectiveOffset,
                        heldAppliedFloorWorld);
                    awaitingFocusedEditApply = false;
                }
                else if ((floorWorld - heldAppliedFloorWorld).sqrMagnitude > ChangeToleranceSqr)
                {
                    // Focus is gone and the floor now reflects the new effective state.
                    CoordinateSnapTool.SyncPositionTrackAppliedState(
                        ev,
                        referenceFloor,
                        tileSize,
                        floorWorld - effectiveOffset * tileSize,
                        effectiveOffset,
                        floorWorld);
                    awaitingFocusedEditApply = false;
                }
                else
                {
                    // End-edit has fired but ADOFAI has not rebuilt the floor yet.
                    CoordinateSnapTool.SyncPositionTrackAppliedState(
                        ev,
                        referenceFloor,
                        tileSize,
                        heldZeroReference,
                        heldAppliedEffectiveOffset,
                        heldAppliedFloorWorld);
                }
            }
            else if (!inputFocused && floorChanged)
            {
                // This covers upstream/path movement and the positionOffset property's own on/off
                // switch. When disabled, effectiveOffset is zero, so both markers settle exactly on
                // the restored floor instead of subtracting the dormant raw offset a second time.
                CoordinateSnapTool.SyncPositionTrackAppliedState(
                    ev,
                    referenceFloor,
                    tileSize,
                    floorWorld - effectiveOffset * tileSize,
                    effectiveOffset,
                    floorWorld);
            }

            // Do not consume a floor movement while a focused edit is still pending; the first
            // unfocused frame must be able to recognize the host editor's applied move.
            if (!(awaitingFocusedEditApply && inputFocused && floorChanged))
            {
                lastObservedFloorWorld = floorWorld;
            }

            lastObservedRawOffset = rawOffset;
            lastObservedEffectiveOffset = effectiveOffset;
            hasLastObservedState = true;
            inputWasFocused = inputFocused;
        }

        private bool IsSameLogicalEvent(LevelEvent ev, int floor, int editorEventIndex)
        {
            if (trackedEvent == null)
            {
                return false;
            }

            if (ReferenceEquals(trackedEvent, ev))
            {
                return true;
            }

            if (trackedFloor != floor)
            {
                return false;
            }

            if (trackedEditorEventIndex >= 0 && editorEventIndex >= 0 &&
                trackedEditorEventIndex == editorEventIndex)
            {
                return true;
            }

            return awaitingFocusedEditApply || inputWasFocused;
        }

        private void BeginTracking(
            LevelEvent ev,
            int floor,
            int editorEventIndex,
            Vector2 floorWorld,
            Vector2 rawOffset,
            Vector2 effectiveOffset,
            bool inputFocused)
        {
            trackedEvent = ev;
            trackedFloor = floor;
            trackedEditorEventIndex = editorEventIndex;
            hasLastObservedState = true;
            lastObservedFloorWorld = floorWorld;
            lastObservedRawOffset = rawOffset;
            lastObservedEffectiveOffset = effectiveOffset;
            inputWasFocused = inputFocused;
            awaitingFocusedEditApply = false;
            heldZeroReference = Vector2.zero;
            heldAppliedEffectiveOffset = Vector2.zero;
            heldAppliedFloorWorld = Vector2.zero;
        }

        private void ResetTracking()
        {
            trackedEvent = null;
            trackedFloor = 0;
            trackedEditorEventIndex = -1;
            hasLastObservedState = false;
            lastObservedFloorWorld = Vector2.zero;
            lastObservedRawOffset = Vector2.zero;
            lastObservedEffectiveOffset = Vector2.zero;
            inputWasFocused = false;
            awaitingFocusedEditApply = false;
            heldZeroReference = Vector2.zero;
            heldAppliedEffectiveOffset = Vector2.zero;
            heldAppliedFloorWorld = Vector2.zero;
        }

        private static bool IsTextInputFocused()
        {
            try
            {
                var eventSystem = UnityEngine.EventSystems.EventSystem.current;
                var selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
                if (selected == null)
                {
                    return false;
                }

                var tmpInput = selected.GetComponent<TMPro.TMP_InputField>() ??
                               selected.GetComponentInParent<TMPro.TMP_InputField>();
                if (tmpInput != null && tmpInput.isFocused)
                {
                    return true;
                }

                var legacyInput = selected.GetComponent<UnityEngine.UI.InputField>() ??
                                  selected.GetComponentInParent<UnityEngine.UI.InputField>();
                return legacyInput != null && legacyInput.isFocused;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool UsesAppliedFloorPositionOffset(LevelEvent ev)
        {
            if (ev == null)
            {
                return false;
            }

            if (ev.eventType == LevelEventType.PositionTrack)
            {
                return true;
            }

            return string.Equals(ev.eventType.ToString(), "FreeRoam", StringComparison.Ordinal);
        }

        private static bool IsThisTileRelative(LevelEvent ev)
        {
            // FreeRoam's positionOffset is tied to the event's own host floor; it has the same
            // applied-floor geometry as PositionTrack relativeTo=ThisTile for this synchronizer.
            if (ev != null && string.Equals(ev.eventType.ToString(), "FreeRoam", StringComparison.Ordinal))
            {
                return true;
            }

            if (!LevelEventCompat.TryGetRaw(ev, "relativeTo", out var raw) || raw == null)
            {
                return true;
            }

            if (raw is int index)
            {
                return index == 0;
            }

            var text = raw.ToString();
            return string.IsNullOrWhiteSpace(text) ||
                   string.Equals(text.Trim(), "ThisTile", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetPositionOffset(LevelEvent ev, out Vector2 value)
        {
            if (LevelEventCompat.TryGetRaw(ev, "positionOffset", out var raw) &&
                TryConvertVector2(raw, out value))
            {
                return true;
            }

            try
            {
                value = Sanitize(ev.Get<Vector2>("positionOffset"));
                return true;
            }
            catch (Exception)
            {
                value = Vector2.zero;
                return false;
            }
        }

        private static bool TryConvertVector2(object raw, out Vector2 value)
        {
            if (raw is Vector2 vector)
            {
                value = Sanitize(vector);
                return true;
            }

            if (raw is Tuple<float, float> pair)
            {
                value = Sanitize(new Vector2(pair.Item1, pair.Item2));
                return true;
            }

            if (raw is IList list && list.Count >= 2 &&
                TryConvertSingle(list[0], out var x) &&
                TryConvertSingle(list[1], out var y))
            {
                value = Sanitize(new Vector2(x, y));
                return true;
            }

            value = Vector2.zero;
            return false;
        }

        private static bool TryConvertSingle(object raw, out float value)
        {
            try
            {
                value = raw == null
                    ? 0f
                    : Convert.ToSingle(raw, System.Globalization.CultureInfo.InvariantCulture);
                value = Sanitize(value);
                return true;
            }
            catch (Exception)
            {
                value = 0f;
                return false;
            }
        }

        private static Vector2 Sanitize(Vector2 value)
        {
            return new Vector2(Sanitize(value.x), Sanitize(value.y));
        }

        private static float Sanitize(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }

        private static bool TryGetFloorWorld(scnEditor editor, int floor, out Vector2 world)
        {
            world = Vector2.zero;
            try
            {
                var floors = GameCompat.GetFloors(editor);
                for (var i = 0; i < floors.Count; i++)
                {
                    var candidate = floors[i];
                    if (candidate != null && candidate.seqID == floor)
                    {
                        var position = candidate.transform.position;
                        world = new Vector2(position.x, position.y);
                        return true;
                    }
                }

                if (floor >= 0 && floor < floors.Count && floors[floor] != null)
                {
                    var position = floors[floor].transform.position;
                    world = new Vector2(position.x, position.y);
                    return true;
                }
            }
            catch (Exception)
            {
                // The editor can rebuild its floor list during the apply frame.
            }

            return false;
        }

        private static int GetEditorEventIndex(scnEditor editor, LevelEvent ev)
        {
            if (editor == null || ev == null)
            {
                return -1;
            }

            var index = 0;
            try
            {
                foreach (var current in GameCompat.GetEditorEvents(editor))
                {
                    if (ReferenceEquals(current, ev))
                    {
                        return index;
                    }
                    index++;
                }
            }
            catch (Exception)
            {
                // Object/floor matching still keeps the short apply transition coherent.
            }

            return -1;
        }
    }
}
