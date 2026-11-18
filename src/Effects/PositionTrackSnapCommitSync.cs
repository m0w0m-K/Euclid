using System;
using System.Reflection;
using ADOFAI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Euclid
{
    // Programmatic PositionTrack snaps must reproduce the same order as a real inspector edit:
    //   focus OLD value -> change raw value while focused -> update inspector text -> end edit.
    //
    // When Euclid's own panel is open, ADOFAI's event-properties panel can be inactive, so the real
    // input cannot receive Unity focus. In that case we still resolve the hidden positionOffset input
    // and invoke its normal onEndEdit callback after its text has been synchronized. That reuses the
    // host editor's actual PositionTrack commit handler instead of guessing which rebuild method to call.
    [DefaultExecutionOrder(10000)]
    internal sealed class PositionTrackSnapCommitSync : MonoBehaviour
    {
        private static readonly Type MarkerDragFocusType = typeof(PositionTrackMarkerDragFocus);
        private static readonly MethodInfo FocusMethod = MarkerDragFocusType.GetMethod(
            "TryFocusPositionOffsetInput",
            BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly MethodInfo ReleaseMethod = MarkerDragFocusType.GetMethod(
            "ReleaseOwnedInput",
            BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly MethodInfo ResolvePropertyRootMethod = MarkerDragFocusType.GetMethod(
            "ResolveSelectedEventPropertyRoot",
            BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly MethodInfo ScoreInputMethod = MarkerDragFocusType.GetMethod(
            "ScoreInput",
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
        internal static bool BlocksProgrammaticEdit => IsPending && !IsMarkerDragging();

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

        // Fallback for the common case where Euclid's panel has hidden ADOFAI's event inspector.
        // GameCompat.TryUpdatePropertyText must be called first so both coordinate fields contain the
        // new raw value. We then invoke the exact input's existing ADOFAI end-edit listeners even
        // though the GameObject is inactive.
        internal static bool TryInvokeHiddenInspectorEndEdit(LevelEvent ev, string key)
        {
            if (ev == null || ev.eventType != LevelEventType.PositionTrack ||
                !string.Equals(key, "positionOffset", StringComparison.OrdinalIgnoreCase) ||
                ResolvePropertyRootMethod == null || ScoreInputMethod == null)
            {
                return false;
            }

            try
            {
                var rootObject = ResolvePropertyRootMethod.Invoke(null, new object[] { scnEditor.instance, ev });
                if (!(rootObject is Component root))
                {
                    return false;
                }

                var rawOffset = ReadOffset(ev);
                Component best = null;
                var bestScore = int.MinValue;

                var tmpInputs = root.GetComponentsInChildren<TMP_InputField>(true);
                for (var i = 0; i < tmpInputs.Length; i++)
                {
                    var input = tmpInputs[i];
                    if (input == null)
                    {
                        continue;
                    }

                    var score = InvokeScore(input, root.transform, input.text, rawOffset);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = input;
                    }
                }

                var legacyInputs = root.GetComponentsInChildren<InputField>(true);
                for (var i = 0; i < legacyInputs.Length; i++)
                {
                    var input = legacyInputs[i];
                    if (input == null)
                    {
                        continue;
                    }

                    var score = InvokeScore(input, root.transform, input.text, rawOffset);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = input;
                    }
                }

                if (best == null || bestScore < 250)
                {
                    return false;
                }

                if (best is TMP_InputField tmp)
                {
                    tmp.onEndEdit.Invoke(tmp.text);
                    settleThroughFrame = Time.frameCount + 2;
                    return true;
                }

                if (best is InputField legacy)
                {
                    legacy.onEndEdit.Invoke(legacy.text);
                    settleThroughFrame = Time.frameCount + 2;
                    return true;
                }
            }
            catch (Exception ex)
            {
                EuclidMod.Logger?.Log("PositionTrack hidden inspector commit failed: " + ex.Message);
            }

            return false;
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

        private static int InvokeScore(Component input, Transform root, string text, Vector2 offset)
        {
            try
            {
                var raw = ScoreInputMethod.Invoke(null, new object[] { input, root, text, offset });
                return raw is int score ? score : int.MinValue;
            }
            catch (Exception)
            {
                return int.MinValue;
            }
        }

        private static Vector2 ReadOffset(LevelEvent ev)
        {
            try
            {
                return ev.Get<Vector2>("positionOffset");
            }
            catch (Exception)
            {
                return Vector2.zero;
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
