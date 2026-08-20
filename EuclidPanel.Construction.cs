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
        // Shape colors use the same compact representation as ADOFAI's color fields:
        // an editable RGBA hex value plus a live swatch on the right. Keep parsing/formatting
        // here so future UI changes do not leak color-string rules into ConstructionShapeTool.
        // Shape list/detail editor and position-picking workflow.
        //
        // There are two ways a pending point pick can complete:
        //   1) clicking an ADOFAI tile -> SyncTileSelectionToCurrentShape()
        //   2) clicking a visible drawn point marker -> TryApplyPendingPointPickFromScene()
        // Shape-list clicks are intentionally NOT a position-pick source. Keep both real scene
        // input paths when changing shape-selection behavior.

        private void ClearInitialDefaultShapeOnce()
        {
            if (initialConstructionShapesCleaned)
            {
                return;
            }

            initialConstructionShapesCleaned = true;
            var shapes = ConstructionShapeTool.Shapes;
            if (shapes.Count == 1 && shapes[0].Id == 1 && shapes[0].Type == ConstructionShapeType.Line)
            {
                ConstructionShapeTool.ClearAll();
            }
        }

        private static void NormalizeConstructionShapes()
        {
            var shapes = ConstructionShapeTool.Shapes;
            for (var i = 0; i < shapes.Count; i++)
            {
                if (shapes[i].Type == ConstructionShapeType.PerpendicularBisector)
                {
                    ConstructionShapeTool.SetType(shapes[i], ConstructionShapeType.Line);
                }
            }
        }

        private void RebuildConstructionUi()
        {
            if (rebuildingConstructionUi)
            {
                return;
            }

            rebuildingConstructionUi = true;
            try
            {
                ClearContentChildren();
                ClearChildren(detailContent);
                BuildPanelContent();
                RefreshTexts();
            }
            finally
            {
                rebuildingConstructionUi = false;
            }
        }

        private void BuildShapeListContent()
        {
            AddLabel(EuclidText.Get("panel.constructionList"), 20, FontStyle.Bold);

            var shapes = ConstructionShapeTool.Shapes;
            if (shapes.Count == 0)
            {
                AddFlexibleSpacer(1f);
            }
            else
            {
                var listContent = AddShapeListScrollArea();
                WithContent(listContent, () => AddShapeListItems(shapes));
            }

            AddSpacer(8f);
            AddButtonRow(
                (EuclidText.Get("button.addShape"), () =>
                {
                    var shape = ConstructionShapeTool.AddShape(latestMeasure);
                    // There is no staged "Draw" step anymore. A newly created shape is live
                    // immediately, and every valid editor change updates its drawn snapshot.
                    ConstructionShapeTool.DrawShape(shape);
                    ConstructionShapeCanvasOverlay.Refresh();
                    RebuildConstructionUi();
                }),
                (EuclidText.Get("button.deleteShape"), () =>
                {
                    ConstructionShapeTool.DeleteSelected();
                    RebuildConstructionUi();
                }));

            AddShapeActionButtons();
        }

        private void AddShapeActionButtons()
        {
            var canCreateIntersections = ConstructionShapeTool.CanCreateIntersectionsFromSelection();
            var canSnap = CoordinateSnapTool.CanSnapSelectedTargetToSelectedShape(latestCamera, GuideLineTool.CoordinateKeyText);
            if (!canSnap)
            {
                GuideLineTool.SnapSelectedShapeDrag = false;
            }

            var row = AddRow();
            var intersectionsText = AddButton(row, EuclidText.Get("button.createIntersections"), () =>
            {
                if (ConstructionShapeTool.CanCreateIntersectionsFromSelection())
                {
                    ConstructionShapeTool.CreateIntersectionsFromSelection();
                }

                RebuildConstructionUi();
            }, 0f);
            shapeIntersectionsText = intersectionsText;
            SetActionButtonState(intersectionsText, canCreateIntersections);

            var snapLabel = EuclidText.Get("button.snap");
            var snapText = AddButton(row, snapLabel, () =>
            {
                if (CoordinateSnapTool.CanSnapSelectedTargetToSelectedShape(latestCamera, GuideLineTool.CoordinateKeyText))
                {
                    GuideLineTool.ToggleSelectedShapeSnap(latestCamera);
                }

                RefreshTexts();
            }, 0f, ButtonSurface.Outline);
            shapeSnapText = snapText;
            SetToggleButtonState(snapText, snapLabel, GuideLineTool.SnapSelectedShapeDrag && canSnap, canSnap);
        }

        private void AddShapeListItems(IReadOnlyList<ConstructionShape> shapes)
        {
            for (var i = 0; i < shapes.Count; i++)
            {
                var shape = shapes[i];
                var row = AddCompactRow(ButtonHeight, 8f);
                var label = ShapeListLabel(shape);
                var text = AddButton(row, label, () =>
                {
                    // Shape-list clicks are only list selection. Position picking from another
                    // shape must be done by clicking the drawn point in the editor viewport.
                    ClearPointPick();
                    ConstructionShapeTool.Select(
                        shape.Id,
                        IsAdditiveSelectionPressed(),
                        IsRangeSelectionPressed());
                    RefreshConstructionSelectionUi();
                }, 0f, ButtonSurface.Outline);
                shapeListTexts[shape.Id] = text;
                SetToggleButtonState(text, label, ConstructionShapeTool.IsSelected(shape.Id));

                var visibleText = AddButton(row, ShapeVisibilityLabel(shape), () =>
                {
                    ConstructionShapeTool.ToggleVisible(shape);
                    RefreshShapeVisibilityButton(shape);
                }, 62f, ConstructionShapeTool.IsVisible(shape) ? ButtonSurface.Filled : ButtonSurface.Outline);
                shapeVisibilityTexts[shape.Id] = visibleText;
                RefreshShapeVisibilityButton(shape);
            }
        }

        private void RefreshConstructionSelectionUi()
        {
            RefreshShapeListSelectionButtons();
            RefreshShapeActionButtons();
            BuildDetailPanelContent();
            SetDetailPanelActive(true);
            RefreshTexts();
        }

        private void RefreshShapeListSelectionButtons()
        {
            var shapes = ConstructionShapeTool.Shapes;
            for (var i = 0; i < shapes.Count; i++)
            {
                var shape = shapes[i];
                if (!shapeListTexts.TryGetValue(shape.Id, out var text))
                {
                    continue;
                }

                var label = ShapeListLabel(shape);
                SetToggleButtonState(text, label, ConstructionShapeTool.IsSelected(shape.Id));
            }
        }

        private void RefreshShapeVisibilityButton(ConstructionShape shape)
        {
            if (shape == null || !shapeVisibilityTexts.TryGetValue(shape.Id, out var text))
            {
                return;
            }

            SetToggleButtonState(
                text,
                ShapeVisibilityLabel(shape),
                ConstructionShapeTool.IsVisible(shape));
        }

        private static string ShapeVisibilityLabel(ConstructionShape shape)
        {
            return ConstructionShapeTool.IsVisible(shape)
                ? EuclidText.Get("button.visibleOn")
                : EuclidText.Get("button.visibleOff");
        }

        private void RefreshShapeActionButtons()
        {
            var canCreateIntersections = ConstructionShapeTool.CanCreateIntersectionsFromSelection();
            var canSnap = CoordinateSnapTool.CanSnapSelectedTargetToSelectedShape(latestCamera, GuideLineTool.CoordinateKeyText);
            if (!canSnap)
            {
                GuideLineTool.SnapSelectedShapeDrag = false;
            }

            if (shapeIntersectionsText != null)
            {
                SetActionButtonState(shapeIntersectionsText, canCreateIntersections);
            }

            if (shapeSnapText != null)
            {
                var snapLabel = EuclidText.Get("button.snap");
                SetToggleButtonState(shapeSnapText, snapLabel, GuideLineTool.SnapSelectedShapeDrag && canSnap, canSnap);
            }
        }

        private RectTransform AddShapeListScrollArea()
        {
            var scrollObject = new GameObject("Shape List Scroll", typeof(RectTransform), typeof(ScrollRect), typeof(LayoutElement));
            scrollObject.transform.SetParent(content, false);

            var layout = scrollObject.GetComponent<LayoutElement>();
            layout.minHeight = 120f;
            layout.preferredHeight = 0f;
            layout.flexibleHeight = 1f;

            var viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportObject.transform.SetParent(scrollObject.transform, false);
            var viewportRect = viewportObject.GetComponent<RectTransform>();
            Stretch(viewportRect, 0f, 0f, 0f, 0f);

            var listObject = new GameObject("List Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            listObject.transform.SetParent(viewportObject.transform, false);
            var listContent = listObject.GetComponent<RectTransform>();
            listContent.anchorMin = new Vector2(0f, 1f);
            listContent.anchorMax = new Vector2(1f, 1f);
            listContent.pivot = new Vector2(0.5f, 1f);
            listContent.anchoredPosition = Vector2.zero;
            listContent.sizeDelta = Vector2.zero;
            ConfigureContentLayout(listObject, new RectOffset(0, 0, 0, 0));

            var scroll = scrollObject.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.scrollSensitivity = 26f;
            scroll.viewport = viewportRect;
            scroll.content = listContent;

            return listContent;
        }

        private void BuildDetailPanelContent()
        {
            if (detailContent == null)
            {
                return;
            }

            ClearChildren(detailContent);
            var shape = ConstructionShapeTool.PrimarySelectedShape;
            if (shape == null)
            {
                SetDetailPanelActive(false);
                return;
            }

            WithContent(detailContent, () =>
            {
                if (shape.Type == ConstructionShapeType.PerpendicularBisector)
                {
                    ConstructionShapeTool.SetType(shape, ConstructionShapeType.Line);
                }

                AddShapeNameEditor(shape);
                AddShapeTypeRow(shape);
                AddShapePointEditor(shape, 0);
                // Keep the P2 section in the layout even for a point shape. Point uses only P1,
                // so P2 stays visible but disabled; this keeps the floating detail panel at a
                // stable height when switching between point, line, and circle.
                AddShapePointEditor(shape, 1, shape.Type != ConstructionShapeType.Point);

                // Keep color editing together, then show derived geometry immediately below it.
                // This makes a/b/theta or r read as information about the final styled shape.
                AddShapeColorRow(shape);
                AddShapeGeometryInfo(shape);
                AddSpacer(4f);
            });

            AlignVisibleDetailPanel();
        }

        private void AddShapeNameEditor(ConstructionShape shape)
        {
            AddLabel(EuclidText.Get("label.shapeName"), LabelTextSize, FontStyle.Bold);
            var value = string.IsNullOrWhiteSpace(shape.Name)
                ? ConstructionShapeTool.GetDefaultShapeName(shape)
                : shape.Name;
            var input = AddFullWidthInputRow(value);
            input.onEndEdit.AddListener(text =>
            {
                ConstructionShapeTool.SetName(shape, text);
                RefreshShapeListSelectionButtons();
            });
        }

        private string ShapeListLabel(ConstructionShape shape)
        {
            if (shape == null)
            {
                return string.Empty;
            }

            // Text glyphs are used instead of external icon assets so the standalone mod stays
            // dependency-free. Keep these simple enough to exist in ADOFAI's TMP font fallback.
            return ShapeTypeIcon(shape.Type) + "  " + ConstructionShapeTool.GetShapeName(shape);
        }

        private static string ShapeTypeIcon(ConstructionShapeType type)
        {
            switch (type)
            {
                case ConstructionShapeType.Point:
                    // Keep the point glyph optically smaller than the line/circle icons. TMP rich text
                    // lets us resize only the glyph while preserving the row label's normal text size.
                    return "<size=68%>●</size>";
                case ConstructionShapeType.Circle:
                    return "○";
                case ConstructionShapeType.Line:
                case ConstructionShapeType.PerpendicularBisector:
                    return "━";
                default:
                    return "·";
            }
        }

        private void AddShapeTypeRow(ConstructionShape shape)
        {
            AddLabel(EuclidText.Get("label.shapeType"), LabelTextSize, FontStyle.Bold);
            var row = AddRow();
            var buttons = new Dictionary<ConstructionShapeType, TMP_Text>();
            AddShapeTypeButton(row, shape, ConstructionShapeType.Point, buttons);
            AddShapeTypeButton(row, shape, ConstructionShapeType.Line, buttons);
            AddShapeTypeButton(row, shape, ConstructionShapeType.Circle, buttons);
        }

        private void AddShapeTypeButton(
            RectTransform row,
            ConstructionShape shape,
            ConstructionShapeType type,
            Dictionary<ConstructionShapeType, TMP_Text> buttons)
        {
            var label = ConstructionShapeTool.GetTypeLabel(type);
            var initialSurface = shape.Type == type ? ButtonSurface.Filled : ButtonSurface.Outline;
            var text = AddButton(row, label, () =>
            {
                ConstructionShapeTool.SetType(shape, type);
                ConstructionShapeTool.DrawShape(shape);
                ConstructionShapeCanvasOverlay.Refresh();
                RefreshShapeTypeButtons(buttons, shape);
                RefreshShapeListSelectionButtons();
                // Point and line/circle have different endpoint rows, so rebuild the detail
                // content immediately when the type changes.
                BuildDetailPanelContent();
                RefreshShapeActionButtons();
                RefreshTexts();
            }, 0f, initialSurface);
            buttons[type] = text;
        }

        private void RefreshShapeTypeButtons(Dictionary<ConstructionShapeType, TMP_Text> buttons, ConstructionShape shape)
        {
            foreach (var pair in buttons)
            {
                SetToggleButtonState(pair.Value, ConstructionShapeTool.GetTypeLabel(pair.Key), shape.Type == pair.Key);
            }
        }

        private void AddShapeGeometryInfo(ConstructionShape shape)
        {
            shapeGeometryInfoText = null;
            if (shape == null || shape.Type == ConstructionShapeType.Point)
            {
                return;
            }

            var row = AddCompactRow(28f, 0f);
            shapeGeometryInfoText = AddSmallLabel(row, FormatShapeGeometryInfo(shape), 0f);
            if (shapeGeometryInfoText != null)
            {
                shapeGeometryInfoText.fontSize = 16f;
                shapeGeometryInfoText.alignment = TextAlignmentOptions.MidlineLeft;
                shapeGeometryInfoText.overflowMode = TextOverflowModes.Overflow;
            }
        }

        private void RefreshShapeGeometryInfo(ConstructionShape shape)
        {
            if (shapeGeometryInfoText != null && shape != null)
            {
                shapeGeometryInfoText.text = FormatShapeGeometryInfo(shape);
            }
        }

        private static string FormatShapeGeometryInfo(ConstructionShape shape)
        {
            if (shape == null)
            {
                return string.Empty;
            }

            var first = ConstructionShapeTool.GetPointForDisplay(shape, 0);
            var second = ConstructionShapeTool.GetPointForDisplay(shape, 1);
            var dx = second.X - first.X;
            var dy = second.Y - first.Y;

            if (shape.Type == ConstructionShapeType.Circle)
            {
                var radius = Math.Sqrt(dx * dx + dy * dy);
                return "r = " + ConstructionShapeTool.Format(radius);
            }

            if (shape.Type != ConstructionShapeType.Line &&
                shape.Type != ConstructionShapeType.PerpendicularBisector)
            {
                return string.Empty;
            }

            const double epsilon = 0.000000001d;
            if (Math.Abs(dx) <= epsilon && Math.Abs(dy) <= epsilon)
            {
                return "a = —    b = —    θ = —";
            }

            var theta = Math.Atan2(dy, dx) * 180d / Math.PI;
            theta %= 180d;
            if (theta < 0d)
            {
                theta += 180d;
            }
            if (Math.Abs(theta - 180d) <= epsilon)
            {
                theta = 0d;
            }

            var thetaText = theta.ToString("0.###", CultureInfo.InvariantCulture) + "°";
            if (Math.Abs(dx) <= epsilon)
            {
                return "a = ∞    b = —    θ = " + thetaText;
            }

            var slope = dy / dx;
            var intercept = first.Y - slope * first.X;
            return "a = " + ConstructionShapeTool.Format(slope)
                + "    b = " + ConstructionShapeTool.Format(intercept)
                + "    θ = " + thetaText;
        }

        private void AddShapeColorRow(ConstructionShape shape)
        {
            AddLabel(EuclidText.Get("label.shapeColor"), LabelTextSize, FontStyle.Bold);

            var current = ConstructionShapeTool.GetColor(shape);
            var updating = false;

            // The color editor lives directly inside Shape Info. The previous floating picker was
            // visually fragile because it had to coexist with ADOFAI's inspector canvas/layout.
            // Inline controls also keep the selected shape visible while editing its color.
            var hexRow = AddCompactRow(CurrentRowHeight, 6f);
            var preview = AddColorPreview(hexRow, current, 54f);
            AddSmallLabel(hexRow, "HEX", 38f);
            var hex = AddInput(hexRow, FormatShapeColorHex(current), 0f);
            if (hex != null)
            {
                hex.characterLimit = 9;
                hex.contentType = TMP_InputField.ContentType.Standard;
            }

            TMP_InputField rInput = null;
            TMP_InputField gInput = null;
            TMP_InputField bInput = null;
            TMP_InputField aInput = null;
            Slider rSlider = null;
            Slider gSlider = null;
            Slider bSlider = null;
            Slider aSlider = null;

            Action<Color, bool> apply = (color, updateHex) =>
            {
                if (updating) return;
                updating = true;

                color.r = Mathf.Clamp01(color.r);
                color.g = Mathf.Clamp01(color.g);
                color.b = Mathf.Clamp01(color.b);
                color.a = Mathf.Clamp01(color.a);
                current = color;

                RefreshShapeColorPreview(preview, color);
                ConstructionShapeTool.SetColor(shape, color);
                ConstructionShapeTool.DrawShape(shape);
                ConstructionShapeCanvasOverlay.Refresh();

                SetPickerChannel(rSlider, rInput, color.r);
                SetPickerChannel(gSlider, gInput, color.g);
                SetPickerChannel(bSlider, bInput, color.b);
                SetPickerChannel(aSlider, aInput, color.a);
                if (updateHex && hex != null)
                {
                    hex.SetTextWithoutNotify(FormatShapeColorHex(color));
                }

                updating = false;
            };

            CreatePickerChannelRow(content, "R", current.r, out rSlider, out rInput);
            CreatePickerChannelRow(content, "G", current.g, out gSlider, out gInput);
            CreatePickerChannelRow(content, "B", current.b, out bSlider, out bInput);
            CreatePickerChannelRow(content, "A", current.a, out aSlider, out aInput);

            Action refreshFromSliders = () => apply(new Color(
                rSlider != null ? rSlider.value / 255f : current.r,
                gSlider != null ? gSlider.value / 255f : current.g,
                bSlider != null ? bSlider.value / 255f : current.b,
                aSlider != null ? aSlider.value / 255f : current.a), true);

            foreach (var slider in new[] { rSlider, gSlider, bSlider, aSlider })
            {
                if (slider != null) slider.onValueChanged.AddListener(_ => refreshFromSliders());
            }

            BindPickerInput(rInput, () => rSlider, refreshFromSliders);
            BindPickerInput(gInput, () => gSlider, refreshFromSliders);
            BindPickerInput(bInput, () => bSlider, refreshFromSliders);
            BindPickerInput(aInput, () => aSlider, refreshFromSliders);

            if (hex != null)
            {
                hex.onValueChanged.AddListener(value =>
                {
                    if (updating || !TryParseShapeColor(value, out var parsed)) return;
                    apply(parsed, false);
                });
                hex.onEndEdit.AddListener(value =>
                {
                    if (TryParseShapeColor(value, out var parsed))
                    {
                        apply(parsed, true);
                    }
                    else
                    {
                        hex.SetTextWithoutNotify(FormatShapeColorHex(current));
                    }
                });
            }

            apply(current, true);
        }

        private RectTransform CreatePickerRow(Transform parent, float height, float spacing)
        {
            var obj = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            obj.transform.SetParent(parent, false);
            var layout = obj.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = true;
            var element = obj.GetComponent<LayoutElement>();
            element.minHeight = height;
            element.preferredHeight = height;
            element.flexibleHeight = 0f;
            return obj.GetComponent<RectTransform>();
        }

        private void CreatePickerChannelRow(Transform parent, string label, float value, out Slider slider, out TMP_InputField input)
        {
            var row = CreatePickerRow(parent, 32f, 6f);
            AddSmallLabel(row, label, 28f);
            slider = CreatePickerSlider(row, value * 255f);
            input = AddInput(row, Mathf.RoundToInt(value * 255f).ToString(CultureInfo.InvariantCulture), 68f);
            if (input != null)
            {
                input.characterLimit = 3;
                input.contentType = TMP_InputField.ContentType.IntegerNumber;
            }
        }

        private Slider CreatePickerSlider(Transform parent, float value)
        {
            var root = new GameObject("Slider", typeof(RectTransform), typeof(Slider), typeof(LayoutElement));
            root.transform.SetParent(parent, false);
            var rootRect = root.GetComponent<RectTransform>();
            var layout = root.GetComponent<LayoutElement>();
            layout.minWidth = 180f;
            layout.preferredWidth = 220f;
            layout.flexibleWidth = 1f;
            layout.minHeight = 16f;
            layout.preferredHeight = 16f;

            var bgObj = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            bgObj.transform.SetParent(root.transform, false);
            var bgRect = bgObj.GetComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0f, 0.38f);
            bgRect.anchorMax = new Vector2(1f, 0.62f);
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            var bg = bgObj.GetComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.2f);

            var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleArea.transform.SetParent(root.transform, false);
            Stretch(handleArea.GetComponent<RectTransform>(), 8f, 0f, 8f, 0f);

            var handleObj = new GameObject("Handle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            handleObj.transform.SetParent(handleArea.transform, false);
            var handleRect = handleObj.GetComponent<RectTransform>();
            // Keep the thumb compact so it reads as a slider handle instead of a tall bar.
            handleRect.sizeDelta = new Vector2(10f, 8f);
            var handle = handleObj.GetComponent<Image>();
            handle.color = Color.white;

            var slider = root.GetComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 255f;
            slider.wholeNumbers = true;
            // No fill graphic: the old translucent fill could expand into a large white
            // rectangle inside the compact inspector. A thin track + handle is clearer here.
            slider.fillRect = null;
            slider.handleRect = handleRect;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.SetValueWithoutNotify(Mathf.Clamp(value, 0f, 255f));
            return slider;
        }

        private static void SetPickerChannel(Slider slider, TMP_InputField input, float normalized)
        {
            var value = Mathf.RoundToInt(Mathf.Clamp01(normalized) * 255f);
            if (slider != null) slider.SetValueWithoutNotify(value);
            if (input != null) input.SetTextWithoutNotify(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void BindPickerInput(TMP_InputField input, Func<Slider> sliderGetter, Action refresh)
        {
            if (input == null) return;
            input.onEndEdit.AddListener(value =>
            {
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    parsed = Mathf.RoundToInt(sliderGetter()?.value ?? 0f);
                }
                parsed = Mathf.Clamp(parsed, 0, 255);
                var slider = sliderGetter();
                if (slider != null) slider.SetValueWithoutNotify(parsed);
                input.SetTextWithoutNotify(parsed.ToString(CultureInfo.InvariantCulture));
                refresh();
            });
        }

        private static string FormatShapeColorHex(Color color)
        {
            return ColorUtility.ToHtmlStringRGBA(color).ToLowerInvariant();
        }

        private static bool TryParseShapeColor(string text, out Color color)
        {
            color = Color.white;
            var value = (text ?? string.Empty).Trim();
            if (value.StartsWith("#", StringComparison.Ordinal))
            {
                value = value.Substring(1);
            }

            if (value.Length == 6)
            {
                value += "ff";
            }

            if (value.Length != 8)
            {
                return false;
            }

            return ColorUtility.TryParseHtmlString("#" + value, out color);
        }

        private static void RefreshShapeColorPreview(Image swatch, Color color)
        {
            if (swatch != null)
            {
                swatch.color = color;
            }
        }

        private void AddShapePointEditor(ConstructionShape shape, int pointIndex, bool enabled = true)
        {
            var point = ConstructionShapeTool.GetPointForDisplay(shape, pointIndex);

            // The source is displayed in the heading instead of consuming another column in the
            // compact editor row. This keeps the controls exactly: Select | X | value | Y | value.
            var sourceText = AddShapePointHeader(pointIndex, point);

            var row = AddCompactRow(CurrentRowHeight, 5f);
            var pickText = AddButton(
                row,
                EuclidText.Get("button.pickPosition"),
                () => BeginPointPick(shape, pointIndex),
                64f,
                ButtonSurface.Outline);

            AddSmallLabel(row, "X", 14f);
            var xField = AddInput(row, ConstructionShapeTool.Format(point.X), 0f);
            AddSmallLabel(row, "Y", 14f);
            var yField = AddInput(row, ConstructionShapeTool.Format(point.Y), 0f);

            StoreShapePointFields(pointIndex, pickText, sourceText, xField, yField);

            if (!enabled)
            {
                // Point shapes do not consume P2. Leave the row in place so the panel does not
                // jump vertically, but make every P2 control non-interactive and visually muted.
                if (pointIndex == 1 && sourceText != null)
                {
                    sourceText.text = EuclidText.Get("label.secondPoint");
                    sourceText.color = new Color(sourceText.color.r, sourceText.color.g, sourceText.color.b, 0.45f);
                }

                var pickButton = FindButton(pickText);
                if (pickButton != null)
                {
                    pickButton.interactable = false;
                }
                SetButtonTextColor(pickText, ButtonSurface.Outline, enabled: false);
                xField.interactable = false;
                yField.interactable = false;
                return;
            }

            xField.onValueChanged.AddListener(_ =>
            {
                ApplyShapeCoordinateEdit(shape, pointIndex, xField, yField, pickText, sourceText);
            });
            yField.onValueChanged.AddListener(_ =>
            {
                ApplyShapeCoordinateEdit(shape, pointIndex, xField, yField, pickText, sourceText);
            });
        }

        private void StoreShapePointFields(
            int pointIndex,
            TMP_Text pickText,
            TMP_Text sourceText,
            TMP_InputField xField,
            TMP_InputField yField)
        {
            if (pointIndex == 0)
            {
                shapeFirstPickText = pickText;
                shapeFirstSourceText = sourceText;
                shapeFirstX = xField;
                shapeFirstY = yField;
                return;
            }

            shapeSecondPickText = pickText;
            shapeSecondSourceText = sourceText;
            shapeSecondX = xField;
            shapeSecondY = yField;
        }

        private TMP_Text AddShapePointHeader(int pointIndex, ConstructionPointRef point)
        {
            // Keep P1/P2 as real section labels rather than a tiny horizontal row.
            // The compact row could collapse its child height under ADOFAI's inspector layout,
            // which made the P1/P2 text disappear even though the coordinate row remained.
            var text = AddLabel(PointHeaderLabel(pointIndex, point), 17, FontStyle.Bold);
            if (text != null)
            {
                text.alignment = TextAlignmentOptions.BottomLeft;
            }
            return text;
        }

        private static string PointHeaderLabel(int pointIndex, ConstructionPointRef point)
        {
            var baseLabel = pointIndex == 0
                ? EuclidText.Get("label.firstPoint")
                : EuclidText.Get("label.secondPoint");

            var source = PointSourceLabel(point);
            if (string.IsNullOrEmpty(source) || point.SourceKind == ConstructionPointSourceKind.Manual)
            {
                return baseLabel;
            }

            return baseLabel + " (" + source + ")";
        }

        private void RefreshShapePointEditors(ConstructionShape shape)
        {
            RefreshShapePointEditor(
                shape,
                0,
                shapeFirstPickText,
                shapeFirstSourceText,
                shapeFirstX,
                shapeFirstY);
            RefreshShapePointEditor(
                shape,
                1,
                shapeSecondPickText,
                shapeSecondSourceText,
                shapeSecondX,
                shapeSecondY);
        }

        private void RefreshShapePointEditor(
            ConstructionShape shape,
            int pointIndex,
            TMP_Text pickText,
            TMP_Text sourceText,
            TMP_InputField xField,
            TMP_InputField yField)
        {
            if (pickText == null || sourceText == null || xField == null || yField == null)
            {
                return;
            }

            var point = ConstructionShapeTool.GetPointForDisplay(shape, pointIndex);
            sourceText.text = PointHeaderLabel(pointIndex, point);

            // Select is an ordinary action button, not a toggle. Pending state lives in the
            // picker state machine and is intentionally not represented as a latched button.
            pickText.text = EuclidText.Get("button.pickPosition");
            SetInputText(xField, ConstructionShapeTool.Format(point.X));
            SetInputText(yField, ConstructionShapeTool.Format(point.Y));
        }

        private static string PointSourceLabel(ConstructionPointRef point)
        {
            if (point.SourceKind == ConstructionPointSourceKind.ShapePoint && point.SourceShapeId > 0)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    EuclidText.Get("source.shapePoint"),
                    point.SourceShapeId);
            }

            if (point.HasTile || point.SourceKind == ConstructionPointSourceKind.Tile)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    EuclidText.Get("source.tile"),
                    point.Tile);
            }

            return string.Empty;
        }

        private void ApplyShapeCoordinateEdit(
            ConstructionShape shape,
            int pointIndex,
            TMP_InputField xField,
            TMP_InputField yField,
            TMP_Text pickText,
            TMP_Text sourceText)
        {
            if (shape == null || xField == null || yField == null)
            {
                return;
            }

            if (!double.TryParse(TextOf(xField), NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !double.TryParse(TextOf(yField), NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            {
                // Temporary input such as "-" or an empty field is allowed while typing. The
                // last valid geometry stays on screen until both coordinates parse again.
                return;
            }

            var original = ConstructionShapeTool.GetPointForDisplay(shape, pointIndex);
            if (Math.Abs(original.X - x) <= 0.0000001d && Math.Abs(original.Y - y) <= 0.0000001d)
            {
                return;
            }

            // Manual coordinate editing severs tile/point provenance immediately. This is also
            // the "reset Select" rule: a pending point pick for this endpoint is cancelled.
            if (pendingPointPickShape == shape && pendingPointPickIndex == pointIndex)
            {
                ClearPointPick();
            }

            ConstructionShapeTool.SetPoint(
                shape,
                pointIndex,
                new ConstructionPointRef
                {
                    HasTile = false,
                    Tile = 0,
                    X = x,
                    Y = y,
                    SourceKind = ConstructionPointSourceKind.Manual,
                    SourceShapeId = 0,
                });
            ConstructionShapeTool.DrawShape(shape);
            ConstructionShapeCanvasOverlay.Refresh();
            RefreshShapeGeometryInfo(shape);

            if (sourceText != null)
            {
                sourceText.text = pointIndex == 0
                    ? EuclidText.Get("label.firstPoint")
                    : EuclidText.Get("label.secondPoint");
            }
            if (pickText != null)
            {
                pickText.text = EuclidText.Get("button.pickPosition");
            }

            RefreshShapeActionButtons();
            RefreshTexts();
        }

        // Consumes a *new* editor tile selection while a position pick is pending.
        // The version guard prevents the selection that existed before BeginPointPick from being reused.
        private void SyncTileSelectionToCurrentShape()
        {
            if (pendingPointPickShape == null || pendingPointPickIndex < 0)
            {
                return;
            }

            TileSelectionOrderTracker.Refresh();
            if (pendingPointPickTileVersion == TileSelectionOrderTracker.Version)
            {
                return;
            }

            pendingPointPickTileVersion = TileSelectionOrderTracker.Version;
            if (!TileSelectionOrderTracker.TryGetMostRecentTile(out var tile) ||
                !ConstructionShapeTool.TryMakePointFromTile(tile.ToString(CultureInfo.InvariantCulture), out var point))
            {
                return;
            }

            ApplyPickedPointToEditor(point);
        }

        // Arms the next tile/drawn-point click for one endpoint. The existing selected tile is
        // deliberately ignored until TileSelectionOrderTracker.Version advances.
        private void BeginPointPick(ConstructionShape shape, int pointIndex)
        {
            // Ordinary momentary button: pressing Select always arms/re-arms this endpoint. It does
            // not toggle off when pressed twice. A successful pick or manual coordinate edit clears it.
            pendingPointPickShape = shape;
            pendingPointPickIndex = pointIndex;
            TileSelectionOrderTracker.Refresh();
            pendingPointPickTileVersion = TileSelectionOrderTracker.Version;
        }

        // Called from EuclidBehaviour.Update before the tile-selection path. This ordering
        // gives an explicitly clicked drawn point priority when it happens to overlap a tile.
        internal bool TryApplyPendingPointPickFromScene(ConstructionShape source)
        {
            if (pendingPointPickShape == null || pendingPointPickIndex < 0)
            {
                return false;
            }

            if (!ConstructionShapeTool.TryGetPointForPick(source, out var point))
            {
                return false;
            }

            ApplyPickedPointToEditor(point);
            selected = true;
            HideBuiltInPanels();
            SetPanelActive(true);
            SetTabSelectedVisual(true);
            return true;
        }

        private void ApplyPickedPointToEditor(ConstructionPointRef point)
        {
            if (pendingPointPickShape == null || pendingPointPickIndex < 0)
            {
                return;
            }

            var pointIndex = pendingPointPickIndex;
            var shape = pendingPointPickShape;
            pendingPointPickIndex = -1;
            pendingPointPickShape = null;
            pendingPointPickTileVersion = -1;

            ConstructionShapeTool.SetPoint(shape, pointIndex, point);
            ConstructionShapeTool.DrawShape(shape);
            ConstructionShapeCanvasOverlay.Refresh();
            SetShapePointEditorText(pointIndex, point);
            RefreshShapeActionButtons();
            RefreshTexts();
            BuildDetailPanelContent();
        }

        private void SetShapePointEditorText(int pointIndex, ConstructionPointRef point)
        {
            var pickText = pointIndex == 0 ? shapeFirstPickText : shapeSecondPickText;
            var sourceText = pointIndex == 0 ? shapeFirstSourceText : shapeSecondSourceText;
            var xField = pointIndex == 0 ? shapeFirstX : shapeSecondX;
            var yField = pointIndex == 0 ? shapeFirstY : shapeSecondY;
            if (xField == null || yField == null)
            {
                return;
            }

            if (sourceText != null)
            {
                sourceText.text = PointHeaderLabel(pointIndex, point);
            }
            if (pickText != null)
            {
                pickText.text = EuclidText.Get("button.pickPosition");
            }
            SetInputText(xField, ConstructionShapeTool.Format(point.X));
            SetInputText(yField, ConstructionShapeTool.Format(point.Y));
        }

        private void ClearInvalidPointPick()
        {
            if (pendingPointPickShape == null)
            {
                return;
            }

            var shapes = ConstructionShapeTool.Shapes;
            for (var i = 0; i < shapes.Count; i++)
            {
                if (shapes[i] == pendingPointPickShape)
                {
                    return;
                }
            }

            pendingPointPickShape = null;
            pendingPointPickIndex = -1;
            pendingPointPickTileVersion = -1;
        }

        private void ClearPointPick()
        {
            pendingPointPickShape = null;
            pendingPointPickIndex = -1;
            pendingPointPickTileVersion = -1;
        }

        private static bool IsAdditiveSelectionPressed()
        {
            return Input.GetKey(KeyCode.LeftControl)
                || Input.GetKey(KeyCode.RightControl)
                || Input.GetKey(KeyCode.LeftCommand)
                || Input.GetKey(KeyCode.RightCommand);
        }

        private static bool IsRangeSelectionPressed()
        {
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        }

    }
}
