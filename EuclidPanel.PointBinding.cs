using System;
using System.Collections.Generic;
using ADOFAI;
using TMPro;
using UnityEngine;

namespace Euclid
{
    internal sealed partial class EuclidPanel
    {
        // Endpoint source binding is intentionally separate from ConstructionPointRef's coordinates.
        // Picking a tile/point snapshots its X/Y. The endpoint only becomes a live reference when
        // the user explicitly turns PIN on. With PIN off, any later source movement/renumbering
        // drops the source label and leaves the saved coordinates untouched.
        private sealed class PointBindingState
        {
            internal ConstructionShape Shape;
            internal int PointIndex;
            internal ConstructionPointSourceKind SourceKind;
            internal int SourceShapeId;
            internal scrFloor SourceTile;
            internal int SnapshotTileSeqId;
            internal double SnapshotX;
            internal double SnapshotY;
            internal bool Pinned;
        }

        private readonly Dictionary<long, PointBindingState> pointBindings =
            new Dictionary<long, PointBindingState>();

        private TMP_Text shapeFirstPinText;
        private TMP_Text shapeSecondPinText;

        private void LateUpdate()
        {
            if (!EuclidMod.Enabled)
            {
                return;
            }

            UpdatePointBindings();
            EnsurePointPinButtons();
            RefreshPointBindingButtons();

            // PIN is the only endpoint control created dynamically after the detail hierarchy is
            // built. Finalize PIN/P2/slider geometry and interactable colors in this same LateUpdate,
            // before Unity's render/layout pass, so no intermediate size or enabled tint is visible.
            var selectedShape = ConstructionShapeTool.PrimarySelectedShape;
            if (selectedShape != null)
            {
                NormalizeDetailControlState(selectedShape);
            }
        }

        private void UpdatePointBindings()
        {
            var shapes = ConstructionShapeTool.Shapes;
            var liveKeys = new HashSet<long>();
            var anyGeometryChanged = false;
            var selectedShapeChanged = false;
            var selectedShape = ConstructionShapeTool.PrimarySelectedShape;

            for (var i = 0; i < shapes.Count; i++)
            {
                var shape = shapes[i];
                if (shape == null)
                {
                    continue;
                }

                var pointCount = shape.Type == ConstructionShapeType.Point ? 1 : 2;
                for (var pointIndex = 0; pointIndex < pointCount; pointIndex++)
                {
                    var point = ConstructionShapeTool.GetPoint(shape, pointIndex);
                    if (!HasBindableSource(point))
                    {
                        RemovePointBinding(shape, pointIndex);
                        continue;
                    }

                    var key = PointBindingKey(shape, pointIndex);
                    liveKeys.Add(key);
                    if (!pointBindings.TryGetValue(key, out var state) ||
                        state == null ||
                        state.Shape != shape ||
                        !BindingMatchesPoint(state, point))
                    {
                        state = CreatePointBinding(shape, pointIndex, point);
                        if (state == null)
                        {
                            ConstructionShapeTool.ClearPointSource(shape, pointIndex);
                            if (selectedShape == shape)
                            {
                                selectedShapeChanged = true;
                            }
                            continue;
                        }

                        pointBindings[key] = state;
                    }

                    if (!TryResolveBindingSource(state, out var sourceX, out var sourceY, out var currentTileSeqId))
                    {
                        // The source object disappeared. Freeze the endpoint where it is and stop
                        // claiming it is still based on that tile/point.
                        ConstructionShapeTool.ClearPointSource(shape, pointIndex);
                        pointBindings.Remove(key);
                        if (selectedShape == shape)
                        {
                            selectedShapeChanged = true;
                        }
                        continue;
                    }

                    if (!state.Pinned)
                    {
                        var sourceMoved = Math.Abs(sourceX - state.SnapshotX) > 0.0000001d ||
                            Math.Abs(sourceY - state.SnapshotY) > 0.0000001d;
                        var sourceRenumbered = state.SourceKind == ConstructionPointSourceKind.Tile &&
                            currentTileSeqId != state.SnapshotTileSeqId;

                        if (sourceMoved || sourceRenumbered)
                        {
                            // Unpinned endpoints are coordinate snapshots. Once the original source
                            // changes, remove the source annotation instead of silently retargeting a
                            // new seqID or implying that the endpoint is still attached.
                            ConstructionShapeTool.ClearPointSource(shape, pointIndex);
                            pointBindings.Remove(key);
                            if (selectedShape == shape)
                            {
                                selectedShapeChanged = true;
                            }
                        }
                        continue;
                    }

                    var changed = Math.Abs(point.X - sourceX) > 0.0000001d ||
                        Math.Abs(point.Y - sourceY) > 0.0000001d;
                    if (state.SourceKind == ConstructionPointSourceKind.Tile &&
                        (point.Tile != currentTileSeqId || !point.HasTile))
                    {
                        changed = true;
                    }

                    if (changed)
                    {
                        point.X = sourceX;
                        point.Y = sourceY;
                        if (state.SourceKind == ConstructionPointSourceKind.Tile)
                        {
                            point.HasTile = true;
                            point.Tile = currentTileSeqId;
                        }

                        ConstructionShapeTool.SetPoint(shape, pointIndex, point);
                        ConstructionShapeTool.DrawShape(shape);
                        anyGeometryChanged = true;
                        if (selectedShape == shape)
                        {
                            selectedShapeChanged = true;
                        }
                    }

                    // Keep a rolling snapshot while PIN is on. This lets us distinguish a normal
                    // seqID/position change of the same source from the user picking a different
                    // source between frames.
                    state.SnapshotX = sourceX;
                    state.SnapshotY = sourceY;
                    state.SnapshotTileSeqId = currentTileSeqId;
                }
            }

            var stale = new List<long>();
            foreach (var pair in pointBindings)
            {
                if (!liveKeys.Contains(pair.Key) || pair.Value == null || !ContainsShape(shapes, pair.Value.Shape))
                {
                    stale.Add(pair.Key);
                }
            }

            for (var i = 0; i < stale.Count; i++)
            {
                pointBindings.Remove(stale[i]);
            }

            if (anyGeometryChanged)
            {
                ConstructionShapeCanvasOverlay.Refresh();
                RefreshShapeActionButtons();
            }

            if (selectedShapeChanged && selectedShape != null)
            {
                // Keep X/Y, the P1/P2 source label, and derived a/b/theta/r in the same frame as
                // the rendered geometry. Use SetTextWithoutNotify here: setting X then Y through
                // normal TMP callbacks would briefly look like a manual edit and detach the source.
                RefreshPointBindingFields(selectedShape);
                RefreshShapeGeometryInfo(selectedShape);
            }
        }

        private void EnsurePointPinButtons()
        {
            var shape = ConstructionShapeTool.PrimarySelectedShape;
            if (shape == null)
            {
                shapeFirstPinText = null;
                shapeSecondPinText = null;
                return;
            }

            EnsurePointPinButton(shape, 0, shapeFirstPickText, ref shapeFirstPinText);
            EnsurePointPinButton(shape, 1, shapeSecondPickText, ref shapeSecondPinText);
        }

        private void EnsurePointPinButton(
            ConstructionShape shape,
            int pointIndex,
            TMP_Text pickText,
            ref TMP_Text pinText)
        {
            if (pickText == null || pickText.transform == null || pickText.transform.parent == null)
            {
                pinText = null;
                return;
            }

            var pickButtonTransform = pickText.transform.parent;
            var row = pickButtonTransform.parent;
            if (row == null)
            {
                pinText = null;
                return;
            }

            if (pinText != null && pinText.transform != null &&
                pinText.transform.parent != null && pinText.transform.parent.parent == row)
            {
                return;
            }

            pinText = AddButton(
                row,
                PointPinLabel(),
                () => TogglePointPin(shape, pointIndex),
                64f,
                ButtonSurface.Outline);

            if (pinText != null && pinText.transform.parent != null)
            {
                pinText.transform.parent.SetSiblingIndex(pickButtonTransform.GetSiblingIndex() + 1);
                // Do not wait for a later frame to copy Pick's measured size. Both endpoint action
                // buttons are fixed 64 px controls by definition, so give PIN that final geometry at
                // creation time and let the row lay out from stable constraints.
                NormalizePointButtonLayout(pinText);
            }
        }

        private void RefreshPointBindingButtons()
        {
            var shape = ConstructionShapeTool.PrimarySelectedShape;
            if (shape == null)
            {
                return;
            }

            RefreshPointBindingButton(shape, 0, shapeFirstPickText, shapeFirstPinText, enabled: true);
            RefreshPointBindingButton(
                shape,
                1,
                shapeSecondPickText,
                shapeSecondPinText,
                enabled: shape.Type != ConstructionShapeType.Point);
        }

        private void RefreshPointBindingButton(
            ConstructionShape shape,
            int pointIndex,
            TMP_Text pickText,
            TMP_Text pinText,
            bool enabled)
        {
            if (pickText != null)
            {
                var picking = enabled && pendingPointPickShape == shape && pendingPointPickIndex == pointIndex;
                SetToggleButtonState(pickText, EuclidText.Get("button.pickPosition"), picking, enabled);
            }

            if (pinText == null)
            {
                return;
            }

            var point = ConstructionShapeTool.GetPoint(shape, pointIndex);
            var canPin = enabled && HasBindableSource(point);
            var pinned = false;
            if (canPin && pointBindings.TryGetValue(PointBindingKey(shape, pointIndex), out var state) && state != null)
            {
                pinned = state.Pinned;
            }

            SetToggleButtonState(pinText, PointPinLabel(), pinned, canPin);
        }

        private void TogglePointPin(ConstructionShape shape, int pointIndex)
        {
            if (shape == null)
            {
                return;
            }

            var point = ConstructionShapeTool.GetPoint(shape, pointIndex);
            if (!HasBindableSource(point))
            {
                return;
            }

            var key = PointBindingKey(shape, pointIndex);
            if (!pointBindings.TryGetValue(key, out var state) ||
                state == null ||
                state.Shape != shape ||
                !BindingMatchesPoint(state, point))
            {
                state = CreatePointBinding(shape, pointIndex, point);
                if (state == null)
                {
                    ConstructionShapeTool.ClearPointSource(shape, pointIndex);
                    RefreshPointBindingFields(shape);
                    RefreshPointBindingButtons();
                    return;
                }
                pointBindings[key] = state;
            }

            if (!TryResolveBindingSource(state, out var sourceX, out var sourceY, out var currentTileSeqId))
            {
                ConstructionShapeTool.ClearPointSource(shape, pointIndex);
                pointBindings.Remove(key);
                RefreshPointBindingFields(shape);
                RefreshPointBindingButtons();
                return;
            }

            state.Pinned = !state.Pinned;
            if (state.Pinned)
            {
                // Pinning immediately adopts the source's current position so the UI and geometry
                // cannot start from different values.
                point.X = sourceX;
                point.Y = sourceY;
                if (state.SourceKind == ConstructionPointSourceKind.Tile)
                {
                    point.HasTile = true;
                    point.Tile = currentTileSeqId;
                }
                ConstructionShapeTool.SetPoint(shape, pointIndex, point);
                ConstructionShapeTool.DrawShape(shape);
                state.SnapshotX = sourceX;
                state.SnapshotY = sourceY;
                state.SnapshotTileSeqId = currentTileSeqId;
                ConstructionShapeCanvasOverlay.Refresh();
                RefreshPointBindingFields(shape);
                RefreshShapeGeometryInfo(shape);
            }
            else
            {
                // Turning PIN off converts live following back into a snapshot from this instant.
                // Keep the source label until that source later moves/renumbers.
                state.SnapshotX = sourceX;
                state.SnapshotY = sourceY;
                state.SnapshotTileSeqId = currentTileSeqId;
            }

            RefreshPointBindingButtons();
        }

        private void RefreshPointBindingFields(ConstructionShape shape)
        {
            if (shape == null || ConstructionShapeTool.PrimarySelectedShape != shape)
            {
                return;
            }

            RefreshPointBindingField(
                shape,
                0,
                shapeFirstSourceText,
                shapeFirstX,
                shapeFirstY);
            RefreshPointBindingField(
                shape,
                1,
                shapeSecondSourceText,
                shapeSecondX,
                shapeSecondY);
        }

        private static void RefreshPointBindingField(
            ConstructionShape shape,
            int pointIndex,
            TMP_Text sourceText,
            TMP_InputField xField,
            TMP_InputField yField)
        {
            if (shape == null)
            {
                return;
            }

            var point = ConstructionShapeTool.GetPointForDisplay(shape, pointIndex);
            if (sourceText != null)
            {
                sourceText.text = PointHeaderLabel(pointIndex, point);
            }
            if (xField != null)
            {
                xField.SetTextWithoutNotify(ConstructionShapeTool.Format(point.X));
            }
            if (yField != null)
            {
                yField.SetTextWithoutNotify(ConstructionShapeTool.Format(point.Y));
            }
        }

        private PointBindingState CreatePointBinding(
            ConstructionShape shape,
            int pointIndex,
            ConstructionPointRef point)
        {
            var state = new PointBindingState
            {
                Shape = shape,
                PointIndex = pointIndex,
                SourceKind = point.SourceKind,
                SourceShapeId = point.SourceShapeId,
                SnapshotTileSeqId = point.Tile,
                SnapshotX = point.X,
                SnapshotY = point.Y,
                Pinned = false,
            };

            if (point.SourceKind == ConstructionPointSourceKind.Tile)
            {
                state.SourceTile = FindFloorBySeqId(point.Tile);
                if (state.SourceTile == null)
                {
                    return null;
                }
            }

            return state;
        }

        private static bool BindingMatchesPoint(PointBindingState state, ConstructionPointRef point)
        {
            if (state.SourceKind != point.SourceKind)
            {
                return false;
            }

            if (point.SourceKind == ConstructionPointSourceKind.ShapePoint)
            {
                return state.SourceShapeId == point.SourceShapeId;
            }

            if (point.SourceKind == ConstructionPointSourceKind.Tile)
            {
                var matchesSnapshot = point.Tile == state.SnapshotTileSeqId &&
                    Math.Abs(point.X - state.SnapshotX) <= 0.0000001d &&
                    Math.Abs(point.Y - state.SnapshotY) <= 0.0000001d;
                if (!state.Pinned)
                {
                    return matchesSnapshot;
                }

                // While PIN is on, the source floor may already have received its new seqID before
                // this LateUpdate runs. Accept either the rolling snapshot from the prior frame or
                // the floor's current seqID; a freshly picked different tile matches neither in the
                // normal case and therefore starts a new, unpinned binding.
                return matchesSnapshot ||
                    (state.SourceTile != null && point.Tile == state.SourceTile.seqID);
            }

            return false;
        }

        private bool TryResolveBindingSource(
            PointBindingState state,
            out double x,
            out double y,
            out int tileSeqId)
        {
            x = 0d;
            y = 0d;
            tileSeqId = state != null ? state.SnapshotTileSeqId : 0;
            if (state == null)
            {
                return false;
            }

            var tileSize = Math.Max(GameCompat.GetTileSize(1.5f), 0.000001f);
            if (state.SourceKind == ConstructionPointSourceKind.ShapePoint)
            {
                var sourceShape = FindShapeById(state.SourceShapeId);
                if (sourceShape == null || !ConstructionShapeTool.IsDrawn(sourceShape) ||
                    ConstructionShapeTool.GetDrawnType(sourceShape) != ConstructionShapeType.Point)
                {
                    return false;
                }

                var world = ConstructionShapeTool.GetDrawnPointWorld(sourceShape, 0);
                x = world.X / tileSize;
                y = world.Y / tileSize;
                return true;
            }

            if (state.SourceKind != ConstructionPointSourceKind.Tile)
            {
                return false;
            }

            if (state.SourceTile == null || !FloorStillExists(state.SourceTile))
            {
                // Some editor rebuilds replace floor components wholesale. Recover the original
                // floor by its last coordinate when possible instead of immediately breaking a pin.
                state.SourceTile = FindFloorNearTileCoordinate(state.SnapshotX, state.SnapshotY);
                if (state.SourceTile == null)
                {
                    return false;
                }
            }

            var position = state.SourceTile.transform.position;
            x = position.x / tileSize;
            y = position.y / tileSize;
            tileSeqId = state.SourceTile.seqID;
            return true;
        }

        private static scrFloor FindFloorBySeqId(int seqId)
        {
            var floors = GameCompat.GetFloors(scnEditor.instance);
            for (var i = 0; i < floors.Count; i++)
            {
                var floor = floors[i];
                if (floor != null && floor.seqID == seqId)
                {
                    return floor;
                }
            }
            return null;
        }

        private static bool FloorStillExists(scrFloor target)
        {
            if (target == null)
            {
                return false;
            }

            var floors = GameCompat.GetFloors(scnEditor.instance);
            for (var i = 0; i < floors.Count; i++)
            {
                if (ReferenceEquals(floors[i], target))
                {
                    return true;
                }
            }
            return false;
        }

        private static scrFloor FindFloorNearTileCoordinate(double x, double y)
        {
            var tileSize = Math.Max(GameCompat.GetTileSize(1.5f), 0.000001f);
            var floors = GameCompat.GetFloors(scnEditor.instance);
            scrFloor found = null;
            var best = double.MaxValue;
            for (var i = 0; i < floors.Count; i++)
            {
                var floor = floors[i];
                if (floor == null)
                {
                    continue;
                }

                var position = floor.transform.position;
                var dx = position.x / tileSize - x;
                var dy = position.y / tileSize - y;
                var distance = dx * dx + dy * dy;
                if (distance < best)
                {
                    best = distance;
                    found = floor;
                }
            }

            return best <= 0.000001d ? found : null;
        }

        private static ConstructionShape FindShapeById(int id)
        {
            var shapes = ConstructionShapeTool.Shapes;
            for (var i = 0; i < shapes.Count; i++)
            {
                if (shapes[i] != null && shapes[i].Id == id)
                {
                    return shapes[i];
                }
            }
            return null;
        }

        private static bool ContainsShape(IReadOnlyList<ConstructionShape> shapes, ConstructionShape shape)
        {
            if (shape == null)
            {
                return false;
            }

            for (var i = 0; i < shapes.Count; i++)
            {
                if (ReferenceEquals(shapes[i], shape))
                {
                    return true;
                }
            }
            return false;
        }

        private void RemovePointBinding(ConstructionShape shape, int pointIndex)
        {
            if (shape != null)
            {
                pointBindings.Remove(PointBindingKey(shape, pointIndex));
            }
        }

        private void ClearPointBindings()
        {
            pointBindings.Clear();
            shapeFirstPinText = null;
            shapeSecondPinText = null;
        }

        private static bool HasBindableSource(ConstructionPointRef point)
        {
            return point.SourceKind == ConstructionPointSourceKind.Tile ||
                (point.SourceKind == ConstructionPointSourceKind.ShapePoint && point.SourceShapeId > 0);
        }

        private static long PointBindingKey(ConstructionShape shape, int pointIndex)
        {
            var id = shape != null ? shape.Id : 0;
            return ((long)id << 2) | (uint)(pointIndex & 0x3);
        }

        private static string PointPinLabel()
        {
            switch (EuclidText.CurrentLocaleCode)
            {
                case "ko": return "고정";
                case "zh-CN": return "固定";
                case "zh-TW": return "固定";
                case "ja": return "固定";
                case "fr": return "Fixer";
                case "de": return "Fix";
                case "ru": return "Закр.";
                case "ro": return "Fix";
                case "pl": return "Przyp.";
                case "es": return "Fijar";
                case "pt-BR": return "Fixar";
                case "vi": return "Ghim";
                case "cs": return "Přip.";
                default: return "Pin";
            }
        }
    }
}
