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
        // Generic UI builders used by the standalone inspector.
        //
        // Prefer these helpers over constructing raw Unity UI objects in feature code. They clone
        // ADOFAI controls when possible, then strip game-specific behaviours and apply a safe fallback.

        // Shape Info is intentionally denser than the main inspector. Keep the compact sizing
        // context-aware so the left tool panel retains the original ADOFAI-like spacing.
        private bool IsDetailContentActive => detailContent != null && content == detailContent;
        private float CurrentButtonHeight => IsDetailContentActive ? 36f : ButtonHeight;
        private float CurrentInputHeight => IsDetailContentActive ? 34f : InputHeight;
        private float CurrentRowHeight => IsDetailContentActive ? 36f : RowHeight;

        private void AddKeyStepRow()
        {
            var row = AddCompactRow(RowHeight, 6f);
            AddSmallLabel(row, EuclidText.Get("label.key"), 24f);
            keyField = AddInput(row, GuideLineTool.CoordinateKeyText, 0f);
            keyField.onEndEdit.AddListener(value => GuideLineTool.CoordinateKeyText = value);

            var autoText = AddButton(row, EuclidText.Get("button.auto"), () =>
            {
                GuideLineTool.CoordinateKeyText = CoordinateSnapTool.SuggestKey(latestCamera);
                SyncCoordinateFieldsFromTool();
                RefreshTexts();
            }, 78f);
            autoText.fontSize = ButtonTextSize;

            AddSmallLabel(row, EuclidText.Get("label.step"), 42f);
            stepField = AddInput(row, GuideLineTool.StepText, 62f);
            stepField.onEndEdit.AddListener(value => GuideLineTool.StepText = value);
        }

        private void AddStepControlRow()
        {
            var row = AddRow();
            AddButton(row, EuclidText.Get("button.minusStep"), () =>
            {
                if (!ApplyCurrentInputFieldsForSnap())
                {
                    RefreshTexts();
                    return;
                }

                GuideLineTool.MoveSelectedAlongGuide(latestCamera, -GuideLineTool.GetStepValue());
                RefreshTexts();
            }, 0f);

            stepField = AddInput(row, GuideLineTool.StepText, 0f);
            stepField.onEndEdit.AddListener(value => GuideLineTool.StepText = value);

            AddButton(row, EuclidText.Get("button.plusStep"), () =>
            {
                if (!ApplyCurrentInputFieldsForSnap())
                {
                    RefreshTexts();
                    return;
                }

                GuideLineTool.MoveSelectedAlongGuide(latestCamera, GuideLineTool.GetStepValue());
                RefreshTexts();
            }, 0f);
        }

        private void AddInputRow(string label, out TMP_InputField xField, out TMP_InputField yField)
        {
            var row = AddCompactRow(RowHeight, 4f);
            AddSmallLabel(row, label, label.Length > 1 ? 58f : 14f);
            AddSmallLabel(row, "X", 14f);
            xField = AddInput(row, "0", 0f);
            AddSmallLabel(row, "Y", 14f);
            yField = AddInput(row, "0", 0f);
        }

        private TMP_InputField AddScalarInputRow(string label, string value)
        {
            var row = AddCompactRow(RowHeight, 4f);
            AddSmallLabel(row, label, 72f);
            return AddInput(row, value, 0f);
        }

        private TMP_InputField AddFullWidthInputRow(string value)
        {
            var row = AddRow();
            return AddInput(row, value, 0f);
        }

        private TMP_Text AddLabel(string text, int fontSize, FontStyle fontStyle)
        {
            var obj = new GameObject(text, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI), typeof(LayoutElement));
            obj.transform.SetParent(content, false);
            var label = obj.GetComponent<TextMeshProUGUI>();
            ApplyTextStyle(label);
            label.text = text;
            label.fontSize = fontSize;
            label.fontStyle = fontStyle == FontStyle.Bold ? FontStyles.Bold : fontStyle == FontStyle.Italic ? FontStyles.Italic : FontStyles.Normal;
            label.lineSpacing = 0f;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.TopLeft;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Overflow;

            var layout = obj.GetComponent<LayoutElement>();
            SetLabelHeight(layout, text, fontSize);
            if (IsDetailContentActive && !string.IsNullOrEmpty(text) && text.IndexOf('\n') < 0)
            {
                var compactHeight = Mathf.Max(22f, fontSize + 7f);
                layout.minHeight = compactHeight;
                layout.preferredHeight = compactHeight;
            }
            return label;
        }

        private TMP_Text AddButton(string label, Action action)
        {
            return AddButton(content, label, action, -1f, ButtonSurface.Filled);
        }

        private TMP_Text AddButton(string label, Action action, ButtonSurface surface)
        {
            return AddButton(content, label, action, -1f, surface);
        }

        private TMP_Text AddInsetButton(string label, Action action)
        {
            var row = new GameObject(label + " Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(content, false);

            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(0, 0, 0, 0);
            layout.spacing = 0f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            var element = row.GetComponent<LayoutElement>();
            element.minHeight = CurrentButtonHeight;
            element.preferredHeight = CurrentButtonHeight;
            element.flexibleHeight = 0f;

            return AddButton(row.transform, label, action, 0f, ButtonSurface.Filled);
        }

        private TMP_Text AddButton(Transform parent, string label, Action action, float width)
        {
            return AddButton(parent, label, action, width, ButtonSurface.Filled);
        }

        private TMP_Text AddButton(Transform parent, string label, Action action, float width, ButtonSurface surface)
        {
            var obj = new GameObject(label, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
            obj.transform.SetParent(parent, false);
            var image = obj.GetComponent<Image>();

            var button = obj.GetComponent<Button>();
            ApplyButtonStyle(button);
            button.targetGraphic = image;
            ApplyButtonSurface(image, surface);
            button.onClick.AddListener(() => action());

            var layout = obj.GetComponent<LayoutElement>();
            layout.minHeight = CurrentButtonHeight;
            layout.preferredHeight = CurrentButtonHeight;
            layout.flexibleHeight = 0f;
            if (width > 0f)
            {
                layout.preferredWidth = width;
                layout.minWidth = width;
                layout.flexibleWidth = 0f;
            }
            else
            {
                layout.preferredWidth = 0f;
                layout.minWidth = 0f;
                layout.flexibleWidth = 1f;
            }

            var textObj = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObj.transform.SetParent(obj.transform, false);
            Stretch(textObj.GetComponent<RectTransform>(), 10f, 5f, 10f, 5f);

            var text = textObj.GetComponent<TextMeshProUGUI>();
            ApplyTextStyle(text);
            text.text = label;
            text.fontSize = ButtonTextSize;
            text.enableAutoSizing = true;
            text.fontSizeMin = 14f;
            text.fontSizeMax = ButtonTextSize;
            SetButtonTextColor(text, surface, enabled: true);
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            return text;
        }

        private void AddButtonRow(params (string label, Action action)[] buttons)
        {
            var row = AddRow();
            foreach (var button in buttons)
            {
                AddButton(row, button.label, button.action, 0f);
            }
        }

        private RectTransform AddRow()
        {
            var obj = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            obj.transform.SetParent(content, false);
            var layout = obj.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            var element = obj.GetComponent<LayoutElement>();
            element.minHeight = CurrentRowHeight;
            element.preferredHeight = CurrentRowHeight;
            element.flexibleHeight = 0f;
            return obj.GetComponent<RectTransform>();
        }

        private RectTransform AddCompactRow(float height, float spacing)
        {
            var obj = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            obj.transform.SetParent(content, false);
            var layout = obj.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            var element = obj.GetComponent<LayoutElement>();
            element.minHeight = height;
            element.preferredHeight = height;
            element.flexibleHeight = 0f;
            return obj.GetComponent<RectTransform>();
        }

        private TMP_Text AddSmallLabel(Transform parent, string text, float width)
        {
            var obj = new GameObject(text, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI), typeof(LayoutElement));
            obj.transform.SetParent(parent, false);
            var label = obj.GetComponent<TextMeshProUGUI>();
            ApplyTextStyle(label);
            label.text = text;
            label.fontSize = LabelTextSize;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;

            var layout = obj.GetComponent<LayoutElement>();
            if (width > 0f)
            {
                layout.minWidth = width;
                layout.preferredWidth = width;
                layout.flexibleWidth = 0f;
            }
            else
            {
                layout.minWidth = 0f;
                layout.preferredWidth = 0f;
                layout.flexibleWidth = 1f;
            }

            // Most callers only need the label to be rendered, but point headers keep this
            // reference so their source text can be refreshed after selecting a tile/point.
            return label;
        }

        private TMP_InputField AddInput(Transform parent, string value, float width)
        {
            var clonedInput = AddClonedInput(parent, value, width);
            if (clonedInput != null)
            {
                return clonedInput;
            }

            var obj = new GameObject("Input", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(TMP_InputField), typeof(LayoutElement));
            obj.transform.SetParent(parent, false);
            ApplyImageStyle(obj.GetComponent<Image>(), uiStyle.InputImage);

            var textObj = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObj.transform.SetParent(obj.transform, false);
            Stretch(textObj.GetComponent<RectTransform>(), 12f, 4f, 12f, 4f);
            var text = textObj.GetComponent<TextMeshProUGUI>();
            ApplyTextStyle(text);
            text.fontSize = ButtonTextSize;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;

            var input = obj.GetComponent<TMP_InputField>();
            input.targetGraphic = obj.GetComponent<Image>();
            input.textViewport = textObj.GetComponent<RectTransform>();
            input.textComponent = text;
            input.text = value;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.caretColor = Color.white;
            input.selectionColor = new Color(0.2f, 0.8f, 1f, 0.45f);

            var layout = obj.GetComponent<LayoutElement>();
            if (width > 0f)
            {
                layout.minWidth = width;
                layout.preferredWidth = width;
                layout.flexibleWidth = 0f;
            }
            else
            {
                layout.minWidth = 78f;
                layout.preferredWidth = 96f;
                layout.flexibleWidth = 1f;
            }

            layout.minHeight = CurrentInputHeight;
            layout.preferredHeight = CurrentInputHeight;
            return input;
        }

        private Image AddColorPreview(Transform parent, Color color, float width)
        {
            var obj = new GameObject("Color Preview", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
            obj.transform.SetParent(parent, false);

            var image = obj.GetComponent<Image>();
            // Reuse the editor button sprite so the preview has the same rounded/bordered shape
            // as nearby ADOFAI controls, then tint the sprite with the actual shape color.
            ApplyImageStyle(image, uiStyle.ButtonImage);
            image.color = color;
            image.raycastTarget = false;

            ConfigureLayout(obj, width, CurrentInputHeight);
            var layout = obj.GetComponent<LayoutElement>();
            layout.minWidth = width;
            layout.preferredWidth = width;
            layout.flexibleWidth = 0f;
            return image;
        }

        private TMP_Text AddClonedButton(Transform parent, string label, Action action, float width)
        {
            if (uiStyle.ButtonTemplate == null)
            {
                return null;
            }

            var obj = Instantiate(uiStyle.ButtonTemplate, parent, false);
            obj.name = label;
            obj.SetActive(true);
            StripTemplateScripts(obj);

            var button = obj.GetComponent<Button>() ?? obj.GetComponentInChildren<Button>(true);
            if (button == null)
            {
                Destroy(obj);
                return null;
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => action());
            button.interactable = true;
            ApplyButtonStyle(button);
            ApplyClonedButtonImage(obj, button);

            var text = FindBestText(obj);
            if (text == null)
            {
                var textObj = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                textObj.transform.SetParent(obj.transform, false);
                Stretch(textObj.GetComponent<RectTransform>(), 10f, 5f, 10f, 5f);
                text = textObj.GetComponent<TextMeshProUGUI>();
            }

            ApplyTextStyle(text);
            Stretch(text.GetComponent<RectTransform>(), 10f, 5f, 10f, 5f);
            text.text = label;
            text.fontSize = ButtonTextSize;
            text.enableAutoSizing = true;
            text.fontSizeMin = 14f;
            text.fontSizeMax = ButtonTextSize;
            SetReadableButtonTextColor(text);
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;

            ConfigureLayout(obj, width, CurrentButtonHeight);
            return text;
        }

        private TMP_InputField AddClonedInput(Transform parent, string value, float width)
        {
            if (uiStyle.InputTemplate == null)
            {
                return null;
            }

            var obj = Instantiate(uiStyle.InputTemplate, parent, false);
            obj.name = "Input";
            obj.SetActive(true);
            StripTemplateScripts(obj);

            var input = obj.GetComponent<TMP_InputField>() ?? obj.GetComponentInChildren<TMP_InputField>(true);
            if (input == null)
            {
                Destroy(obj);
                return null;
            }

            input.onEndEdit.RemoveAllListeners();
            input.onValueChanged.RemoveAllListeners();
            input.onSelect.RemoveAllListeners();
            input.onDeselect.RemoveAllListeners();
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.caretColor = Color.white;
            input.selectionColor = new Color(0.2f, 0.8f, 1f, 0.45f);
            input.text = value;

            if (input.textComponent != null)
            {
                ApplyTextStyle(input.textComponent);
                input.textComponent.fontSize = ButtonTextSize;
                input.textComponent.color = Color.white;
                input.textComponent.alignment = TextAlignmentOptions.MidlineLeft;
                input.textComponent.textWrappingMode = TextWrappingModes.NoWrap;
                input.textComponent.overflowMode = TextOverflowModes.Overflow;
            }

            if (input.placeholder is TMP_Text placeholder)
            {
                placeholder.text = string.Empty;
            }

            ConfigureInputLayout(obj, width, CurrentInputHeight);
            return input;
        }

        private static void ConfigureLayout(GameObject obj, float width, float height)
        {
            var rect = obj.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.localScale = Vector3.one;
                rect.localRotation = Quaternion.identity;
            }

            var layout = obj.GetComponent<LayoutElement>() ?? obj.AddComponent<LayoutElement>();
            layout.minHeight = height;
            layout.preferredHeight = height;
            if (width > 0f)
            {
                layout.minWidth = width;
                layout.preferredWidth = width;
                layout.flexibleWidth = 0f;
            }
            else
            {
                layout.minWidth = 0f;
                layout.preferredWidth = 0f;
                layout.flexibleWidth = 1f;
            }
        }

        private static void ConfigureInputLayout(GameObject obj, float width, float height)
        {
            ConfigureLayout(obj, width, height);
            if (width > 0f)
            {
                return;
            }

            var layout = obj.GetComponent<LayoutElement>() ?? obj.AddComponent<LayoutElement>();
            layout.minWidth = 78f;
            layout.preferredWidth = 96f;
            layout.flexibleWidth = 1f;
        }

        private void ApplyClonedButtonImage(GameObject obj, Button button)
        {
            if (obj == null || button == null)
            {
                return;
            }

            var primary = button.targetGraphic as Image
                ?? button.GetComponent<Image>()
                ?? obj.GetComponent<Image>()
                ?? obj.GetComponentInChildren<Image>(true);

            var images = obj.GetComponentsInChildren<Image>(true);
            foreach (var image in images)
            {
                if (image == null)
                {
                    continue;
                }

                if (image == primary)
                {
                    image.enabled = true;
                    ApplyImageStyle(image, uiStyle.ButtonImage);
                    button.targetGraphic = image;
                    continue;
                }

                if (image.GetComponentInParent<TMP_InputField>() == null)
                {
                    image.enabled = false;
                    image.raycastTarget = false;
                }
            }
        }

        private static TMP_Text FindBestText(GameObject obj)
        {
            var texts = obj.GetComponentsInChildren<TMP_Text>(true);
            TMP_Text fallback = null;
            foreach (var text in texts)
            {
                if (text == null)
                {
                    continue;
                }

                fallback = fallback ?? text;
                if (text.GetComponentInParent<TMP_InputField>() == null
                    && !text.name.ToLowerInvariant().Contains("placeholder"))
                {
                    return text;
                }
            }

            return fallback;
        }

        // Cloned ADOFAI controls may carry behaviours that expect the original inspector context.
        // Remove those behaviours before wiring our own listeners, while preserving UI primitives.
        private static void StripTemplateScripts(GameObject obj)
        {
            foreach (var behaviour in obj.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null || KeepTemplateBehaviour(behaviour))
                {
                    continue;
                }

                Destroy(behaviour);
            }
        }

        private static bool KeepTemplateBehaviour(MonoBehaviour behaviour)
        {
            var ns = behaviour.GetType().Namespace ?? string.Empty;
            return ns == "UnityEngine.UI"
                || ns == "TMPro"
                || ns == "UnityEngine.EventSystems";
        }

        private void AddSpacer(float height)
        {
            var obj = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
            obj.transform.SetParent(content, false);
            var layout = obj.GetComponent<LayoutElement>();
            layout.minHeight = height;
            layout.preferredHeight = height;
            layout.flexibleHeight = 0f;
        }

        private void AddFlexibleSpacer(float weight)
        {
            var obj = new GameObject("Flexible Spacer", typeof(RectTransform), typeof(LayoutElement));
            obj.transform.SetParent(content, false);
            var layout = obj.GetComponent<LayoutElement>();
            layout.minHeight = 0f;
            layout.preferredHeight = 0f;
            layout.flexibleHeight = Mathf.Max(0.01f, weight);
        }

        private void PositionTab()
        {
            var tabsRoot = GameCompat.GetInspectorTabs(owner);
            if (owner == null || tabsRoot == null || tabObject == null)
            {
                return;
            }

            var tabRect = tabObject.GetComponent<RectTransform>();
            if (tabPositionInitialized)
            {
                tabRect.anchorMin = tabBaseAnchorMin;
                tabRect.anchorMax = tabBaseAnchorMax;
                tabRect.pivot = tabBasePivot;
                tabRect.sizeDelta = tabBaseSizeDelta;
                tabRect.anchoredPosition = tabBaseAnchoredPosition;
                tabRect.localScale = Vector3.one;
                tabObject.transform.SetAsLastSibling();
                return;
            }

            // Clone the bottom-most real inspector tab and measure the centre-to-centre spacing
            // between the existing tabs. Using only the template height caused the Euclid tab to sit at a
            // slightly different visual gap because ADOFAI's tab pitch includes extra spacing.
            var template = FindTemplateTab(tabsRoot);
            if (template == null)
            {
                tabBaseAnchorMin = tabRect.anchorMin;
                tabBaseAnchorMax = tabRect.anchorMax;
                tabBasePivot = tabRect.pivot;
                tabBaseSizeDelta = tabRect.sizeDelta;
                tabBaseAnchoredPosition = tabRect.anchoredPosition;
                tabPositionInitialized = true;
                return;
            }

            var templateRect = template.GetComponent<RectTransform>();
            CopyRectTransform(templateRect, tabRect);

            var yPositions = new List<float>();
            foreach (Transform child in tabsRoot)
            {
                if (child == null || child.gameObject == tabObject || child.name == TabObjectName)
                {
                    continue;
                }

                // Decorations can also be direct children of the tabs root. Only actual
                // InspectorTab objects participate in the vertical pitch calculation.
                if (child.GetComponent<InspectorTab>() == null)
                {
                    continue;
                }

                var rect = child.GetComponent<RectTransform>();
                if (rect != null)
                {
                    yPositions.Add(rect.anchoredPosition.y);
                }
            }

            var bottomY = templateRect.anchoredPosition.y;
            var tabStep = 0f;
            if (yPositions.Count > 0)
            {
                yPositions.Sort((a, b) => b.CompareTo(a));
                bottomY = yPositions[yPositions.Count - 1];

                var gaps = new List<float>();
                for (var i = 1; i < yPositions.Count; i++)
                {
                    var gap = Mathf.Abs(yPositions[i - 1] - yPositions[i]);
                    if (gap > 1f)
                    {
                        gaps.Add(gap);
                    }
                }

                if (gaps.Count > 0)
                {
                    gaps.Sort();
                    tabStep = gaps[gaps.Count / 2];
                }
            }

            if (tabStep <= 1f)
            {
                tabStep = Mathf.Abs(templateRect.rect.height);
            }
            if (tabStep <= 1f)
            {
                tabStep = Mathf.Abs(templateRect.sizeDelta.y);
            }
            if (tabStep <= 1f)
            {
                tabStep = 76f;
            }

            tabRect.anchoredPosition = new Vector2(templateRect.anchoredPosition.x, bottomY - tabStep);
            tabRect.sizeDelta = templateRect.sizeDelta;
            tabRect.localScale = Vector3.one;
            tabObject.transform.SetAsLastSibling();

            tabBaseAnchorMin = tabRect.anchorMin;
            tabBaseAnchorMax = tabRect.anchorMax;
            tabBasePivot = tabRect.pivot;
            tabBaseSizeDelta = tabRect.sizeDelta;
            tabBaseAnchoredPosition = tabRect.anchoredPosition;
            tabPositionInitialized = true;
        }

        private void RemoveOrphanedObjects()
        {
            RemoveNamedChild(GameCompat.GetInspectorTabs(owner), TabObjectName);
            RemoveNamedChild(GameCompat.GetInspectorPanels(owner), PanelObjectName);
            var ownerPanels = GameCompat.GetInspectorPanels(owner);
            RemoveNamedChild(ownerPanels != null ? ownerPanels.parent : null, DetailPanelObjectName);
            var rightInspector = FindRightInspectorPanel();
            RemoveNamedChild(GameCompat.GetInspectorPanels(rightInspector), DetailPanelObjectName);
            var canvas = owner != null ? owner.GetComponentInParent<Canvas>() : null;
            RemoveNamedChild(canvas != null ? canvas.transform : null, DetailPanelObjectName);
        }

        private static void RemoveNamedChild(Transform parent, string childName)
        {
            if (parent == null)
            {
                return;
            }

            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i);
                if (child != null && child.name == childName)
                {
                    child.gameObject.SetActive(false);
                    Destroy(child.gameObject);
                }
            }
        }

        private static void RemoveNamedChildExcept(Transform parent, string childName, Transform excludedParent)
        {
            if (parent == null || parent == excludedParent)
            {
                return;
            }

            RemoveNamedChild(parent, childName);
        }

        private static Transform FindTemplateTab(Transform parent)
        {
            if (parent == null)
            {
                return null;
            }

            // The lowest existing inspector tab is the best visual template for a tab appended
            // beneath the stack. This preserves the same root/child dimensions as its neighbour.
            Transform best = null;
            var bestY = float.PositiveInfinity;
            for (var i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child == null || child.name == TabObjectName || child.GetComponent<InspectorTab>() == null)
                {
                    continue;
                }

                var rect = child.GetComponent<RectTransform>();
                if (rect == null)
                {
                    continue;
                }

                if (best == null || rect.anchoredPosition.y < bestY)
                {
                    best = child;
                    bestY = rect.anchoredPosition.y;
                }
            }

            if (best != null)
            {
                return best;
            }

            // Fallback for a future editor version where tabs no longer carry InspectorTab.
            for (var i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child == null || child.name == TabObjectName)
                {
                    continue;
                }

                var rect = child.GetComponent<RectTransform>();
                if (rect != null && (best == null || rect.anchoredPosition.y < bestY))
                {
                    best = child;
                    bestY = rect.anchoredPosition.y;
                }
            }

            return best;
        }

        private static void CopyRectTransform(RectTransform source, RectTransform target)
        {
            if (source == null || target == null)
            {
                return;
            }

            target.anchorMin = source.anchorMin;
            target.anchorMax = source.anchorMax;
            target.pivot = source.pivot;
            target.sizeDelta = source.sizeDelta;
            target.anchoredPosition = source.anchoredPosition;
            target.localRotation = Quaternion.identity;
            target.localScale = Vector3.one;
        }

        private Button PrepareClonedTab(bool clonedFromTemplate)
        {
            if (tabObject == null)
            {
                return null;
            }

            visualTab = tabObject.GetComponent<InspectorTab>();
            var mainButton = visualTab != null ? visualTab.button : tabObject.GetComponent<Button>();
            if (visualTab != null)
            {
                if (visualTab.icon != null)
                {
                    visualTab.icon.enabled = false;
                    visualTab.icon.raycastTarget = false;
                }

                visualTab.enabled = false;
            }

            foreach (var cycle in tabObject.GetComponentsInChildren<CycleButtons>(true))
            {
                if (cycle != null)
                {
                    cycle.gameObject.SetActive(false);
                }
            }

            var buttons = tabObject.GetComponentsInChildren<Button>(true);
            for (var i = 0; i < buttons.Length; i++)
            {
                var button = buttons[i];
                button.onClick.RemoveAllListeners();

                if (mainButton == null)
                {
                    mainButton = button;
                }

                if (button == mainButton)
                {
                    button.interactable = true;
                    continue;
                }

                button.interactable = false;
                button.gameObject.SetActive(false);
            }

            var images = tabObject.GetComponentsInChildren<Image>(true);
            foreach (var image in images)
            {
                if (image == null)
                {
                    continue;
                }

                image.raycastTarget = image.gameObject == tabObject || image.GetComponent<Button>() != null;
            }

            if (!clonedFromTemplate)
            {
                var image = tabObject.GetComponent<Image>();
                if (image != null)
                {
                    image.color = new Color(1f, 1f, 1f, 0.18f);
                    image.raycastTarget = true;
                }
            }

            return mainButton;
        }

        private void SetTabVisible(bool visible)
        {
            if (tabObject != null && tabObject.activeSelf != visible)
            {
                tabObject.SetActive(visible);
            }
        }

        private void SetPanelActive(bool active)
        {
            if (panelObject != null && panelObject.activeSelf != active)
            {
                panelObject.SetActive(active);
            }

            SetDetailPanelActive(active);
        }

        private void SetDetailPanelActive(bool active)
        {
            active = active && ConstructionShapeTool.PrimarySelectedShape != null;
            if (detailPanelObject != null && detailPanelObject.activeSelf != active)
            {
                detailPanelObject.SetActive(active);
            }

            if (active)
            {
                AlignVisibleDetailPanel();
            }
        }

        private void ShowDetailOwnerPanel()
        {
            if (detailOwner == null || detailPanelObject == null)
            {
                return;
            }

            try
            {
                GameCompat.TrySetInspectorVisible(detailOwner, true);
            }
            catch (Exception)
            {
                // Some editor states rebuild the inspector while tabs are changing.
            }

            var detailPanelsRoot = GameCompat.GetInspectorPanels(detailOwner);
            if (detailPanelsRoot != null)
            {
                foreach (Transform child in detailPanelsRoot)
                {
                    if (child != null && child.gameObject != detailPanelObject)
                    {
                        child.gameObject.SetActive(false);
                    }
                }
            }

            try
            {
                GameCompat.SetInspectorChrome(detailOwner, true, false, EuclidText.Get("panel.shapeInfo"));
            }
            catch (Exception)
            {
                // Cosmetic only.
            }
        }

        private void AlignVisibleDetailPanel()
        {
            if (detailPanelObject == null)
            {
                return;
            }

            AlignDetailPanelToHost(
                detailPanelObject.GetComponent<RectTransform>(),
                panelObject != null ? panelObject.GetComponent<RectTransform>() : null,
                detailPanelObject.transform.parent);
        }

        private void SetTabSelectedVisual(bool value)
        {
            tabVisualSelected = value;

            // A cloned ADOFAI tab already knows how selected/unselected tabs should look.
            // Reuse that exact visual state instead of applying a custom grey tint, which made
            // this tab look disabled while the other inactive tabs only looked unselected.
            try
            {
                if (visualTab != null)
                {
                    visualTab.SetSelected(value);
                    if (tabButton != null)
                    {
                        tabButton.interactable = true;
                    }

                    // The original icon is hidden, but SetSelected still updates its tint. Mirror
                    // that tint onto the Å text so our replacement icon follows the same theme.
                    if (tabLabel != null && visualTab.icon != null)
                    {
                        tabLabel.color = visualTab.icon.color;
                    }

                    if (!value)
                    {
                        PositionTab();
                    }
                    return;
                }
            }
            catch (Exception)
            {
                // Fall through to a neutral fallback when the game's tab implementation changes.
            }

            if (tabButton != null)
            {
                tabButton.interactable = true;
                if (tabButton.targetGraphic != null)
                {
                    var colors = tabButton.colors;
                    tabButton.targetGraphic.color = value ? colors.selectedColor : colors.normalColor;
                }
            }

            if (tabLabel != null)
            {
                tabLabel.color = value ? Color.white : new Color(1f, 1f, 1f, 0.72f);
            }

            if (!value)
            {
                PositionTab();
            }
        }

        private void ApplyManualTabVisual(bool selectedVisual)
        {
            if (tabLabel != null)
            {
                tabLabel.color = selectedVisual
                    ? new Color(0.82f, 1f, 1f, 1f)
                    : new Color(0.62f, 0.78f, 0.78f, 0.9f);
            }

            if (tabButton != null)
            {
                var colors = tabButton.colors;
                colors.normalColor = selectedVisual
                    ? new Color(1f, 1f, 1f, 0.78f)
                    : new Color(0.58f, 0.58f, 0.58f, 0.78f);
                colors.highlightedColor = selectedVisual
                    ? new Color(1f, 1f, 1f, 0.9f)
                    : new Color(0.7f, 0.7f, 0.7f, 0.85f);
                colors.pressedColor = new Color(0.86f, 0.86f, 0.86f, 0.95f);
                colors.selectedColor = colors.normalColor;
                tabButton.colors = colors;

                if (tabButton.targetGraphic is Image targetImage)
                {
                    targetImage.color = colors.normalColor;
                }
            }

            var rootImage = tabObject != null ? tabObject.GetComponent<Image>() : null;
            if (rootImage != null)
            {
                rootImage.color = selectedVisual
                    ? new Color(1f, 1f, 1f, 0.72f)
                    : new Color(0.58f, 0.58f, 0.58f, 0.62f);
            }
        }

        private static void Stretch(RectTransform rect, float left, float top, float right, float bottom)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private void ApplyTextStyle(TMP_Text text)
        {
            if (text == null)
            {
                return;
            }

            if (uiStyle.Font != null)
            {
                text.font = uiStyle.Font;
            }

            if (uiStyle.FontMaterial != null)
            {
                text.fontMaterial = uiStyle.FontMaterial;
            }
        }

        private void ApplyImageStyle(Image image, ImageStyle style)
        {
            if (image == null)
            {
                return;
            }

            image.sprite = style.Sprite;
            image.overrideSprite = style.Sprite;
            image.type = style.Type;
            image.color = style.Color;
            image.material = null;
            image.raycastTarget = true;
            image.preserveAspect = false;
            image.canvasRenderer.SetColor(Color.white);

            var outline = image.GetComponent<Outline>();
            if (style.Sprite != null || style.OutlineColor.a <= 0f)
            {
                if (outline != null)
                {
                    outline.enabled = false;
                }

                return;
            }

            outline = outline ?? image.gameObject.AddComponent<Outline>();
            outline.enabled = true;
            outline.effectColor = style.OutlineColor;
            outline.effectDistance = style.OutlineDistance;
            outline.useGraphicAlpha = false;
        }

        private void ApplyButtonStyle(Button button)
        {
            if (button == null)
            {
                return;
            }

            button.transition = Selectable.Transition.ColorTint;
            button.colors = uiStyle.ButtonColors;
        }

        private static void SetInputText(TMP_InputField field, string value)
        {
            if (field != null)
            {
                field.text = value;
            }
        }

        private static string TextOf(TMP_InputField field)
        {
            return field != null ? field.text : string.Empty;
        }

        private static void SetFixedHeight(TMP_Text text, float height)
        {
            if (text == null)
            {
                return;
            }

            var layout = text.GetComponent<LayoutElement>();
            if (layout != null)
            {
                layout.minHeight = height;
                layout.preferredHeight = height;
            }
        }

        private static void SetLabelHeight(LayoutElement layout, string text, int fontSize)
        {
            if (layout == null)
            {
                return;
            }

            var lineCount = 1;
            if (!string.IsNullOrEmpty(text))
            {
                for (var i = 0; i < text.Length; i++)
                {
                    if (text[i] == '\n')
                    {
                        lineCount++;
                    }
                }
            }

            var height = lineCount * (fontSize + 6f) + 8f;
            layout.minHeight = height;
            layout.preferredHeight = height;
        }

        private void SetActionButtonState(TMP_Text text, bool enabled)
        {
            if (text == null)
            {
                return;
            }

            var button = FindButton(text);
            if (button != null)
            {
                button.interactable = enabled;
                ApplyButtonStyle(button);
            }

            var image = FindButtonImage(text);
            ApplyButtonSurface(image, enabled ? ButtonSurface.Filled : ButtonSurface.Outline);
            SetButtonTextColor(text, enabled ? ButtonSurface.Filled : ButtonSurface.Outline, enabled);
        }

        private void SetToggleButtonState(TMP_Text text, string label, bool selected, bool enabled = true)
        {
            if (text == null)
            {
                return;
            }

            text.text = label;

            var button = FindButton(text);
            var image = FindButtonImage(text);
            if (button != null)
            {
                button.interactable = enabled;
                button.transition = Selectable.Transition.None;
                if (image != null)
                {
                    button.targetGraphic = image;
                }
            }

            ApplyButtonSurface(image, selected ? ButtonSurface.Filled : ButtonSurface.Outline);
            SetButtonTextColor(text, selected ? ButtonSurface.Filled : ButtonSurface.Outline, enabled);
        }

        private void ApplyButtonSurface(Image image, ButtonSurface surface)
        {
            ApplyImageStyle(image, surface == ButtonSurface.Filled ? uiStyle.ButtonImage : uiStyle.InputImage);
            if (image == null)
            {
                return;
            }

            image.enabled = true;
            image.SetAllDirty();
        }

        private static void SetButtonTextColor(TMP_Text text, ButtonSurface surface, bool enabled)
        {
            if (text == null)
            {
                return;
            }

            if (!enabled)
            {
                text.color = DisabledButtonTextColor;
                return;
            }

            text.color = surface == ButtonSurface.Filled
                ? Color.black
                : Color.white;
        }

        private static void SetReadableButtonTextColor(TMP_Text text)
        {
            SetButtonTextColor(text, ButtonSurface.Filled, enabled: true);
        }

        private static Color ReadableTextColor(Image image)
        {
            if (image == null)
            {
                return Color.white;
            }

            var color = image.color;
            var luminance = color.r * 0.299f + color.g * 0.587f + color.b * 0.114f;
            return luminance > 0.56f && color.a > 0.35f ? Color.black : Color.white;
        }

        private static Button FindButton(TMP_Text text)
        {
            return text != null ? text.GetComponentInParent<Button>() : null;
        }

        private static Image FindButtonImage(TMP_Text text)
        {
            if (text == null)
            {
                return null;
            }

            var button = text.GetComponentInParent<Button>();
            if (button != null)
            {
                var ownImage = button.GetComponent<Image>();
                if (ownImage != null)
                {
                    return ownImage;
                }

                if (button.targetGraphic is Image targetImage)
                {
                    return targetImage;
                }

                return button.GetComponentInChildren<Image>(true);
            }

            return text.GetComponentInParent<Image>();
        }

    }
}
