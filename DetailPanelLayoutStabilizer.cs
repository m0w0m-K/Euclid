using UnityEngine;
using UnityEngine.UI;

namespace Euclid
{
    // The Shape Info hierarchy is rebuilt immediately when the shape type changes. Unity's layout
    // system otherwise gets one render opportunity with the freshly-created children in an
    // intermediate size before ContentSizeFitter/LayoutGroups settle. Normalize the conflicting
    // height rules and force the complete detail-panel layout in LateUpdate, before canvas render.
    internal sealed class DetailPanelLayoutStabilizer : MonoBehaviour
    {
        private const string DetailPanelName = "Euclid_DetailPanel";
        private const string ContentName = "Content";
        private const float SliderHeight = 20f;
        private const float SliderHandleWidth = 10f;
        private const float SliderHandleHeight = 12f;

        private GameObject detailPanel;
        private RectTransform detailContent;
        private int lastHierarchySignature;

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

            var signature = ComputeHierarchySignature(detailContent);
            if (signature == lastHierarchySignature)
            {
                return;
            }

            lastHierarchySignature = signature;
            NormalizeRowsAndSliders(detailContent);
            ForceStableLayout();
        }

        private bool ResolvePanel()
        {
            if (detailPanel != null && detailContent != null)
            {
                return detailPanel.activeInHierarchy;
            }

            detailPanel = GameObject.Find(DetailPanelName);
            if (detailPanel == null)
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

            lastHierarchySignature = 0;
            return detailPanel.activeInHierarchy;
        }

        private static int ComputeHierarchySignature(RectTransform root)
        {
            if (root == null)
            {
                return 0;
            }

            unchecked
            {
                var hash = 17;
                hash = hash * 31 + root.childCount;
                for (var i = 0; i < root.childCount; i++)
                {
                    var child = root.GetChild(i);
                    hash = hash * 31 + (child != null ? child.GetInstanceID() : 0);
                    if (child != null)
                    {
                        hash = hash * 31 + child.childCount;
                    }
                }
                return hash;
            }
        }

        private static void NormalizeRowsAndSliders(RectTransform content)
        {
            if (content == null)
            {
                return;
            }

            // Every detail row already carries an explicit LayoutElement height. Let that preferred
            // height win instead of stretching children to a transient parent height during rebuild.
            var horizontalLayouts = content.GetComponentsInChildren<HorizontalLayoutGroup>(true);
            for (var i = 0; i < horizontalLayouts.Length; i++)
            {
                var layout = horizontalLayouts[i];
                if (layout == null)
                {
                    continue;
                }

                layout.childControlHeight = true;
                layout.childForceExpandHeight = false;
            }

            var sliders = content.GetComponentsInChildren<Slider>(true);
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

                var handle = slider.handleRect;
                if (handle == null)
                {
                    continue;
                }

                // Keep the handle slightly taller than before while making its vertical anchors
                // explicit, so Slider's own horizontal anchor updates cannot temporarily stretch it.
                var min = handle.anchorMin;
                var max = handle.anchorMax;
                min.y = 0.5f;
                max.y = 0.5f;
                handle.anchorMin = min;
                handle.anchorMax = max;
                handle.pivot = new Vector2(handle.pivot.x, 0.5f);
                handle.sizeDelta = new Vector2(SliderHandleWidth, SliderHandleHeight);
            }
        }

        private void ForceStableLayout()
        {
            if (detailContent == null || detailPanel == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(detailContent);

            var panelRect = detailPanel.GetComponent<RectTransform>();
            if (panelRect != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
            }

            // ContentSizeFitter can update its own RectTransform during the first forced pass.
            // Run the content once more using that final size so no intermediate frame is rendered.
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(detailContent);
            Canvas.ForceUpdateCanvases();
        }

        private void ResetCachedPanel()
        {
            detailPanel = null;
            detailContent = null;
            lastHierarchySignature = 0;
        }
    }
}
