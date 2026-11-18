using System.Collections.Generic;
using ADOFAI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Euclid
{
    // Renders construction shapes on a dedicated child Canvas below ADOFAI's editor UI.
    //
    // Why this exists instead of OnGUI/GUI.depth:
    // GUI.depth only sorts IMGUI calls against other IMGUI calls. ADOFAI's inspector is a Unity
    // Canvas, so construction geometry could still appear above the panel. This layer is a child
    // Canvas with a sorting order immediately below the host editor Canvas. Result:
    //     world/tile camera < construction shapes < editor UI panels
    //
    // Keep point hit-testing in CameraFrameOverlay; it intentionally uses screen coordinates and
    // therefore does not depend on this renderer's Canvas scale factor.
    internal static class ConstructionShapeCanvasOverlay
    {
        private const string RootName = "Euclid_ConstructionShapeOverlay";
        private static GameObject rootObject;
        private static Canvas hostCanvas;
        private static Canvas layerCanvas;
        private static ConstructionShapeOverlayGraphic graphic;
        private static RectTransform layerRect;
        private static TMP_FontAsset labelFont;
        private static Material labelMaterial;
        private static readonly List<TextMeshProUGUI> labels = new List<TextMeshProUGUI>();

        internal static void Ensure(InspectorPanel panel)
        {
            if (panel == null)
            {
                return;
            }

            var nearestCanvas = panel.GetComponentInParent<Canvas>();
            if (nearestCanvas == null)
            {
                return;
            }

            if (rootObject != null && hostCanvas == nearestCanvas && graphic != null)
            {
                UpdateSorting();
                CaptureLabelStyle(panel);
                return;
            }

            Destroy();
            hostCanvas = nearestCanvas;
            CaptureLabelStyle(panel);

            rootObject = new GameObject(RootName, typeof(RectTransform), typeof(Canvas));
            rootObject.transform.SetParent(hostCanvas.transform, false);
            layerRect = rootObject.GetComponent<RectTransform>();
            Stretch(layerRect);

            layerCanvas = rootObject.GetComponent<Canvas>();
            layerCanvas.overrideSorting = true;
            layerCanvas.additionalShaderChannels = hostCanvas.additionalShaderChannels;
            UpdateSorting();

            var geometryObject = new GameObject("Geometry", typeof(RectTransform), typeof(CanvasRenderer), typeof(ConstructionShapeOverlayGraphic));
            geometryObject.transform.SetParent(rootObject.transform, false);
            var geometryRect = geometryObject.GetComponent<RectTransform>();
            Stretch(geometryRect);

            graphic = geometryObject.GetComponent<ConstructionShapeOverlayGraphic>();
            graphic.raycastTarget = false;
            graphic.color = Color.white;

            rootObject.SetActive(true);
        }

        internal static void Refresh()
        {
            if (rootObject == null || graphic == null)
            {
                return;
            }

            var visible = CanRender();
            if (rootObject.activeSelf != visible)
            {
                rootObject.SetActive(visible);
            }

            if (!visible)
            {
                return;
            }

            UpdateSorting();
            graphic.SetVerticesDirty();
            SyncLabels();
        }

        internal static void SetVisible(bool visible)
        {
            if (rootObject != null)
            {
                rootObject.SetActive(visible && CanRender());
            }
        }

        internal static void Destroy()
        {
            if (rootObject != null)
            {
                Object.Destroy(rootObject);
            }

            rootObject = null;
            hostCanvas = null;
            layerCanvas = null;
            graphic = null;
            layerRect = null;
            labels.Clear();
            labelFont = null;
            labelMaterial = null;
        }

        private static void CaptureLabelStyle(InspectorPanel panel)
        {
            if (panel != null && panel.title != null)
            {
                labelFont = panel.title.font;
                labelMaterial = panel.title.fontMaterial;
            }
        }

        private static void UpdateSorting()
        {
            if (layerCanvas == null || hostCanvas == null)
            {
                return;
            }

            layerCanvas.overrideSorting = true;
            var sortingReference = hostCanvas.overrideSorting ? hostCanvas : hostCanvas.rootCanvas;
            if (sortingReference == null)
            {
                sortingReference = hostCanvas;
            }

            layerCanvas.sortingLayerID = sortingReference.sortingLayerID;
            // One order below the editor UI keeps shapes above the camera/world but below panels.
            layerCanvas.sortingOrder = sortingReference.sortingOrder - 1;
        }

        private static bool CanRender()
        {
            var editor = scnEditor.instance;
            return editor != null && !GameCompat.IsEditorPlaying(editor);
        }

        private static void SyncLabels()
        {
            if (graphic == null)
            {
                HideLabelsFrom(0);
                return;
            }

            var labelIndex = 0;
            var shapes = ConstructionShapeTool.Shapes;
            for (var i = 0; i < shapes.Count; i++)
            {
                var shape = shapes[i];
                if (shape == null || !ConstructionShapeTool.IsVisible(shape) || !ConstructionShapeTool.IsDrawn(shape))
                {
                    continue;
                }

                if (!graphic.TryGetShapeLabelPosition(shape, out var localPoint))
                {
                    continue;
                }

                var label = GetOrCreateLabel(labelIndex++);
                label.gameObject.SetActive(true);
                label.text = ConstructionShapeTool.GetShapeName(shape);
                label.color = ConstructionShapeOverlayGraphic.GetMarkerColor(shape);
                label.rectTransform.anchoredPosition = localPoint + new Vector2(8f, 22f);
            }

            // Effect-position markers share this same below-editor-UI Canvas. This is deliberate:
            // drawing them from MonoBehaviour.OnGUI would place them above ADOFAI Canvas panels.
            if (graphic.TryGetEffectMarkerLabelPosition(out var effectPoint, out var effectText, out var effectColor))
            {
                var label = GetOrCreateLabel(labelIndex++);
                label.gameObject.SetActive(true);
                label.text = effectText;
                label.color = effectColor;
                label.rectTransform.anchoredPosition = effectPoint + new Vector2(8f, 22f);
            }

            HideLabelsFrom(labelIndex);
        }

        private static TextMeshProUGUI GetOrCreateLabel(int index)
        {
            while (labels.Count <= index)
            {
                var obj = new GameObject("Shape Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                obj.transform.SetParent(rootObject.transform, false);
                var rect = obj.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.sizeDelta = new Vector2(90f, 24f);

                var text = obj.GetComponent<TextMeshProUGUI>();
                text.raycastTarget = false;
                text.alignment = TextAlignmentOptions.MidlineLeft;
                text.fontSize = 16f;
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Overflow;
                if (labelFont != null)
                {
                    text.font = labelFont;
                }
                if (labelMaterial != null)
                {
                    text.fontMaterial = labelMaterial;
                }

                labels.Add(text);
            }

            var label = labels[index];
            if (labelFont != null)
            {
                label.font = labelFont;
            }
            if (labelMaterial != null)
            {
                label.fontMaterial = labelMaterial;
            }
            return label;
        }

        private static void HideLabelsFrom(int startIndex)
        {
            for (var i = startIndex; i < labels.Count; i++)
            {
                if (labels[i] != null)
                {
                    labels[i].gameObject.SetActive(false);
                }
            }
        }

        private static void Stretch(RectTransform rect)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            rect.localScale = Vector3.one;
        }
    }

    internal sealed class ConstructionShapeOverlayGraphic : Graphic
    {
        private const int CircleSegments = 72;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var camera = GetEditorCamera();
            if (camera == null || scnEditor.instance == null || GameCompat.IsEditorPlaying(scnEditor.instance))
            {
                return;
            }

            var shapes = ConstructionShapeTool.Shapes;
            for (var i = 0; i < shapes.Count; i++)
            {
                DrawShape(vh, camera, shapes[i]);
            }

            // Camera/effect geometry lives on the same below-editor-UI Canvas as constructions.
            // Do not move these back to OnGUI: IMGUI always risks appearing above editor panels.
            DrawCameraFrame(vh, camera);
            DrawEffectMarker(vh, camera);
        }

        internal bool TryGetEffectMarkerLabelPosition(out Vector2 localPoint, out string label, out Color labelColor)
        {
            localPoint = Vector2.zero;
            label = string.Empty;
            labelColor = Color.white;
            var camera = GetEditorCamera();
            if (camera == null || !TryGetCurrentEffectVisual(out var visual))
            {
                return false;
            }

            label = visual.Label;
            labelColor = EuclidMod.GetEffectOverlayColors(visual.Kind).Label;
            return TryWorldToLocal(camera, visual.TargetWorld, out localPoint);
        }

        internal bool TryGetShapeLabelPosition(ConstructionShape shape, out Vector2 localPoint)
        {
            localPoint = Vector2.zero;
            if (shape == null || !ConstructionShapeTool.IsDrawn(shape))
            {
                return false;
            }

            var camera = GetEditorCamera();
            if (camera == null)
            {
                return false;
            }

            var type = ConstructionShapeTool.GetDrawnType(shape);
            if (type == ConstructionShapeType.Point || type == ConstructionShapeType.Circle)
            {
                return TryWorldToLocal(camera, ConstructionShapeTool.GetDrawnPointWorld(shape, 0).ToVector2(), out localPoint);
            }

            if (ConstructionShapeTool.TryGetDrawnLineGeometry(shape, out var line))
            {
                return TryWorldToLocal(camera, line.Anchor.ToVector2(), out localPoint);
            }

            return false;
        }

        internal static Color GetMarkerColor(ConstructionShape shape)
        {
            var baseColor = ConstructionShapeTool.GetColor(shape);
            var selected = shape != null && ConstructionShapeTool.IsSelected(shape.Id);
            var color = selected ? Color.Lerp(baseColor, Color.white, 0.35f) : Color.Lerp(baseColor, Color.white, 0.12f);
            color.a = baseColor.a * (selected ? 1f : 0.94f);
            return color;
        }

        private void DrawCameraFrame(VertexHelper vh, Camera camera)
        {
            if (!EuclidMod.ShowCameraFrame)
            {
                return;
            }

            var snapshot = EuclidMod.Behaviour != null
                ? EuclidMod.Behaviour.CameraFrame
                : CameraFrameSnapshot.Unavailable(string.Empty);
            if (snapshot.State != CameraFrameState.Ready)
            {
                return;
            }

            var corners = snapshot.Corners;
            if (corners == null || corners.Length < 2)
            {
                return;
            }

            var points = new Vector2[corners.Length];
            for (var i = 0; i < corners.Length; i++)
            {
                if (!TryWorldToLocal(camera, corners[i], out points[i]))
                {
                    return;
                }
            }

            var color = EuclidMod.CameraFrameColor;
            for (var i = 0; i < points.Length; i++)
            {
                AddLine(vh, points[i], points[(i + 1) % points.Length], color, 2f);
            }
        }

        private static bool TryGetCurrentEffectVisual(out EffectOverlayVisual visual)
        {
            var cameraFrame = EuclidMod.Behaviour != null
                ? EuclidMod.Behaviour.CameraFrame
                : CameraFrameSnapshot.Unavailable(string.Empty);
            if (cameraFrame.State == CameraFrameState.Ready)
            {
                visual = new EffectOverlayVisual(
                    EffectOverlayKind.CameraMove,
                    cameraFrame.ReferencePoint,
                    cameraFrame.Center,
                    EuclidText.Get("effect.moveCamera"));
                return true;
            }

            return CoordinateSnapTool.TryGetFocusedEffectVisual(out visual);
        }

        private void DrawEffectMarker(VertexHelper vh, Camera camera)
        {
            if (!TryGetCurrentEffectVisual(out var visual) ||
                !TryWorldToLocal(camera, visual.ReferenceWorld, out var reference) ||
                !TryWorldToLocal(camera, visual.TargetWorld, out var target))
            {
                return;
            }

            var colors = EuclidMod.GetEffectOverlayColors(visual.Kind);

            // Every supported effect now uses the same visual grammar:
            //   tile/reference marker ---- offset segment ----> editable position marker
            // The four colors are independently configurable per effect in UMM Options.
            AddLine(vh, reference, target, colors.Segment, 2f);
            AddDiamond(vh, reference, colors.TileMarker, 7f, 1.8f);

            // The editable position marker is one composite marker. Keep its center diamond and
            // corner reticle on the exact same target coordinate so asynchronous PositionTrack
            // application can never make the two visual pieces appear detached.
            AddDiamond(vh, target, colors.PositionMarker, 5.5f, 1.6f);
            AddCornerReticle(vh, target, colors.PositionMarker, 10f, 4.5f, 2f);

            if (visual.Kind != EffectOverlayKind.CameraMove &&
                CoordinateSnapTool.TryGetPositionOffsetSnapPreview(out var from, out var to) &&
                TryWorldToLocal(camera, from, out var fromLocal) &&
                TryWorldToLocal(camera, to, out var toLocal))
            {
                var previewColor = colors.Segment;
                previewColor.a *= 0.72f;
                AddDashedLine(vh, fromLocal, toLocal, previewColor, 1.65f, 9f, 6f);

                // The dashed line itself communicates the snap destination. Do not draw another
                // position-colored diamond there; that used to look like the center of the main
                // position marker had separated from its corner reticle.
            }
        }

        private void DrawShape(VertexHelper vh, Camera camera, ConstructionShape shape)
        {
            if (shape == null || !ConstructionShapeTool.IsVisible(shape) || !ConstructionShapeTool.IsDrawn(shape))
            {
                return;
            }

            var selected = ConstructionShapeTool.IsSelected(shape.Id);
            var baseColor = ConstructionShapeTool.GetColor(shape);
            var lineColor = selected ? Color.Lerp(baseColor, Color.white, 0.22f) : baseColor;
            lineColor.a = baseColor.a * (selected ? 0.96f : 0.72f);
            var markerColor = GetMarkerColor(shape);
            var width = selected ? 2.5f : 1.65f;

            var type = ConstructionShapeTool.GetDrawnType(shape);
            if (type == ConstructionShapeType.Point)
            {
                if (TryWorldToLocal(camera, ConstructionShapeTool.GetDrawnPointWorld(shape, 0).ToVector2(), out var point))
                {
                    // A construction Point should read as an actual mathematical point, not as a
                    // snap/drag handle. Keep it as a compact filled disc; interaction hit-testing
                    // intentionally stays larger in CameraFrameOverlay so the tiny marker is still easy to click.
                    AddDisc(vh, point, markerColor, selected ? 5f : 3.8f, 18);
                }
                return;
            }

            if (type == ConstructionShapeType.Circle)
            {
                DrawCircle(vh, camera, shape, lineColor, width);
                return;
            }

            if (ConstructionShapeTool.TryGetDrawnLineGeometry(shape, out var line))
            {
                if (!TryWorldToLocal(camera, line.Anchor.ToVector2(), out var anchor) ||
                    !TryWorldToLocal(camera, (line.Anchor + line.Direction).ToVector2(), out var directionEnd))
                {
                    return;
                }

                var direction = directionEnd - anchor;
                if (direction.sqrMagnitude <= 0.0001f)
                {
                    return;
                }

                direction.Normalize();
                // Render far beyond the visible editor bounds so the mathematical line still
                // reads as infinite when zoomed or panned away from P1. The previous extent was
                // large enough for ordinary views but could visibly terminate in wide/zoomed scenes.
                var rect = rectTransform.rect;
                var diagonal = Mathf.Sqrt(rect.width * rect.width + rect.height * rect.height);
                var extent = diagonal * 4f + 512f;
                AddLine(vh, anchor - direction * extent, anchor + direction * extent, lineColor, width);
                // A mathematical line has no distinguished center/anchor point.
            }
        }

        private void DrawCircle(VertexHelper vh, Camera camera, ConstructionShape shape, Color lineColor, float width)
        {
            var centerWorld = ConstructionShapeTool.GetDrawnPointWorld(shape, 0);
            var secondWorld = ConstructionShapeTool.GetDrawnPointWorld(shape, 1);
            var radius = (secondWorld - centerWorld).Magnitude;
            if (radius <= 0.000001d)
            {
                return;
            }

            Vector2 previous = Vector2.zero;
            var hasPrevious = false;
            for (var i = 0; i <= CircleSegments; i++)
            {
                var angle = Mathf.PI * 2f * i / CircleSegments;
                var world = new Vector2(
                    (float)(centerWorld.X + radius * Mathf.Cos(angle)),
                    (float)(centerWorld.Y + radius * Mathf.Sin(angle)));
                if (!TryWorldToLocal(camera, world, out var point))
                {
                    hasPrevious = false;
                    continue;
                }

                if (hasPrevious)
                {
                    AddLine(vh, previous, point, lineColor, width);
                }

                previous = point;
                hasPrevious = true;
            }

            // A circle no longer gets a separate center cross marker. P1 can still be
            // inspected/selected from the shape editor when its exact center is needed.
        }

        private bool TryWorldToLocal(Camera camera, Vector2 world, out Vector2 localPoint)
        {
            localPoint = Vector2.zero;
            if (camera == null)
            {
                return false;
            }

            var screen = camera.WorldToScreenPoint(new Vector3(world.x, world.y, 0f));
            if (screen.z < 0f)
            {
                return false;
            }

            var rootCanvas = canvas != null ? canvas.rootCanvas : null;
            var uiCamera = rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? (canvas.worldCamera != null ? canvas.worldCamera : rootCanvas.worldCamera)
                : null;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rectTransform,
                new Vector2(screen.x, screen.y),
                uiCamera,
                out localPoint);
        }

        private static Camera GetEditorCamera()
        {
            var editorCamera = GameCompat.GetEditorCamera(scnEditor.instance);
            return editorCamera != null ? editorCamera : Camera.main;
        }

        private static void AddDashedLine(VertexHelper vh, Vector2 start, Vector2 end, Color color, float width, float dashLength, float gapLength)
        {
            var delta = end - start;
            var length = delta.magnitude;
            if (length <= 0.001f)
            {
                return;
            }

            var direction = delta / length;
            var distance = 0f;
            while (distance < length)
            {
                var dashEnd = Mathf.Min(distance + dashLength, length);
                AddLine(vh, start + direction * distance, start + direction * dashEnd, color, width);
                distance = dashEnd + gapLength;
            }
        }

        private static void AddCornerReticle(VertexHelper vh, Vector2 center, Color color, float radius, float armLength, float width)
        {
            var left = center.x - radius;
            var right = center.x + radius;
            var top = center.y + radius;
            var bottom = center.y - radius;

            AddLine(vh, new Vector2(left, top), new Vector2(left + armLength, top), color, width);
            AddLine(vh, new Vector2(left, top), new Vector2(left, top - armLength), color, width);
            AddLine(vh, new Vector2(right, top), new Vector2(right - armLength, top), color, width);
            AddLine(vh, new Vector2(right, top), new Vector2(right, top - armLength), color, width);
            AddLine(vh, new Vector2(left, bottom), new Vector2(left + armLength, bottom), color, width);
            AddLine(vh, new Vector2(left, bottom), new Vector2(left, bottom + armLength), color, width);
            AddLine(vh, new Vector2(right, bottom), new Vector2(right - armLength, bottom), color, width);
            AddLine(vh, new Vector2(right, bottom), new Vector2(right, bottom + armLength), color, width);
        }

        private static void AddLine(VertexHelper vh, Vector2 start, Vector2 end, Color color, float width)
        {
            var delta = end - start;
            if (delta.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            var normal = new Vector2(-delta.y, delta.x).normalized * (width * 0.5f);
            AddQuad(vh, start + normal, start - normal, end - normal, end + normal, color);
        }

        private static void AddCross(VertexHelper vh, Vector2 center, Color color, float radius, float width)
        {
            AddLine(vh, center + new Vector2(-radius, 0f), center + new Vector2(radius, 0f), color, width);
            AddLine(vh, center + new Vector2(0f, -radius), center + new Vector2(0f, radius), color, width);
        }

        private static void AddDisc(VertexHelper vh, Vector2 center, Color color, float radius, int segments)
        {
            if (radius <= 0f || segments < 3)
            {
                return;
            }

            var centerIndex = vh.currentVertCount;
            vh.AddVert(center, color, Vector2.zero);
            for (var i = 0; i <= segments; i++)
            {
                var angle = Mathf.PI * 2f * i / segments;
                vh.AddVert(
                    center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius,
                    color,
                    Vector2.zero);
            }

            for (var i = 0; i < segments; i++)
            {
                vh.AddTriangle(centerIndex, centerIndex + i + 1, centerIndex + i + 2);
            }
        }

        private static void AddDiamond(VertexHelper vh, Vector2 center, Color color, float radius, float width)
        {
            var top = center + new Vector2(0f, radius);
            var right = center + new Vector2(radius, 0f);
            var bottom = center + new Vector2(0f, -radius);
            var left = center + new Vector2(-radius, 0f);
            AddLine(vh, top, right, color, width);
            AddLine(vh, right, bottom, color, width);
            AddLine(vh, bottom, left, color, width);
            AddLine(vh, left, top, color, width);
        }

        private static void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
        {
            var start = vh.currentVertCount;
            vh.AddVert(a, color, Vector2.zero);
            vh.AddVert(b, color, Vector2.zero);
            vh.AddVert(c, color, Vector2.zero);
            vh.AddVert(d, color, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
