using System;
using System.Collections.Generic;
using System.Globalization;
using ADOFAI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Euclid
{
    internal sealed partial class EuclidPanel
    {
        // Captured ADOFAI visual style plus formatting-only helpers.
        // No editor state mutation should be added to this file.

        private readonly struct ImageStyle
        {
            internal ImageStyle(Sprite sprite, Image.Type type, Color color, Color outlineColor, Vector2 outlineDistance)
            {
                Sprite = sprite;
                Type = type;
                Color = color;
                OutlineColor = outlineColor;
                OutlineDistance = outlineDistance;
            }

            internal Sprite Sprite { get; }

            internal Image.Type Type { get; }

            internal Color Color { get; }

            internal Color OutlineColor { get; }

            internal Vector2 OutlineDistance { get; }
        }

        private readonly struct ScrollbarState
        {
            internal ScrollbarState(Scrollbar scrollbar, bool wasEnabled, bool wasActive)
            {
                Scrollbar = scrollbar;
                WasEnabled = wasEnabled;
                WasActive = wasActive;
            }

            internal Scrollbar Scrollbar { get; }

            internal bool WasEnabled { get; }

            internal bool WasActive { get; }
        }

        private readonly struct UiStyle
        {
            private UiStyle(
                TMP_FontAsset font,
                Material fontMaterial,
                GameObject buttonTemplate,
                GameObject inputTemplate,
                ImageStyle buttonImage,
                ImageStyle inputImage,
                ImageStyle toggleOnImage,
                ImageStyle toggleOffImage,
                ColorBlock buttonColors)
            {
                Font = font;
                FontMaterial = fontMaterial;
                ButtonTemplate = buttonTemplate;
                InputTemplate = inputTemplate;
                ButtonImage = buttonImage;
                InputImage = inputImage;
                ToggleOnImage = toggleOnImage;
                ToggleOffImage = toggleOffImage;
                ButtonColors = buttonColors;
            }

            internal TMP_FontAsset Font { get; }

            internal Material FontMaterial { get; }

            internal GameObject ButtonTemplate { get; }

            internal GameObject InputTemplate { get; }

            internal ImageStyle ButtonImage { get; }

            internal ImageStyle InputImage { get; }

            internal ImageStyle ToggleOnImage { get; }

            internal ImageStyle ToggleOffImage { get; }

            internal ColorBlock ButtonColors { get; }

            // Snapshot reusable visual assets from the current inspector. The generated rounded
            // sprites are fallbacks for Unity/ADOFAI versions where suitable templates cannot be found.
            internal static UiStyle Capture(InspectorPanel panel)
            {
                var font = panel != null && panel.title != null ? panel.title.font : null;
                var fontMaterial = panel != null && panel.title != null ? panel.title.fontMaterial : null;
                var buttonTemplate = FindButtonTemplate(panel);
                var inputTemplate = FindInputTemplate(panel);

                var buttonImage = new ImageStyle(
                    CreateRoundedRectSprite("Euclid_Button_Fill", filled: true),
                    Image.Type.Sliced,
                    Color.white,
                    new Color(1f, 1f, 1f, 0f),
                    Vector2.zero);
                var inputStyle = new ImageStyle(
                    CreateRoundedRectSprite("Euclid_Button_Outline", filled: false),
                    Image.Type.Sliced,
                    Color.white,
                    new Color(1f, 1f, 1f, 0f),
                    Vector2.zero);
                var toggleOnStyle = buttonImage;
                var toggleOffStyle = inputStyle;

                var buttonColors = ColorBlock.defaultColorBlock;
                buttonColors.normalColor = Color.white;
                buttonColors.highlightedColor = Color.white;
                buttonColors.pressedColor = Color.white;
                buttonColors.selectedColor = Color.white;
                buttonColors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 1f);
                buttonColors.fadeDuration = 0.08f;

                return new UiStyle(font, fontMaterial, buttonTemplate, inputTemplate, buttonImage, inputStyle, toggleOnStyle, toggleOffStyle, buttonColors);
            }

            private static Sprite CreateRoundedRectSprite(string name, bool filled)
            {
                const int size = 64;
                const int radius = 9;
                const int border = 4;

                var texture = new Texture2D(size, size, TextureFormat.ARGB32, false);
                texture.name = name + "_Texture";
                texture.filterMode = FilterMode.Bilinear;
                texture.wrapMode = TextureWrapMode.Clamp;

                var clear = new Color(1f, 1f, 1f, 0f);
                var white = Color.white;
                for (var y = 0; y < size; y++)
                {
                    for (var x = 0; x < size; x++)
                    {
                        var fx = x + 0.5f;
                        var fy = y + 0.5f;
                        var inside = IsInsideRoundedRect(fx, fy, 0f, 0f, size, size, radius);
                        var inner = IsInsideRoundedRect(fx, fy, border, border, size - border, size - border, radius - border);
                        texture.SetPixel(x, y, inside && (filled || !inner) ? white : clear);
                    }
                }

                texture.Apply(false, true);
                var sprite = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, size, size),
                    new Vector2(0.5f, 0.5f),
                    100f,
                    0,
                    SpriteMeshType.FullRect,
                    new Vector4(12f, 12f, 12f, 12f));
                sprite.name = name;
                return sprite;
            }

            private static bool IsInsideRoundedRect(float x, float y, float left, float bottom, float right, float top, float radius)
            {
                if (x < left || x >= right || y < bottom || y >= top)
                {
                    return false;
                }

                var clampedX = Mathf.Clamp(x, left + radius, right - radius);
                var clampedY = Mathf.Clamp(y, bottom + radius, top - radius);
                var dx = x - clampedX;
                var dy = y - clampedY;
                return dx * dx + dy * dy <= radius * radius;
            }

            private static ImageStyle SpriteOrFallback(Sprite sprite, Color spriteColor, Color fallbackColor, Color outlineColor)
            {
                if (sprite != null && HasUsableBorder(sprite))
                {
                    return new ImageStyle(sprite, SpriteType(sprite), spriteColor, new Color(1f, 1f, 1f, 0f), Vector2.zero);
                }

                return new ImageStyle(null, Image.Type.Simple, fallbackColor, outlineColor, new Vector2(1.35f, -1.35f));
            }

            private static bool HasUsableBorder(Sprite sprite)
            {
                if (sprite == null)
                {
                    return false;
                }

                var border = sprite.border;
                return Mathf.Abs(border.x) > 0.01f
                    || Mathf.Abs(border.y) > 0.01f
                    || Mathf.Abs(border.z) > 0.01f
                    || Mathf.Abs(border.w) > 0.01f;
            }

            private static Image.Type SpriteType(Sprite sprite)
            {
                if (sprite == null)
                {
                    return Image.Type.Simple;
                }

                var border = sprite.border;
                return Mathf.Abs(border.x) > 0.01f
                    || Mathf.Abs(border.y) > 0.01f
                    || Mathf.Abs(border.z) > 0.01f
                    || Mathf.Abs(border.w) > 0.01f
                    ? Image.Type.Sliced
                    : Image.Type.Simple;
            }

            private static Sprite FindSprite(string name)
            {
                var loaded = Resources.Load<Sprite>(name);
                if (loaded != null)
                {
                    return loaded;
                }

                var sprites = Resources.FindObjectsOfTypeAll<Sprite>();
                for (var i = 0; i < sprites.Length; i++)
                {
                    var sprite = sprites[i];
                    if (sprite != null && sprite.name == name)
                    {
                        return sprite;
                    }
                }

                return null;
            }

            private static GameObject FindInputTemplate(InspectorPanel panel)
            {
                if (panel == null)
                {
                    return null;
                }

                var panelsRoot = GameCompat.GetInspectorPanels(panel);
                var tabsRoot = GameCompat.GetInspectorTabs(panel);
                var inputs = panelsRoot != null
                    ? panelsRoot.GetComponentsInChildren<TMP_InputField>(true)
                    : panel.GetComponentsInChildren<TMP_InputField>(true);

                TMP_InputField best = null;
                var bestScore = float.MinValue;
                foreach (var input in inputs)
                {
                    if (input == null || input.name == PanelObjectName)
                    {
                        continue;
                    }

                    if (tabsRoot != null && input.transform.IsChildOf(tabsRoot))
                    {
                        continue;
                    }

                    var score = ScoreRect(input.GetComponent<RectTransform>());
                    if (score > bestScore)
                    {
                        best = input;
                        bestScore = score;
                    }
                }

                return best != null ? best.gameObject : null;
            }

            private static GameObject FindButtonTemplate(InspectorPanel panel)
            {
                if (panel == null)
                {
                    return null;
                }

                var panelsRoot = GameCompat.GetInspectorPanels(panel);
                var tabsRoot = GameCompat.GetInspectorTabs(panel);
                var buttons = panelsRoot != null
                    ? panelsRoot.GetComponentsInChildren<Button>(true)
                    : panel.GetComponentsInChildren<Button>(true);

                Button best = null;
                var bestScore = float.MinValue;
                foreach (var button in buttons)
                {
                    if (button == null || button.GetComponentInParent<TMP_InputField>() != null)
                    {
                        continue;
                    }

                    if (tabsRoot != null && button.transform.IsChildOf(tabsRoot))
                    {
                        continue;
                    }

                    if (button.GetComponentInChildren<TMP_Text>(true) == null)
                    {
                        continue;
                    }

                    var score = ScoreRect(button.GetComponent<RectTransform>());
                    if (button.gameObject.activeInHierarchy)
                    {
                        score += 1000f;
                    }

                    if (score > bestScore)
                    {
                        best = button;
                        bestScore = score;
                    }
                }

                return best != null ? best.gameObject : null;
            }

            private static float ScoreRect(RectTransform rect)
            {
                if (rect == null)
                {
                    return 0f;
                }

                var width = Mathf.Abs(rect.rect.width);
                var height = Mathf.Abs(rect.rect.height);
                if (width <= 1f)
                {
                    width = Mathf.Abs(rect.sizeDelta.x);
                }

                if (height <= 1f)
                {
                    height = Mathf.Abs(rect.sizeDelta.y);
                }

                return width * height + width + height;
            }
        }

        private static string FormatMeasure(MeasureSnapshot snapshot)
        {
            if (snapshot.State != MeasureState.Ready)
            {
                return snapshot.Message;
            }

            var tileSize = GetTileSize();
            var distance = snapshot.Distance / tileSize;
            var midpoint = snapshot.Midpoint / tileSize;
            var midpointFromStart = snapshot.MidpointOffsetFromStart / tileSize;
            var midpointFromEnd = snapshot.MidpointOffsetFromEnd / tileSize;
            var delta = snapshot.Delta / tileSize;

            return string.Format(
                "{0}\n{1} -> {2} {3}\n{4}\n{5:0.#####}\n{6}\n{7:0.#####}, {8:0.#####}\n{9}\n{10:0.#####}, {11:0.#####}\n{12}\n{13:0.#####}, {14:0.#####}\n{15}\n{16:0.#####} deg\n{17} {18}\n{19:0.#####}, {20:0.#####}\n{17} {21}\n{22:0.#####}, {23:0.#####}",
                EuclidText.Get("measure.tiles"),
                snapshot.StartSeqId,
                snapshot.EndSeqId,
                snapshot.Count,
                EuclidText.Get("measure.distance"),
                distance,
                EuclidText.Get("measure.midpoint"),
                midpoint.x,
                midpoint.y,
                EuclidText.Format("measure.midpointFromTile", snapshot.StartSeqId),
                midpointFromStart.x,
                midpointFromStart.y,
                EuclidText.Format("measure.midpointFromTile", snapshot.EndSeqId),
                midpointFromEnd.x,
                midpointFromEnd.y,
                EuclidText.Get("measure.angle"),
                snapshot.AngleDegrees,
                EuclidText.Get("measure.relative"),
                EuclidText.Format("measure.tileToTile", snapshot.StartSeqId, snapshot.EndSeqId),
                delta.x,
                delta.y,
                EuclidText.Format("measure.tileToTile", snapshot.EndSeqId, snapshot.StartSeqId),
                -delta.x,
                -delta.y);
        }

        private static float GetTileSize()
        {
            return GameCompat.GetTileSize(1.5f);
        }
    }
}
