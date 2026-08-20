using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;

namespace Euclid
{
    internal sealed partial class EuclidPanel
    {
        private readonly HashSet<int> endpointValidationHooks = new HashSet<int>();

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

        // ADOFAI's cloned TMP input fields can carry a non-zero ColorBlock fade. Shape-type rebuilds
        // need P2 to be disabled before the new hierarchy is ever rendered, so apply both the state
        // and its final tint synchronously during construction.
        private static void SetInputInteractableImmediate(TMP_InputField input, bool enabled)
        {
            if (input == null)
            {
                return;
            }

            var colors = input.colors;
            colors.fadeDuration = 0f;
            input.colors = colors;
            input.interactable = enabled;

            if (!enabled && input.targetGraphic != null)
            {
                input.targetGraphic.CrossFadeColor(colors.disabledColor, 0f, true, true);
            }
        }
    }
}
