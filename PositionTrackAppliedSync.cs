using System;
using System.Collections;
using System.Reflection;
using ADOFAI;
using UnityEngine;

namespace Euclid
{
    // Keeps CoordinateSnapTool's PositionTrack cache coherent when Euclid performs an immediate
    // programmatic edit such as Snap. Marker dragging is deliberately excluded: that path is
    // deferred through the real ADOFAI input-field focus lifecycle instead.
    internal static class PositionTrackAppliedSync
    {
        private static readonly Type CoordinateSnapType = typeof(CoordinateSnapTool);
        private static readonly FieldInfo HasReferenceField = GetField("hasPositionTrackReference");
        private static readonly FieldInfo ReferenceEventField = GetField("positionTrackReferenceEvent");
        private static readonly FieldInfo ReferenceFloorField = GetField("positionTrackReferenceFloor");
        private static readonly FieldInfo ReferenceTileSizeField = GetField("positionTrackReferenceTileSize");
        private static readonly FieldInfo ZeroReferenceField = GetField("positionTrackZeroReference");
        private static readonly FieldInfo AppliedOffsetField = GetField("positionTrackAppliedOffsetTiles");
        private static readonly FieldInfo AppliedFloorField = GetField("positionTrackAppliedFloorWorld");
        private static readonly FieldInfo ReferenceProvisionalField = GetField("positionTrackReferenceProvisional");

        internal readonly struct Baseline
        {
            internal Baseline(bool valid, int floor, float tileSize, Vector2 zeroReference)
            {
                IsValid = valid;
                Floor = floor;
                TileSize = tileSize;
                ZeroReference = zeroReference;
            }

            internal bool IsValid { get; }
            internal int Floor { get; }
            internal float TileSize { get; }
            internal Vector2 ZeroReference { get; }
        }

        internal static Baseline CaptureBeforeEdit(LevelEvent ev, string key)
        {
            if (!IsSupported(ev, key) || !IsThisTileRelative(ev) ||
                !TryGetPositionOffset(ev, out var oldOffset))
            {
                return default;
            }

            var editor = scnEditor.instance;
            if (editor == null || !TryGetFloorWorld(editor, ev.floor, out var floorWorld))
            {
                return default;
            }

            var tileSize = Mathf.Max(GameCompat.GetTileSize(), 0.000001f);
            var zeroReference = floorWorld - oldOffset * tileSize;
            return new Baseline(true, ev.floor, tileSize, zeroReference);
        }

        internal static void NotifyImmediateApply(
            LevelEvent ev,
            string key,
            Vector2 newOffset,
            Baseline baseline)
        {
            if (!baseline.IsValid || !IsSupported(ev, key) || !IsThisTileRelative(ev))
            {
                return;
            }

            var editor = scnEditor.instance;
            if (editor == null)
            {
                return;
            }

            // Snapshot the transform exactly as it exists after ApplyPropertiesToRealEvents.
            // If ADOFAI completes the floor move one frame later, keeping this snapshot as the
            // applied-floor baseline prevents CoordinateSnapTool from treating the new raw offset
            // as an unexplained movement. The normal floor-change branch will reconcile it later.
            if (!TryGetFloorWorld(editor, baseline.Floor, out var appliedFloorWorld))
            {
                appliedFloorWorld = baseline.ZeroReference + newOffset * baseline.TileSize;
            }

            ForceCoordinateCache(
                ev,
                baseline.Floor,
                baseline.TileSize,
                baseline.ZeroReference,
                newOffset,
                appliedFloorWorld);
        }

        private static bool IsSupported(LevelEvent ev, string key)
        {
            return ev != null &&
                   ev.eventType == LevelEventType.PositionTrack &&
                   string.Equals(key, "positionOffset", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsThisTileRelative(LevelEvent ev)
        {
            if (!LevelEventCompat.TryGetRaw(ev, "relativeTo", out var raw) || raw == null)
            {
                return true;
            }

            if (raw is int index)
            {
                return index == 0;
            }

            var text = raw.ToString();
            return string.IsNullOrWhiteSpace(text) ||
                   string.Equals(text.Trim(), "ThisTile", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetPositionOffset(LevelEvent ev, out Vector2 value)
        {
            if (LevelEventCompat.TryGetRaw(ev, "positionOffset", out var raw) &&
                TryConvertVector2(raw, out value))
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
            if (raw is Vector2 vector)
            {
                value = Sanitize(vector);
                return true;
            }

            if (raw is Tuple<float, float> pair)
            {
                value = Sanitize(new Vector2(pair.Item1, pair.Item2));
                return true;
            }

            if (raw is IList list && list.Count >= 2 &&
                TryConvertSingle(list[0], out var x) &&
                TryConvertSingle(list[1], out var y))
            {
                value = Sanitize(new Vector2(x, y));
                return true;
            }

            value = Vector2.zero;
            return false;
        }

        private static bool TryConvertSingle(object raw, out float value)
        {
            try
            {
                value = raw == null
                    ? 0f
                    : Convert.ToSingle(raw, System.Globalization.CultureInfo.InvariantCulture);
                value = Sanitize(value);
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

        private static bool TryGetFloorWorld(scnEditor editor, int floor, out Vector2 world)
        {
            world = Vector2.zero;
            try
            {
                var floors = GameCompat.GetFloors(editor);
                for (var i = 0; i < floors.Count; i++)
                {
                    var candidate = floors[i];
                    if (candidate != null && candidate.seqID == floor)
                    {
                        var position = candidate.transform.position;
                        world = new Vector2(position.x, position.y);
                        return true;
                    }
                }

                if (floor >= 0 && floor < floors.Count && floors[floor] != null)
                {
                    var position = floors[floor].transform.position;
                    world = new Vector2(position.x, position.y);
                    return true;
                }
            }
            catch (Exception)
            {
                // ADOFAI may rebuild the floor list during an applied edit.
            }

            return false;
        }

        private static void ForceCoordinateCache(
            LevelEvent ev,
            int floor,
            float tileSize,
            Vector2 zeroReference,
            Vector2 appliedOffset,
            Vector2 appliedFloorWorld)
        {
            if (HasReferenceField == null || ReferenceEventField == null ||
                ReferenceFloorField == null || ReferenceTileSizeField == null ||
                ZeroReferenceField == null || AppliedOffsetField == null ||
                AppliedFloorField == null || ReferenceProvisionalField == null)
            {
                return;
            }

            try
            {
                ReferenceEventField.SetValue(null, ev);
                ReferenceFloorField.SetValue(null, floor);
                ReferenceTileSizeField.SetValue(null, tileSize);
                ZeroReferenceField.SetValue(null, zeroReference);
                AppliedOffsetField.SetValue(null, appliedOffset);
                AppliedFloorField.SetValue(null, appliedFloorWorld);
                ReferenceProvisionalField.SetValue(null, false);
                HasReferenceField.SetValue(null, true);
            }
            catch (Exception)
            {
                // Keep the edit working even if a future game/mod build renames the private cache.
            }
        }

        private static FieldInfo GetField(string name)
        {
            return CoordinateSnapType.GetField(name, BindingFlags.Static | BindingFlags.NonPublic);
        }
    }
}
