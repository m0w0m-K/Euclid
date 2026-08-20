using UnityEngine;

namespace Euclid
{
    // While shape snapping is enabled, switching the single selected construction shape should
    // immediately move the current coordinate target onto the newly selected geometry. This keeps
    // the snap mode persistent instead of requiring the Snap button to be toggled again.
    internal sealed class SelectedShapeSnapSync : MonoBehaviour
    {
        private ConstructionShape lastSelectedShape;
        private bool snapWasEnabled;

        internal static void Install()
        {
            var behaviour = EuclidMod.Behaviour;
            if (behaviour == null || behaviour.GetComponent<SelectedShapeSnapSync>() != null)
            {
                return;
            }

            behaviour.gameObject.AddComponent<SelectedShapeSnapSync>();
        }

        private void Update()
        {
            if (!EuclidMod.Enabled)
            {
                snapWasEnabled = false;
                lastSelectedShape = ConstructionShapeTool.PrimarySelectedShape;
                return;
            }

            var selectedShape = ConstructionShapeTool.PrimarySelectedShape;
            var snapEnabled = GuideLineTool.SnapSelectedShapeDrag;

            if (!snapEnabled)
            {
                // Track the current selection while disabled so merely turning snap on does not
                // cause a second redundant snap; ToggleSelectedShapeSnap already performs the first.
                snapWasEnabled = false;
                lastSelectedShape = selectedShape;
                return;
            }

            if (!snapWasEnabled)
            {
                snapWasEnabled = true;
                lastSelectedShape = selectedShape;
                return;
            }

            if (selectedShape == null || ReferenceEquals(selectedShape, lastSelectedShape))
            {
                lastSelectedShape = selectedShape;
                return;
            }

            var cameraFrame = EuclidMod.Behaviour != null
                ? EuclidMod.Behaviour.CameraFrame
                : CameraFrameSnapshot.Unavailable(string.Empty);

            // ADOFAI can replace the selected LevelEvent for one frame after PositionTrack is
            // applied. Do not consume the shape-selection change until a valid target exists;
            // retry next Update instead of silently losing the automatic snap.
            if (!CoordinateSnapTool.CanSnapSelectedTargetToSelectedShape(
                    cameraFrame,
                    GuideLineTool.CoordinateKeyText))
            {
                return;
            }

            GuideLineTool.SnapSelectedToShape(cameraFrame);
            lastSelectedShape = selectedShape;
        }
    }
}
