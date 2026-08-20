using System;
using System.Reflection;
using ADOFAI;
using UnityEngine;

namespace Euclid
{
    // Programmatic PositionTrack snaps must use the same commit boundary as a real inspector edit.
    // Directly calling ApplyPropertiesToRealEvents updates the event data, but on some ADOFAI builds
    // it does not execute the complete floor/path rebuild that happens when the positionOffset input
    // actually loses focus. Reuse PositionTrackMarkerDragFocus's proven input resolver, keep the
    // field focused for at least one LateUpdate so PositionTrackFocusSync can observe the pending raw
    // change, then release it and let ADOFAI perform its normal one-shot commit.
    internal sealed class PositionTrackSnapCommitSync : MonoBehaviour
    {
        private static readonly MethodInfo FocusMethod = typeof(PositionTrackMarkerDragFocus).GetMethod(
            "TryFocusPositionOffsetInput",
            BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly MethodInfo ReleaseMethod = typeof(PositionTrackMarkerDragFocus).GetMethod(
            "ReleaseOwnedInput",
            BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly FieldInfo PositionOffsetDraggingField = typeof(CameraFrameOverlay).GetField(
            "draggingPositionOffset",
            BindingFlags.Static | BindingFlags.NonPublic);

        private static bool pending;
        private static int releaseAfterFrame = -1;
        private static int settleThroughFrame = -1;

        // Keep a one-frame barrier after releasing the inspector input. ADOFAI can rebuild the floor
        // hierarchy at the end of that frame, and another snap calculated against the old transform is
        // exactly what caused the occasional opposite-direction marker jump.
        internal static bool IsPending => pending || Time.frameCount <= settleThroughFrame;

        internal static void Install()
        {
            var behaviour = EuclidMod.Behaviour;
            if (behaviour == null || behaviour.GetComponent<PositionTrackSnapCommitSync>() != null)
            {
                return;
            }

            behaviour.gameObject.AddComponent<PositionTrackSnapCommitSync>();
        }

        internal static bool TryScheduleImmediateCommit(LevelEvent ev, string key)
        {
            if (ev == null || ev.eventType != LevelEventType.PositionTrack ||
                !string.Equals(key, "positionOffset", StringComparison.OrdinalIgnoreCase) ||
                FocusMethod == null || ReleaseMethod == null || IsMarkerDragging())
            {
                return false;
            }

            try
            {
                var focused = FocusMethod.Invoke(null, new object[] { scnEditor.instance, ev });
                if (!(focused is bool success) || !success)
                {
                    return false;
                }

                pending = true;
                settleThroughFrame = -1;
                // Keep the field alive for a full frame. PositionTrackFocusSync must see the new raw
                // value while the real input is focused before ADOFAI receives the end-edit signal.
                releaseAfterFrame = Time.frameCount + 1;
                return true;
            }
            catch (Exception ex)
            {
                EuclidMod.Logger?.Log("PositionTrack snap focus commit failed: " + ex.Message);
                pending = false;
                releaseAfterFrame = -1;
                return false;
            }
        }

        private void LateUpdate()
        {
            if (!pending)
            {
                return;
            }

            if (!EuclidMod.Enabled)
            {
                ReleaseNow(settle: false);
                return;
            }

            // If the user starts a scene-marker drag before the scheduled release, keep ownership of
            // the same input. MarkerDragFocus will release it when that drag ends; do not interrupt it.
            if (IsMarkerDragging())
            {
                return;
            }

            if (Time.frameCount < releaseAfterFrame)
            {
                return;
            }

            ReleaseNow(settle: true);
        }

        private void OnDisable()
        {
            if (pending)
            {
                ReleaseNow(settle: false);
            }
            settleThroughFrame = -1;
        }

        private void OnDestroy()
        {
            if (pending)
            {
                ReleaseNow(settle: false);
            }
            settleThroughFrame = -1;
        }

        private static void ReleaseNow(bool settle)
        {
            try
            {
                ReleaseMethod?.Invoke(null, null);
            }
            catch (Exception ex)
            {
                EuclidMod.Logger?.Log("PositionTrack snap focus release failed: " + ex.Message);
            }
            finally
            {
                pending = false;
                releaseAfterFrame = -1;
                settleThroughFrame = settle ? Time.frameCount + 1 : -1;
            }
        }

        private static bool IsMarkerDragging()
        {
            if (PositionOffsetDraggingField == null)
            {
                return false;
            }

            try
            {
                return PositionOffsetDraggingField.GetValue(null) is bool active && active;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
