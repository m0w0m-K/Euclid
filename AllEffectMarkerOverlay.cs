using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Euclid
{
    // Optional read-only layer for every unselected effect marker. The normal construction overlay
    // keeps ownership of the selected marker so drag hit-testing and pending PositionTrack edits are
    // unchanged. This layer sits one sorting order below that overlay and never receives raycasts.
    internal sealed class AllEffectMarkerOverlay : MonoBehaviour
    {
        private const string RootName = "Euclid_AllEffectMarkers";
        private const float RefreshInterval = 0.1f;

        private readonly List<EffectOverlayVisual> visuals = new List<EffectOverlayVisual>();
        private readonly List<TextMeshProUGUI> labels = new List<TextMeshProUGUI>();

        private GameObject rootObject;
        private Canvas hostCanvas;
        private Canvas layerCanvas;
        private RectTransform layerRect;
        private AllEffectMarkerGraphic graphic;
        private TMP_FontAsset labelFont;
        private Material labelMaterial;
        private float nextRefreshTime;

        internal static void Install()
        {
            var behaviour = EuclidMod.Behaviour;
            if (behaviour == null || behaviour.GetComponent<AllEffectMarkerOverlay>() != null)
            {
                return;
            }

            behaviour.gameObject.AddComponent<AllEffectMarkerOverlay>();
        }

        private void Update()
        {
            if (!EuclidMod.Enabled || !AllEffectMarkerSettings.Enabled)
            {
                SetVisible(false);
                return;
            }

            var editor = scnEditor.instance;
            if (editor == null || GameCompat.IsEditorPlaying(editor))
            {
                SetVisible(false);
                return;
            }

            var panel = ResolveInspectorPanel(editor);
            var canvas = panel != null ? panel.GetComponentInParent<Canvas>() : null;
            if (canvas == null)
            {
                SetVisible(false);
                return;
            }

            Ensure(canvas, panel);
            SetVisible(true);

            if (Time.unscaledTime < nextRefreshTime)
            {
                return;
            }

            nextRefreshTime = Time.unscaledTime + RefreshInterval;
            RefreshNow();
        }

        private void OnDisable()
        {
            SetVisible(false);
        }

        private void OnDestroy()
        {
            DestroyLayer();
        }

        private void Ensure(Canvas canvas, InspectorPanel panel)
        {
            if (rootObject != null && hostCanvas == canvas && layerCanvas != null && graphic != null)
            {
                UpdateSorting();
                CaptureLabelStyle(panel);
                return;
            }

            DestroyLayer();
            hostCanvas = canvas;
            CaptureLabelStyle(panel);

            rootObject = new GameObject(RootName, typeof(RectTransform), typeof(Canvas));
            rootObject.transform.SetParent(hostCanvas.transform, false);
            layerRect = rootObject.GetComponent<RectTransform>();
            Stretch(layerRect);

            layerCanvas = rootObject.GetComponent<Canvas>();
            layerCanvas.overrideSorting = true;
            layerCanvas.additionalShaderChannels = hostCanvas.additionalShaderChannels;
            UpdateSorting();

            var geometryObject = new GameObject(
                "Geometry",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(AllEffectMarkerGraphic));
            geometryObject.transform.SetParent(rootObject.transform, false);
            var geometryRect = geometryObject.GetComponent<RectTransform>();
            Stretch(geometryRect);

            graphic = geometryObject.GetComponent<AllEffectMarkerGraphic>();
            graphic.raycastTarget = false;
            graphic.color = Color.white;
            graphic.SetSource(visuals);

            rootObject.SetActive(true);
        }

        private void RefreshNow()
        {
            if (graphic == null || rootObject == null)
            {
                return;
            }

            EffectOverlayCollection.CollectBackground(visuals);
            graphic.SetSource(visuals);
            graphic.SetVerticesDirty();
            SyncLabels();
        }

        private void SyncLabels()
        {
            var camera = GetEditorCamera();
            if (camera == null || layerRect == null)
            {
                HideLabelsFrom(0);
                return;
            }

            var labelIndex = 0;
            for (var i = 0; i < visuals.Count; i++)
            {
                var visual = visuals[i];
                if (!TryWorldToLocal(camera, visual.TargetWorld, out var point))
                {
                    continue;
                }

                var text = GetOrCreateLabel(labelIndex++);
                text.gameObject.SetActive(true);
                text.text = visual.Label;
                var color = EuclidMod.GetEffectOverlayColors(visual.Kind).Label;
                color.a *= 0.62f;
                text.color = color;
                text.rectTransform.anchoredPosition = point + new Vector2(8f, 20f);
            }

            HideLabelsFrom(labelIndex);
        }

        private TextMeshProUGUI GetOrCreateLabel(int index)
        {
            while (labels.Count <= index)
            {
                var obj = new GameObject(
                    "Effect Label",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(TextMeshProUGUI));
                obj.transform.SetParent(rootObject.transform, false);

                var rect = obj.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.sizeDelta = new Vector2(120f, 24f);

                var text = obj.GetComponent<TextMeshProUGUI>();
                text.raycastTarget = false;
                text.alignment = TextAlignmentOptions.MidlineLeft;
                text.fontSize = 14f;
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Overflow;
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

        private void HideLabelsFrom(int startIndex)
        {
            for (var i = startIndex; i < labels.Count; i++)
            {
                if (labels[i] != null)
                {
                    labels[i].gameObject.SetActive(false);
                }
            }
        }

        private void CaptureLabelStyle(InspectorPanel panel)
        {
            if (panel != null && panel.title != null)
            {
                labelFont = panel.title.font;
                labelMaterial = panel.title.fontMaterial;
            }
        }

        private void UpdateSorting()
        {
            if (layerCanvas == null || hostCanvas == null)
            {
                return;
            }

            var sortingReference = hostCanvas.overrideSorting ? hostCanvas : hostCanvas.rootCanvas;
            if (sortingReference == null)
            {
                sortingReference = hostCanvas;
            }

            layerCanvas.overrideSorting = true;
            layerCanvas.sortingLayerID = sortingReference.sortingLayerID;
            // ConstructionShapeCanvasOverlay uses host - 1. Keep unselected markers underneath it.
            layerCanvas.sortingOrder = sortingReference.sortingOrder - 2;
        }

        private void SetVisible(bool visible)
        {
            if (rootObject != null && rootObject.activeSelf != visible)
            {
                rootObject.SetActive(visible);
            }
        }

        private void DestroyLayer()
        {
            if (rootObject != null)
            {
                Destroy(rootObject);
            }

            rootObject = null;
            hostCanvas = null;
            layerCanvas = null;
            layerRect = null;
            graphic = null;
            labels.Clear();
            visuals.Clear();
            labelFont = null;
            labelMaterial = null;
        }

        private static InspectorPanel ResolveInspectorPanel(scnEditor editor)
        {
            var panel = GameCompat.GetSettingsPanel(editor);
            if (panel != null)
            {
                return panel;
            }

            try
            {
                var candidates = Resources.FindObjectsOfTypeAll<InspectorPanel>();
                for (var i = 0; i < candidates.Length; i++)
                {
                    var candidate = candidates[i];
                    if (candidate != null && candidate.gameObject.scene.IsValid())
                    {
                        return candidate;
                    }
                }
            }
            catch (Exception)
            {
                // Retry on the next update while the editor UI is rebuilding.
            }

            return null;
        }

        private bool TryWorldToLocal(Camera camera, Vector2 world, out Vector2 localPoint)
        {
            localPoint = Vector2.zero;
            if (camera == null || layerRect == null)
            {
                return false;
            }

            var screen = camera.WorldToScreenPoint(new Vector3(world.x, world.y, 0f));
            if (screen.z < 0f)
            {
                return false;
            }

            var rootCanvas = layerCanvas != null ? layerCanvas.rootCanvas : null;
            var uiCamera = rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? (layerCanvas.worldCamera != null ? layerCanvas.worldCamera : rootCanvas.worldCamera)
                : null;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                layerRect,
                new Vector2(screen.x, screen.y),
                uiCamera,
                out localPoint);
        }

        private static Camera GetEditorCamera()
        {
            var editorCamera = GameCompat.GetEditorCamera(scnEditor.instance);
            return editorCamera != null ? editorCamera : Camera.main;
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

    internal sealed class AllEffectMarkerGraphic : Graphic
    {
        private IReadOnlyList<EffectOverlayVisual> visuals;

        internal void SetSource(IReadOnlyList<EffectOverlayVisual> source)
        {
            visuals = source;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (visuals == null || visuals.Count == 0 || !AllEffectMarkerSettings.Enabled)
            {
                return;
            }

            var camera = GetEditorCamera();
            if (camera == null)
            {
                return;
            }

            for (var i = 0; i < visuals.Count; i++)
            {
                var visual = visuals[i];
                if (!TryWorldToLocal(camera, visual.ReferenceWorld, out var reference) ||
                    !TryWorldToLocal(camera, visual.TargetWorld, out var target))
                {
                    continue;
                }

                var colors = EuclidMod.GetEffectOverlayColors(visual.Kind);
                var segment = Fade(colors.Segment, 0.58f);
                var tile = Fade(colors.TileMarker, 0.66f);
                var position = Fade(colors.PositionMarker, 0.66f);

                AddLine(vh, reference, target, segment, 1.5f);
                AddDiamond(vh, reference, tile, 5.8f, 1.45f);
                AddDiamond(vh, target, position, 4.8f, 1.35f);
                AddCornerReticle(vh, target, position, 8.5f, 3.8f, 1.45f);
            }
        }

        private static Color Fade(Color color, float multiplier)
        {
            color.a *= multiplier;
            return color;
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

        private static void AddCornerReticle(
            VertexHelper vh,
            Vector2 center,
            Color color,
            float radius,
            float armLength,
            float width)
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
