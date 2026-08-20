using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using ADOFAI;
using UnityEngine;

namespace Euclid
{
    // Collects the effect-marker model used by the below-editor overlay. The selected event keeps
    // its interactive/pending-edit semantics from CoordinateSnapTool; unselected events are read
    // only from their already-applied editor state so collecting every marker never disturbs the
    // single-event PositionTrack cache.
    internal static class EffectOverlayCollection
    {
        internal static void CollectVisible(List<EffectOverlayVisual> visuals)
        {
            if (visuals == null)
            {
                return;
            }

            visuals.Clear();
            if (!EuclidMod.ShowAllEffectMarkers)
            {
                CollectFocused(visuals);
                return;
            }

            CollectAll(visuals);
        }

        private static void CollectFocused(List<EffectOverlayVisual> visuals)
        {
            var cameraFrame = EuclidMod.Behaviour != null
                ? EuclidMod.Behaviour.CameraFrame
                : CameraFrameSnapshot.Unavailable(string.Empty);
            if (cameraFrame.State == CameraFrameState.Ready)
            {
                visuals.Add(new EffectOverlayVisual(
                    EffectOverlayKind.CameraMove,
                    cameraFrame.ReferencePoint,
                    cameraFrame.Center,
                    EuclidText.Get("effect.moveCamera")));
                return;
            }

            if (CoordinateSnapTool.TryGetFocusedEffectVisual(out var visual))
            {
                visuals.Add(visual);
            }
        }

        private static void CollectAll(List<EffectOverlayVisual> visuals)
        {
            var editor = scnEditor.instance;
            if (editor == null || GameCompat.IsEditorPlaying(editor))
            {
                return;
            }

            var panel = GameCompat.GetLevelEventsPanel(editor);
            var selectedEvent = GameCompat.GetSelectedEvent(panel);

            // Camera events depend on previous camera events, so build their markers in timeline
            // order once rather than recomputing the entire camera state separately for each event.
            CameraFrameSnapshot.AppendAllMoveCameraVisuals(visuals, selectedEvent);

            var selectedNonCameraWasAdded = false;
            foreach (var ev in GameCompat.GetEditorEvents(editor))
            {
                if (ev == null || ev.eventType == LevelEventType.MoveCamera)
                {
                    continue;
                }

                if (ReferenceEquals(ev, selectedEvent))
                {
                    if (CoordinateSnapTool.TryGetFocusedEffectVisual(out var focusedVisual))
                    {
                        visuals.Add(focusedVisual);
                        selectedNonCameraWasAdded = true;
                    }
                    continue;
                }

                if (TryBuildReadOnlyVisual(editor, ev, out var visual))
                {
                    visuals.Add(visual);
                }
            }

            // During an inspector rebuild selectedEvent can briefly be a replacement object that is
            // not yet present in editor.events. Keep its interactive marker visible in that frame.
            if (!selectedNonCameraWasAdded && selectedEvent != null &&
                selectedEvent.eventType != LevelEventType.MoveCamera &&
                CoordinateSnapTool.TryGetFocusedEffectVisual(out var selectedVisual))
            {
                visuals.Add(selectedVisual);
            }
        }

        private static bool TryBuildReadOnlyVisual(scnEditor editor, LevelEvent ev, out EffectOverlayVisual visual)
        {
            visual = default;
            if (editor == null || ev == null || !TryGetPositionOffset(ev, out var offsetTiles))
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
                    // This PositionTrack has already been applied to the unselected floor. Remove
                    // only this event's own offset to recover its pre-effect tile/reference point.
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
                for (var i = 0; i < floors.Count; i++)
                {
                    var candidate = floors[i];
                    if (candidate != null && candidate.seqID == floor)
                    {
                        var position = candidate.transform.position;
                        return new Vector2(position.x, position.y);
                    }
                }

                if (floor >= 0 && floor < floors.Count && floors[floor] != null)
                {
                    var position = floors[floor].transform.position;
                    return new Vector2(position.x, position.y);
                }
            }
            catch (Exception)
            {
                // Ignore one-frame editor rebuild gaps; the overlay will refresh again immediately.
            }

            return Vector2.zero;
        }
    }
}
