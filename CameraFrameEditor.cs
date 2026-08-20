using System;
using System.Collections.Generic;
using System.Reflection;
using ADOFAI;
using UnityEngine;

namespace Euclid
{
    internal static class CameraFrameEditor
    {
        internal static bool TryMoveCenter(CameraFrameSnapshot snapshot, Vector2 worldCenter, bool saveUndoState)
        {
            if (snapshot.State != CameraFrameState.Ready || snapshot.SelectedEvent == null || snapshot.TileSize <= 0.000001f)
            {
                return false;
            }

            var offsetTiles = (worldCenter - snapshot.ReferencePoint) / snapshot.TileSize;
            return TrySetVectorProperty(snapshot.SelectedEvent, "position", offsetTiles, saveUndoState);
        }

        internal static bool TrySetVectorProperty(LevelEvent ev, string key, Vector2 value, bool saveUndoState)
        {
            try
            {
                var editor = scnEditor.instance;
                if (editor == null || ev == null || string.IsNullOrWhiteSpace(key))
                {
                    return false;
                }

                // Snap/immediate edits need the already-applied PositionTrack origin from BEFORE
                // the raw offset is replaced. Otherwise the next drag can interpret the snap delta
                // as a change of the tile/reference origin and move that marker in the opposite direction.
                var positionTrackBaseline = PositionTrackAppliedSync.CaptureBeforeEdit(ev, key);

                if (saveUndoState)
                {
                    TrySaveState(editor);
                }

                if (!LevelEventCompat.SetRaw(ev, key, value))
                {
                    return false;
                }

                if (ev.disabled == null)
                {
                    ev.disabled = new Dictionary<string, bool>();
                }

                ev.disabled[key] = false;

                // PositionTrack can rebuild every following floor when its offset is applied.
                // During a Euclid marker drag, PositionTrackMarkerDragFocus gives the actual
                // ADOFAI positionOffset input field focus. Keep only the raw value live while that
                // field owns focus; releasing the marker deactivates the field and lets ADOFAI run
                // its normal one-shot end-edit commit path.
                var deferRealApply = PositionTrackMarkerDragFocus.ShouldDeferApply(ev, key);
                if (!deferRealApply)
                {
                    var applied = GameCompat.TryApplyPropertiesToRealEvents(ev);
                    if (applied)
                    {
                        // A direct snap bypasses the focused-input transition observed by
                        // PositionTrackFocusSync, so synchronize the marker cache explicitly.
                        PositionTrackAppliedSync.NotifyImmediateApply(
                            ev,
                            key,
                            value,
                            positionTrackBaseline);
                    }
                }

                MarkUnsaved(editor);
                RefreshInspectorProperty(editor, ev, key, refreshPanel: saveUndoState && !deferRealApply);
                return true;
            }
            catch (Exception ex)
            {
                EuclidMod.Logger?.Error(ex.ToString());
                return false;
            }
        }

        private static void TrySaveState(scnEditor editor)
        {
            try
            {
                GameCompat.TrySaveState(editor);
            }
            catch (Exception)
            {
                // Position editing still works without an undo snapshot.
            }
        }

        private static void MarkUnsaved(scnEditor editor)
        {
            try
            {
                var property = typeof(scnEditor).GetProperty("unsavedChanges", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                property?.SetValue(editor, true, null);
            }
            catch (Exception)
            {
                // SaveState usually marks the level dirty; this is only a fallback indicator.
            }
        }

        private static void RefreshInspectorProperty(scnEditor editor, LevelEvent ev, string key, bool refreshPanel)
        {
            try
            {
                GameCompat.TryUpdatePropertyText(editor, ev, key);
            }
            catch (Exception)
            {
                // Some inspector states cannot refresh text immediately.
            }

            if (!refreshPanel)
            {
                return;
            }

            try
            {
                GameCompat.TryRefreshEventPanel(editor, ev);
            }
            catch (Exception)
            {
                // The event data was already updated; UI can refresh on the next selection change.
            }
        }
    }
}
