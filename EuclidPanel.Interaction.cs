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
        // Panel visibility, ADOFAI editor coordination, scrolling, and model <-> input syncing.
        // Methods in this file should orchestrate existing tools rather than implement geometry.

        private static RectOffset ContentPadding()
        {
            return new RectOffset(6, 22, 8, 14);
        }

        private void ShowToolPanel()
        {
            if (owner == null)
            {
                return;
            }

            if (selected && panelObject != null && panelObject.activeSelf)
            {
                CloseToolPanel();
                return;
            }

            selected = true;
            ClearOwnerSelection();
            GameCompat.TrySetInspectorVisible(owner, true);
            HideBuiltInPanels();
            SetPanelActive(true);
            SetTabSelectedVisual(true);

            try
            {
                GameCompat.SetInspectorChrome(owner, true, false, EuclidText.Get("panel.euclid"));
            }
            catch (Exception)
            {
                // Title refresh is cosmetic.
            }

            RefreshTexts();
        }

        private void CloseToolPanel()
        {
            selected = false;
            ClearOwnerSelection();
            SetPanelActive(false);
            SetTabSelectedVisual(false);

            try
            {
                GameCompat.TrySetInspectorVisible(owner, false);
            }
            catch (Exception)
            {
                // If the panel is being rebuilt, hiding our content is enough.
            }
        }

        private void ClearOwnerSelection()
        {
            try
            {
                GameCompat.ClearInspectorSelection(owner);
            }
            catch (Exception)
            {
                // Selection clearing is only to keep built-in tab toggle state sane.
            }
        }

        // ADOFAI opens its built-in inspector when a floor is selected. During point picking
        // we immediately hide those panels again so Euclid remains visible.
        private void HideBuiltInPanels()
        {
            var panelsRoot = GameCompat.GetInspectorPanels(owner);
            if (owner == null || panelsRoot == null)
            {
                return;
            }

            foreach (Transform child in panelsRoot)
            {
                if (child != null && child.gameObject != panelObject)
                {
                    child.gameObject.SetActive(false);
                }
            }

            var tabsRoot = GameCompat.GetInspectorTabs(owner);
            if (tabsRoot != null)
            {
                foreach (Transform child in tabsRoot)
                {
                    if (child != null && child.gameObject != tabObject)
                    {
                        var tab = child.GetComponent<InspectorTab>();
                        if (tab != null)
                        {
                            tab.SetSelected(false);
                        }
                    }
                }
            }
        }

        private bool BuiltInPanelIsVisible()
        {
            var panelsRoot = GameCompat.GetInspectorPanels(owner);
            if (owner == null || panelsRoot == null || panelObject == null || !panelObject.activeSelf)
            {
                return false;
            }

            foreach (Transform child in panelsRoot)
            {
                if (child != null && child.gameObject != panelObject && child.gameObject.activeSelf)
                {
                    return true;
                }
            }

            return false;
        }

        private void ClearContentChildren()
        {
            ClearChildren(content);
        }

        private static void ClearChildren(RectTransform target)
        {
            if (target == null)
            {
                return;
            }

            for (var i = target.childCount - 1; i >= 0; i--)
            {
                var child = target.GetChild(i);
                if (child != null)
                {
                    child.gameObject.SetActive(false);
                    child.SetParent(null, false);
                    Destroy(child.gameObject);
                }
            }
        }

        private void WithContent(RectTransform target, Action build)
        {
            if (target == null || build == null)
            {
                return;
            }

            var previous = content;
            content = target;
            try
            {
                build();
            }
            finally
            {
                content = previous;
            }
        }

        private void SetContentChildrenVisible(bool visible)
        {
            if (content == null)
            {
                return;
            }

            for (var i = 0; i < content.childCount; i++)
            {
                var child = content.GetChild(i);
                if (child != null && child.gameObject.activeSelf != visible)
                {
                    child.gameObject.SetActive(visible);
                }
            }
        }

        private void DisableOuterScroll(RectTransform host)
        {
            if (host == null)
            {
                return;
            }

            var scrolls = host.GetComponentsInParent<ScrollRect>(true);
            ScrollRect candidate = null;
            for (var i = 0; i < scrolls.Length; i++)
            {
                var scroll = scrolls[i];
                if (scroll != null && scroll.content == host)
                {
                    candidate = scroll;
                    break;
                }

                if (candidate == null && scroll != null)
                {
                    candidate = scroll;
                }
            }

            if (candidate == null)
            {
                return;
            }

            if (outerScroll != candidate)
            {
                RestoreOuterScroll();
                outerScroll = candidate;
                outerScrollWasEnabled = outerScroll.enabled;
            }

            outerScroll.enabled = false;
            HideOuterScrollbars(outerScroll);
        }

        private void HideOuterScrollbars(ScrollRect scroll)
        {
            if (scroll == null)
            {
                return;
            }

            outerScrollbarStates.Clear();
            HideScrollbar(scroll.verticalScrollbar);
            HideScrollbar(scroll.horizontalScrollbar);
        }

        private void HideScrollbar(Scrollbar scrollbar)
        {
            if (scrollbar == null)
            {
                return;
            }

            for (var i = 0; i < outerScrollbarStates.Count; i++)
            {
                if (outerScrollbarStates[i].Scrollbar == scrollbar)
                {
                    return;
                }
            }

            outerScrollbarStates.Add(new ScrollbarState(scrollbar, scrollbar.enabled, scrollbar.gameObject.activeSelf));
            scrollbar.enabled = false;
            scrollbar.gameObject.SetActive(false);
        }

        private void RestoreOuterScroll()
        {
            if (outerScroll == null)
            {
                return;
            }

            outerScroll.enabled = outerScrollWasEnabled;
            for (var i = 0; i < outerScrollbarStates.Count; i++)
            {
                var state = outerScrollbarStates[i];
                if (state.Scrollbar == null)
                {
                    continue;
                }

                state.Scrollbar.enabled = state.WasEnabled;
                state.Scrollbar.gameObject.SetActive(state.WasActive);
            }

            outerScrollbarStates.Clear();
            outerScroll = null;
            outerScrollWasEnabled = false;
        }

        private static bool IsEditorPlaying()
        {
            return GameCompat.IsEditorPlaying(scnEditor.instance);
        }

        // Cheap per-tick UI refresh. Avoid rebuilding the hierarchy here; hierarchy rebuilds belong
        // to RebuildConstructionUi() so ordinary editor ticks do not allocate large UI trees.
        private void RefreshTexts()
        {
            // Keep the ADOFAI inspector chrome labeled even after the game refreshes its own
            // tabs/title state. This is the visible name of this editor tab, not the UMM mod name.
            if (selected && owner != null)
            {
                try
                {
                    GameCompat.SetInspectorChrome(owner, true, false, EuclidText.Get("panel.euclid"));
                }
                catch (Exception)
                {
                    // Cosmetic only; editor rebuilds can briefly invalidate title objects.
                }
            }

            var canShapeSnap = CoordinateSnapTool.CanSnapSelectedTargetToSelectedShape(
                latestCamera,
                GuideLineTool.CoordinateKeyText);
            if (GuideLineTool.SnapSelectedShapeDrag && !canShapeSnap)
            {
                GuideLineTool.SnapSelectedShapeDrag = false;
            }

            if (shapeSnapText != null)
            {
                var snapLabel = EuclidText.Get("button.snap");
                SetToggleButtonState(shapeSnapText, snapLabel, GuideLineTool.SnapSelectedShapeDrag && canShapeSnap, canShapeSnap);
            }

            if (measureText != null)
            {
                measureText.text = FormatMeasure(latestMeasure);
            }

            if (guideStatusText != null)
            {
                guideStatusText.text = GuideLineTool.Message;
            }

            if (targetText != null)
            {
                targetText.text = CoordinateSnapTool.DescribeTarget(latestCamera, GuideLineTool.CoordinateKeyText);
            }

            if (showGuideText != null)
            {
                SetToggleButtonState(showGuideText, EuclidText.Get("button.showGuide"), GuideLineTool.Active);
            }

            if (snapDragText != null)
            {
                SetToggleButtonState(snapDragText, EuclidText.Get("button.snapDrag"), GuideLineTool.SnapCameraDrag);
            }

            if (dragCameraText != null)
            {
                SetToggleButtonState(dragCameraText, EuclidText.Get("button.dragCenter"), GuideLineTool.EnableCameraDrag);
            }

            if (selectedLineText != null)
            {
                SetToggleButtonState(selectedLineText, EuclidText.Get("button.selectedLine"), guideLinePreset == GuideLinePreset.SelectedLine);
            }

            if (perpendicularText != null)
            {
                SetToggleButtonState(perpendicularText, EuclidText.Get("button.perpendicular"), guideLinePreset == GuideLinePreset.Perpendicular);
            }
        }

        private void SyncLineFieldsFromTool()
        {
            SetInputText(anchorX, GuideLineTool.FormatValue(GuideLineTool.Anchor.x));
            SetInputText(anchorY, GuideLineTool.FormatValue(GuideLineTool.Anchor.y));
            SetInputText(directionX, GuideLineTool.FormatValue(GuideLineTool.Direction.x));
            SetInputText(directionY, GuideLineTool.FormatValue(GuideLineTool.Direction.y));
            lineFieldsDirty = false;
        }

        private void SyncCircleFieldsFromTool()
        {
            SetInputText(circleCenterX, GuideLineTool.FormatValue(GuideLineTool.CircleCenter.x));
            SetInputText(circleCenterY, GuideLineTool.FormatValue(GuideLineTool.CircleCenter.y));
            SetInputText(circleRadius, GuideLineTool.FormatRadius(GuideLineTool.CircleRadius));
            circleFieldsDirty = false;
        }

        private void SyncCoordinateFieldsFromTool()
        {
            SetInputText(keyField, GuideLineTool.CoordinateKeyText);
            SetInputText(stepField, GuideLineTool.StepText);
        }

        private void StoreLineFields()
        {
            GuideLineTool.SetFieldTexts(TextOf(anchorX), TextOf(anchorY), TextOf(directionX), TextOf(directionY));
        }

        private void StoreCircleFields()
        {
            GuideLineTool.SetCircleFieldTexts(TextOf(circleCenterX), TextOf(circleCenterY), TextOf(circleRadius));
        }

        private void StoreCoordinateFields()
        {
            if (keyField != null)
            {
                GuideLineTool.CoordinateKeyText = TextOf(keyField);
            }

            GuideLineTool.StepText = TextOf(stepField);
        }

        private void UseAutoCoordinateKey()
        {
            GuideLineTool.CoordinateKeyText = CoordinateSnapTool.SuggestKey(latestCamera);
        }

        private bool ApplyCurrentLineFields(bool force = false)
        {
            if (!force && !lineFieldsDirty)
            {
                return GuideLineTool.Snapshot.IsValid;
            }

            StoreLineFields();
            guideLinePreset = GuideLinePreset.None;
            var applied = GuideLineTool.ApplyFields();
            if (applied)
            {
                SyncLineFieldsFromTool();
            }

            return applied;
        }

        private void RegisterLineField(TMP_InputField field)
        {
            if (field == null)
            {
                return;
            }

            field.onValueChanged.AddListener(_ => lineFieldsDirty = true);
        }

        private bool ApplyCurrentCircleFields(bool force = false)
        {
            if (!force && !circleFieldsDirty)
            {
                return GuideLineTool.CircleSnapshot.IsValid;
            }

            StoreCircleFields();
            var applied = GuideLineTool.ApplyCircleFields();
            if (applied)
            {
                SyncCircleFieldsFromTool();
            }

            return applied;
        }

        private void RegisterCircleField(TMP_InputField field)
        {
            if (field == null)
            {
                return;
            }

            field.onValueChanged.AddListener(_ => circleFieldsDirty = true);
        }

        private bool ApplyCurrentInputFieldsForSnap()
        {
            StoreCoordinateFields();
            if (!ApplyCurrentLineFields())
            {
                return false;
            }

            UseAutoCoordinateKey();
            return true;
        }

    }
}
