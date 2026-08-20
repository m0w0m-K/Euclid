using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ADOFAI;
using UnityEngine;

namespace Euclid
{
    // Bridges geometry produced by GuideLineTool/ConstructionShapeTool to editable ADOFAI data.
    // This is the main place to inspect when snapping changes the wrong event property or coordinate.
    // Event/member compatibility should still be delegated to GameCompat/LevelEventCompat.
    internal static class CoordinateSnapTool
    {
        private const double CachedPointToleranceSqr = 0.001d * 0.001d;
        private static bool hasCachedGuideParameter;
        private static int cachedGuideRevision;
        private static string cachedTargetKey;
        private static double cachedGuideParameter;

        internal static string DescribeTarget(CameraFrameSnapshot cameraFrame, string requestedKey)
        {
            if (TryGetCameraTarget(cameraFrame, out _))
            {
                return EuclidText.Get("target.camera");
            }

            var ev = GetFocusedEvent();
            if (ev == null)
            {
                return EuclidText.Get("target.none");
            }

            if (TryGetPositionOffsetTarget(ev, out var positionTrackTarget))
            {
                return EuclidText.Format("target.label", positionTrackTarget.Label);
            }

            var keys = GetVectorKeys(ev);
            if (keys.Count == 0)
            {
                return EuclidText.Format("target.noVector", ev.eventType);
            }

            var key = ResolveKey(ev, requestedKey);
            return EuclidText.Format("target.property", ev.eventType, key);
        }

        internal static string SuggestKey(CameraFrameSnapshot cameraFrame)
        {
            if (TryGetCameraTarget(cameraFrame, out _))
            {
                return "position";
            }

            var ev = GetFocusedEvent();
            if (IsPositionOffsetEvent(ev))
            {
                return "positionOffset";
            }

            return ev == null ? "position" : ResolveKey(ev, "position") ?? "position";
        }

        internal static bool TrySnapToGuide(CameraFrameSnapshot cameraFrame, GuideLineSnapshot guideLine, string requestedKey, out string message)
        {
            return TryMoveAlongGuide(cameraFrame, guideLine, requestedKey, 0f, out message);
        }

        internal static bool TrySnapToPoint(CameraFrameSnapshot cameraFrame, Vector2d point, string requestedKey, out string message)
        {
            ClearGuideParameterCache();
            if (!TryCaptureTarget(cameraFrame, requestedKey, out var target, out message))
            {
                return false;
            }

            if (!target.TrySetWorldPoint(point, saveUndoState: true))
            {
                message = EuclidText.Get("message.updateFailed");
                return false;
            }

            message = EuclidText.Format("message.snappedIntersection", target.Label);
            return true;
        }

        internal static bool TryGetFocusedPositionOffsetPoint(out Vector2 point, out string label)
        {
            point = Vector2.zero;
            label = string.Empty;
            var ev = GetFocusedEvent();
            if (!TryGetPositionOffsetTarget(ev, out var target))
            {
                return false;
            }

            point = target.WorldPoint;
            label = target.Label;
            return true;
        }

        internal static bool TryGetFocusedEffectVisual(out EffectOverlayVisual visual)
        {
            visual = default;
            var ev = GetFocusedEvent();
            if (!TryGetPositionOffsetTarget(ev, out var target))
            {
                return false;
            }

            var eventName = ev?.eventType.ToString() ?? string.Empty;
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
                    // Free Roam removal uses the same visual palette as the Free Roam area.
                    // The label still distinguishes the two operations.
                    kind = EffectOverlayKind.FreeRoam;
                    break;
                default:
                    return false;
            }

            // Use the exact same origin as the editable coordinate target. PositionTrack needs
            // a pre-event origin rather than the already-repositioned floor transform; keeping
            // both paths tied to CoordinateTarget prevents the marker and written offset from
            // drifting apart.
            visual = new EffectOverlayVisual(kind, target.ReferenceWorld, target.WorldPoint, GetEffectDisplayName(ev));
            return true;
        }

        internal static bool TryMoveFocusedPositionOffsetToWorld(Vector2 world, bool saveUndoState, out string message)
        {
            var ev = GetFocusedEvent();
            if (!TryGetPositionOffsetTarget(ev, out var target))
            {
                message = EuclidText.Get("message.selectPositionOffsetEvent");
                return false;
            }

            if (!target.TrySetWorldPoint(new Vector2d(world), saveUndoState))
            {
                message = EuclidText.Get("message.updateFailed");
                return false;
            }

            message = EuclidText.Format("message.movedToShape", target.Label);
            return true;
        }

        internal static bool TryGetPositionOffsetSnapPreview(out Vector2 from, out Vector2 to)
        {
            from = Vector2.zero;
            to = Vector2.zero;
            if (!GuideLineTool.SnapSelectedShapeDrag)
            {
                return false;
            }

            var ev = GetFocusedEvent();
            if (!TryGetPositionOffsetTarget(ev, out var target))
            {
                return false;
            }

            if (!ConstructionShapeTool.TryGetSnapPointForSingleSelectedShape(target.WorldPointD, out var point))
            {
                return false;
            }

            from = target.WorldPoint;
            to = point.ToVector2();
            return true;
        }

        internal static bool CanSnapSelectedTargetToSelectedShape(CameraFrameSnapshot cameraFrame, string requestedKey)
        {
            return TryCaptureSelectedShapeSnapTarget(cameraFrame, requestedKey, out _, out _, out _);
        }

        internal static bool TrySnapSelectedTargetToSelectedShape(
            CameraFrameSnapshot cameraFrame,
            string requestedKey,
            out string message)
        {
            ClearGuideParameterCache();
            if (!TryCaptureSelectedShapeSnapTarget(cameraFrame, requestedKey, out var target, out var point, out message))
            {
                return false;
            }

            if (!target.TrySetWorldPoint(point, saveUndoState: true))
            {
                message = EuclidText.Get("message.updateFailed");
                return false;
            }

            message = EuclidText.Format("message.snappedToShape", target.Label);
            return true;
        }

        private static bool TryCaptureSelectedShapeSnapTarget(
            CameraFrameSnapshot cameraFrame,
            string requestedKey,
            out CoordinateTarget target,
            out Vector2d point,
            out string message)
        {
            target = default;
            point = Vector2d.Zero;
            if (!ConstructionShapeTool.CanSnapToSingleSelectedShape())
            {
                message = EuclidText.Get("message.selectSingleShape");
                return false;
            }

            if (!TryCaptureTarget(cameraFrame, requestedKey, out target, out message))
            {
                return false;
            }

            if (!ConstructionShapeTool.TryGetSnapPointForSingleSelectedShape(target.WorldPointD, out point))
            {
                message = EuclidText.Get("message.selectSingleShape");
                return false;
            }

            message = string.Empty;
            return true;
        }

        internal static bool TryMoveAlongGuide(CameraFrameSnapshot cameraFrame, GuideLineSnapshot guideLine, string requestedKey, double distance, out string message)
        {
            if (!guideLine.IsValid)
            {
                ClearGuideParameterCache();
                message = EuclidText.Get("message.guideInactive");
                return false;
            }

            if (!TryCaptureTarget(cameraFrame, requestedKey, out var target, out message))
            {
                ClearGuideParameterCache();
                return false;
            }

            var parameter = ResolveGuideParameter(target, guideLine, distance);
            if (Math.Abs(distance) > double.Epsilon)
            {
                parameter += distance / guideLine.DirectionLength;
            }

            var moved = guideLine.PointAt(parameter);
            if (!target.TrySetWorldPoint(moved, saveUndoState: true))
            {
                ClearGuideParameterCache();
                message = EuclidText.Get("message.updateFailed");
                return false;
            }

            RememberGuideParameter(target, guideLine, parameter);
            message = distance == 0f
                ? EuclidText.Format("message.snapped", target.Label)
                : EuclidText.Format("message.moved", target.Label, distance);
            return true;
        }

        private static double ResolveGuideParameter(CoordinateTarget target, GuideLineSnapshot guideLine, double distance)
        {
            if (Math.Abs(distance) <= double.Epsilon ||
                !hasCachedGuideParameter ||
                cachedGuideRevision != guideLine.Revision ||
                !string.Equals(cachedTargetKey, target.CacheKey, StringComparison.Ordinal))
            {
                return guideLine.ParameterOf(target.WorldPoint);
            }

            var cachedPoint = guideLine.PointAt(cachedGuideParameter);
            if ((cachedPoint - target.WorldPointD).SqrMagnitude > CachedPointToleranceSqr)
            {
                return guideLine.ParameterOf(target.WorldPoint);
            }

            return cachedGuideParameter;
        }

        private static void RememberGuideParameter(CoordinateTarget target, GuideLineSnapshot guideLine, double parameter)
        {
            hasCachedGuideParameter = true;
            cachedGuideRevision = guideLine.Revision;
            cachedTargetKey = target.CacheKey;
            cachedGuideParameter = parameter;
        }

        private static void ClearGuideParameterCache()
        {
            hasCachedGuideParameter = false;
            cachedGuideRevision = 0;
            cachedTargetKey = null;
            cachedGuideParameter = 0d;
        }

        private static bool TryCaptureTarget(CameraFrameSnapshot cameraFrame, string requestedKey, out CoordinateTarget target, out string message)
        {
            if (TryGetCameraTarget(cameraFrame, out target))
            {
                message = string.Empty;
                return true;
            }

            var ev = GetFocusedEvent();
            if (ev == null)
            {
                target = default;
                message = EuclidText.Get("message.selectCoordinateEvent");
                return false;
            }

            if (TryGetPositionOffsetTarget(ev, out target))
            {
                message = string.Empty;
                return true;
            }

            var key = ResolveKey(ev, requestedKey);
            if (string.IsNullOrWhiteSpace(key))
            {
                target = default;
                message = EuclidText.Format("message.noVectorCoordinate", ev.eventType);
                return false;
            }

            if (!TryGetVector2(ev, key, out var value))
            {
                value = GetDefaultVector2(ev, key);
            }

            target = CoordinateTarget.ForTileUnitEventProperty(ev, key, value, GetTileSize());
            message = string.Empty;
            return true;
        }

        private static bool TryGetCameraTarget(CameraFrameSnapshot cameraFrame, out CoordinateTarget target)
        {
            if (cameraFrame.State == CameraFrameState.Ready &&
                cameraFrame.SelectedEvent != null &&
                cameraFrame.SelectedEvent.eventType == LevelEventType.MoveCamera)
            {
                target = CoordinateTarget.ForCamera(cameraFrame);
                return true;
            }

            target = default;
            return false;
        }

        private static LevelEvent GetFocusedEvent()
        {
            try
            {
                var editor = scnEditor.instance;
                var panel = GameCompat.GetLevelEventsPanel(editor);
                if (editor == null || panel == null)
                {
                    return null;
                }

                var ev = GameCompat.GetSelectedEvent(panel);
                if (ev == null)
                {
                    return null;
                }

                // ADOFAI can leave levelEventsPanel.selectedEvent pointing at the object that was
                // just deleted. Reject that stale reference so PositionTrack/MoveTrack markers
                // disappear as soon as the event is removed instead of lingering in the viewport.
                if (GameCompat.TryGetSelectedEventType(panel, out var selectedType) &&
                    selectedType != ev.eventType)
                {
                    return null;
                }

                return CurrentEditorContainsEvent(editor, ev) ? ev : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool CurrentEditorContainsEvent(scnEditor editor, LevelEvent ev)
        {
            if (editor == null || ev == null)
            {
                return false;
            }

            try
            {
                // editor.events is the authoritative collection used by the editor timeline.
                // Do not fall back to the panel's selected-floor collection here: that collection
                // can remain stale for the same deletion frame as panel.selectedEvent.
                foreach (var current in GameCompat.GetEditorEvents(editor))
                {
                    if (ReferenceEquals(current, ev))
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                // Treat uncertain state as not focused. A temporary hidden marker is safer than
                // leaving a deleted event interactive in the viewport.
            }

            return false;
        }

        private static string ResolveKey(LevelEvent ev, string requestedKey)
        {
            var keys = GetVectorKeys(ev);
            if (keys.Count == 0)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(requestedKey))
            {
                var exact = keys.FirstOrDefault(key => string.Equals(key, requestedKey.Trim(), StringComparison.OrdinalIgnoreCase));
                if (exact != null)
                {
                    return exact;
                }
            }

            return keys.FirstOrDefault(key => string.Equals(key, "position", StringComparison.OrdinalIgnoreCase)) ?? keys[0];
        }

        private static List<string> GetVectorKeys(LevelEvent ev)
        {
            var keys = new List<string>();
            if (IsPositionOffsetEvent(ev))
            {
                keys.Add("positionOffset");
            }

            if (ev?.info?.propertiesInfo != null)
            {
                foreach (var pair in ev.info.propertiesInfo)
                {
                    if (pair.Value != null && pair.Value.type == PropertyType.Vector2 && !keys.Contains(pair.Key))
                    {
                        keys.Add(pair.Key);
                    }
                }
            }

            if (ev != null)
            {
                foreach (var pair in LevelEventCompat.EnumerateRaw(ev))
                {
                    if (!keys.Contains(pair.Key) && TryConvertVector2(pair.Value, out _))
                    {
                        keys.Add(pair.Key);
                    }
                }
            }

            keys.Sort(StringComparer.OrdinalIgnoreCase);
            return keys;
        }

        private static bool TryGetVector2(LevelEvent ev, string key, out Vector2 value)
        {
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
                value = Vector2.zero;
                return false;
            }
        }

        private static Vector2 GetDefaultVector2(LevelEvent ev, string key)
        {
            try
            {
                if (ev.info?.propertiesInfo != null &&
                    ev.info.propertiesInfo.TryGetValue(key, out var info) &&
                    info.value_default != null &&
                    TryConvertVector2(info.value_default, out var value))
                {
                    return value;
                }
            }
            catch (Exception)
            {
                // Keep zero if the metadata is not fully available.
            }

            return Vector2.zero;
        }

        private static bool TryGetPositionOffsetTarget(LevelEvent ev, out CoordinateTarget target)
        {
            target = default;
            if (!IsPositionOffsetEvent(ev))
            {
                return false;
            }

            if (!TryGetVector2(ev, "positionOffset", out var offsetTiles))
            {
                offsetTiles = GetDefaultVector2(ev, "positionOffset");
            }

            var editor = scnEditor.instance;
            var tileSize = GetTileSize();
            var referencePoint = GetPositionOffsetReferencePoint(editor, ev, offsetTiles, tileSize);
            target = CoordinateTarget.ForTileOffsetEventProperty(
                ev,
                "positionOffset",
                referencePoint,
                tileSize,
                offsetTiles,
                GetEffectDisplayName(ev));
            return true;
        }

        // Human-readable names for the position-like editor effects shown beside the scene marker.
        // Use eventType.ToString() instead of enum constants for optional/newer event types so this
        // compatibility layer keeps compiling across ADOFAI versions that add/remove enum members.
        private static string GetEffectDisplayName(LevelEvent ev)
        {
            var name = ev?.eventType.ToString() ?? string.Empty;
            switch (name)
            {
                case "MoveCamera":
                    return EuclidText.Get("effect.moveCamera");
                case "MoveTrack":
                case "MoveDecorations":
                    return EuclidText.Get("effect.moveTrack");
                case "PositionTrack":
                    return EuclidText.Get("effect.positionTrack");
                case "FreeRoam":
                    return EuclidText.Get("effect.freeRoam");
                case "FreeRoamRemove":
                    return EuclidText.Get("effect.freeRoamRemove");
                default:
                    return string.IsNullOrWhiteSpace(name) ? "positionOffset" : name;
            }
        }

        private static bool IsPositionOffsetEvent(LevelEvent ev)
        {
            if (ev == null)
            {
                return false;
            }

            if (IsPositionTrack(ev))
            {
                return true;
            }

            var eventName = ev.eventType.ToString();
            if (eventName == "MoveTrack" || eventName == "MoveDecorations")
            {
                return true;
            }

            try
            {
                if (ev.info?.propertiesInfo != null &&
                    ev.info.propertiesInfo.TryGetValue("positionOffset", out var info) &&
                    info != null &&
                    info.type == PropertyType.Vector2)
                {
                    return true;
                }
            }
            catch (Exception)
            {
                // Fall back to raw data below.
            }

            return LevelEventCompat.ContainsKey(ev, "positionOffset");
        }

        private static bool IsPositionTrack(LevelEvent ev)
        {
            return ev != null && ev.eventType == LevelEventType.PositionTrack;
        }

        private static Vector2 GetPositionOffsetReferencePoint(
            scnEditor editor,
            LevelEvent ev,
            Vector2 offsetTiles,
            float tileSize)
        {
            if (!IsPositionTrack(ev))
            {
                return GetFloorPosition(editor, ev != null ? ev.floor : 0);
            }

            var eventFloor = ev != null ? ev.floor : 0;
            var relativeTo = GetTileRelativeTo(ev);
            var referenceFloor = eventFloor;
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

            var reference = GetFloorPosition(editor, referenceFloor);

            // ADOFAI's editor transform for ThisTile already contains the focused PositionTrack
            // displacement. Recover the position that this tile would have if this event did not
            // exist, then use that exact origin both for the reference marker and for converting a
            // dragged/snapped world point back into positionOffset. Start/End must not subtract
            // the offset even when the event happens to live on the first/last floor.
            if (string.Equals(relativeTo, "ThisTile", StringComparison.OrdinalIgnoreCase))
            {
                reference -= offsetTiles * Mathf.Max(tileSize, 0.000001f);
            }

            return reference;
        }

        private static string GetTileRelativeTo(LevelEvent ev)
        {
            try
            {
                if (LevelEventCompat.TryGetRaw(ev, "relativeTo", out var raw))
                {
                    return NormalizeTileRelativeTo(raw);
                }
            }
            catch (Exception)
            {
                // Try the typed getter below.
            }

            try
            {
                return NormalizeTileRelativeTo(ev.Get<TileRelativeTo>("relativeTo"));
            }
            catch (Exception)
            {
                return "ThisTile";
            }
        }

        private static string NormalizeTileRelativeTo(object raw)
        {
            if (raw == null)
            {
                return "ThisTile";
            }

            if (raw is int index)
            {
                switch (index)
                {
                    case 1:
                        return "Start";
                    case 2:
                        return "End";
                    default:
                        return "ThisTile";
                }
            }

            var text = raw.ToString();
            return string.IsNullOrWhiteSpace(text) ? "ThisTile" : text.Trim();
        }

        private static int GetLastFloorIndex(scnEditor editor)
        {
            var floors = GameCompat.GetFloors(editor);
            return floors.Count > 0 ? floors.Count - 1 : 0;
        }

        private static Vector2 GetFloorPosition(scnEditor editor, int floor)
        {
            try
            {
                var floors = GameCompat.GetFloors(editor);
                if (floor >= 0 && floor < floors.Count && floors[floor] != null)
                {
                    var position = floors[floor].transform.position;
                    return new Vector2(position.x, position.y);
                }

                var selectedFloors = GameCompat.GetSelectedFloors(editor);
                if (selectedFloors.Count > 0 && selectedFloors[0] != null)
                {
                    var position = selectedFloors[0].transform.position;
                    return new Vector2(position.x, position.y);
                }
            }
            catch (Exception)
            {
                // Zero keeps snapping usable even if the editor has rebuilt floors mid-frame.
            }

            return Vector2.zero;
        }

        private static float GetTileSize()
        {
            return GameCompat.GetTileSize();
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
                case IList list when list.Count >= 2
                    && TryConvertSingle(list[0], out var x)
                    && TryConvertSingle(list[1], out var y):
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

            if (raw is string text && string.IsNullOrWhiteSpace(text))
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

        private readonly struct CoordinateTarget
        {
            private readonly CameraFrameSnapshot cameraFrame;
            private readonly LevelEvent ev;
            private readonly string key;
            private readonly bool camera;
            private readonly bool tileOffset;
            private readonly Vector2 referencePoint;
            private readonly float tileSize;
            private readonly string label;

            private CoordinateTarget(
                CameraFrameSnapshot cameraFrame,
                LevelEvent ev,
                string key,
                Vector2 worldPoint,
                bool camera,
                bool tileOffset,
                Vector2 referencePoint,
                float tileSize,
                string label)
            {
                this.cameraFrame = cameraFrame;
                this.ev = ev;
                this.key = key;
                this.camera = camera;
                this.tileOffset = tileOffset;
                this.referencePoint = referencePoint;
                this.tileSize = tileSize <= 0.000001f ? 1f : tileSize;
                this.label = label;
                WorldPoint = worldPoint;
            }

            internal Vector2 WorldPoint { get; }

            internal Vector2 ReferenceWorld => referencePoint;

            internal Vector2d WorldPointD => new Vector2d(WorldPoint);

            internal string CacheKey => camera
                ? $"camera:{ev?.GetHashCode() ?? 0}:position"
                : $"event:{ev?.GetHashCode() ?? 0}:{key}:{tileOffset}";

            internal string Label => !string.IsNullOrEmpty(label)
                ? label
                : camera
                    ? EuclidText.Get("effect.moveCamera")
                    : string.Equals(key, "positionOffset", StringComparison.OrdinalIgnoreCase)
                        ? GetEffectDisplayName(ev)
                        : $"{ev.eventType}.{key}";

            internal static CoordinateTarget ForCamera(CameraFrameSnapshot cameraFrame)
            {
                return new CoordinateTarget(
                    cameraFrame,
                    cameraFrame.SelectedEvent,
                    "position",
                    cameraFrame.Center,
                    camera: true,
                    tileOffset: false,
                    referencePoint: Vector2.zero,
                    tileSize: 1f,
                    label: EuclidText.Get("effect.moveCamera"));
            }

            internal static CoordinateTarget ForEventProperty(LevelEvent ev, string key, Vector2 value)
            {
                return new CoordinateTarget(
                    default,
                    ev,
                    key,
                    value,
                    camera: false,
                    tileOffset: false,
                    referencePoint: Vector2.zero,
                    tileSize: 1f,
                    label: null);
            }

            internal static CoordinateTarget ForTileUnitEventProperty(LevelEvent ev, string key, Vector2 value, float tileSize)
            {
                var scale = Mathf.Max(tileSize, 0.000001f);
                return new CoordinateTarget(
                    default,
                    ev,
                    key,
                    value * scale,
                    camera: false,
                    tileOffset: true,
                    referencePoint: Vector2.zero,
                    tileSize: scale,
                    label: null);
            }

            internal static CoordinateTarget ForTileOffsetEventProperty(
                LevelEvent ev,
                string key,
                Vector2 referencePoint,
                float tileSize,
                Vector2 offsetTiles,
                string label)
            {
                return new CoordinateTarget(
                    default,
                    ev,
                    key,
                    referencePoint + offsetTiles * Mathf.Max(tileSize, 0.000001f),
                    camera: false,
                    tileOffset: true,
                    referencePoint: referencePoint,
                    tileSize: tileSize,
                    label: label);
            }

            internal bool TrySetWorldPoint(Vector2d value, bool saveUndoState)
            {
                if (camera)
                {
                    return CameraFrameEditor.TryMoveCenter(cameraFrame, value.ToVector2(), saveUndoState);
                }

                if (tileOffset)
                {
                    var offsetTiles = new Vector2(
                        (float)((value.X - referencePoint.x) / tileSize),
                        (float)((value.Y - referencePoint.y) / tileSize));
                    return CameraFrameEditor.TrySetVectorProperty(ev, key, offsetTiles, saveUndoState);
                }

                return CameraFrameEditor.TrySetVectorProperty(ev, key, value.ToVector2(), saveUndoState);
            }
        }
    }
}
