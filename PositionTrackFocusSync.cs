using System;
using System.Collections;
using System.Reflection;
using ADOFAI;
using UnityEngine;

namespace Euclid
{
    // PositionTrack's inspector edits positionOffset immediately, but ADOFAI does not move the
    // floor transform until the coordinate input loses focus. CoordinateSnapTool therefore cannot
    // distinguish a pending edit from an already-applied floor by looking at values alone.
    //
    // This component observes the actual Unity input-field focus. While an offset edit is focused,
    // it pins CoordinateSnapTool's zero reference to the last applied tile position. Once focus is
    // lost and the floor transform catches up, it rebases the zero reference as:
    //     applied floor world - current positionOffset * tileSize
    // so the position marker lands on the moved tile and the tile marker remains at the pre-effect
    // position. Earlier track/path movement is still preserved because it is already contained in
    // the applied floor world position.
    internal sealed class PositionTrackFocusSync : MonoBehaviour
    {
        private const float ChangeToleranceSqr = 0.00000001f;

        private static readonly Type CoordinateSnapType = typeof(CoordinateSnapTool);
        private static readonly FieldInfo HasReferenceField = GetCoordinateField("hasPositionTrackReference");
        private static readonly FieldInfo ReferenceEventField = GetCoordinateField("positionTrackReferenceEvent");
        private static readonly FieldInfo ReferenceFloorField = GetCoordinateField("positionTrackReferenceFloor");
        private static readonly FieldInfo ReferenceTileSizeField = GetCoordinateField("positionTrackReferenceTileSize");
        private static readonly FieldInfo ZeroReferenceField = GetCoordinateField("positionTrackZeroReference");
        private static readonly FieldInfo AppliedOffsetField = GetCoordinateField("positionTrackAppliedOffsetTiles");
        private static readonly FieldInfo AppliedFloorField = GetCoordinateField("positionTrackAppliedFloorWorld");
        private static readonly FieldInfo ReferenceProvisionalField = GetCoordinateField("positionTrackReferenceProvisional");

        private LevelEvent trackedEvent;
        private int trackedFloor;
        private int trackedEditorEventIndex = -1;
        private bool hasLastObservedState;
        private Vector2 lastObservedFloorWorld;
        private Vector2 lastObservedRawOffset;
        private bool inputWasFocused;

        // True after positionOffset changed while a text field had real Unity focus. Until ADOFAI
        // applies that edit to the floor transform, keep the old applied state pinned in the cache.
        private bool awaitingFocusedEditApply;
        private Vector2 heldZeroReference;
        private Vector2 heldAppliedOffset;
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
            if (editor == null || ev == null || ev.eventType != LevelEventType.PositionTrack ||
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
            var editorEventIndex = GetEditorEventIndex(editor, ev);
            var inputFocused = IsTextInputFocused();

            if (!IsSameLogicalEvent(ev, referenceFloor, editorEventIndex))
            {
                BeginTracking(ev, referenceFloor, editorEventIndex, floorWorld, rawOffset, inputFocused);
                return;
            }

            // ADOFAI can replace the selected LevelEvent object while applying inspector edits.
            // Preserve the pending-edit baseline across that replacement instead of letting
            // CoordinateSnapTool treat the replacement object as a brand-new PositionTrack.
            if (!ReferenceEquals(trackedEvent, ev) && awaitingFocusedEditApply)
            {
                ForceCoordinateCache(
                    ev,
                    referenceFloor,
                    tileSize,
                    heldZeroReference,
                    heldAppliedOffset,
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
                // The raw inspector value changed while the field is genuinely focused, so the
                // displayed floor still represents the previously applied offset. Capture that
                // applied state once and keep it stable for the entire text edit.
                awaitingFocusedEditApply = true;
                heldAppliedOffset = lastObservedRawOffset;
                heldAppliedFloorWorld = lastObservedFloorWorld;
                heldZeroReference = heldAppliedFloorWorld - heldAppliedOffset * tileSize;
            }

            if (awaitingFocusedEditApply)
            {
                if (inputFocused)
                {
                    // Even if ADOFAI rebuilds UI/event objects mid-edit, the marker origin must not
                    // rebase from the raw value that has not yet been applied to the floor.
                    ForceCoordinateCache(
                        ev,
                        referenceFloor,
                        tileSize,
                        heldZeroReference,
                        heldAppliedOffset,
                        heldAppliedFloorWorld);
                }
                else if ((rawOffset - heldAppliedOffset).sqrMagnitude <= ChangeToleranceSqr)
                {
                    // The user returned the field to its original value before leaving it.
                    ForceCoordinateCache(
                        ev,
                        referenceFloor,
                        tileSize,
                        heldZeroReference,
                        heldAppliedOffset,
                        heldAppliedFloorWorld);
                    awaitingFocusedEditApply = false;
                }
                else if (floorChanged ||
                         (floorWorld - heldAppliedFloorWorld).sqrMagnitude > ChangeToleranceSqr)
                {
                    // Focus is gone and ADOFAI has now moved the actual tile. This is the exact
                    // applied state requested by the overlay:
                    //   tile marker     = actual tile - current offset
                    //   position marker = actual tile
                    var zeroReference = floorWorld - rawOffset * tileSize;
                    ForceCoordinateCache(
                        ev,
                        referenceFloor,
                        tileSize,
                        zeroReference,
                        rawOffset,
                        floorWorld);
                    awaitingFocusedEditApply = false;
                }
                else
                {
                    // Focus has ended but the floor transform has not caught up yet. Keep the
                    // pre-apply reference pinned until ADOFAI performs its deferred move.
                    ForceCoordinateCache(
                        ev,
                        referenceFloor,
                        tileSize,
                        heldZeroReference,
                        heldAppliedOffset,
                        heldAppliedFloorWorld);
                }
            }
            else if (!inputFocused && floorChanged)
            {
                // No pending focused edit: this floor movement came from an already-applied change
                // (for example an earlier track/path effect). Follow that movement, but subtract
                // this PositionTrack's own offset from the tile marker.
                var zeroReference = floorWorld - rawOffset * tileSize;
                ForceCoordinateCache(
                    ev,
                    referenceFloor,
                    tileSize,
                    zeroReference,
                    rawOffset,
                    floorWorld);
            }

            // If the floor moved while the pending field was still focused, do not consume that
            // movement as the new baseline. The first unfocused frame must still observe it and
            // perform the applied-state rebase above.
            if (!(awaitingFocusedEditApply && inputFocused && floorChanged))
            {
                lastObservedFloorWorld = floorWorld;
            }

            lastObservedRawOffset = rawOffset;
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

            // During the exact focus-loss/apply window, object identity and even editor list
            // reconstruction may change. The selected PositionTrack on the same floor is still the
            // edit we were tracking until the deferred floor application completes.
            return awaitingFocusedEditApply || inputWasFocused;
        }

        private void BeginTracking(
            LevelEvent ev,
            int floor,
            int editorEventIndex,
            Vector2 floorWorld,
            Vector2 rawOffset,
            bool inputFocused)
        {
            trackedEvent = ev;
            trackedFloor = floor;
            trackedEditorEventIndex = editorEventIndex;
            hasLastObservedState = true;
            lastObservedFloorWorld = floorWorld;
            lastObservedRawOffset = rawOffset;
            inputWasFocused = inputFocused;
            awaitingFocusedEditApply = false;
            heldZeroReference = Vector2.zero;
            heldAppliedOffset = Vector2.zero;
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
            inputWasFocused = false;
            awaitingFocusedEditApply = false;
            heldZeroReference = Vector2.zero;
            heldAppliedOffset = Vector2.zero;
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

        private static bool IsThisTileRelative(LevelEvent ev)
        {
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
                value = raw == null ? 0f : Convert.ToSingle(raw, System.Globalization.CultureInfo.InvariantCulture);
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    value = 0f;
                }
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
                // Editor floor lists can be rebuilt during focus loss. Retry on the next LateUpdate.
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
            foreach (var current in GameCompat.GetEditorEvents(editor))
            {
                if (ReferenceEquals(current, ev))
                {
                    return index;
                }
                index++;
            }

            return -1;
        }

        private static void ForceCoordinateCache(
            LevelEvent ev,
            int referenceFloor,
            float tileSize,
            Vector2 zeroReference,
            Vector2 appliedOffset,
            Vector2 appliedFloorWorld)
        {
            if (HasReferenceField == null || ReferenceEventField == null ||
                ReferenceFloorField == null || ReferenceTileSizeField == null ||
                ZeroReferenceField == null || AppliedOffsetField == null ||
                AppliedFloorField == null || ReferenceProvisionalField == null)
            {
                return;
            }

            try
            {
                ReferenceEventField.SetValue(null, ev);
                ReferenceFloorField.SetValue(null, referenceFloor);
                ReferenceTileSizeField.SetValue(null, tileSize);
                ZeroReferenceField.SetValue(null, zeroReference);
                AppliedOffsetField.SetValue(null, appliedOffset);
                AppliedFloorField.SetValue(null, appliedFloorWorld);
                ReferenceProvisionalField.SetValue(null, false);
                HasReferenceField.SetValue(null, true);
            }
            catch (Exception)
            {
                // A missing/changed private field should disable only this compatibility shim.
            }
        }

        private static FieldInfo GetCoordinateField(string name)
        {
            return CoordinateSnapType.GetField(name, BindingFlags.Static | BindingFlags.NonPublic);
        }
    }
}
