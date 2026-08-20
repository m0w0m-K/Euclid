using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using ADOFAI;
using UnityEngine;

namespace Euclid
{
    // Read-only collection for the optional "show all effect markers" layer. The currently selected
    // effect is deliberately excluded because the normal ConstructionShapeCanvasOverlay already
    // draws that one using the interactive/pending-edit CoordinateSnapTool state.
    internal static class EffectOverlayCollection
    {
        internal static void CollectBackground(List<EffectOverlayVisual> visuals)
        {
            if (visuals == null)
            {
                return;
            }

            visuals.Clear();
            if (!AllEffectMarkerSettings.Enabled)
            {
                return;
            }

            var editor = scnEditor.instance;
            if (editor == null || GameCompat.IsEditorPlaying(editor))
            {
                return;
            }

            var panel = GameCompat.GetLevelEventsPanel(editor);
            var selectedEvent = GameCompat.GetSelectedEvent(panel);

            AppendBackgroundMoveCameraVisuals(editor, selectedEvent, visuals);

            foreach (var ev in GameCompat.GetEditorEvents(editor))
            {
                if (ev == null || ev.eventType == LevelEventType.MoveCamera || ReferenceEquals(ev, selectedEvent))
                {
                    continue;
                }

                if (TryBuildReadOnlyVisual(editor, ev, out var visual))
                {
                    visuals.Add(visual);
                }
            }
        }

        private static void AppendBackgroundMoveCameraVisuals(
            scnEditor editor,
            LevelEvent selectedEvent,
            List<EffectOverlayVisual> visuals)
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
                if (ReferenceEquals(item.Event, selectedEvent))
                {
                    continue;
                }

                visuals.Add(new EffectOverlayVisual(
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
            var offsetTiles = positionUsed ? GetVector2(ev, "position", previous.OffsetTiles) : previous.OffsetTiles;
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
                    try
                    {
                        return (CamMovementType)index;
                    }
                    catch (Exception)
                    {
                        return fallback;
                    }
                }

                var text = raw?.ToString();
                if (!string.IsNullOrWhiteSpace(text) && Enum.TryParse(text.Trim(), true, out CamMovementType parsed))
                {
                    return parsed;
                }
            }

            return fallback;
        }

        private static bool TryBuildReadOnlyVisual(scnEditor editor, LevelEvent ev, out EffectOverlayVisual visual)
        {
            visual = default;
            if (editor == null || ev == null || !TryGetPositionOffset(ev, out var rawOffsetTiles))
            {
                return false;
            }

            var eventName = ev.eventType.ToString();
            EffectOverlayKind kind;
            switch (eventName)
            {
                case "MoveTrack":
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

            // Disabled properties keep their stored raw value, but ADOFAI applies zero. Background
            // markers must use that effective value just like the selected interactive marker.
            var offsetTiles = LevelEventCompat.IsPropertyEnabled(ev, "positionOffset")
                ? rawOffsetTiles
                : Vector2.zero;

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
                    // The unselected PositionTrack is already applied. With the property disabled,
                    // effective offset is zero and both markers coincide with the restored tile.
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

            visual = new EffectOverlayVisual(
                kind,
                referenceWorld,
                targetWorld,
                GetEffectDisplayName(ev));
            return true;
        }

        private static string GetEffectDisplayName(LevelEvent ev)
        {
            var name = ev?.eventType.ToString() ?? string.Empty;
            switch (name)
            {
                case "MoveCamera":
                    return EuclidText.Get("effect.moveCamera");
                case "MoveTrack":
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

        private static string GetTileRelativeTo(LevelEvent ev)
        {
            if (LevelEventCompat.TryGetRaw(ev, "relativeTo", out var raw))
            {
                return NormalizeTileRelativeTo(raw);
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

        private static bool TryGetPositionOffset(LevelEvent ev, out Vector2 value)
        {
            if (LevelEventCompat.TryGetRaw(ev, "positionOffset", out var raw) && TryConvertVector2(raw, out value))
            {
                return true;
            }

            try
            {
                value = Sanitize(ev.Get<Vector2>("positionOffset"));
                return true;
            }
            catch (Exception)
            {
                value = Vector2.zero;
                return false;
            }
        }

        private static Vector2 GetVector2(LevelEvent ev, string key, Vector2 fallback)
        {
            if (LevelEventCompat.TryGetRaw(ev, key, out var raw) && TryConvertVector2(raw, out var value))
            {
                return value;
            }

            try
            {
                return Sanitize(ev.Get<Vector2>(key));
            }
            catch (Exception)
            {
                return fallback;
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

        private static bool IsPropertyUsed(LevelEvent ev, string key)
        {
            return LevelEventCompat.IsPropertyEnabled(ev, key);
        }

        private static double GetEventStartTime(scnEditor editor, LevelEvent ev)
        {
            var floor = GetFloor(editor, ev.floor);
            if (floor == null)
            {
                return ev.floor + SafeGetFloat(ev, "angleOffset", 0f) / 180d;
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
                // Keep fallback BPM while the editor rebuilds level settings.
            }

            var speed = Math.Abs(floor.speed) > 0.0001f ? floor.speed : 1f;
            return floor.entryTime + SafeGetFloat(ev, "angleOffset", 0f) / 180d * 60d / (bpm * speed);
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
                // Ignore one-frame editor rebuild gaps; the overlay refreshes continuously.
            }

            return Vector2.zero;
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
                    // Defaults are valid until level settings are available.
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
