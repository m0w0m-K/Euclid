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

                // Only used by the manual fallback. The preferred PositionTrack snap path below
                // commits through ADOFAI's real input-focus lifecycle and lets PositionTrackFocusSync
                // derive its own applied baseline from the actual floor movement.
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
                // During a scene-marker drag the real inspector input stays focused, so only the raw
                // value should change. Releasing the marker commits once through ADOFAI's end-edit.
                var deferRealApply = PositionTrackMarkerDragFocus.ShouldDeferApply(ev, key);
                var commitThroughInspectorFocus = false;

                if (!deferRealApply &&
                    ev.eventType == LevelEventType.PositionTrack &&
                    string.Equals(key, "positionOffset", StringComparison.OrdinalIgnoreCase))
                {
                    // A programmatic snap used to call ApplyPropertiesToRealEvents directly. That
                    // updates the event but can miss the host editor's complete PositionTrack floor
                    // rebuild. Put the new value into the real inspector control, focus it for one
                    // frame, then let PositionTrackSnapCommitSync release it normally.
                    try
                    {
                        GameCompat.TryUpdatePropertyText(editor, ev, key);
                    }
                    catch (Exception)
                    {
                        // If the inspector text cannot be synchronized, fall back below.
                    }

                    commitThroughInspectorFocus = PositionTrackSnapCommitSync.TryScheduleImmediateCommit(ev, key);
                }

                if (!deferRealApply && !commitThroughInspectorFocus)
                {
                    var applied = GameCompat.TryApplyPropertiesToRealEvents(ev);
                    if (applied)
                    {
                        // Compatibility fallback for builds where the real positionOffset input could
                        // not be resolved. Normal snaps should not use this path anymore.
                        PositionTrackAppliedSync.NotifyImmediateApply(
                            ev,
                            key,
                            value,
                            positionTrackBaseline);
                    }
                }

                MarkUnsaved(editor);

                if (commitThroughInspectorFocus)
                {
                    // Refreshing the whole event panel here would destroy/recreate the exact input
                    // that now owns focus. Its text was already synchronized above; ADOFAI will
                    // refresh the panel itself when end-edit commits on the next frame.
                    return true;
                }

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
