using UnityEngine;

namespace Euclid
{
    // Runtime coordinator. Capture model snapshots here, then pass them to the panel/overlay.
    // Update may coordinate scene-level input ordering (for example, point-marker picking before
    // tile-selection sync), but feature state and UI construction belong in the tool/panel files.
    internal sealed class EuclidBehaviour : MonoBehaviour
    {
        private float nextCaptureTime;
        private EuclidPanel internalPanel;
        private object editorLevelIdentity;
        private bool hasEditorLevelIdentity;

        internal MeasureSnapshot Snapshot { get; private set; } = MeasureSnapshot.Unavailable("Not captured yet.");

        internal CameraFrameSnapshot CameraFrame { get; private set; } = CameraFrameSnapshot.Unavailable("Not captured yet.");

        private void Awake()
        {
            internalPanel = gameObject.AddComponent<EuclidPanel>();
        }

        private void Update()
        {
            if (!EuclidMod.Enabled)
            {
                internalPanel?.Hide();
                return;
            }

            DetectEditorMapChange();
            TryConsumeConstructionPointPick();
            TileSelectionOrderTracker.Refresh();

            if (Time.unscaledTime >= nextCaptureTime)
            {
                nextCaptureTime = Time.unscaledTime + 0.1f;
                RefreshNow();
            }

            if (GuideLineTool.SnapSelectedShapeDrag &&
                !CoordinateSnapTool.CanSnapSelectedTargetToSelectedShape(CameraFrame, GuideLineTool.CoordinateKeyText))
            {
                GuideLineTool.SnapSelectedShapeDrag = false;
            }

            if (EuclidMod.ShowOverlay)
            {
                internalPanel?.Tick(Snapshot, CameraFrame);
            }
            else
            {
                internalPanel?.Hide();
            }
        }

        private void DetectEditorMapChange()
        {
            var identity = GameCompat.GetEditorLevelIdentity(scnEditor.instance);
            if (identity == null)
            {
                // Keep the last non-null identity across the short teardown window between maps.
                return;
            }

            if (!hasEditorLevelIdentity)
            {
                editorLevelIdentity = identity;
                hasEditorLevelIdentity = true;
                return;
            }

            if (ReferenceEquals(editorLevelIdentity, identity))
            {
                return;
            }

            editorLevelIdentity = identity;
            internalPanel?.HandleEditorMapChanged();
        }

        private void TryConsumeConstructionPointPick()
        {
            if (internalPanel == null ||
                !internalPanel.IsPointPickPending ||
                !Input.GetMouseButtonDown(0))
            {
                return;
            }

            var mouse = (Vector2)Input.mousePosition;
            if (internalPanel.IsScreenPointOverToolUi(mouse))
            {
                return;
            }

            if (!CameraFrameOverlay.TryPickConstructionPointAtScreenPosition(mouse, out var source))
            {
                return;
            }

            if (internalPanel.TryApplyPendingPointPickFromScene(source))
            {
                // The editor may also see the same click as a floor click. Resetting the axes and
                // hiding built-in panels in TryApplyPendingPointPickFromScene keeps the tool active.
                try
                {
                    Input.ResetInputAxes();
                }
                catch
                {
                    // Some input backends reject ResetInputAxes; the pick itself is already complete.
                }
            }
        }

        private void TryConsumeConstructionPointPickFromGuiEvent()
        {
            if (internalPanel == null || !internalPanel.IsPointPickPending)
            {
                return;
            }

            var current = Event.current;
            if (current == null || current.type != EventType.MouseDown || current.button != 0)
            {
                return;
            }

            // Event.current.mousePosition uses top-left IMGUI coordinates. Point hit-testing converts
            // against the same editor-camera projection used by the Canvas shape renderer.
            var guiMouse = current.mousePosition;
            var screenMouse = new Vector2(guiMouse.x, Screen.height - guiMouse.y);
            if (internalPanel.IsScreenPointOverToolUi(screenMouse) ||
                !CameraFrameOverlay.TryPickConstructionPointAtGuiPosition(guiMouse, out var source) ||
                !internalPanel.TryApplyPendingPointPickFromScene(source))
            {
                return;
            }

            current.Use();
            try
            {
                Input.ResetInputAxes();
            }
            catch
            {
                // The point pick is already complete; this only suppresses the editor's same click.
            }
        }

        private void OnDisable()
        {
            internalPanel?.Hide();
            ConstructionShapeCanvasOverlay.SetVisible(false);
        }

        private void OnDestroy()
        {
            ConstructionShapeCanvasOverlay.Destroy();
        }

        private void OnGUI()
        {
            if (!EuclidMod.Enabled)
            {
                return;
            }

            TryConsumeConstructionPointPickFromGuiEvent();

            var guideLine = GuideLineTool.Snapshot;
            if (!IsEditorPlaying())
            {
                CameraFrameOverlay.DrawSavedGuideLines();
                CameraFrameOverlay.DrawGuideLine(guideLine);
                if (CameraFrameOverlay.DrawPositionOffsetTarget(guideLine))
                {
                    RefreshNow();
                    internalPanel?.Tick(Snapshot, CameraFrame);
                }
            }

            if (EuclidMod.ShowCameraFrame)
            {
                if (CameraFrameOverlay.Draw(CameraFrame, guideLine))
                {
                    RefreshNow();
                    internalPanel?.Tick(Snapshot, CameraFrame);
                }
            }
        }

        internal void RefreshNow()
        {
            Snapshot = MeasureSnapshot.Capture();
            CameraFrame = CameraFrameSnapshot.Capture();
        }

        internal void HidePanel()
        {
            internalPanel?.Hide();
        }

        private static bool IsEditorPlaying()
        {
            return GameCompat.IsEditorPlaying(scnEditor.instance);
        }
    }
}
