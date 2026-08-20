using System;
using System.Collections.Generic;
using System.Globalization;
using ADOFAI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Euclid
{
    // Coordinates the standalone Euclid inspector tab.
    //
    // This type is split across partial files by responsibility:
    //   EuclidPanel.cs              - lifecycle, installation, panel roots
    //   EuclidPanel.Construction.cs - shape list/editor and point picking
    //   EuclidPanel.Interaction.cs  - visibility, field syncing, editor interaction
    //   EuclidPanel.UiFactory.cs    - reusable UI construction helpers
    //   EuclidPanel.Style.cs        - captured ADOFAI UI style and formatting
    //
    // Keep game-version compatibility access in GameCompat instead of adding new direct
    // InspectorPanel/scnEditor field lookups here. That keeps future ADOFAI ports localized.
    internal sealed partial class EuclidPanel : MonoBehaviour
    {
        private const string TabObjectName = "Euclid_InternalTab";
        private const string PanelObjectName = "Euclid_InternalPanel";
        private const string DetailPanelObjectName = "Euclid_DetailPanel";
        private const int ButtonTextSize = 19;
        private const int LabelTextSize = 17;
        private const float ButtonHeight = 44f;
        private const float InputHeight = 40f;
        private const float RowHeight = 44f;
        private const float DetailPanelMinHeight = 220f;
        private const float DetailPanelContentTopOffset = 50f;
        private const float DetailPanelExtraBottomPadding = 10f;
        private static readonly Color DisabledButtonTextColor = new Color(0.68f, 0.68f, 0.68f, 0.82f);

        private InspectorPanel owner;
        private InspectorPanel detailOwner;
        private GameObject tabObject;
        private GameObject panelObject;
        private GameObject detailPanelObject;
        private RectTransform content;
        private RectTransform detailContent;
        private RectTransform detailHeaderRect;
        // The shape-detail window is a floating inspector. Once the user drags it, automatic
        // layout may resize it but must not snap it back to its original right-side position.
        private bool detailPanelDragging;
        private bool detailPanelUserMoved;
        private Vector2 detailDragStartMouseLocal;
        private Vector2 detailDragStartAnchoredPosition;
        private bool detailLayoutLogged;
        private UiStyle uiStyle;
        private InspectorTab visualTab;
        private Button tabButton;
        private TMP_Text tabLabel;
        private bool tabVisualSelected;
        private bool tabPositionInitialized;
        private Vector2 tabBaseAnchorMin;
        private Vector2 tabBaseAnchorMax;
        private Vector2 tabBasePivot;
        private Vector2 tabBaseSizeDelta;
        private Vector2 tabBaseAnchoredPosition;
        private bool selected;
        private bool rebuildingConstructionUi;
        private ScrollRect outerScroll;
        private bool outerScrollWasEnabled;
        private readonly List<ScrollbarState> outerScrollbarStates = new List<ScrollbarState>();
        private bool initialConstructionShapesCleaned;

        private MeasureSnapshot latestMeasure = MeasureSnapshot.Unavailable("Not captured yet.");
        private CameraFrameSnapshot latestCamera = CameraFrameSnapshot.Unavailable("Not captured yet.");
        private TMP_Text measureText;
        private TMP_Text guideStatusText;
        private TMP_Text targetText;
        private TMP_Text showGuideText;
        private TMP_Text snapDragText;
        private TMP_Text dragCameraText;
        private TMP_Text selectedLineText;
        private TMP_Text perpendicularText;
        private TMP_InputField anchorX;
        private TMP_InputField anchorY;
        private TMP_InputField directionX;
        private TMP_InputField directionY;
        private TMP_InputField circleCenterX;
        private TMP_InputField circleCenterY;
        private TMP_InputField circleRadius;
        private TMP_InputField keyField;
        private TMP_InputField stepField;
        private TMP_Text shapeFirstPickText;
        private TMP_Text shapeFirstSourceText;
        private TMP_InputField shapeFirstX;
        private TMP_InputField shapeFirstY;
        private TMP_Text shapeSecondPickText;
        private TMP_Text shapeSecondSourceText;
        private TMP_InputField shapeSecondX;
        private TMP_InputField shapeSecondY;
        private TMP_Text shapeGeometryInfoText;
        private TMP_Text shapeSnapText;
        private TMP_Text shapeIntersectionsText;
        // Position-pick state for the "select position" buttons in the shape editor.
        // pendingPointPickTileVersion is the selection version at the moment picking starts.
        // A tile must only be consumed after TileSelectionOrderTracker.Version advances, otherwise
        // the tile that was already selected when the button was pressed would be applied instantly.
        private int pendingPointPickIndex = -1;
        private ConstructionShape pendingPointPickShape;
        private int pendingPointPickTileVersion = -1;

        private void OnDestroy()
        {
            if (detailPanelObject != null)
            {
                Destroy(detailPanelObject);
                detailPanelObject = null;
                detailContent = null;
                detailHeaderRect = null;
            }
        }
        private readonly Dictionary<int, TMP_Text> shapeListTexts = new Dictionary<int, TMP_Text>();
        private readonly Dictionary<int, TMP_Text> shapeVisibilityTexts = new Dictionary<int, TMP_Text>();
        private GuideLinePreset guideLinePreset;
        private bool lineFieldsDirty;
        private bool circleFieldsDirty;

        private enum GuideLinePreset
        {
            None,
            SelectedLine,
            Perpendicular,
        }

        private enum ButtonSurface
        {
            Filled,
            Outline,
        }

        internal bool IsPointPickPending => pendingPointPickShape != null && pendingPointPickIndex >= 0;

        // Called by the runtime coordinator when a different editor map is detected. Construction
        // shapes are intentionally map-local and must never leak into the next level.
        internal void HandleEditorMapChanged()
        {
            ClearPointPick();
            ConstructionShapeTool.ClearAll();
            shapeListTexts.Clear();
            shapeVisibilityTexts.Clear();

            if (owner != null && panelObject != null)
            {
                RebuildConstructionUi();
            }
        }

        // Scene point picking can run from Update or the authoritative OnGUI event path.
        // Ignore clicks on this tool's own UI so a point hidden behind the panel cannot be picked.
        internal bool IsScreenPointOverToolUi(Vector2 screenPoint)
        {
            return ContainsScreenPoint(tabObject, screenPoint)
                || ContainsScreenPoint(panelObject, screenPoint)
                || ContainsScreenPoint(detailPanelObject, screenPoint);
        }

        private static bool ContainsScreenPoint(GameObject target, Vector2 screenPoint)
        {
            if (target == null || !target.activeInHierarchy)
            {
                return false;
            }

            var rect = target.GetComponent<RectTransform>();
            if (rect == null)
            {
                return false;
            }

            var canvas = rect.GetComponentInParent<Canvas>();
            var eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            return RectTransformUtility.RectangleContainsScreenPoint(rect, screenPoint, eventCamera);
        }

        internal void Tick(MeasureSnapshot measure, CameraFrameSnapshot cameraFrame)
        {
            latestMeasure = measure;
            latestCamera = cameraFrame;

            var editor = scnEditor.instance;
            var settingsPanel = GameCompat.GetSettingsPanel(editor);
            if (editor == null || settingsPanel == null)
            {
                Hide();
                return;
            }

            EnsureInstalled(settingsPanel);
            ConstructionShapeCanvasOverlay.Ensure(settingsPanel);

            var playing = GameCompat.IsEditorPlaying(editor);
            SetTabVisible(!playing);

            if (playing)
            {
                ConstructionShapeCanvasOverlay.SetVisible(false);
                SetPanelActive(false);
                SetTabSelectedVisual(false);
                return;
            }

            ConstructionShapeCanvasOverlay.Refresh();

            if (selected)
            {
                var wasPickingPoint = pendingPointPickShape != null && pendingPointPickIndex >= 0;

                // IMPORTANT: this call is what lets a normal ADOFAI tile click complete a pending
                // shape point pick. Do not remove it when changing panel/update flow. Point-shape
                // picking has a separate path, so missing this call only breaks tile picking.
                SyncTileSelectionToCurrentShape();

                // Selecting a floor normally opens one of ADOFAI's built-in inspector panels.
                // If that click was consumed by the position picker, keep this tool visible so
                // the newly picked tile/coordinates can be seen immediately.
                if (wasPickingPoint && pendingPointPickShape == null)
                {
                    HideBuiltInPanels();
                }

                SetPanelActive(true);
                SetTabSelectedVisual(true);
            }
            else
            {
                SetPanelActive(false);
                SetTabSelectedVisual(false);
            }

            if (selected && BuiltInPanelIsVisible())
            {
                if (IsPointPickPending)
                {
                    // Clicking a tile for position picking normally opens ADOFAI's own inspector.
                    // Keep this panel selected until the pending pick is completed or cancelled.
                    HideBuiltInPanels();
                }
                else
                {
                    selected = false;
                    SetPanelActive(false);
                    SetTabSelectedVisual(false);
                }
            }

            HandleDetailPanelDrag();
            RefreshTexts();
        }

        internal void Hide()
        {
            selected = false;
            SetTabVisible(false);
            SetPanelActive(false);
            SetTabSelectedVisual(false);
            RestoreOuterScroll();
            ConstructionShapeCanvasOverlay.SetVisible(false);
        }

        private void EnsureInstalled(InspectorPanel panel)
        {
            if (owner == panel && tabObject != null && panelObject != null)
            {
                return;
            }

            DestroyExisting();
            owner = panel;
            uiStyle = UiStyle.Capture(panel);
            RemoveOrphanedObjects();
            CreateTab();
            CreatePanel();
            if (tabObject == null || panelObject == null || content == null)
            {
                DestroyExisting();
                return;
            }

            SyncLineFieldsFromTool();
            SyncCoordinateFieldsFromTool();
            RefreshTexts();
        }

        private void DestroyExisting()
        {
            if (tabObject != null)
            {
                Destroy(tabObject);
            }

            if (panelObject != null)
            {
                Destroy(panelObject);
            }

            if (detailPanelObject != null)
            {
                Destroy(detailPanelObject);
            }

            RestoreOuterScroll();
            tabObject = null;
            panelObject = null;
            detailPanelObject = null;
            content = null;
            detailContent = null;
            detailHeaderRect = null;
            detailPanelDragging = false;
            detailPanelUserMoved = false;
            visualTab = null;
            tabButton = null;
            tabLabel = null;
            tabVisualSelected = false;
            tabPositionInitialized = false;
        }

        private void CreateTab()
        {
            var tabsRoot = GameCompat.GetInspectorTabs(owner);
            if (tabsRoot == null)
            {
                return;
            }

            var template = FindTemplateTab(tabsRoot);
            tabObject = template != null
                ? Instantiate(template.gameObject, tabsRoot)
                : new GameObject(TabObjectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));

            tabObject.name = TabObjectName;
            tabObject.transform.SetParent(tabsRoot, false);
            tabObject.SetActive(true);
            tabObject.transform.SetAsLastSibling();

            var tabRect = tabObject.GetComponent<RectTransform>();
            if (template != null)
            {
                CopyRectTransform(template.GetComponent<RectTransform>(), tabRect);
            }
            else
            {
                tabRect.anchorMin = new Vector2(1f, 1f);
                tabRect.anchorMax = new Vector2(1f, 1f);
                tabRect.pivot = new Vector2(0.5f, 0.5f);
                tabRect.sizeDelta = new Vector2(64f, 64f);
                tabRect.anchoredPosition = new Vector2(32f, -32f);
            }

            tabButton = PrepareClonedTab(template != null) ?? tabObject.GetComponentInChildren<Button>(true) ?? tabObject.AddComponent<Button>();
            tabButton.onClick.RemoveAllListeners();
            tabButton.onClick.AddListener(ShowToolPanel);

            var labelObject = new GameObject("ToolIconLabel", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(tabObject.transform, false);
            var labelRect = labelObject.GetComponent<RectTransform>();
            Stretch(labelRect, 0f, 0f, 0f, 0f);

            var label = labelObject.GetComponent<TextMeshProUGUI>();
            tabLabel = label;
            if (owner.title != null)
            {
                label.font = owner.title.font;
                label.fontMaterial = owner.title.fontMaterial;
            }

            // The measured-angle glyph is a compact visual mark for Euclid's geometry tools.
            // Keep it as text so the tab follows ADOFAI's font/tint behavior without an external sprite.
            label.text = "Å";
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 26f;
            label.fontStyle = FontStyles.Normal;
            label.color = new Color(0.82f, 1f, 1f, 1f);
            label.raycastTarget = false;

            SetTabSelectedVisual(false);
            PositionTab();
        }

        private void CreatePanel()
        {
            var panelsRoot = GameCompat.GetInspectorPanels(owner);
            if (panelsRoot == null)
            {
                return;
            }

            panelObject = new GameObject(PanelObjectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panelObject.transform.SetParent(panelsRoot, false);
            var panelRect = panelObject.GetComponent<RectTransform>();
            Stretch(panelRect, 0f, 0f, 0f, 0f);

            var background = panelObject.GetComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.86f);

            var contentObject = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup));
            contentObject.transform.SetParent(panelObject.transform, false);
            content = contentObject.GetComponent<RectTransform>();
            Stretch(content, 16f, 16f, 16f, 16f);
            ConfigureRootContentLayout(contentObject, ContentPadding());

            EnsureDetailPanel(panelRect);
            BuildPanelContent();
            SetPanelActive(false);
        }

        private void EnsureDetailPanel(RectTransform host)
        {
            var parent = DetailPanelParent(host);
            if (parent == null)
            {
                return;
            }

            if (detailPanelObject != null && detailContent != null && detailPanelObject.transform.parent == parent)
            {
                return;
            }

            if (detailPanelObject != null)
            {
                detailPanelObject.SetActive(false);
                Destroy(detailPanelObject);
                detailPanelObject = null;
                detailContent = null;
            }

            RemoveNamedChild(parent, DetailPanelObjectName);
            RemoveDetailPanelFromKnownParents(host, parent);

            detailPanelObject = new GameObject(DetailPanelObjectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            detailPanelObject.transform.SetParent(parent, false);
            detailPanelObject.transform.SetAsLastSibling();
            detailPanelUserMoved = false;
            detailPanelDragging = false;

            var panelRect = detailPanelObject.GetComponent<RectTransform>();
            AlignDetailPanelToHost(panelRect, host, parent);

            var background = detailPanelObject.GetComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.86f);

            // Fixed header: acts as the drag handle and owns the close button.
            CreateDetailPanelHeader(panelRect);

            // Shape Info is intentionally non-scrollable. Its compact editor controls should
            // remain visible as one static panel instead of moving independently under the header.
            var contentObject = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentObject.transform.SetParent(detailPanelObject.transform, false);
            detailContent = contentObject.GetComponent<RectTransform>();
            detailContent.anchorMin = new Vector2(0f, 1f);
            detailContent.anchorMax = new Vector2(1f, 1f);
            detailContent.pivot = new Vector2(0.5f, 1f);
            detailContent.anchoredPosition = new Vector2(0f, -DetailPanelContentTopOffset);
            detailContent.sizeDelta = new Vector2(-32f, 0f);
            ConfigureContentLayout(contentObject, new RectOffset(6, 14, 4, 8));
            var detailLayout = contentObject.GetComponent<VerticalLayoutGroup>();
            if (detailLayout != null)
            {
                detailLayout.spacing = 4f;
            }
            AddPanelBorder(detailPanelObject.transform);
            detailPanelObject.SetActive(false);
        }

        private void CreateDetailPanelHeader(RectTransform panelRect)
        {
            var headerObject = new GameObject("Header", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(HorizontalLayoutGroup));
            headerObject.transform.SetParent(panelRect, false);
            detailHeaderRect = headerObject.GetComponent<RectTransform>();
            detailHeaderRect.anchorMin = new Vector2(0f, 1f);
            detailHeaderRect.anchorMax = new Vector2(1f, 1f);
            detailHeaderRect.pivot = new Vector2(0.5f, 1f);
            detailHeaderRect.sizeDelta = new Vector2(0f, 44f);
            detailHeaderRect.anchoredPosition = Vector2.zero;

            var headerImage = headerObject.GetComponent<Image>();
            headerImage.color = new Color(1f, 1f, 1f, 0.035f);
            headerImage.raycastTarget = true;

            var layout = headerObject.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(14, 8, 4, 4);
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = true;

            var title = AddSmallLabel(headerObject.transform, EuclidText.Get("panel.shapeInfo"), 0f);
            title.fontSize = 22f;
            title.fontStyle = FontStyles.Bold;
            title.alignment = TextAlignmentOptions.MidlineLeft;

            var closeText = AddButton(headerObject.transform, "×", CloseShapeDetailPanel, 34f, ButtonSurface.Outline);
            if (closeText != null)
            {
                closeText.fontSize = 25f;

                // The X is intentionally text-only. Keep the transparent Image alive so the
                // Button still has a raycast target, but remove its visible outline/background.
                var closeImage = FindButtonImage(closeText);
                if (closeImage != null)
                {
                    closeImage.color = Color.clear;
                    closeImage.sprite = null;
                    closeImage.overrideSprite = null;
                    var closeOutline = closeImage.GetComponent<Outline>();
                    if (closeOutline != null)
                    {
                        closeOutline.enabled = false;
                    }
                }
            }
        }

        private void CloseShapeDetailPanel()
        {
            // Closing Shape Info means "finish editing this shape", so list selection and any
            // pending endpoint pick are cleared together. The left tool panel remains open.
            ClearPointPick();
            ConstructionShapeTool.ClearSelection();
            RefreshShapeListSelectionButtons();
            RefreshShapeActionButtons();
            SetDetailPanelActive(false);
            RefreshTexts();
        }

        private void HandleDetailPanelDrag()
        {
            if (detailPanelObject == null || detailHeaderRect == null || !detailPanelObject.activeInHierarchy)
            {
                detailPanelDragging = false;
                return;
            }

            var panelRect = detailPanelObject.GetComponent<RectTransform>();
            var parentRect = panelRect != null ? panelRect.parent as RectTransform : null;
            if (panelRect == null || parentRect == null)
            {
                detailPanelDragging = false;
                return;
            }

            var canvas = detailPanelObject.GetComponentInParent<Canvas>();
            var eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            var mouse = (Vector2)Input.mousePosition;

            if (Input.GetMouseButtonDown(0)
                && RectTransformUtility.RectangleContainsScreenPoint(detailHeaderRect, mouse, eventCamera)
                && RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, mouse, eventCamera, out var startLocal))
            {
                detailPanelDragging = true;
                detailDragStartMouseLocal = startLocal;
                detailDragStartAnchoredPosition = panelRect.anchoredPosition;
                detailPanelObject.transform.SetAsLastSibling();
            }

            if (!Input.GetMouseButton(0))
            {
                detailPanelDragging = false;
                return;
            }

            if (!detailPanelDragging
                || !RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, mouse, eventCamera, out var currentLocal))
            {
                return;
            }

            panelRect.anchoredPosition = detailDragStartAnchoredPosition + (currentLocal - detailDragStartMouseLocal);
            detailPanelUserMoved = true;
        }

        private void AlignDetailPanelToHost(RectTransform panelRect, RectTransform host, Transform parent)
        {
            if (panelRect == null)
            {
                return;
            }

            var preserveUserPosition = detailPanelUserMoved
                && detailPanelObject != null
                && panelRect.gameObject == detailPanelObject;
            var userPosition = panelRect.anchoredPosition;

            var parentRect = parent as RectTransform;
            if (parentRect == null || host == null)
            {
                panelRect.anchorMin = new Vector2(1f, 0.08f);
                panelRect.anchorMax = new Vector2(1f, 0.92f);
                panelRect.pivot = new Vector2(1f, 0.5f);
                panelRect.sizeDelta = new Vector2(420f, 0f);
                panelRect.anchoredPosition = preserveUserPosition ? userPosition : Vector2.zero;
                return;
            }

            var corners = new Vector3[4];
            host.GetWorldCorners(corners);
            var bottomLeft = parentRect.InverseTransformPoint(corners[0]);
            var topRight = parentRect.InverseTransformPoint(corners[2]);
            var top = Mathf.Max(bottomLeft.y, topRight.y);
            var bottom = Mathf.Min(bottomLeft.y, topRight.y);
            var availableHeight = Mathf.Max(DetailPanelMinHeight, top - bottom);
            var height = Mathf.Min(availableHeight, PreferredDetailPanelHeight(availableHeight));
            var centerY = top - height * 0.5f;
            var anchorY = parentRect.rect.yMin + parentRect.rect.height * 0.5f;

            panelRect.anchorMin = new Vector2(1f, 0.5f);
            panelRect.anchorMax = new Vector2(1f, 0.5f);
            panelRect.pivot = new Vector2(1f, 0.5f);
            panelRect.sizeDelta = new Vector2(420f, height);
            panelRect.anchoredPosition = new Vector2(0f, centerY - anchorY);
            if (preserveUserPosition)
            {
                panelRect.anchoredPosition = userPosition;
            }

            if (!detailLayoutLogged
                && detailContent != null
                && detailContent.childCount > 0
                && detailPanelObject != null)
            {
                detailLayoutLogged = true;
                EuclidMod.Logger?.Log(
                    "Detail panel layout: owner=" + GetInstanceID()
                    + ", panel=" + detailPanelObject.GetInstanceID()
                    + ", parent=" + parent.name
                    + ", available=" + availableHeight.ToString("0.##", CultureInfo.InvariantCulture)
                    + ", preferred=" + PreferredDetailPanelHeight(availableHeight).ToString("0.##", CultureInfo.InvariantCulture)
                    + ", applied=" + height.ToString("0.##", CultureInfo.InvariantCulture));
            }
        }

        private float PreferredDetailPanelHeight(float maxHeight)
        {
            if (detailContent == null)
            {
                return Mathf.Min(maxHeight, DetailPanelMinHeight);
            }

            var contentHeight = MeasureDirectContentHeight(detailContent);

            var preferred = contentHeight + DetailPanelContentTopOffset + DetailPanelExtraBottomPadding;
            return Mathf.Clamp(preferred, DetailPanelMinHeight, maxHeight);
        }

        private static float MeasureDirectContentHeight(RectTransform target)
        {
            if (target == null)
            {
                return 0f;
            }

            var layout = target.GetComponent<VerticalLayoutGroup>();
            var height = layout != null ? layout.padding.top + layout.padding.bottom : 0f;
            var visibleChildren = 0;
            foreach (RectTransform child in target)
            {
                if (child == null || !child.gameObject.activeSelf)
                {
                    continue;
                }

                var element = child.GetComponent<LayoutElement>();
                var preferred = element != null && element.preferredHeight >= 0f
                    ? element.preferredHeight
                    : LayoutUtility.GetPreferredHeight(child);
                var minimum = element != null && element.minHeight >= 0f
                    ? element.minHeight
                    : 0f;
                height += Mathf.Max(0f, preferred, minimum);
                visibleChildren++;
            }

            if (layout != null && visibleChildren > 1)
            {
                height += layout.spacing * (visibleChildren - 1);
            }

            return height;
        }

        private void AddPanelBorder(Transform parent)
        {
            AddBorderLine(parent, "Border Top", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), 0f, 0f, 0f, 2.4f);
            AddBorderLine(parent, "Border Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), 0f, 0f, 0f, 2.4f);
            AddBorderLine(parent, "Border Left", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), 0f, 0f, 2.4f, 0f);
            AddBorderLine(parent, "Border Right", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), 0f, 0f, 2.4f, 0f);
        }

        private static void AddBorderLine(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            float x,
            float y,
            float width,
            float height)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            obj.transform.SetParent(parent, false);
            var rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
            var image = obj.GetComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.72f);
            image.raycastTarget = false;
        }

        private Transform DetailPanelParent(RectTransform host)
        {
            detailOwner = FindRightInspectorPanel();
            var canvas = host != null ? host.GetComponentInParent<Canvas>() : null;
            if (canvas == null && owner != null)
            {
                canvas = owner.GetComponentInParent<Canvas>();
            }

            if (canvas == null && detailOwner != null)
            {
                canvas = detailOwner.GetComponentInParent<Canvas>();
            }

            if (canvas != null)
            {
                return canvas.transform;
            }

            if (owner != null)
            {
                return owner.transform.root;
            }

            if (host != null)
            {
                return host.root;
            }

            return transform;
        }

        private void RemoveDetailPanelFromKnownParents(RectTransform host, Transform desiredParent)
        {
            var ownerCanvas = owner != null ? owner.GetComponentInParent<Canvas>() : null;
            var hostCanvas = host != null ? host.GetComponentInParent<Canvas>() : null;
            var rightInspector = FindRightInspectorPanel();
            var ownerPanels = GameCompat.GetInspectorPanels(owner);
            var rightPanels = GameCompat.GetInspectorPanels(rightInspector);
            RemoveNamedChildExcept(ownerPanels != null ? ownerPanels.parent : null, DetailPanelObjectName, desiredParent);
            RemoveNamedChildExcept(rightPanels, DetailPanelObjectName, desiredParent);
            RemoveNamedChildExcept(ownerCanvas != null ? ownerCanvas.transform : null, DetailPanelObjectName, desiredParent);
            RemoveNamedChildExcept(host?.parent, DetailPanelObjectName, desiredParent);
            RemoveNamedChildExcept(hostCanvas != null ? hostCanvas.transform : null, DetailPanelObjectName, desiredParent);
        }

        private InspectorPanel FindRightInspectorPanel()
        {
            var editor = scnEditor.instance;
            if (editor == null)
            {
                return null;
            }

            var details = GetInspectorPanelField(editor, "detailsPanel");
            if (details != null && details != owner)
            {
                return details;
            }

            var inspector = GetInspectorPanelField(editor, "inspectorPanel");
            if (inspector != null && inspector != owner)
            {
                return inspector;
            }

            return null;
        }

        private static InspectorPanel GetInspectorPanelField(object target, string fieldName)
        {
            return GameCompat.TryGetMember(target, fieldName, out InspectorPanel panel) ? panel : null;
        }

        private void ConfigureContentLayout(GameObject contentObject, RectOffset padding)
        {
            if (contentObject == null)
            {
                return;
            }

            var layout = contentObject.GetComponent<VerticalLayoutGroup>() ?? contentObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = padding;
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            var fitter = contentObject.GetComponent<ContentSizeFitter>() ?? contentObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private void ConfigureRootContentLayout(GameObject contentObject, RectOffset padding)
        {
            if (contentObject == null)
            {
                return;
            }

            var rect = contentObject.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = Vector2.zero;
            }

            var layout = contentObject.GetComponent<VerticalLayoutGroup>() ?? contentObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = padding;
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            var fitter = contentObject.GetComponent<ContentSizeFitter>();
            if (fitter != null)
            {
                fitter.enabled = false;
            }
        }

        private void BuildPanelContent()
        {
            showGuideText = null;
            snapDragText = null;
            dragCameraText = null;
            selectedLineText = null;
            perpendicularText = null;
            anchorX = null;
            anchorY = null;
            directionX = null;
            directionY = null;
            circleCenterX = null;
            circleCenterY = null;
            circleRadius = null;
            keyField = null;
            stepField = null;
            targetText = null;
            guideStatusText = null;
            measureText = null;
            shapeFirstPickText = null;
            shapeFirstSourceText = null;
            shapeFirstX = null;
            shapeFirstY = null;
            shapeSecondPickText = null;
            shapeSecondSourceText = null;
            shapeSecondX = null;
            shapeSecondY = null;
            shapeSnapText = null;
            shapeIntersectionsText = null;
            shapeListTexts.Clear();
            shapeVisibilityTexts.Clear();
            ClearInvalidPointPick();

            ClearInitialDefaultShapeOnce();
            NormalizeConstructionShapes();
            BuildShapeListContent();
            BuildDetailPanelContent();
        }

    }
}
