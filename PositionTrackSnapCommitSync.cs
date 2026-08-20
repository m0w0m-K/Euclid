using System;
using System.Reflection;
using ADOFAI;
using UnityEngine;

namespace Euclid
{
    // Programmatic PositionTrack snaps must reproduce the same order as a real inspector edit:
    //   focus OLD value -> change raw value while focused -> update inspector text -> end edit.
    //
    // Focusing only after the raw value changed does not reliably trigger ADOFAI's PositionTrack
    // commit path, because the input begins its edit already containing the new value. It also means
    // PositionTrackFocusSync never observes "raw changed while focused", which is the state needed to
    // preserve the pre-effect tile/reference origin. This component now owns that ordering explicitly.
    [DefaultExecutionOrder(10000)]
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
        private static bool releaseArmed;
        private static int releaseAfterFrame = -1;
        private static int settleThroughFrame = -1;

        // Keep a short barrier after end-edit. ADOFAI may rebuild/move the floor at the end of the
        // same frame, so another snap must not calculate against the old transform during that gap.
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

        // MUST be called before LevelEvent.positionOffset is changed. The field has to acquire focus
        // while it still represents the last applied value, exactly like a real user edit.
        internal static bool TryBeginImmediateCommit(LevelEvent ev, string key)
        {
            if (pending || Time.frameCount <= settleThroughFrame ||
                ev == null || ev.eventType != LevelEventType.PositionTrack ||
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
                releaseArmed = false;
                releaseAfterFrame = -1;
                settleThroughFrame = -1;
                return true;
            }
            catch (Exception ex)
            {
                EuclidMod.Logger?.Log("PositionTrack snap focus begin failed: " + ex.Message);
                ResetPendingState();
                return false;
            }
        }

        // Call after the raw value and inspector text have both been updated. Keep the input focused
        // through at least one complete LateUpdate so PositionTrackFocusSync can capture the previous
        // applied floor/offset before this component releases focus later in execution order.
        internal static void ArmImmediateCommit()
        {
            if (!pending)
            {
                return;
            }

            releaseArmed = true;
            releaseAfterFrame = Time.frameCount + 1;
        }

        internal static void CancelImmediateCommit()
        {
            if (!pending)
            {
                return;
            }

            ReleaseNow(settle: false);
        }

        private void LateUpdate()
        {
            if (!pending || !releaseArmed)
            {
                return;
            }

            if (!EuclidMod.Enabled)
            {
                ReleaseNow(settle: false);
                return;
            }

            // If the user grabs the marker before the scheduled snap release, keep the same real
            // input focused. MarkerDragFocus will continue the raw edit and release it on MouseUp.
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
                releaseArmed = false;
                releaseAfterFrame = -1;
                settleThroughFrame = settle ? Time.frameCount + 2 : -1;
            }
        }

        private static void ResetPendingState()
        {
            pending = false;
            releaseArmed = false;
            releaseAfterFrame = -1;
            settleThroughFrame = -1;
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
