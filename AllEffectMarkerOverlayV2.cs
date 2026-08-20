using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using ADOFAI;
using UnityEngine;
using UnityEngine.UI;

namespace Euclid
{
    // Selection-independent all-effect overlay.
    //
    // The previous implementation re-resolved an InspectorPanel every update and only rebuilt its
    // mesh every 0.1 s. That made the layer disappear when the inspector stopped exposing a panel,
    // and made camera/floor motion visibly update at 10 Hz. This version caches a persistent editor
    // Canvas once it is found, rebuilds the lightweight marker model every frame, and lets the
    // existing Graphic re-project world positions every frame as well.
    internal sealed class AllEffectMarkerOverlayV2 : MonoBehaviour
    {
        private const string RootName = "Euclid_AllEffectMarkersV2";

        private readonly List<EffectOverlayVisual> visuals = new List<EffectOverlayVisual>();

        private GameObject rootObject;
        private Canvas hostCanvas;
        private Canvas layerCanvas;
        private AllEffectMarkerGraphic graphic;

        internal static void Install()
        {
            var behaviour = EuclidMod.Behaviour;
            if (behaviour == null || behaviour.GetComponent<AllEffectMarkerOverlayV2>() != null)
            {
                return;
            }

            behaviour.gameObject.AddComponent<AllEffectMarkerOverlayV2>();
        }

        private void Update()
        {
            if (!EuclidMod.Enabled || !EuclidMod.ShowOverlay || !AllEffectMarkerSettings.Enabled)
            {
                SetVisible(false);
                return;
            }

            var editor = scnEditor.instance;
            if (editor == null || GameCompat.IsEditorPlaying(editor))
            {
                SetVisible(false);
                return;
            }

            if (hostCanvas == null)
            {
                hostCanvas = ResolvePersistentEditorCanvas(editor);
            }

            if (hostCanvas == null)
            {
                SetVisible(false);
                return;
            }

            EnsureLayer(hostCanvas);
            SetVisible(true);

            CollectAllUnfocusedVisuals(editor, visuals);
            graphic.SetSource(visuals);

            // Re-project every frame. This is cheap compared with applying PositionTrack to the
            // floor hierarchy and removes the old 0.1-second stepping when the camera or tiles move.
            graphic.SetVerticesDirty();
        }

        private void OnDisable()
        {
            SetVisible(false);
        }

        private void OnDestroy()
        {
            DestroyLayer();
        }

        private void EnsureLayer(Canvas canvas)
        {
            if (rootObject != null && layerCanvas != null && graphic != null)
            {
                UpdateSorting();
                return;
            }

            DestroyLayer();
            hostCanvas = canvas;

            rootObject = new GameObject(RootName, typeof(RectTransform), typeof(Canvas));
            rootObject.transform.SetParent(hostCanvas.transform, false);
            Stretch(rootObject.GetComponent<RectTransform>());

            layerCanvas = rootObject.GetComponent<Canvas>();
            layerCanvas.overrideSorting = true;
            layerCanvas.additionalShaderChannels = hostCanvas.additionalShaderChannels;
            UpdateSorting();

            var geometryObject = new GameObject(
                "Geometry",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(AllEffectMarkerGraphic));
            geometryObject.transform.SetParent(rootObject.transform, false);
            Stretch(geometryObject.GetComponent<RectTransform>());

            graphic = geometryObject.GetComponent<AllEffectMarkerGraphic>();
            graphic.raycastTarget = false;
            graphic.color = Color.white;
            graphic.SetSource(visuals);

            rootObject.SetActive(true);
        }

        private static Canvas ResolvePersistentEditorCanvas(scnEditor editor)
        {
            // Prefer the same persistent settings/editor canvas Euclid already uses. Once cached,
            // this reference survives event deselection even if the inspector lookup later returns null.
            try
            {
                var settingsPanel = GameCompat.GetSettingsPanel(editor);
                var canvas = settingsPanel != null ? settingsPanel.GetComponentInParent<Canvas>() : null;
                if (canvas != null)
                {
                    return canvas;
                }
            }
            catch (Exception)
            {
                // Fall through to already-created Euclid/editor canvases.
            }

            try
            {
                var canvases = Resources.FindObjectsOfTypeAll<Canvas>();

                // ConstructionShapeCanvasOverlay is already attached to the correct editor canvas.
                // Recover its parent canvas without depending on any currently selected event.
                for (var i = 0; i < canvases.Length; i++)
                {
                    var candidate = canvases[i];
                    if (candidate == null ||
                        !string.Equals(candidate.gameObject.name, "Euclid_ConstructionShapeOverlay", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var parent = candidate.transform.parent;
                    var host = parent != null ? parent.GetComponentInParent<Canvas>() : null;
                    if (host != null)
                    {
                        return host;
                    }
                }

                // Last-resort editor UI canvas. Restrict this to scene-valid InspectorPanels so a
                // prefab/resource canvas cannot accidentally become the overlay host.
                var panels = Resources.FindObjectsOfTypeAll<InspectorPanel>();
                for (var i = 0; i < panels.Length; i++)
                {
                    var panel = panels[i];
                    if (panel == null || !panel.gameObject.scene.IsValid())
                    {
                        continue;
                    }

                    var canvas = panel.GetComponentInParent<Canvas>();
                    if (canvas != null)
                    {
                        return canvas;
                    }
                }
            }
            catch (Exception)
            {
                // Retry next frame while the editor UI is rebuilding.
            }

            return null;
        }

        private void UpdateSorting()
        {
            if (layerCanvas == null || hostCanvas == null)
            {
                return;
            }

            var sortingReference = hostCanvas.overrideSorting ? hostCanvas : hostCanvas.rootCanvas;
            if (sortingReference == null)
            {
                sortingReference = hostCanvas;
            }

            layerCanvas.overrideSorting = true;
            layerCanvas.sortingLayerID = sortingReference.sortingLayerID;
            // Selected/construction markers use host - 1. Keep unfocused markers below them.
            layerCanvas.sortingOrder = sortingReference.sortingOrder - 2;
        }

        private void SetVisible(bool visible)
        {
            if (rootObject != null && rootObject.activeSelf != visible)
            {
                rootObject.SetActive(visible);
            }
        }

        private void DestroyLayer()
        {
            if (rootObject != null)
            {
                Destroy(rootObject);
            }

            rootObject = null;
            layerCanvas = null;
            graphic = null;
            visuals.Clear();
        }

        private static void Stretch(RectTransform rect)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            rect.localScale = Vector3.one;
        }

        private static void CollectAllUnfocusedVisuals(scnEditor editor, List<EffectOverlayVisual> output)
        {
            output.Clear();
            if (editor == null)
            {
                return;
            }

            var panel = GameCompat.GetLevelEventsPanel(editor);
            var selectedEvent = GameCompat.GetSelectedEvent(panel);

            // selectedEvent can remain populated after the user visually deselects the effect.
            // Exclude it only when the normal foreground overlay can actually draw it.
            LevelEvent foregroundEvent = null;
            var cameraFrame = EuclidMod.Behaviour != null
                ? EuclidMod.Behaviour.CameraFrame
                : CameraFrameSnapshot.Unavailable(string.Empty);
            if (cameraFrame.State == CameraFrameState.Ready)
            {
                foregroundEvent = cameraFrame.SelectedEvent;
            }
            else if (CoordinateSnapTool.TryGetFocusedEffectVisual(out _))
            {
                foregroundEvent = selectedEvent;
            }

            AppendMoveCameraVisuals(editor, foregroundEvent, output);

            foreach (var ev in GameCompat.GetEditorEvents(editor))
            {
                if (ev == null || ev.eventType == LevelEventType.MoveCamera || ReferenceEquals(ev, foregroundEvent))
                {
                    continue;
                }

                if (TryBuildPositionOffsetVisual(editor, ev, out var visual))
                {
                    output.Add(visual);
                }
            }
        }

        private static bool TryBuildPositionOffsetVisual(
            scnEditor editor,
            LevelEvent ev,
            out EffectOverlayVisual visual)
        {
            visual = default;
            if (editor == null || ev == null || !TryGetVector2(ev, "positionOffset", out var offsetTiles))
            {
                return false;
            }

            var eventName = ev.eventType.ToString();
            EffectOverlayKind kind;
            switch (eventName)
            {
                case "MoveTrack":
                case "MoveDecorations":
                    kind = EffectOverlayKind.TrackMove;
                    break;
                case "PositionTrack":
                    kind = EffectOverlayKind.TrackPosition;
                    break;
                case "FreeRoam":
                case "FreeRoamRemove":
                    kind = EffectOverlayKind.FreeRoam;
                    break;
                default:
                    return false;
            }

            var tileSize = Mathf.Max(GameCompat.GetTileSize(), 0.000001f);
            Vector2 referenceWorld;
            Vector2 targetWorld;

            if (eventName == "PositionTrack")
            {
                var relativeTo = GetTileRelativeTo(ev);
                var referenceFloor = ev.floor;
                switch (relativeTo)
                {
                    case "Start":
                    case "FirstTile":
                        referenceFloor = 0;
                        break;
                    case "End":
                    case "LastTile":
                        referenceFloor = GetLastFloorIndex(editor);
                        break;
                }

                var displayedFloorWorld = GetFloorPosition(editor, referenceFloor);
                if (string.Equals(relativeTo, "ThisTile", StringComparison.OrdinalIgnoreCase))
                {
                    // Unfocused PositionTrack values are already applied to the displayed floor.
                    targetWorld = displayedFloorWorld;
                    referenceWorld = displayedFloorWorld - offsetTiles * tileSize;
                }
                else
                {
                    referenceWorld = displayedFloorWorld;
                    targetWorld = referenceWorld + offsetTiles * tileSize;
                }
            }
            else
            {
                referenceWorld = GetFloorPosition(editor, ev.floor);
                targetWorld = referenceWorld + offsetTiles * tileSize;
            }

            visual = new EffectOverlayVisual(kind, referenceWorld, targetWorld, eventName);
            return true;
        }

        private static void AppendMoveCameraVisuals(
            scnEditor editor,
            LevelEvent foregroundEvent,
            List<EffectOverlayVisual> output)
        {
            var timeline = new List<CameraTimelineItem>();
            var index = 0;
            foreach (var ev in GameCompat.GetEditorEvents(editor))
            {
                if (ev != null && ev.eventType == LevelEventType.MoveCamera)
                {
                    timeline.Add(new CameraTimelineItem(ev, index, GetEventStartTime(editor, ev)));
                }
                index++;
            }

            timeline.Sort(CameraTimelineItem.Compare);
            var tileSize = Mathf.Max(GameCompat.GetTileSize(), 0.000001f);
            var state = CameraMarkerState.FromLevelSettings(editor, tileSize);

            for (var i = 0; i < timeline.Count; i++)
            {
                var item = timeline[i];
                state = ApplyMoveCamera(editor, state, item.Event, tileSize);
                if (ReferenceEquals(item.Event, foregroundEvent))
                {
                    continue;
                }

                output.Add(new EffectOverlayVisual(
                    EffectOverlayKind.CameraMove,
                    state.ReferencePoint,
                    state.Center,
                    EuclidText.Get("effect.moveCamera")));
            }
        }

        private static CameraMarkerState ApplyMoveCamera(
            scnEditor editor,
            CameraMarkerState previous,
            LevelEvent ev,
            float tileSize)
        {
            var relativeTo = IsPropertyUsed(ev, "relativeTo")
                ? GetCameraRelativeTo(ev, previous.RelativeTo)
                : previous.RelativeTo;
            var positionUsed = IsPropertyUsed(ev, "position");
            var offsetTiles = positionUsed && TryGetVector2(ev, "position", out var position)
                ? position
                : previous.OffsetTiles;
            var referencePoint = ResolveCameraReference(editor, previous.Center, ev.floor, relativeTo);
            var center = positionUsed ? referencePoint + offsetTiles * tileSize : previous.Center;
            return new CameraMarkerState(center, relativeTo, offsetTiles, referencePoint);
        }

        private static Vector2 ResolveCameraReference(
            scnEditor editor,
            Vector2 previousCenter,
            int floor,
            CamMovementType relativeTo)
        {
            switch (relativeTo)
            {
                case CamMovementType.Global:
                    return Vector2.zero;
                case CamMovementType.LastPosition:
                case CamMovementType.LastPositionNoRotation:
                    return previousCenter;
                case CamMovementType.Player:
                case CamMovementType.Tile:
                default:
                    return GetFloorPosition(editor, floor);
            }
        }

        private static CamMovementType GetCameraRelativeTo(LevelEvent ev, CamMovementType fallback)
        {
            if (LevelEventCompat.TryGetRaw(ev, "relativeTo", out var raw))
            {
                if (raw is CamMovementType movementType)
                {
                    return movementType;
                }

                if (raw is int index)
                {
                    return (CamMovementType)index;
                }

                var text = raw?.ToString();
                if (!string.IsNullOrWhiteSpace(text) && Enum.TryParse(text.Trim(), true, out CamMovementType parsed))
                {
                    return parsed;
                }
            }

            return fallback;
        }

        private static string GetTileRelativeTo(LevelEvent ev)
        {
            if (LevelEventCompat.TryGetRaw(ev, "relativeTo", out var raw))
            {
                if (raw is int index)
                {
                    switch (index)
                    {
                        case 1: return "Start";
                        case 2: return "End";
                        default: return "ThisTile";
                    }
                }

                var text = raw?.ToString();
                return string.IsNullOrWhiteSpace(text) ? "ThisTile" : text.Trim();
            }

            try
            {
                return ev.Get<TileRelativeTo>("relativeTo").ToString();
            }
            catch (Exception)
            {
                return "ThisTile";
            }
        }

        private static bool TryGetVector2(LevelEvent ev, string key, out Vector2 value)
        {
            value = Vector2.zero;
            if (ev == null)
            {
                return false;
            }

            if (LevelEventCompat.TryGetRaw(ev, key, out var raw) && TryConvertVector2(raw, out value))
            {
                return true;
            }

            try
            {
                value = Sanitize(ev.Get<Vector2>(key));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool TryConvertVector2(object raw, out Vector2 value)
        {
            switch (raw)
            {
                case Vector2 vector:
                    value = Sanitize(vector);
                    return true;
                case Tuple<float, float> pair:
                    value = Sanitize(new Vector2(pair.Item1, pair.Item2));
                    return true;
                case IList list when list.Count >= 2 &&
                                         TryConvertSingle(list[0], out var x) &&
                                         TryConvertSingle(list[1], out var y):
                    value = Sanitize(new Vector2(x, y));
                    return true;
                default:
                    value = Vector2.zero;
                    return false;
            }
        }

        private static bool TryConvertSingle(object raw, out float value)
        {
            if (raw == null)
            {
                value = 0f;
                return true;
            }

            try
            {
                value = Sanitize(Convert.ToSingle(raw, CultureInfo.InvariantCulture));
                return true;
            }
            catch (Exception)
            {
                value = 0f;
                return false;
            }
        }

        private static Vector2 Sanitize(Vector2 value)
        {
            return new Vector2(Sanitize(value.x), Sanitize(value.y));
        }

        private static float Sanitize(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }

        private static bool IsPropertyUsed(LevelEvent ev, string key)
        {
            return ev.disabled == null || !ev.disabled.TryGetValue(key, out var disabled) || !disabled;
        }

        private static int GetLastFloorIndex(scnEditor editor)
        {
            var floors = GameCompat.GetFloors(editor);
            return floors.Count > 0 ? floors.Count - 1 : 0;
        }

        private static scrFloor GetFloor(scnEditor editor, int floor)
        {
            var floors = GameCompat.GetFloors(editor);
            for (var i = 0; i < floors.Count; i++)
            {
                var candidate = floors[i];
                if (candidate != null && candidate.seqID == floor)
                {
                    return candidate;
                }
            }

            return floor >= 0 && floor < floors.Count ? floors[floor] : null;
        }

        private static Vector2 GetFloorPosition(scnEditor editor, int floor)
        {
            try
            {
                var candidate = GetFloor(editor, floor);
                if (candidate != null)
                {
                    var position = candidate.transform.position;
                    return new Vector2(position.x, position.y);
                }
            }
            catch (Exception)
            {
                // One-frame floor-list rebuild; the next Update will retry.
            }

            return Vector2.zero;
        }

        private static double GetEventStartTime(scnEditor editor, LevelEvent ev)
        {
            var floor = GetFloor(editor, ev.floor);
            var angleOffset = SafeGetFloat(ev, "angleOffset", 0f);
            if (floor == null)
            {
                return ev.floor + angleOffset / 180d;
            }

            var bpm = 100d;
            try
            {
                if (GameCompat.TryGetLevelSetting(editor, "bpm", out double levelBpm))
                {
                    bpm = levelBpm;
                }
            }
            catch (Exception)
            {
                // Keep fallback BPM.
            }

            var speed = Math.Abs(floor.speed) > 0.0001f ? floor.speed : 1f;
            return floor.entryTime + angleOffset / 180d * 60d / (bpm * speed);
        }

        private static float SafeGetFloat(LevelEvent ev, string key, float fallback)
        {
            try
            {
                return ev.GetFloat(key);
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        private readonly struct CameraMarkerState
        {
            internal CameraMarkerState(
                Vector2 center,
                CamMovementType relativeTo,
                Vector2 offsetTiles,
                Vector2 referencePoint)
            {
                Center = center;
                RelativeTo = relativeTo;
                OffsetTiles = offsetTiles;
                ReferencePoint = referencePoint;
            }

            internal Vector2 Center { get; }
            internal CamMovementType RelativeTo { get; }
            internal Vector2 OffsetTiles { get; }
            internal Vector2 ReferencePoint { get; }

            internal static CameraMarkerState FromLevelSettings(scnEditor editor, float tileSize)
            {
                var relativeTo = CamMovementType.Tile;
                var offsetTiles = Vector2.zero;

                try
                {
                    if (GameCompat.TryGetLevelSetting(editor, "camRelativeTo", out CamMovementType levelRelativeTo))
                    {
                        relativeTo = levelRelativeTo;
                    }
                    if (GameCompat.TryGetLevelSetting(editor, "camPosition", out Vector2 levelPosition))
                    {
                        offsetTiles = Sanitize(levelPosition);
                    }
                }
                catch (Exception)
                {
                    // Defaults remain valid until settings are available.
                }

                var referencePoint = ResolveCameraReference(editor, Vector2.zero, 0, relativeTo);
                return new CameraMarkerState(
                    referencePoint + offsetTiles * tileSize,
                    relativeTo,
                    offsetTiles,
                    referencePoint);
            }
        }

        private readonly struct CameraTimelineItem
        {
            internal CameraTimelineItem(LevelEvent ev, int index, double startTime)
            {
                Event = ev;
                Index = index;
                StartTime = startTime;
            }

            internal LevelEvent Event { get; }
            private int Index { get; }
            private double StartTime { get; }

            internal static int Compare(CameraTimelineItem left, CameraTimelineItem right)
            {
                var timeCompare = left.StartTime.CompareTo(right.StartTime);
                return timeCompare != 0 ? timeCompare : left.Index.CompareTo(right.Index);
            }
        }
    }
}
