using ADOFAI;
using UnityEngine;

namespace Euclid
{
    // Direct manipulation for the selected MoveCamera frame.
    //
    // Center dragging remains in CameraFrameOverlay. This component owns only transform handles:
    // - four corners scale the frame uniformly by editing MoveCamera.zoom
    // - the handle above the top edge edits MoveCamera.rotation
    //
    // The drag always starts from CameraFrameSnapshot's effective value. If the selected event has
    // zoom/rotation disabled and therefore inherits an earlier value, the first actual drag enables
    // the property at that inherited value instead of jumping to the property's serialized default.
    internal sealed class CameraFrameTransformHandles : MonoBehaviour
    {
        private const float ZoomHandleRadius = 13f;
        private const float ZoomHandleHalfSize = 5f;
        private const float RotationHandleRadius = 14f;
        private const float RotationHandleVisualRadius = 6f;
        private const float RotationHandleOffset = 34f;
        private const float MinWorldRadius = 0.0001f;

        private static Texture2D whiteTexture;

        private bool draggingZoom;
        private bool draggingRotation;
        private bool dragSavedUndoState;
        private LevelEvent dragEvent;
        private Vector2 dragCenterWorld;
        private float dragStartWorldRadius;
        private float dragStartZoom;
        private float dragStartRotation;
        private float dragStartMouseAngle;

        internal static void Install()
        {
            var behaviour = EuclidMod.Behaviour;
            if (behaviour == null || behaviour.GetComponent<CameraFrameTransformHandles>() != null)
            {
                return;
            }

            behaviour.gameObject.AddComponent<CameraFrameTransformHandles>();
        }

        private void OnGUI()
        {
            if (!CanInteract(out var snapshot, out var camera))
            {
                CancelDrag();
                return;
            }

            if ((draggingZoom || draggingRotation) && !ReferenceEquals(dragEvent, snapshot.SelectedEvent))
            {
                CancelDrag();
            }

            if (!TryGetHandleGeometry(camera, snapshot, out var centerGui, out var corners, out var topMid, out var rotationHandle))
            {
                CancelDrag();
                return;
            }

            DrawHandles(centerGui, corners, topMid, rotationHandle);

            // A drag keeps its hotControl, so only the active mode sees MouseDrag/MouseUp. On a new
            // MouseDown, test the small transform handles before CameraFrameOverlay gets a chance to
            // treat the click as a center drag.
            var changed = HandleZoom(camera, snapshot, corners);
            changed |= HandleRotation(camera, snapshot, rotationHandle);
            if (changed)
            {
                RefreshAfterEdit();
            }
        }

        private static bool CanInteract(out CameraFrameSnapshot snapshot, out Camera camera)
        {
            snapshot = EuclidMod.Behaviour != null
                ? EuclidMod.Behaviour.CameraFrame
                : CameraFrameSnapshot.Unavailable(string.Empty);
            camera = null;

            if (!EuclidMod.Enabled || !EuclidMod.ShowCameraFrame || !GuideLineTool.EnableCameraDrag ||
                snapshot.State != CameraFrameState.Ready || snapshot.SelectedEvent == null)
            {
                return false;
            }

            var editor = scnEditor.instance;
            if (editor == null || GameCompat.IsEditorPlaying(editor))
            {
                return false;
            }

            camera = GameCompat.GetEditorCamera(editor);
            if (camera == null)
            {
                camera = Camera.main;
            }
            return camera != null;
        }

        private bool HandleZoom(Camera camera, CameraFrameSnapshot snapshot, Vector2[] corners)
        {
            var ev = Event.current;
            if (ev == null)
            {
                return false;
            }

            var controlId = GUIUtility.GetControlID("EuclidCameraZoomHandle".GetHashCode(), FocusType.Passive);
            switch (ev.GetTypeForControl(controlId))
            {
                case EventType.MouseDown:
                    if (ev.button != 0 || draggingRotation)
                    {
                        break;
                    }

                    var cornerIndex = FindNearestHandle(ev.mousePosition, corners, ZoomHandleRadius);
                    if (cornerIndex < 0)
                    {
                        break;
                    }

                    var cornerWorld = snapshot.Corners[cornerIndex];
                    dragStartWorldRadius = Vector2.Distance(snapshot.Center, cornerWorld);
                    if (dragStartWorldRadius <= MinWorldRadius)
                    {
                        break;
                    }

                    GUIUtility.hotControl = controlId;
                    GUIUtility.keyboardControl = 0;
                    draggingZoom = true;
                    draggingRotation = false;
                    dragSavedUndoState = false;
                    dragEvent = snapshot.SelectedEvent;
                    dragCenterWorld = snapshot.Center;
                    dragStartZoom = Mathf.Max(snapshot.ZoomPercent, 1f);
                    SuppressEditorMouseInput();
                    ev.Use();
                    break;

                case EventType.MouseDrag:
                    if (!draggingZoom || GUIUtility.hotControl != controlId)
                    {
                        break;
                    }

                    SuppressEditorMouseInput();
                    var changed = ApplyZoomFromMouse(camera, snapshot, !dragSavedUndoState);
                    dragSavedUndoState |= changed;
                    ev.Use();
                    return changed;

                case EventType.MouseUp:
                    if (!draggingZoom || GUIUtility.hotControl != controlId || ev.button != 0)
                    {
                        break;
                    }

                    SuppressEditorMouseInput();
                    var finalChanged = dragSavedUndoState && ApplyZoomFromMouse(camera, snapshot, saveUndoState: false);
                    CancelDrag();
                    ev.Use();
                    return finalChanged;
            }

            return false;
        }

        private bool HandleRotation(Camera camera, CameraFrameSnapshot snapshot, Vector2 rotationHandle)
        {
            var ev = Event.current;
            if (ev == null)
            {
                return false;
            }

            var controlId = GUIUtility.GetControlID("EuclidCameraRotationHandle".GetHashCode(), FocusType.Passive);
            switch (ev.GetTypeForControl(controlId))
            {
                case EventType.MouseDown:
                    if (ev.button != 0 || draggingZoom ||
                        Vector2.Distance(ev.mousePosition, rotationHandle) > RotationHandleRadius)
                    {
                        break;
                    }

                    var startWorld = GuiToWorld(camera, ev.mousePosition);
                    var startVector = startWorld - snapshot.Center;
                    if (startVector.sqrMagnitude <= MinWorldRadius * MinWorldRadius)
                    {
                        break;
                    }

                    GUIUtility.hotControl = controlId;
                    GUIUtility.keyboardControl = 0;
                    draggingRotation = true;
                    draggingZoom = false;
                    dragSavedUndoState = false;
                    dragEvent = snapshot.SelectedEvent;
                    dragCenterWorld = snapshot.Center;
                    dragStartRotation = snapshot.RotationDegrees;
                    dragStartMouseAngle = Mathf.Atan2(startVector.y, startVector.x) * Mathf.Rad2Deg;
                    SuppressEditorMouseInput();
                    ev.Use();
                    break;

                case EventType.MouseDrag:
                    if (!draggingRotation || GUIUtility.hotControl != controlId)
                    {
                        break;
                    }

                    SuppressEditorMouseInput();
                    var changed = ApplyRotationFromMouse(camera, snapshot, !dragSavedUndoState);
                    dragSavedUndoState |= changed;
                    ev.Use();
                    return changed;

                case EventType.MouseUp:
                    if (!draggingRotation || GUIUtility.hotControl != controlId || ev.button != 0)
                    {
                        break;
                    }

                    SuppressEditorMouseInput();
                    var finalChanged = dragSavedUndoState && ApplyRotationFromMouse(camera, snapshot, saveUndoState: false);
                    CancelDrag();
                    ev.Use();
                    return finalChanged;
            }

            return false;
        }

        private bool ApplyZoomFromMouse(Camera camera, CameraFrameSnapshot snapshot, bool saveUndoState)
        {
            if (!ReferenceEquals(dragEvent, snapshot.SelectedEvent) || dragStartWorldRadius <= MinWorldRadius)
            {
                return false;
            }

            var mouseWorld = GuiToWorld(camera, Event.current.mousePosition);
            var currentRadius = Vector2.Distance(dragCenterWorld, mouseWorld);
            var ratio = Mathf.Max(currentRadius / dragStartWorldRadius, 0.01f);
            var zoom = Mathf.Clamp(dragStartZoom * ratio, 1f, 10000f);
            return CameraFrameEditor.TrySetZoom(snapshot, zoom, saveUndoState);
        }

        private bool ApplyRotationFromMouse(Camera camera, CameraFrameSnapshot snapshot, bool saveUndoState)
        {
            if (!ReferenceEquals(dragEvent, snapshot.SelectedEvent))
            {
                return false;
            }

            var mouseWorld = GuiToWorld(camera, Event.current.mousePosition);
            var vector = mouseWorld - dragCenterWorld;
            if (vector.sqrMagnitude <= MinWorldRadius * MinWorldRadius)
            {
                return false;
            }

            var angle = Mathf.Atan2(vector.y, vector.x) * Mathf.Rad2Deg;
            var delta = Mathf.DeltaAngle(dragStartMouseAngle, angle);
            var rotation = dragStartRotation + delta;
            return CameraFrameEditor.TrySetRotation(snapshot, rotation, saveUndoState);
        }

        private void DrawHandles(Vector2 center, Vector2[] corners, Vector2 topMid, Vector2 rotationHandle)
        {
            var baseColor = EuclidMod.CameraFrameColor;
            var zoomColor = draggingZoom ? Color.Lerp(baseColor, Color.white, 0.7f) : baseColor;
            var rotationColor = draggingRotation ? Color.Lerp(baseColor, Color.white, 0.7f) : baseColor;

            for (var i = 0; i < corners.Length; i++)
            {
                DrawSquare(corners[i], zoomColor, ZoomHandleHalfSize, 1.8f);
            }

            DrawLine(topMid, rotationHandle, rotationColor, 1.5f);
            DrawCircle(rotationHandle, rotationColor, RotationHandleVisualRadius, 1.8f);

            // A short radial tick makes the rotation handle read as distinct from a fifth resize
            // handle without adding text to the editor viewport.
            var radial = rotationHandle - center;
            if (radial.sqrMagnitude > 0.001f)
            {
                radial.Normalize();
                var tangent = new Vector2(-radial.y, radial.x);
                DrawLine(
                    rotationHandle - tangent * 3.5f,
                    rotationHandle + tangent * 3.5f,
                    rotationColor,
                    1.4f);
            }
        }

        private static bool TryGetHandleGeometry(
            Camera camera,
            CameraFrameSnapshot snapshot,
            out Vector2 center,
            out Vector2[] corners,
            out Vector2 topMid,
            out Vector2 rotationHandle)
        {
            center = WorldToGui(camera, snapshot.Center);
            var worldCorners = snapshot.Corners;
            corners = null;
            topMid = Vector2.zero;
            rotationHandle = Vector2.zero;
            if (worldCorners == null || worldCorners.Length != 4)
            {
                return false;
            }

            corners = new Vector2[4];
            for (var i = 0; i < worldCorners.Length; i++)
            {
                corners[i] = WorldToGui(camera, worldCorners[i]);
            }

            // CameraFrameSnapshot orders corners as bottom-left, bottom-right, top-right, top-left.
            topMid = (corners[2] + corners[3]) * 0.5f;
            var outward = topMid - center;
            if (outward.sqrMagnitude <= 0.001f)
            {
                outward = Vector2.up;
            }
            else
            {
                outward.Normalize();
            }

            rotationHandle = topMid + outward * RotationHandleOffset;
            return true;
        }

        private static int FindNearestHandle(Vector2 mouse, Vector2[] points, float radius)
        {
            var best = radius * radius;
            var result = -1;
            for (var i = 0; i < points.Length; i++)
            {
                var sqr = (mouse - points[i]).sqrMagnitude;
                if (sqr <= best)
                {
                    best = sqr;
                    result = i;
                }
            }
            return result;
        }

        private void RefreshAfterEdit()
        {
            EuclidMod.Behaviour?.RefreshNow();
            ConstructionShapeCanvasOverlay.Refresh();
        }

        private void CancelDrag()
        {
            if (draggingZoom || draggingRotation)
            {
                GUIUtility.hotControl = 0;
            }

            draggingZoom = false;
            draggingRotation = false;
            dragSavedUndoState = false;
            dragEvent = null;
            dragStartWorldRadius = 0f;
        }

        private void OnDisable()
        {
            CancelDrag();
        }

        private void OnDestroy()
        {
            CancelDrag();
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

        private static void SuppressEditorMouseInput()
        {
            try
            {
                Input.ResetInputAxes();
            }
            catch
            {
                // Event.Use still suppresses the normal IMGUI path when the input backend rejects this.
            }
        }

        private static void DrawSquare(Vector2 center, Color color, float halfSize, float width)
        {
            var a = center + new Vector2(-halfSize, -halfSize);
            var b = center + new Vector2(halfSize, -halfSize);
            var c = center + new Vector2(halfSize, halfSize);
            var d = center + new Vector2(-halfSize, halfSize);
            DrawLine(a, b, color, width);
            DrawLine(b, c, color, width);
            DrawLine(c, d, color, width);
            DrawLine(d, a, color, width);
        }

        private static void DrawCircle(Vector2 center, Color color, float radius, float width)
        {
            const int segments = 20;
            var previous = center + Vector2.right * radius;
            for (var i = 1; i <= segments; i++)
            {
                var angle = Mathf.PI * 2f * i / segments;
                var point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                DrawLine(previous, point, color, width);
                previous = point;
            }
        }

        private static void DrawLine(Vector2 start, Vector2 end, Color color, float width)
        {
            EnsureTexture();
            var delta = end - start;
            if (delta.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            var previousMatrix = GUI.matrix;
            var previousColor = GUI.color;
            var angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            GUI.color = color;
            GUIUtility.RotateAroundPivot(angle, start);
            GUI.DrawTexture(
                new Rect(start.x, start.y - width * 0.5f, delta.magnitude, width),
                whiteTexture);
            GUI.matrix = previousMatrix;
            GUI.color = previousColor;
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
