using System;
using UnityEngine;

namespace Euclid
{
    internal static class CameraFrameOverlay
    {
        private const float CenterHandleRadius = 18f;
        private const float ConstructionPointPickRadius = 14f;
        private static Texture2D whiteTexture;
        private static bool draggingCenter;
        private static bool draggingPositionOffset;
        private static bool dragSavedUndoState;
        private static bool positionOffsetDragSavedUndoState;

        internal static void DrawGuideLine(GuideLineSnapshot guideLine)
        {
            if (!guideLine.IsValid || !CanDrawInEditor())
            {
                return;
            }

            var camera = GetEditorCamera();
            if (camera == null)
            {
                return;
            }

            DrawInfiniteGuideLine(camera, guideLine, new Color(0.2f, 0.85f, 1f, 0.72f), new Color(0.2f, 0.85f, 1f, 0.95f), 2f);
        }

        internal static void DrawSavedGuideLines()
        {
            if (!CanDrawInEditor())
            {
                return;
            }

            var camera = GetEditorCamera();
            if (camera == null)
            {
                return;
            }

            if (GuideLineTool.TryGetSavedLine(1, out var line1))
            {
                DrawInfiniteGuideLine(camera, line1, new Color(1f, 0.55f, 0.18f, 0.5f), new Color(1f, 0.55f, 0.18f, 0.9f), 1.5f);
            }

            if (GuideLineTool.TryGetSavedLine(2, out var line2))
            {
                DrawInfiniteGuideLine(camera, line2, new Color(0.85f, 0.35f, 1f, 0.5f), new Color(0.85f, 0.35f, 1f, 0.9f), 1.5f);
            }

            if (GuideLineTool.TryGetIntersection(out var intersection))
            {
                var point = WorldToGui(camera, intersection.ToVector2());
                var color = new Color(1f, 1f, 0.25f, 0.98f);
                DrawCross(point, color, 11f, 2.5f);
                DrawDiamond(point, color, 8f, 2f);
            }

            DrawCircle(camera, GuideLineTool.CircleSnapshot);
        }

        // Screen-space hit test used while one of the endpoint Pick buttons is armed.
        // This intentionally only sees visible, already-drawn point shapes. The shape-list rows
        // are not a pick source; the user must click the point marker in the editor viewport.
        internal static bool TryPickConstructionPointAtScreenPosition(Vector2 screenPoint, out ConstructionShape pickedShape)
        {
            // Input.mousePosition / WorldToScreenPoint use bottom-left origin, while IMGUI uses
            // top-left origin. Convert to the exact coordinate system used to draw the point marker.
            var guiPoint = new Vector2(screenPoint.x, Screen.height - screenPoint.y);
            return TryPickConstructionPointAtGuiPosition(guiPoint, out pickedShape);
        }

        // Preferred hit test for OnGUI mouse events. The dedicated Canvas renderer and this picker
        // both use the editor camera projection, so visible point markers and hit targets stay aligned.
        internal static bool TryPickConstructionPointAtGuiPosition(Vector2 guiPoint, out ConstructionShape pickedShape)
        {
            pickedShape = null;
            if (!CanDrawInEditor())
            {
                return false;
            }

            var camera = GetEditorCamera();
            if (camera == null)
            {
                return false;
            }

            var bestDistanceSqr = ConstructionPointPickRadius * ConstructionPointPickRadius;
            var shapes = ConstructionShapeTool.Shapes;
            for (var i = shapes.Count - 1; i >= 0; i--)
            {
                var shape = shapes[i];
                if (shape == null ||
                    !ConstructionShapeTool.IsVisible(shape) ||
                    !ConstructionShapeTool.IsDrawn(shape) ||
                    ConstructionShapeTool.GetDrawnType(shape) != ConstructionShapeType.Point)
                {
                    continue;
                }

                var world = ConstructionShapeTool.GetDrawnPointWorld(shape, 0).ToVector2();
                var marker = WorldToGui(camera, world);
                var delta = marker - guiPoint;
                var distanceSqr = delta.sqrMagnitude;
                if (distanceSqr > bestDistanceSqr)
                {
                    continue;
                }

                bestDistanceSqr = distanceSqr;
                pickedShape = shape;
            }

            return pickedShape != null;
        }

        internal static bool DrawPositionOffsetTarget(GuideLineSnapshot guideLine)
        {
            if (!CanDrawInEditor())
            {
                CancelPositionOffsetDrag();
                return false;
            }

            var camera = GetEditorCamera();
            if (camera == null ||
                !CoordinateSnapTool.TryGetFocusedPositionOffsetPoint(out var worldPoint, out _))
            {
                CancelPositionOffsetDrag();
                return false;
            }

            var point = WorldToGui(camera, worldPoint);

            // Visuals for effect-position targets are rendered by ConstructionShapeCanvasOverlay,
            // one Canvas sorting order below ADOFAI's editor UI. Keep only IMGUI hit-testing here:
            // GUI controls are still useful for drag capture, but drawing them here would put the
            // marker above inspector panels regardless of world Z.
            return HandlePositionOffsetDrag(camera, point, guideLine);
        }

        private static void DrawInfiniteGuideLine(Camera camera, GuideLineSnapshot guideLine, Color lineColor, Color anchorColor, float width)
        {
            if (!guideLine.IsValid)
            {
                return;
            }

            var anchor = WorldToGui(camera, guideLine.Anchor);
            var directionEnd = WorldToGui(camera, guideLine.Anchor + guideLine.Direction);
            var direction = directionEnd - anchor;
            if (direction.sqrMagnitude <= 0.000001f)
            {
                return;
            }

            direction.Normalize();
            var length = Mathf.Sqrt(Screen.width * Screen.width + Screen.height * Screen.height) * 2f;
            DrawLine(anchor - direction * length, anchor + direction * length, lineColor, width);
            DrawCross(anchor, anchorColor, 6f, 2f);
        }

        private static void DrawCircle(Camera camera, ConstructionCircleSnapshot circle)
        {
            if (!circle.IsValid)
            {
                return;
            }

            DrawCircle(
                camera,
                circle.CenterD,
                circle.Radius,
                new Color(0.3f, 0.95f, 1f, 0.62f),
                1.75f);
        }

        private static void DrawCircle(Camera camera, Vector2d center, double radius, Color color, float width)
        {
            if (radius <= 0.000001d)
            {
                return;
            }

            const int segments = 96;
            var previous = WorldToGui(camera, new Vector2((float)(center.X + radius), (float)center.Y));

            for (var i = 1; i <= segments; i++)
            {
                var angle = i * Mathf.PI * 2f / segments;
                var point = WorldToGui(
                    camera,
                    new Vector2(
                        (float)(center.X + Math.Cos(angle) * radius),
                        (float)(center.Y + Math.Sin(angle) * radius)));
                DrawLine(previous, point, color, width);
                previous = point;
            }

            // Do not draw a center cross. The circle itself is the construction result.
        }

        internal static bool Draw(CameraFrameSnapshot snapshot, GuideLineSnapshot guideLine)
        {
            if (snapshot.State != CameraFrameState.Ready)
            {
                CancelCenterDrag();
                return false;
            }

            var camera = GetEditorCamera();
            if (camera == null)
            {
                CancelCenterDrag();
                return false;
            }

            // Visuals are intentionally rendered by ConstructionShapeCanvasOverlay one sorting
            // order below ADOFAI's editor UI. This IMGUI path now handles interaction only.
            var center = WorldToGui(camera, snapshot.Center);
            return HandleCenterDrag(camera, snapshot, center, guideLine);
        }

        private static bool CanDrawInEditor()
        {
            var editor = scnEditor.instance;
            return editor != null && !GameCompat.IsEditorPlaying(editor);
        }

        private static Camera GetEditorCamera()
        {
            var editorCamera = GameCompat.GetEditorCamera(scnEditor.instance);
            return editorCamera != null ? editorCamera : Camera.main;
        }

        private static Vector2 WorldToGui(Camera camera, Vector2 world)
        {
            var screen = camera.WorldToScreenPoint(new Vector3(world.x, world.y, 0f));
            return new Vector2(screen.x, Screen.height - screen.y);
        }

        private static Vector2 GuiToWorld(Camera camera, Vector2 gui)
        {
            var depth = Mathf.Abs(camera.transform.position.z);
            if (depth <= 0.0001f)
            {
                depth = 10f;
            }

            var screen = new Vector3(gui.x, Screen.height - gui.y, depth);
            var world = camera.ScreenToWorldPoint(screen);
            return new Vector2(world.x, world.y);
        }

        private static void DrawReferenceMarker(CameraFrameSnapshot snapshot, Vector2 reference, Vector2 center)
        {
            var isPlayerProxy = snapshot.RelativeTo == CamMovementType.Player;
            var lineColor = isPlayerProxy
                ? new Color(0.35f, 0.75f, 1f, 0.9f)
                : new Color(0.2f, 1f, 0.5f, 0.9f);
            var markerColor = isPlayerProxy
                ? new Color(0.9f, 0.45f, 1f, 0.98f)
                : new Color(0.35f, 0.75f, 1f, 0.95f);

            DrawLine(reference, center, lineColor, 2f);
            DrawCross(reference, markerColor, isPlayerProxy ? 8f : 6f, isPlayerProxy ? 2.5f : 2f);

            if (isPlayerProxy)
            {
                DrawDiamond(reference, markerColor, 9f, 2f);
            }
        }

        private static void DrawDiamond(Vector2 center, Color color, float radius, float width)
        {
            var top = center + Vector2.up * radius;
            var right = center + Vector2.right * radius;
            var bottom = center + Vector2.down * radius;
            var left = center + Vector2.left * radius;
            DrawLine(top, right, color, width);
            DrawLine(right, bottom, color, width);
            DrawLine(bottom, left, color, width);
            DrawLine(left, top, color, width);
        }

        private static bool HandleCenterDrag(Camera camera, CameraFrameSnapshot snapshot, Vector2 center, GuideLineSnapshot guideLine)
        {
            if (!GuideLineTool.EnableCameraDrag)
            {
                CancelCenterDrag();
                return false;
            }

            var ev = Event.current;
            var controlId = GUIUtility.GetControlID("EuclidCameraCenterDrag".GetHashCode(), FocusType.Passive);

            switch (ev.GetTypeForControl(controlId))
            {
                case EventType.MouseDown:
                    if (ev.button == 0 && Vector2.Distance(ev.mousePosition, center) <= CenterHandleRadius)
                    {
                        GUIUtility.hotControl = controlId;
                        GUIUtility.keyboardControl = 0;
                        draggingCenter = true;
                        dragSavedUndoState = false;
                        SuppressEditorMouseInput();
                        ev.Use();
                    }

                    break;

                case EventType.MouseDrag:
                    if (draggingCenter && GUIUtility.hotControl == controlId)
                    {
                        SuppressEditorMouseInput();
                        var changed = MoveCenterToMouse(camera, snapshot, guideLine, !dragSavedUndoState);
                        dragSavedUndoState = dragSavedUndoState || changed;
                        ev.Use();
                        return changed;
                    }

                    break;

                case EventType.MouseUp:
                    if (draggingCenter && GUIUtility.hotControl == controlId && ev.button == 0)
                    {
                        SuppressEditorMouseInput();
                        var changed = dragSavedUndoState && MoveCenterToMouse(camera, snapshot, guideLine, saveUndoState: false);
                        CancelCenterDrag();
                        ev.Use();
                        return changed;
                    }

                    break;
            }

            return false;
        }

        private static bool MoveCenterToMouse(Camera camera, CameraFrameSnapshot snapshot, GuideLineSnapshot guideLine, bool saveUndoState)
        {
            var world = GuiToWorld(camera, Event.current.mousePosition);
            if (TrySnapToSelectedConstructionShape(world, out var snappedWorld))
            {
                world = snappedWorld;
            }
            else if (GuideLineTool.SnapCameraDrag && guideLine.IsValid)
            {
                world = guideLine.Project(world);
            }

            return CameraFrameEditor.TryMoveCenter(snapshot, world, saveUndoState);
        }

        private static bool HandlePositionOffsetDrag(Camera camera, Vector2 center, GuideLineSnapshot guideLine)
        {
            var ev = Event.current;
            var controlId = GUIUtility.GetControlID("EuclidPositionOffsetDrag".GetHashCode(), FocusType.Passive);

            switch (ev.GetTypeForControl(controlId))
            {
                case EventType.MouseDown:
                    if (ev.button == 0 && Vector2.Distance(ev.mousePosition, center) <= CenterHandleRadius)
                    {
                        GUIUtility.hotControl = controlId;
                        GUIUtility.keyboardControl = 0;
                        draggingPositionOffset = true;
                        positionOffsetDragSavedUndoState = false;
                        SuppressEditorMouseInput();
                        ev.Use();
                    }

                    break;

                case EventType.MouseDrag:
                    if (draggingPositionOffset && GUIUtility.hotControl == controlId)
                    {
                        SuppressEditorMouseInput();
                        var changed = MovePositionOffsetToMouse(camera, guideLine, !positionOffsetDragSavedUndoState);
                        positionOffsetDragSavedUndoState = positionOffsetDragSavedUndoState || changed;
                        ev.Use();
                        return changed;
                    }

                    break;

                case EventType.MouseUp:
                    if (draggingPositionOffset && GUIUtility.hotControl == controlId && ev.button == 0)
                    {
                        SuppressEditorMouseInput();
                        var changed = positionOffsetDragSavedUndoState &&
                            MovePositionOffsetToMouse(camera, guideLine, saveUndoState: false);
                        CancelPositionOffsetDrag();
                        ev.Use();
                        return changed;
                    }

                    break;
            }

            return false;
        }

        private static bool MovePositionOffsetToMouse(Camera camera, GuideLineSnapshot guideLine, bool saveUndoState)
        {
            var world = GuiToWorld(camera, Event.current.mousePosition);
            if (TrySnapToSelectedConstructionShape(world, out var snappedWorld))
            {
                world = snappedWorld;
            }
            else if (GuideLineTool.SnapCameraDrag && guideLine.IsValid)
            {
                world = guideLine.Project(world);
            }

            return CoordinateSnapTool.TryMoveFocusedPositionOffsetToWorld(world, saveUndoState, out _);
        }

        private static bool TrySnapToSelectedConstructionShape(Vector2 world, out Vector2 snappedWorld)
        {
            snappedWorld = world;
            if (!GuideLineTool.SnapSelectedShapeDrag)
            {
                return false;
            }

            if (!ConstructionShapeTool.TryGetSnapPointForSingleSelectedShape(new Vector2d(world), out var point))
            {
                return false;
            }

            snappedWorld = point.ToVector2();
            return true;
        }

        private static void SuppressEditorMouseInput()
        {
            try
            {
                Input.ResetInputAxes();
            }
            catch
            {
                // Some platforms do not allow resetting axes during IMGUI; Event.Use still handles the normal path.
            }
        }

        private static void DrawCenterHandle(Vector2 center)
        {
            var color = draggingCenter
                ? Color.Lerp(EuclidMod.CameraFrameColor, Color.white, 0.7f)
                : EuclidMod.CameraFrameColor;

            // Preserve a compact draggable center handle without the old crosshair.
            DrawLine(center + new Vector2(-5f, -5f), center + new Vector2(5f, -5f), color, 1.5f);
            DrawLine(center + new Vector2(5f, -5f), center + new Vector2(5f, 5f), color, 1.5f);
            DrawLine(center + new Vector2(5f, 5f), center + new Vector2(-5f, 5f), color, 1.5f);
            DrawLine(center + new Vector2(-5f, 5f), center + new Vector2(-5f, -5f), color, 1.5f);
        }

        private static void CancelCenterDrag()
        {
            if (draggingCenter)
            {
                GUIUtility.hotControl = 0;
            }

            draggingCenter = false;
            dragSavedUndoState = false;
        }

        private static void CancelPositionOffsetDrag()
        {
            if (draggingPositionOffset)
            {
                GUIUtility.hotControl = 0;
            }

            draggingPositionOffset = false;
            positionOffsetDragSavedUndoState = false;
        }

        private static void DrawLine(Vector2 start, Vector2 end, Color color, float width)
        {
            EnsureTexture();

            var previousMatrix = GUI.matrix;
            var previousColor = GUI.color;
            var delta = end - start;
            var angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            var length = delta.magnitude;

            GUI.color = color;
            GUIUtility.RotateAroundPivot(angle, start);
            GUI.DrawTexture(new Rect(start.x, start.y - width * 0.5f, length, width), whiteTexture);
            GUI.matrix = previousMatrix;
            GUI.color = previousColor;
        }

        private static void DrawCross(Vector2 center, Color color, float radius, float width)
        {
            DrawLine(center + Vector2.left * radius, center + Vector2.right * radius, color, width);
            DrawLine(center + Vector2.down * radius, center + Vector2.up * radius, color, width);
        }

        private static void EnsureTexture()
        {
            if (whiteTexture != null)
            {
                return;
            }

            whiteTexture = new Texture2D(1, 1);
            whiteTexture.SetPixel(0, 0, Color.white);
            whiteTexture.Apply();
        }
    }
}
