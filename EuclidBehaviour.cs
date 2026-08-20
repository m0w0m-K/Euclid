using System;
using UnityEngine;

namespace Euclid
{
    // Runtime coordinator. Capture model snapshots here, then pass them to the panel/overlay.
    // Update may coordinate scene-level input ordering (for example, point-marker picking before
    // tile-selection sync), but feature state and UI construction belong in the tool/panel files.
    internal sealed class EuclidBehaviour : MonoBehaviour
    {
        private static readonly string[] LevelPathMemberNames =
        {
            "levelPath",
            "currentLevelPath",
            "loadedLevelPath",
            "filePath",
            "filepath",
            "filename",
            "fileName",
            "path",
        };

        private float nextCaptureTime;
        private EuclidPanel internalPanel;
        private object editorLevelIdentity;
        private object editorSettingsPanelIdentity;
        private string editorLevelPathKey;
        private bool editorMapReady;

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
            var editor = scnEditor.instance;
            var floors = GameCompat.GetFloors(editor);
            var hasLevelData = editor != null &&
                GameCompat.TryGetMember(editor, "levelData", out object levelData) &&
                levelData != null;
            var ready = editor != null && hasLevelData && floors.Count > 0;

            // Opening another map passes through a teardown/reload boundary even on game builds
            // that reuse the same scnEditor or LevelData instance. Treat that boundary itself as a
            // map change instead of deliberately carrying Euclid's construction geometry across it.
            if (!ready)
            {
                if (editorMapReady)
                {
                    ResetEditorMapLocalState();
                }

                editorMapReady = false;
                editorLevelIdentity = null;
                editorSettingsPanelIdentity = null;
                editorLevelPathKey = null;
                return;
            }

            var settingsPanel = GameCompat.GetSettingsPanel(editor);
            var pathKey = ResolveLevelPathKey(editor, levelData);

            if (!editorMapReady)
            {
                editorMapReady = true;
                editorLevelIdentity = levelData;
                editorSettingsPanelIdentity = settingsPanel;
                editorLevelPathKey = pathKey;
                return;
            }

            var changed = false;
            if (!string.IsNullOrWhiteSpace(pathKey) && !string.IsNullOrWhiteSpace(editorLevelPathKey))
            {
                changed = !string.Equals(pathKey, editorLevelPathKey, StringComparison.OrdinalIgnoreCase);
            }
            else if (!ReferenceEquals(editorLevelIdentity, levelData))
            {
                changed = true;
            }
            else if (editorSettingsPanelIdentity != null && settingsPanel != null &&
                     !ReferenceEquals(editorSettingsPanelIdentity, settingsPanel))
            {
                // Some editor builds recreate their inspector hierarchy when a different map opens
                // while retaining/reusing LevelData. This is a useful secondary map-boundary token.
                changed = true;
            }

            if (changed)
            {
                ResetEditorMapLocalState();
            }

            editorLevelIdentity = levelData;
            editorSettingsPanelIdentity = settingsPanel;
            if (!string.IsNullOrWhiteSpace(pathKey))
            {
                editorLevelPathKey = pathKey;
            }
        }

        private void ResetEditorMapLocalState()
        {
            GuideLineTool.SnapSelectedShapeDrag = false;
            internalPanel?.HandleEditorMapChanged();
            Snapshot = MeasureSnapshot.Unavailable("Map changed.");
            CameraFrame = CameraFrameSnapshot.Unavailable("Map changed.");
            nextCaptureTime = 0f;
        }

        private static string ResolveLevelPathKey(scnEditor editor, object levelData)
        {
            var fromEditor = TryGetPathLikeMember(editor);
            if (!string.IsNullOrWhiteSpace(fromEditor))
            {
                return fromEditor;
            }

            return TryGetPathLikeMember(levelData);
        }

        private static string TryGetPathLikeMember(object target)
        {
            if (target == null)
            {
                return null;
            }

            for (var i = 0; i < LevelPathMemberNames.Length; i++)
            {
                if (GameCompat.TryGetMember(target, LevelPathMemberNames[i], out string value) &&
                    !string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return null;
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
