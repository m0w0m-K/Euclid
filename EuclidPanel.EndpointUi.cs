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

        // Small UI/state fixes that depend on controls created by the construction detail panel.
        // Keep these here instead of making the large construction builder responsible for
        // per-frame housekeeping.
        private void Update()
        {
            if (!EuclidMod.Enabled)
            {
                return;
            }

            var shape = ConstructionShapeTool.PrimarySelectedShape;
            if (shape != null)
            {
                EnsureEndpointValidation(shape, 0, shapeFirstX, shapeFirstY, shapeFirstPickText, shapeFirstSourceText);
                if (shape.Type != ConstructionShapeType.Point)
                {
                    EnsureEndpointValidation(shape, 1, shapeSecondX, shapeSecondY, shapeSecondPickText, shapeSecondSourceText);
                }

                MatchPointButtonSize(shapeFirstPickText, shapeFirstPinText);
                if (shape.Type != ConstructionShapeType.Point)
                {
                    MatchPointButtonSize(shapeSecondPickText, shapeSecondPinText);
                }
            }

            CompactShapeColorHandles();
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

            // An unfinished value such as "", "-", NaN, or Infinity must not remain in the
            // inspector after focus leaves the field. Commit a real coordinate value of 0.
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

        private static void MatchPointButtonSize(TMP_Text pickText, TMP_Text pinText)
        {
            if (pickText == null || pinText == null ||
                pickText.transform == null || pinText.transform == null ||
                pickText.transform.parent == null || pinText.transform.parent == null)
            {
                return;
            }

            var pickLayout = pickText.transform.parent.GetComponent<LayoutElement>();
            var pinLayout = pinText.transform.parent.GetComponent<LayoutElement>();
            if (pickLayout == null || pinLayout == null)
            {
                return;
            }

            pinLayout.minWidth = pickLayout.minWidth;
            pinLayout.preferredWidth = pickLayout.preferredWidth;
            pinLayout.flexibleWidth = pickLayout.flexibleWidth;
            pinLayout.minHeight = pickLayout.minHeight;
            pinLayout.preferredHeight = pickLayout.preferredHeight;
            pinLayout.flexibleHeight = pickLayout.flexibleHeight;
        }

        private void CompactShapeColorHandles()
        {
            if (detailContent == null)
            {
                return;
            }

            var sliders = detailContent.GetComponentsInChildren<Slider>(true);
            for (var i = 0; i < sliders.Length; i++)
            {
                var slider = sliders[i];
                if (slider == null || slider.handleRect == null)
                {
                    continue;
                }

                var size = slider.handleRect.sizeDelta;
                if (Mathf.Abs(size.y - 6f) > 0.01f)
                {
                    slider.handleRect.sizeDelta = new Vector2(size.x, 6f);
                }
            }
        }
    }
}
