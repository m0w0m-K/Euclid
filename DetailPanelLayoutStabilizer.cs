using UnityEngine;
using UnityEngine.UI;

namespace Euclid
{
    // Shape Info is rebuilt when the selected shape/type changes. Its rows mix flexible controls
    // with explicitly fixed-width/fixed-height controls. Unity's default force-expand flags can
    // temporarily ignore those fixed LayoutElements during the first layout pass, which is why a
    // fixed button or slider handle visibly stretches for one frame and then snaps back.
    //
    // Run after normal scripts, remove that force-expand conflict, finish the layout immediately,
    // then re-assert the Slider handle rect after Slider/LayoutGroup have done their own rebuild.
    [DefaultExecutionOrder(20000)]
    internal sealed class DetailPanelLayoutStabilizer : MonoBehaviour
    {
        private const string DetailPanelName = "Euclid_DetailPanel";
        private const string ContentName = "Content";
        private const float SliderHeight = 20f;
        private const float SliderHandleWidth = 10f;
        private const float SliderHandleHeight = 12f;

        private GameObject detailPanel;
        private RectTransform detailContent;

        internal static void Install()
        {
            var behaviour = EuclidMod.Behaviour;
            if (behaviour == null || behaviour.GetComponent<DetailPanelLayoutStabilizer>() != null)
            {
                return;
            }

            behaviour.gameObject.AddComponent<DetailPanelLayoutStabilizer>();
        }

        private void LateUpdate()
        {
            if (!EuclidMod.Enabled)
            {
                ResetCachedPanel();
                return;
            }

            if (!ResolvePanel())
            {
                return;
            }

            StabilizeCurrentPanel();
        }

        private bool ResolvePanel()
        {
            if (detailPanel != null && detailContent != null && detailPanel.activeInHierarchy)
            {
                return true;
            }

            detailPanel = GameObject.Find(DetailPanelName);
            if (detailPanel == null || !detailPanel.activeInHierarchy)
            {
                detailContent = null;
                return false;
            }

            var content = detailPanel.transform.Find(ContentName);
            detailContent = content as RectTransform;
            if (detailContent == null)
            {
                ResetCachedPanel();
                return false;
            }

            return true;
        }

        private void StabilizeCurrentPanel()
        {
            if (detailPanel == null || detailContent == null)
            {
                return;
            }

            // In Shape Info, width=0 controls already opt into flexibleWidth=1. Fixed controls have
            // flexibleWidth=0. Therefore force-expanding every child is both unnecessary and the
            // reason fixed buttons briefly become the same size as flexible buttons after rebuild.
            var horizontalLayouts = detailPanel.GetComponentsInChildren<HorizontalLayoutGroup>(true);
            for (var i = 0; i < horizontalLayouts.Length; i++)
            {
                var layout = horizontalLayouts[i];
                if (layout == null)
                {
                    continue;
                }

                layout.childControlWidth = true;
                layout.childForceExpandWidth = false;
                layout.childControlHeight = true;
                layout.childForceExpandHeight = false;
            }

            var sliders = detailPanel.GetComponentsInChildren<Slider>(true);
            for (var i = 0; i < sliders.Length; i++)
            {
                var slider = sliders[i];
                if (slider == null)
                {
                    continue;
                }

                var element = slider.GetComponent<LayoutElement>() ?? slider.gameObject.AddComponent<LayoutElement>();
                element.minHeight = SliderHeight;
                element.preferredHeight = SliderHeight;
                element.flexibleHeight = 0f;
            }

            // Finish all parent/ContentSizeFitter calculations now instead of allowing a transient
            // hierarchy size to survive until Unity's next frame.
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(detailContent);

            var panelRect = detailPanel.GetComponent<RectTransform>();
            if (panelRect != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
            }

            Canvas.ForceUpdateCanvases();

            // Slider.UpdateVisuals can rewrite handle anchors during the forced pass above. Set the
            // final vertical geometry last, with no further forced layout after this point. This also
            // makes the requested handle a little taller than the original 8 px version.
            for (var i = 0; i < sliders.Length; i++)
            {
                var slider = sliders[i];
                var handle = slider != null ? slider.handleRect : null;
                if (handle == null)
                {
                    continue;
                }

                var min = handle.anchorMin;
                var max = handle.anchorMax;
                min.y = 0.5f;
                max.y = 0.5f;
                handle.anchorMin = min;
                handle.anchorMax = max;
                handle.pivot = new Vector2(handle.pivot.x, 0.5f);
                handle.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, SliderHandleWidth);
                handle.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, SliderHandleHeight);
            }
        }

        private void ResetCachedPanel()
        {
            detailPanel = null;
            detailContent = null;
        }
    }
}
