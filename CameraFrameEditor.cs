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

                var isPositionTrackOffset = ev.eventType == LevelEventType.PositionTrack &&
                    string.Equals(key, "positionOffset", StringComparison.OrdinalIgnoreCase);

                // A previous snap can still be waiting for ADOFAI's end-edit/floor rebuild. Starting
                // another programmatic snap against that old floor is what produced occasional
                // opposite-direction reference jumps. Marker dragging is allowed to continue the same
                // focused edit and is handled separately by PositionTrackMarkerDragFocus.
                if (isPositionTrackOffset && PositionTrackSnapCommitSync.BlocksProgrammaticEdit)
                {
                    return false;
                }

                // Compatibility fallback only. The preferred snap path focuses the real inspector
                // BEFORE changing raw positionOffset, so PositionTrackFocusSync can capture the old
                // applied floor/offset exactly like it does during marker dragging.
                var positionTrackBaseline = PositionTrackAppliedSync.CaptureBeforeEdit(ev, key);
                var commitThroughInspectorFocus = isPositionTrackOffset &&
                    PositionTrackSnapCommitSync.TryBeginImmediateCommit(ev, key);

                if (saveUndoState)
                {
                    TrySaveState(editor);
                }

                if (!LevelEventCompat.SetRaw(ev, key, value))
                {
                    if (commitThroughInspectorFocus)
                    {
                        PositionTrackSnapCommitSync.CancelImmediateCommit();
                    }
                    return false;
                }

                if (ev.disabled == null)
                {
                    ev.disabled = new Dictionary<string, bool>();
                }

                ev.disabled[key] = false;

                // Scene-marker dragging already owns/focuses the same inspector input. While it is
                // active, keep only raw data live and let MouseUp release the field once.
                var deferRealApply = PositionTrackMarkerDragFocus.ShouldDeferApply(ev, key);

                if (commitThroughInspectorFocus)
                {
                    // This ordering is intentional: focus old value -> SetRaw(new) -> update visible
                    // inspector text while still focused -> hold one frame -> end edit. Focusing after
                    // SetRaw made the input start with the new value and ADOFAI had nothing to commit.
                    var textSynced = false;
                    try
                    {
                        textSynced = GameCompat.TryUpdatePropertyText(editor, ev, key);
                    }
                    catch (Exception)
                    {
                        textSynced = false;
                    }

                    if (textSynced)
                    {
                        PositionTrackSnapCommitSync.ArmImmediateCommit();
                        MarkUnsaved(editor);
                        return true;
                    }

                    // Resolver succeeded but the inspector row could not be refreshed. Releasing the
                    // old field can restore its old text/value, so re-write the requested raw value
                    // before trying the inactive-inspector callback/direct fallback below.
                    PositionTrackSnapCommitSync.CancelImmediateCommit();
                    if (!LevelEventCompat.SetRaw(ev, key, value))
                    {
                        return false;
                    }
                    ev.disabled[key] = false;
                }

                if (!deferRealApply && isPositionTrackOffset)
                {
                    // Euclid's own tab can hide ADOFAI's event-properties panel. Hidden inputs cannot
                    // receive Unity focus, but UpdatePropertyText can still synchronize them. Invoke
                    // the already-wired ADOFAI onEndEdit listener directly so snapping uses the same
                    // host PositionTrack commit logic instead of merely changing the marker/raw data.
                    var hiddenTextSynced = false;
                    try
                    {
                        hiddenTextSynced = GameCompat.TryUpdatePropertyText(editor, ev, key);
                    }
                    catch (Exception)
                    {
                        hiddenTextSynced = false;
                    }

                    if (hiddenTextSynced && PositionTrackSnapCommitSync.TryInvokeHiddenInspectorEndEdit(ev, key))
                    {
                        MarkUnsaved(editor);
                        return true;
                    }
                }

                if (!deferRealApply)
                {
                    var applied = GameCompat.TryApplyPropertiesToRealEvents(ev);
                    if (applied && isPositionTrackOffset)
                    {
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
                PositionTrackSnapCommitSync.CancelImmediateCommit();
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
