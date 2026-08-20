using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Euclid
{
    internal sealed partial class EuclidPanel
    {
        private readonly HashSet<int> endpointValidationHooks = new HashSet<int>();

        // Endpoint text validation belongs to Update because the detail hierarchy can be rebuilt by
        // a button click at any time. Final layout/interactable normalization is done from the
        // PointBinding LateUpdate after dynamic PIN buttons have also been created.
        private void Update()
        {
            if (!EuclidMod.Enabled)
            {
                return;
            }

            var shape = ConstructionShapeTool.PrimarySelectedShape;
            if (shape == null)
            {
                return;
            }

            EnsureEndpointValidation(shape, 0, shapeFirstX, shapeFirstY, shapeFirstPickText, shapeFirstSourceText);
            if (shape.Type != ConstructionShapeType.Point)
            {
                EnsureEndpointValidation(shape, 1, shapeSecondX, shapeSecondY, shapeSecondPickText, shapeSecondSourceText);
            }
        }

        private void EnsureEndpointValidation(
            ConstructionShape shape,
            int pointIndex,
            TMP_InputField xField,
            TMP_InputField yField,
            TMP_Text pickText,
            TMP_Text sourceText)
        {
            HookEndpointField(shape, pointIndex, xField, yField, pickText, sourceText, xField);
            HookEndpointField(shape, pointIndex, xField, yField, pickText, sourceText, yField);
        }

        private void HookEndpointField(
            ConstructionShape shape,
            int pointIndex,
            TMP_InputField xField,
            TMP_InputField yField,
            TMP_Text pickText,
            TMP_Text sourceText,
            TMP_InputField field)
        {
            if (shape == null || field == null)
            {
                return;
            }

            var instanceId = field.GetInstanceID();
            if (!endpointValidationHooks.Add(instanceId))
            {
                return;
            }

            field.onEndEdit.AddListener(_ => NormalizeEndpointAfterFocusLoss(
                shape,
                pointIndex,
                xField,
                yField,
                pickText,
                sourceText));
        }

        private void NormalizeEndpointAfterFocusLoss(
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

            var xValid = TryParseFiniteCoordinate(TextOf(xField), out var x);
            var yValid = TryParseFiniteCoordinate(TextOf(yField), out var y);
            if (xValid && yValid)
            {
                return;
            }

            // An unfinished value such as "", "-", NaN, Infinity, or arbitrary text must not
            // remain in the inspector after focus leaves the field. Commit a real value of 0.
            if (!xValid)
            {
                x = 0d;
                xField.SetTextWithoutNotify("0");
            }
            if (!yValid)
            {
                y = 0d;
                yField.SetTextWithoutNotify("0");
            }

            if (pendingPointPickShape == shape && pendingPointPickIndex == pointIndex)
            {
                ClearPointPick();
            }

            // Focus-loss normalization is a manual coordinate edit. It therefore detaches a live
            // tile/point pin just like typing any other valid coordinate does.
            RemovePointBinding(shape, pointIndex);
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

            if (sourceText != null)
            {
                sourceText.text = pointIndex == 0
                    ? EuclidText.Get("label.firstPoint")
                    : EuclidText.Get("label.secondPoint");
            }
            if (pickText != null)
            {
                SetToggleButtonState(pickText, EuclidText.Get("button.pickPosition"), selected: false);
            }

            RefreshShapeGeometryInfo(shape);
            RefreshShapeActionButtons();
            RefreshPointBindingButtons();
            RefreshTexts();
        }

        private static bool TryParseFiniteCoordinate(string text, out double value)
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return false;
            }

            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        // Called at the end of EuclidPanel.LateUpdate, after any dynamic PIN button has been added.
        // Give every control its final geometry and interaction state before Unity renders the frame.
        // This replaces the old "copy whatever size Pick currently has" and separate layout
        // stabilizer, both of which exposed intermediate states for one or more frames.
        private void NormalizeDetailControlState(ConstructionShape shape)
        {
            if (shape == null || detailContent == null)
            {
                return;
            }

            NormalizePointButtonLayout(shapeFirstPickText);
            NormalizePointButtonLayout(shapeFirstPinText);
            NormalizePointButtonLayout(shapeSecondPickText);
            NormalizePointButtonLayout(shapeSecondPinText);

            var secondEnabled = shape.Type != ConstructionShapeType.Point;
            SetInputInteractableImmediate(shapeSecondX, secondEnabled);
            SetInputInteractableImmediate(shapeSecondY, secondEnabled);

            NormalizeShapeColorControls();
        }

        private void NormalizePointButtonLayout(TMP_Text text)
        {
            if (text == null || text.transform == null || text.transform.parent == null)
            {
                return;
            }

            const float width = 64f;
            // This method runs after WithContent(detailContent, ...) has restored `content` to the
            // main panel, so CurrentButtonHeight would return the main-panel height. Endpoint buttons
            // are detail-panel controls and their construction-time height is always 36 px.
            const float height = 36f;
            var root = text.transform.parent as RectTransform;
            var layout = text.transform.parent.GetComponent<LayoutElement>();
            if (layout != null)
            {
                layout.minWidth = width;
                layout.preferredWidth = width;
                layout.flexibleWidth = 0f;
                layout.minHeight = height;
                layout.preferredHeight = height;
                layout.flexibleHeight = 0f;
            }

            // A newly-created GameObject starts at Unity's default RectTransform size until the next
            // layout pass. Setting the fixed size immediately prevents PIN from appearing oversized
            // for the frame in which it is inserted into the endpoint row.
            if (root != null)
            {
                root.sizeDelta = new Vector2(width, height);
            }
        }

        private static void SetInputInteractableImmediate(TMP_InputField input, bool enabled)
        {
            if (input == null)
            {
                return;
            }

            var colors = input.colors;
            if (colors.fadeDuration != 0f)
            {
                colors.fadeDuration = 0f;
                input.colors = colors;
            }

            if (input.interactable != enabled)
            {
                input.interactable = enabled;
            }

            // TMP_InputField cloned from ADOFAI can already be part-way through its original color
            // fade when the Point UI is rebuilt. Snap the disabled P2 fields to their final tint so
            // they never flash as enabled first.
            if (!enabled && input.targetGraphic != null)
            {
                input.targetGraphic.CrossFadeColor(colors.disabledColor, 0f, true, true);
            }
        }

        private void NormalizeShapeColorControls()
        {
            var sliders = detailContent.GetComponentsInChildren<Slider>(true);
            for (var i = 0; i < sliders.Length; i++)
            {
                var slider = sliders[i];
                if (slider == null)
                {
                    continue;
                }

                var rowLayout = slider.transform.parent != null
                    ? slider.transform.parent.GetComponent<HorizontalLayoutGroup>()
                    : null;
                if (rowLayout != null)
                {
                    rowLayout.childControlHeight = true;
                    rowLayout.childForceExpandHeight = false;
                }

                var sliderLayout = slider.GetComponent<LayoutElement>();
                if (sliderLayout != null)
                {
                    sliderLayout.minHeight = 20f;
                    sliderLayout.preferredHeight = 20f;
                    sliderLayout.flexibleHeight = 0f;
                }

                var handle = slider.handleRect;
                if (handle == null)
                {
                    continue;
                }

                // Slider owns the X anchors. Only lock Y to the center and set the intended thumb
                // dimensions. The slightly taller 12 px handle is the final size, not a correction
                // applied by a separate component one frame later.
                var anchorMin = handle.anchorMin;
                var anchorMax = handle.anchorMax;
                anchorMin.y = 0.5f;
                anchorMax.y = 0.5f;
                handle.anchorMin = anchorMin;
                handle.anchorMax = anchorMax;
                handle.pivot = new Vector2(handle.pivot.x, 0.5f);
                handle.sizeDelta = new Vector2(10f, 12f);
            }
        }
    }
}
