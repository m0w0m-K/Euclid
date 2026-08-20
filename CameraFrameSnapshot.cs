using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using ADOFAI;
using UnityEngine;

namespace Euclid
{
    internal enum CameraFrameState
    {
        Ready,
        NoEditor,
        EditorPlayMode,
        NoMoveCameraSelected,
        Unavailable
    }

    internal readonly struct CameraFrameSnapshot
    {
        private const float DefaultGameplayOrthoSize = 5f;

        private CameraFrameSnapshot(
            CameraFrameState state,
            string message,
            LevelEvent selectedEvent,
            int floor,
            CamMovementType relativeTo,
            Vector2 center,
            Vector2 offset,
            Vector2 offsetTiles,
            Vector2 referencePoint,
            float zoomPercent,
            float rotationDegrees,
            float tileSize,
            float halfWidth,
            float halfHeight)
        {
            State = state;
            Message = message;
            SelectedEvent = selectedEvent;
            Floor = floor;
            RelativeTo = relativeTo;
            Center = center;
            Offset = offset;
            OffsetTiles = offsetTiles;
            ReferencePoint = referencePoint;
            ZoomPercent = zoomPercent;
            RotationDegrees = rotationDegrees;
            TileSize = tileSize;
            HalfWidth = halfWidth;
            HalfHeight = halfHeight;
        }

        internal CameraFrameState State { get; }

        internal string Message { get; }

        internal LevelEvent SelectedEvent { get; }

        internal int Floor { get; }

        internal CamMovementType RelativeTo { get; }

        internal Vector2 Center { get; }

        internal Vector2 Offset { get; }

        internal Vector2 OffsetTiles { get; }

        internal Vector2 ReferencePoint { get; }

        internal float ZoomPercent { get; }

        internal float RotationDegrees { get; }

        internal float TileSize { get; }

        internal float HalfWidth { get; }

        internal float HalfHeight { get; }

        internal Vector2[] Corners
        {
            get
            {
                var radians = RotationDegrees * Mathf.Deg2Rad;
                var right = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
                var up = new Vector2(-Mathf.Sin(radians), Mathf.Cos(radians));

                return new[]
                {
                    Center - right * HalfWidth - up * HalfHeight,
                    Center + right * HalfWidth - up * HalfHeight,
                    Center + right * HalfWidth + up * HalfHeight,
                    Center - right * HalfWidth + up * HalfHeight,
                };
            }
        }

        internal static CameraFrameSnapshot Unavailable(string message)
        {
            return new CameraFrameSnapshot(CameraFrameState.Unavailable, message, null, -1, CamMovementType.Tile, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, 100f, 0f, 1f, 0f, 0f);
        }

        internal static CameraFrameSnapshot Capture()
        {
            try
            {
                var editor = scnEditor.instance;
                if (editor == null)
                {
                    return new CameraFrameSnapshot(CameraFrameState.NoEditor, EuclidText.Get("message.openEditor"), null, -1, CamMovementType.Tile, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, 100f, 0f, 1f, 0f, 0f);
                }

                if (GameCompat.IsEditorPlaying(editor))
                {
                    return new CameraFrameSnapshot(CameraFrameState.EditorPlayMode, EuclidText.Get("message.editorPlayback"), null, -1, CamMovementType.Tile, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, 100f, 0f, 1f, 0f, 0f);
                }

                var ev = GetFocusedMoveCameraEvent(editor);
                if (ev == null)
                {
                    return new CameraFrameSnapshot(CameraFrameState.NoMoveCameraSelected, EuclidText.Get("message.selectMoveCamera"), null, -1, CamMovementType.Tile, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, 100f, 0f, 1f, 0f, 0f);
                }

                var tileSize = GetTileSize();
                var state = BuildStateThroughSelectedEvent(editor, ev, tileSize);
                var zoomSize = Mathf.Max(0.0001f, state.ZoomPercent / 100f);
                var halfHeight = DefaultGameplayOrthoSize * zoomSize;
                var halfWidth = halfHeight * GetGameplayAspect();

                return new CameraFrameSnapshot(
                    CameraFrameState.Ready,
                    string.Empty,
                    ev,
                    ev.floor,
                    state.RelativeTo,
                    state.Center,
                    state.Offset,
                    state.OffsetTiles,
                    state.ReferencePoint,
                    state.ZoomPercent,
                    state.RotationDegrees,
                    tileSize,
                    halfWidth,
                    halfHeight);
            }
            catch (Exception ex)
            {
                EuclidMod.Logger?.Error(ex.ToString());
                return Unavailable(EuclidText.Get("message.cameraFrameFailed"));
            }
        }

        private static LevelEvent GetFocusedMoveCameraEvent(scnEditor editor)
        {
            var panel = GameCompat.GetLevelEventsPanel(editor);
            if (panel == null)
            {
                return null;
            }

            if (!GameCompat.TryGetSelectedEventType(panel, out var selectedType) || selectedType != LevelEventType.MoveCamera)
            {
                return null;
            }

            var ev = GameCompat.GetSelectedEvent(panel);
            if (ev == null || ev.eventType != LevelEventType.MoveCamera)
            {
                return null;
            }

            return CurrentSelectionContainsEvent(editor, ev) ? ev : null;
        }

        private static bool CurrentSelectionContainsEvent(scnEditor editor, LevelEvent ev)
        {
            try
            {
                var selectedEvents = GameCompat.GetSelectedFloorEvents(editor, LevelEventType.MoveCamera);
                if (selectedEvents != null && selectedEvents.Contains(ev))
                {
                    return true;
                }
            }
            catch (Exception)
            {
                // Fall back to floor matching below if the editor has not built the event list yet.
            }

            var selectedFloors = GameCompat.GetSelectedFloors(editor);
            if (selectedFloors.Count == 0)
            {
                return false;
            }

            foreach (var floor in selectedFloors)
            {
                if (floor != null && floor.seqID == ev.floor)
                {
                    return true;
                }
            }

            return false;
        }

        private static CameraState BuildStateThroughSelectedEvent(scnEditor editor, LevelEvent selectedEvent, float tileSize)
        {
            var state = CameraState.FromLevelSettings(editor, tileSize);
            var selectedItem = TimelineItem.FromEvent(editor, selectedEvent, int.MaxValue);
            var timeline = BuildCameraTimeline(editor);

            foreach (var item in timeline)
            {
                if (ReferenceEquals(item.Event, selectedEvent))
                {
                    return ApplyMoveCamera(editor, state, item.Event, tileSize, preferSelectedPlayerReference: true);
                }

                if (item.IsBefore(selectedItem))
                {
                    state = ApplyMoveCamera(editor, state, item.Event, tileSize);
                }
            }

            return ApplyMoveCamera(editor, state, selectedEvent, tileSize, preferSelectedPlayerReference: true);
        }

        private static List<TimelineItem> BuildCameraTimeline(scnEditor editor)
        {
            var timeline = new List<TimelineItem>();
            var index = 0;
            foreach (var ev in GameCompat.GetEditorEvents(editor))
            {
                if (ev != null && ev.eventType == LevelEventType.MoveCamera)
                {
                    timeline.Add(TimelineItem.FromEvent(editor, ev, index));
                }

                index++;
            }

            timeline.Sort(TimelineItem.Compare);
            return timeline;
        }

        private static float GetTileSize()
        {
            return GameCompat.GetTileSize();
        }

        private static Vector2 GetPosition(LevelEvent ev)
        {
            if (LevelEventCompat.TryGetRaw(ev, "position", out var raw) && TryConvertVector2(raw, out var value))
            {
                return value;
            }

            try
            {
                return Sanitize(RDUtils.GetRandomVector2(ev, "position"));
            }
            catch (Exception)
            {
                try
                {
                    var pair = ev.GetFloatPair("position");
                    return Sanitize(new Vector2(pair.Item1, pair.Item2));
                }
                catch (Exception)
                {
                    return Vector2.zero;
                }
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

        private static Vector2 Sanitize(Vector2 value)
        {
            return new Vector2(Sanitize(value.x), Sanitize(value.y));
        }

        private static float Sanitize(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
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

        private static CamMovementType GetRelativeTo(LevelEvent ev, CamMovementType fallback)
        {
            if (LevelEventCompat.TryGetRaw(ev, "relativeTo", out var raw))
            {
                if (raw is CamMovementType movementType)
                {
                    return movementType;
                }

                if (raw is string text && Enum.TryParse(text, out CamMovementType parsed))
                {
                    return parsed;
                }
            }

            return fallback;
        }

        private static CameraState ApplyMoveCamera(scnEditor editor, CameraState previous, LevelEvent ev, float tileSize, bool preferSelectedPlayerReference = false)
        {
            var relativeTo = IsPropertyUsed(ev, "relativeTo")
                ? GetRelativeTo(ev, previous.RelativeTo)
                : previous.RelativeTo;
            var positionUsed = IsPropertyUsed(ev, "position");
            var usePlayerTileProxy = preferSelectedPlayerReference && relativeTo == CamMovementType.Player;
            var offsetTiles = positionUsed
                ? GetPosition(ev)
                : usePlayerTileProxy ? Vector2.zero : previous.OffsetTiles;
            var offset = offsetTiles * tileSize;
            var referencePoint = ResolveReferencePoint(editor, previous.Center, ev.floor, relativeTo, preferSelectedPlayerReference);
            var center = positionUsed || usePlayerTileProxy
                ? referencePoint + offset
                : previous.Center;
            var zoomPercent = IsPropertyUsed(ev, "zoom")
                ? SafeGetFloat(ev, "zoom", previous.ZoomPercent)
                : previous.ZoomPercent;
            var rotation = IsPropertyUsed(ev, "rotation")
                ? SafeGetFloat(ev, "rotation", previous.RotationDegrees)
                : previous.RotationDegrees;

            return new CameraState(center, relativeTo, offset, offsetTiles, referencePoint, zoomPercent, rotation);
        }

        private static Vector2 ResolveReferencePoint(scnEditor editor, Vector2 previousCenter, int floor, CamMovementType relativeTo, bool preferSelectedPlayerReference = false)
        {
            switch (relativeTo)
            {
                case CamMovementType.Global:
                    return Vector2.zero;
                case CamMovementType.LastPosition:
                case CamMovementType.LastPositionNoRotation:
                    return previousCenter;
                case CamMovementType.Player:
                    // The editor does not expose a live player transform here, so the event floor is the editor proxy.
                    return preferSelectedPlayerReference
                        ? GetSelectedOrFloorPosition(editor, floor)
                        : GetFloorPosition(editor, floor);
                case CamMovementType.Tile:
                default:
                    return GetFloorPosition(editor, floor);
            }
        }

        private static Vector2 GetSelectedOrFloorPosition(scnEditor editor, int floor)
        {
            try
            {
                var selectedFloors = GameCompat.GetSelectedFloors(editor);
                if (selectedFloors.Count > 0 && selectedFloors[0] != null)
                {
                    var position = selectedFloors[0].transform.position;
                    return new Vector2(position.x, position.y);
                }
            }
            catch (Exception)
            {
                // Fall back to the event floor below.
            }

            return GetFloorPosition(editor, floor);
        }

        private static Vector2 GetFloorPosition(scnEditor editor, int floor)
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

            return Vector2.zero;
        }

        private static float GetGameplayAspect()
        {
            return Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f;
        }

        private static double GetEventStartTime(scnEditor editor, LevelEvent ev)
        {
            var floor = GetFloor(editor, ev.floor);
            if (floor == null)
            {
                return ev.floor + GetAngleOffset(ev) / 180d;
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
                // Keep a stable fallback if level settings are not ready.
            }

            var speed = Math.Abs(floor.speed) > 0.0001f ? floor.speed : 1f;
            return floor.entryTime + GetAngleOffset(ev) / 180d * 60d / (bpm * speed);
        }

        private static float GetAngleOffset(LevelEvent ev)
        {
            return SafeGetFloat(ev, "angleOffset", 0f);
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

        private static bool IsPropertyUsed(LevelEvent ev, string key)
        {
            return ev.disabled == null || !ev.disabled.TryGetValue(key, out var disabled) || !disabled;
        }

        private static scrFloor GetFloor(scnEditor editor, int floor)
        {
            var floors = GameCompat.GetFloors(editor);
            return floor >= 0 && floor < floors.Count ? floors[floor] : null;
        }

        private readonly struct CameraState
        {
            internal CameraState(
                Vector2 center,
                CamMovementType relativeTo,
                Vector2 offset,
                Vector2 offsetTiles,
                Vector2 referencePoint,
                float zoomPercent,
                float rotationDegrees)
            {
                Center = center;
                RelativeTo = relativeTo;
                Offset = offset;
                OffsetTiles = offsetTiles;
                ReferencePoint = referencePoint;
                ZoomPercent = zoomPercent;
                RotationDegrees = rotationDegrees;
            }

            internal Vector2 Center { get; }

            internal CamMovementType RelativeTo { get; }

            internal Vector2 Offset { get; }

            internal Vector2 OffsetTiles { get; }

            internal Vector2 ReferencePoint { get; }

            internal float ZoomPercent { get; }

            internal float RotationDegrees { get; }

            internal static CameraState FromLevelSettings(scnEditor editor, float tileSize)
            {
                var relativeTo = CamMovementType.Tile;
                var offsetTiles = Vector2.zero;
                var zoom = 100f;
                var rotation = 0f;

                try
                {
                    if (GameCompat.TryGetLevelSetting(editor, "camRelativeTo", out CamMovementType levelRelativeTo))
                    {
                        relativeTo = levelRelativeTo;
                    }
                    if (GameCompat.TryGetLevelSetting(editor, "camPosition", out Vector2 levelPosition))
                    {
                        offsetTiles = levelPosition;
                    }
                    if (GameCompat.TryGetLevelSetting(editor, "camZoom", out float levelZoom))
                    {
                        zoom = levelZoom;
                    }
                    if (GameCompat.TryGetLevelSetting(editor, "camRotation", out float levelRotation))
                    {
                        rotation = levelRotation;
                    }
                }
                catch (Exception)
                {
                    // Keep defaults until the level settings are available.
                }

                var referencePoint = ResolveReferencePoint(editor, Vector2.zero, 0, relativeTo);
                var offset = offsetTiles * tileSize;
                return new CameraState(referencePoint + offset, relativeTo, offset, offsetTiles, referencePoint, zoom, rotation);
            }
        }

        private readonly struct TimelineItem
        {
            private const double TimeEpsilon = 0.000001d;

            private TimelineItem(LevelEvent ev, int index, double startTime)
            {
                Event = ev;
                Index = index;
                StartTime = startTime;
            }

            internal LevelEvent Event { get; }

            private int Index { get; }

            private double StartTime { get; }

            internal static TimelineItem FromEvent(scnEditor editor, LevelEvent ev, int index)
            {
                return new TimelineItem(ev, index, GetEventStartTime(editor, ev));
            }

            internal bool IsBefore(TimelineItem other)
            {
                if (StartTime < other.StartTime - TimeEpsilon)
                {
                    return true;
                }

                return Math.Abs(StartTime - other.StartTime) <= TimeEpsilon && Index < other.Index;
            }

            internal static int Compare(TimelineItem left, TimelineItem right)
            {
                var timeCompare = left.StartTime.CompareTo(right.StartTime);
                return timeCompare != 0 ? timeCompare : left.Index.CompareTo(right.Index);
            }
        }
    }
}
