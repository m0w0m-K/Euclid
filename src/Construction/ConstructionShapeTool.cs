using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Euclid
{
    internal enum ConstructionShapeType
    {
        Point,
        Line,
        PerpendicularBisector,
        Circle
    }

    internal sealed class ConstructionShape
    {
        internal int Id;
        internal string Name;
        internal ConstructionShapeType Type;
        // Per-shape display color. This is intentionally independent from the drawn geometry
        // snapshot so recoloring an existing shape does not require pressing Draw again.
        internal Color Color = new Color(0.25f, 0.95f, 1f, 1f);
        internal bool Visible = true;
        internal ConstructionPointRef First;
        internal ConstructionPointRef Second;
        internal bool Drawn;
        internal ConstructionShapeType DrawnType;
        internal ConstructionPointRef DrawnFirst;
        internal ConstructionPointRef DrawnSecond;
    }

    // Remembers where an endpoint came from so the editor can show whether the
    // current coordinates were picked from a tile, picked from a drawn point,
    // or entered manually. Tile provenance is informational only: selecting a tile
    // snapshots its coordinates and later tile renumbering must not move the shape.
    internal enum ConstructionPointSourceKind
    {
        Manual,
        Tile,
        ShapePoint,
    }

    internal struct ConstructionPointRef
    {
        internal bool HasTile;
        internal int Tile;
        internal double X;
        internal double Y;
        internal ConstructionPointSourceKind SourceKind;
        internal int SourceShapeId;
    }

    internal readonly struct ConstructionShapeSnapshot
    {
        internal ConstructionShapeSnapshot(
            int id,
            ConstructionShapeType type,
            bool selected,
            bool valid,
            Vector2d first,
            Vector2d second,
            double radius)
        {
            Id = id;
            Type = type;
            Selected = selected;
            Valid = valid;
            First = first;
            Second = second;
            Radius = radius;
        }

        internal int Id { get; }

        internal ConstructionShapeType Type { get; }

        internal bool Selected { get; }

        internal bool Valid { get; }

        internal Vector2d First { get; }

        internal Vector2d Second { get; }

        internal double Radius { get; }
    }

    internal readonly struct ConstructionLineGeometry
    {
        internal ConstructionLineGeometry(Vector2d anchor, Vector2d direction)
        {
            Anchor = anchor;
            Direction = direction;
        }

        internal Vector2d Anchor { get; }

        internal Vector2d Direction { get; }
    }

    // Owns construction-shape state and geometry. UI code should mutate shapes through this
    // class rather than editing the list directly so selection/drawing state stays consistent.
    // Keep rendering concerns in CameraFrameOverlay and editor widgets in EuclidPanel.
    internal static class ConstructionShapeTool
    {
        private const double MinLengthSqr = 0.000001d;
        private const double IntersectTolerance = 0.000000001d;
        private const double DuplicatePointToleranceSqr = 0.00001d * 0.00001d;

        private static readonly List<ConstructionShape> shapes = new List<ConstructionShape>();
        private static readonly HashSet<int> selectedIds = new HashSet<int>();
        private static int nextId = 1;
        private static int lastSelectedIndex = -1;

        internal static IReadOnlyList<ConstructionShape> Shapes => shapes;

        internal static IReadOnlyCollection<int> SelectedIds => selectedIds;

        internal static int SelectedCount => selectedIds.Count;

        internal static ConstructionShape PrimarySelectedShape
        {
            get
            {
                return TryGetSingleSelectedShape(out var shape) ? shape : null;
            }
        }

        internal static void EnsureDefault(MeasureSnapshot measure)
        {
            if (shapes.Count > 0)
            {
                return;
            }

            AddShape(measure);
        }

        internal static ConstructionShape AddShape(MeasureSnapshot measure)
        {
            var shape = new ConstructionShape
            {
                Id = AllocateId(),
                Type = ConstructionShapeType.Line,
                Drawn = false,
            };

            InitializePoints(shape, measure);
            shapes.Add(shape);
            selectedIds.Clear();
            selectedIds.Add(shape.Id);
            lastSelectedIndex = shapes.Count - 1;
            return shape;
        }

        internal static void DeleteSelected()
        {
            if (selectedIds.Count == 0)
            {
                return;
            }

            shapes.RemoveAll(shape => selectedIds.Contains(shape.Id));
            selectedIds.Clear();
            if (shapes.Count > 0)
            {
                var index = Mathf.Clamp(lastSelectedIndex, 0, shapes.Count - 1);
                selectedIds.Add(shapes[index].Id);
                lastSelectedIndex = index;
            }
            else
            {
                lastSelectedIndex = -1;
            }
        }

        internal static void ClearAll()
        {
            shapes.Clear();
            selectedIds.Clear();
            lastSelectedIndex = -1;
            nextId = 1;
        }

        internal static void ClearSelection()
        {
            selectedIds.Clear();
            lastSelectedIndex = -1;
        }

        private static int AllocateId()
        {
            var id = 1;
            while (IdExists(id))
            {
                id++;
            }

            nextId = Math.Max(nextId, id + 1);
            return id;
        }

        private static bool IdExists(int id)
        {
            for (var i = 0; i < shapes.Count; i++)
            {
                if (shapes[i].Id == id)
                {
                    return true;
                }
            }

            return false;
        }

        internal static void Select(int id, bool additive, bool range)
        {
            var index = shapes.FindIndex(shape => shape.Id == id);
            if (index < 0)
            {
                return;
            }

            if (range && lastSelectedIndex >= 0)
            {
                if (!additive)
                {
                    selectedIds.Clear();
                }

                var start = Math.Min(lastSelectedIndex, index);
                var end = Math.Max(lastSelectedIndex, index);
                for (var i = start; i <= end; i++)
                {
                    selectedIds.Add(shapes[i].Id);
                }
                return;
            }

            if (additive)
            {
                if (!selectedIds.Add(id))
                {
                    selectedIds.Remove(id);
                }
            }
            else
            {
                if (selectedIds.Count == 1 && selectedIds.Contains(id))
                {
                    selectedIds.Clear();
                    lastSelectedIndex = index;
                    return;
                }

                selectedIds.Clear();
                selectedIds.Add(id);
            }

            lastSelectedIndex = index;
        }

        internal static void SetType(ConstructionShape shape, ConstructionShapeType type)
        {
            if (shape == null)
            {
                return;
            }

            if (shape.Type == type)
            {
                return;
            }

            shape.Type = type;
        }

        internal static void SetName(ConstructionShape shape, string name)
        {
            if (shape == null)
            {
                return;
            }

            shape.Name = name ?? string.Empty;
        }

        internal static Color GetColor(ConstructionShape shape)
        {
            return shape != null ? shape.Color : new Color(0.25f, 0.95f, 1f, 1f);
        }

        internal static void SetColor(ConstructionShape shape, Color color)
        {
            if (shape == null)
            {
                return;
            }

            shape.Color = color;
        }

        internal static void SetTile(ConstructionShape shape, int pointIndex, string text)
        {
            if (shape == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                var point = GetPoint(shape, pointIndex);
                point.HasTile = false;
                point.Tile = 0;
                point.SourceKind = ConstructionPointSourceKind.Manual;
                point.SourceShapeId = 0;
                SetPoint(shape, pointIndex, point);
                return;
            }

            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tile))
            {
                return;
            }

            SetTile(shape, pointIndex, tile);
        }

        internal static void SetTile(ConstructionShape shape, int pointIndex, int tile)
        {
            if (shape == null)
            {
                return;
            }

            var point = GetPoint(shape, pointIndex);
            point.HasTile = true;
            point.Tile = tile;
            point.SourceKind = ConstructionPointSourceKind.Tile;
            point.SourceShapeId = 0;
            var coord = WorldToTileUnits(GetTileWorld(tile));
            point.X = coord.X;
            point.Y = coord.Y;
            SetPoint(shape, pointIndex, point);
        }

        internal static void SetCoordinate(ConstructionShape shape, int pointIndex, string xText, string yText)
        {
            if (shape == null ||
                !double.TryParse(xText, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !double.TryParse(yText, NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            {
                return;
            }

            var point = GetPoint(shape, pointIndex);
            point.HasTile = false;
            point.Tile = 0;
            point.SourceKind = ConstructionPointSourceKind.Manual;
            point.SourceShapeId = 0;
            point.X = x;
            point.Y = y;
            SetPoint(shape, pointIndex, point);
        }

        internal static void SetPoint(ConstructionShape shape, int pointIndex, ConstructionPointRef point)
        {
            if (shape == null)
            {
                return;
            }

            SetPointInternal(shape, pointIndex, point);
        }

        internal static bool TryMakePointFromTile(string text, out ConstructionPointRef point)
        {
            point = default;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tile))
            {
                return false;
            }

            point = FromTile(tile, GetTileWorld(tile));
            return true;
        }

        internal static ConstructionPointRef MakePointFromWorld(Vector2d world)
        {
            var coord = WorldToTileUnits(world.ToVector2());
            return new ConstructionPointRef
            {
                HasTile = false,
                Tile = 0,
                X = coord.X,
                Y = coord.Y,
                SourceKind = ConstructionPointSourceKind.Manual,
                SourceShapeId = 0,
            };
        }

        internal static bool TryGetPointForPick(ConstructionShape shape, out ConstructionPointRef point)
        {
            point = default;
            // Position picking from another construction shape is intentionally limited to
            // points that are already drawn in the editor. Clicking a row in the shape list
            // is ordinary list selection and must not complete a position pick.
            if (shape == null || !shape.Drawn || shape.DrawnType != ConstructionShapeType.Point)
            {
                return false;
            }

            var world = GetDrawnPointWorld(shape, 0);
            point = MakePointFromWorld(world);
            point.SourceKind = ConstructionPointSourceKind.ShapePoint;
            point.SourceShapeId = shape.Id;
            return true;
        }

        internal static void ClearPointSource(ConstructionShape shape, int pointIndex)
        {
            if (shape == null)
            {
                return;
            }

            // Drop provenance while preserving the coordinate snapshot exactly as displayed.
            var point = GetPointForDisplay(shape, pointIndex);
            point.HasTile = false;
            point.Tile = 0;
            point.SourceKind = ConstructionPointSourceKind.Manual;
            point.SourceShapeId = 0;
            SetPoint(shape, pointIndex, point);
        }

        internal static void DrawShape(ConstructionShape shape)
        {
            if (shape == null)
            {
                return;
            }

            CommitDrawnGeometry(shape);
        }

        private static void CommitDrawnGeometry(ConstructionShape shape)
        {
            if (shape == null)
            {
                return;
            }

            shape.DrawnType = shape.Type;
            shape.DrawnFirst = shape.First;
            shape.DrawnSecond = shape.Second;
            shape.Drawn = true;
        }

        internal static bool IsDrawn(ConstructionShape shape)
        {
            return shape != null && shape.Drawn;
        }

        internal static bool IsVisible(ConstructionShape shape)
        {
            return shape == null || shape.Visible;
        }

        internal static void ToggleVisible(ConstructionShape shape)
        {
            if (shape != null)
            {
                shape.Visible = !shape.Visible;
            }
        }

        internal static bool TryGetSnapPointForSingleSelectedShape(Vector2d source, out Vector2d point)
        {
            point = Vector2d.Zero;
            if (!TryGetSingleSelectedShape(out var shape) || !IsDrawn(shape))
            {
                return false;
            }

            if (shape.DrawnType == ConstructionShapeType.Point)
            {
                point = GetDrawnPointWorld(shape, 0);
                return true;
            }

            if (shape.DrawnType == ConstructionShapeType.Circle && TryGetDrawnCircle(shape, out var circle))
            {
                var delta = source - circle.Center;
                if (delta.SqrMagnitude <= MinLengthSqr)
                {
                    point = circle.Center + new Vector2d(circle.Radius, 0d);
                    return true;
                }

                point = circle.Center + delta / delta.Magnitude * circle.Radius;
                return true;
            }

            if (TryGetDrawnLineGeometry(shape, out var line))
            {
                var parameter = Vector2d.Dot(source - line.Anchor, line.Direction) /
                    Vector2d.Dot(line.Direction, line.Direction);
                point = line.Anchor + line.Direction * parameter;
                return true;
            }

            return false;
        }

        internal static bool CanSnapToSingleSelectedShape()
        {
            return TryGetSingleSelectedShape(out var shape) && IsDrawn(shape);
        }

        internal static string GetShapeName(ConstructionShape shape)
        {
            if (shape == null)
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(shape.Name) ? GetDefaultShapeName(shape) : shape.Name.Trim();
        }

        internal static string GetDefaultShapeName(ConstructionShape shape)
        {
            return shape == null ? string.Empty : $"{GetTypeLabel(shape.Type)} {shape.Id}";
        }

        internal static string GetTypeLabel(ConstructionShapeType type)
        {
            switch (type)
            {
                case ConstructionShapeType.Point:
                    return EuclidText.Get("shape.point");
                case ConstructionShapeType.Line:
                    return EuclidText.Get("shape.line");
                case ConstructionShapeType.PerpendicularBisector:
                    return EuclidText.Get("shape.perpendicular");
                case ConstructionShapeType.Circle:
                    return EuclidText.Get("shape.circle");
                default:
                    return type.ToString();
            }
        }

        internal static ConstructionPointRef GetPoint(ConstructionShape shape, int index)
        {
            return index == 0 ? shape.First : shape.Second;
        }

        internal static ConstructionShapeType GetDrawnType(ConstructionShape shape)
        {
            return shape != null && shape.Drawn ? shape.DrawnType : default;
        }

        internal static ConstructionPointRef GetPointForDisplay(ConstructionShape shape, int index)
        {
            if (shape == null)
            {
                return default;
            }

            // A tile pick is a coordinate snapshot, not a live reference to seqID.
            // Keep the recorded Tile/SourceKind only for the UI source label. If tiles are
            // inserted before it and seqIDs change, the construction endpoint stays put.
            return GetPoint(shape, index);
        }

        internal static bool IsSelected(int id)
        {
            return selectedIds.Contains(id);
        }

        internal static Vector2d GetPointWorld(ConstructionShape shape, int index)
        {
            return GetPointWorld(shape, index, useDrawn: false);
        }

        internal static Vector2d GetDrawnPointWorld(ConstructionShape shape, int index)
        {
            return GetPointWorld(shape, index, useDrawn: true);
        }

        private static Vector2d GetPointWorld(ConstructionShape shape, int index, bool useDrawn)
        {
            var point = useDrawn ? GetDrawnPoint(shape, index) : GetPoint(shape, index);
            // Never resolve a picked tile again by seqID here. X/Y are the authoritative
            // coordinates captured when the endpoint was selected.
            return TileUnitsToWorld(point.X, point.Y);
        }

        private static ConstructionPointRef GetDrawnPoint(ConstructionShape shape, int index)
        {
            return index == 0 ? shape.DrawnFirst : shape.DrawnSecond;
        }

        internal static string Format(double value)
        {
            return value.ToString("0.#####", CultureInfo.InvariantCulture);
        }

        internal static List<ConstructionShapeSnapshot> GetSnapshots()
        {
            var snapshots = new List<ConstructionShapeSnapshot>();
            for (var i = 0; i < shapes.Count; i++)
            {
                var shape = shapes[i];
                if (!shape.Drawn)
                {
                    continue;
                }

                var type = shape.DrawnType;
                var first = GetDrawnPointWorld(shape, 0);
                var second = GetDrawnPointWorld(shape, 1);
                var radius = type == ConstructionShapeType.Circle
                    ? Math.Sqrt((second - first).SqrMagnitude)
                    : 0d;
                var valid = type == ConstructionShapeType.Point || (second - first).SqrMagnitude > MinLengthSqr;
                snapshots.Add(new ConstructionShapeSnapshot(shape.Id, type, selectedIds.Contains(shape.Id), valid, first, second, radius));
            }

            return snapshots;
        }

        internal static bool TryGetLineGeometry(ConstructionShape shape, out ConstructionLineGeometry line)
        {
            return TryGetLineGeometry(shape, useDrawn: false, out line);
        }

        internal static bool TryGetDrawnLineGeometry(ConstructionShape shape, out ConstructionLineGeometry line)
        {
            if (!IsDrawn(shape))
            {
                line = default;
                return false;
            }

            return TryGetLineGeometry(shape, useDrawn: true, out line);
        }

        private static bool TryGetLineGeometry(ConstructionShape shape, bool useDrawn, out ConstructionLineGeometry line)
        {
            line = default;
            if (shape == null)
            {
                return false;
            }

            var type = useDrawn ? shape.DrawnType : shape.Type;
            var first = GetPointWorld(shape, 0, useDrawn);
            var second = GetPointWorld(shape, 1, useDrawn);
            var delta = second - first;
            if (delta.SqrMagnitude <= MinLengthSqr)
            {
                return false;
            }

            if (type == ConstructionShapeType.Line)
            {
                line = new ConstructionLineGeometry(first, delta);
                return true;
            }

            if (type == ConstructionShapeType.PerpendicularBisector)
            {
                var midpoint = (first + second) * 0.5d;
                line = new ConstructionLineGeometry(midpoint, new Vector2d(-delta.Y, delta.X));
                return true;
            }

            return false;
        }

        internal static bool TryGetPrimaryGuideLine(out GuideLineSnapshot snapshot)
        {
            snapshot = default;
            var shape = PrimarySelectedShape;
            if (!TryGetDrawnLineGeometry(shape, out var line))
            {
                return false;
            }

            snapshot = new GuideLineSnapshot(true, line.Anchor, line.Direction, 0, string.Empty);
            return true;
        }

        internal static void CreateIntersectionsFromSelection()
        {
            var points = CollectIntersectionsFromSelection(stopAtFirst: false);
            if (points.Count == 0)
            {
                return;
            }

            selectedIds.Clear();
            for (var i = 0; i < points.Count; i++)
            {
                var shape = AddPointAt(points[i]);
                selectedIds.Add(shape.Id);
                lastSelectedIndex = shapes.Count - 1;
            }
        }

        internal static bool CanCreateIntersectionsFromSelection()
        {
            return CollectIntersectionsFromSelection(stopAtFirst: true).Count > 0;
        }

        internal static void CreateLinesFromSelectedPoints()
        {
            if (!TryCollectLineCandidatesFromSelectedPoints(stopAtFirst: false, out var candidates) ||
                candidates.Count == 0)
            {
                return;
            }

            var firstCreatedIndex = shapes.Count;
            var created = new List<ConstructionShape>(candidates.Count);
            for (var i = 0; i < candidates.Count; i++)
            {
                created.Add(AddLineAt(candidates[i].First, candidates[i].Second));
            }

            selectedIds.Clear();
            for (var i = 0; i < created.Count; i++)
            {
                selectedIds.Add(created[i].Id);
                lastSelectedIndex = firstCreatedIndex + i;
            }
        }

        internal static bool CanCreateLinesFromSelectedPoints()
        {
            return TryCollectLineCandidatesFromSelectedPoints(stopAtFirst: true, out var candidates) &&
                candidates.Count > 0;
        }

        private static bool TryGetSingleSelectedShape(out ConstructionShape shape)
        {
            shape = null;
            if (selectedIds.Count != 1)
            {
                return false;
            }

            for (var i = 0; i < shapes.Count; i++)
            {
                if (selectedIds.Contains(shapes[i].Id))
                {
                    shape = shapes[i];
                    return true;
                }
            }

            return false;
        }

        private static List<Vector2d> CollectIntersectionsFromSelection(bool stopAtFirst)
        {
            var points = new List<Vector2d>();
            var selected = GetSelectedShapes();
            if (selected.Count < 2)
            {
                return points;
            }

            for (var i = 0; i < selected.Count - 1; i++)
            {
                for (var j = i + 1; j < selected.Count; j++)
                {
                    var previousCount = points.Count;
                    AddIntersections(selected[i], selected[j], points);
                    if (stopAtFirst && points.Count > previousCount)
                    {
                        return points;
                    }
                }
            }

            return points;
        }

        private static bool TryCollectLineCandidatesFromSelectedPoints(bool stopAtFirst, out List<LineCandidate> candidates)
        {
            candidates = new List<LineCandidate>();
            if (!TryGetSelectedDrawnPoints(out var selectedPoints) || selectedPoints.Count < 2)
            {
                return false;
            }

            var knownLines = GetDrawnLineGeometries();
            for (var i = 0; i < selectedPoints.Count - 1; i++)
            {
                var first = GetDrawnPointWorld(selectedPoints[i], 0);
                for (var j = i + 1; j < selectedPoints.Count; j++)
                {
                    var second = GetDrawnPointWorld(selectedPoints[j], 0);
                    var line = new ConstructionLineGeometry(first, second - first);
                    if (line.Direction.SqrMagnitude <= MinLengthSqr || ContainsEquivalentLine(knownLines, line))
                    {
                        continue;
                    }

                    candidates.Add(new LineCandidate(first, second));
                    knownLines.Add(line);
                    if (stopAtFirst)
                    {
                        return true;
                    }
                }
            }

            return candidates.Count > 0;
        }

        private static List<ConstructionLineGeometry> GetDrawnLineGeometries()
        {
            var lines = new List<ConstructionLineGeometry>();
            for (var i = 0; i < shapes.Count; i++)
            {
                if (TryGetDrawnLineGeometry(shapes[i], out var line))
                {
                    lines.Add(line);
                }
            }

            return lines;
        }

        private static List<ConstructionShape> GetSelectedShapes()
        {
            var selected = new List<ConstructionShape>();
            for (var i = 0; i < shapes.Count; i++)
            {
                if (selectedIds.Contains(shapes[i].Id))
                {
                    selected.Add(shapes[i]);
                }
            }

            return selected;
        }

        private static bool TryGetSelectedDrawnPoints(out List<ConstructionShape> selectedPoints)
        {
            selectedPoints = new List<ConstructionShape>();
            for (var i = 0; i < shapes.Count; i++)
            {
                var shape = shapes[i];
                if (!selectedIds.Contains(shape.Id))
                {
                    continue;
                }

                if (!IsDrawn(shape) || shape.DrawnType != ConstructionShapeType.Point)
                {
                    selectedPoints.Clear();
                    return false;
                }

                selectedPoints.Add(shape);
            }

            return true;
        }

        private static ConstructionShape AddPointAt(Vector2d world)
        {
            var coord = WorldToTileUnits(world.ToVector2());
            var shape = new ConstructionShape
            {
                Id = AllocateId(),
                Type = ConstructionShapeType.Point,
                First = new ConstructionPointRef { HasTile = false, Tile = 0, X = coord.X, Y = coord.Y, SourceKind = ConstructionPointSourceKind.Manual },
                Second = new ConstructionPointRef { HasTile = false, Tile = 0, X = coord.X, Y = coord.Y, SourceKind = ConstructionPointSourceKind.Manual },
            };
            CommitDrawnGeometry(shape);
            shapes.Add(shape);
            return shape;
        }

        private static ConstructionShape AddLineAt(Vector2d firstWorld, Vector2d secondWorld)
        {
            var first = WorldToTileUnits(firstWorld.ToVector2());
            var second = WorldToTileUnits(secondWorld.ToVector2());
            var shape = new ConstructionShape
            {
                Id = AllocateId(),
                Type = ConstructionShapeType.Line,
                First = new ConstructionPointRef { HasTile = false, Tile = 0, X = first.X, Y = first.Y, SourceKind = ConstructionPointSourceKind.Manual },
                Second = new ConstructionPointRef { HasTile = false, Tile = 0, X = second.X, Y = second.Y, SourceKind = ConstructionPointSourceKind.Manual },
            };
            CommitDrawnGeometry(shape);
            shapes.Add(shape);
            return shape;
        }

        private static void AddIntersections(ConstructionShape a, ConstructionShape b, List<Vector2d> points)
        {
            var aLine = TryGetDrawnLineGeometry(a, out var lineA);
            var bLine = TryGetDrawnLineGeometry(b, out var lineB);
            var aCircle = TryGetDrawnCircle(a, out var circleA);
            var bCircle = TryGetDrawnCircle(b, out var circleB);

            if (aLine && bLine)
            {
                if (TryIntersectLines(lineA, lineB, out var point))
                {
                    AddUnique(points, point);
                }
                return;
            }

            if (aLine && bCircle)
            {
                AddLineCircleIntersections(lineA, circleB, points);
                return;
            }

            if (aCircle && bLine)
            {
                AddLineCircleIntersections(lineB, circleA, points);
                return;
            }

            if (aCircle && bCircle)
            {
                AddCircleCircleIntersections(circleA, circleB, points);
            }
        }

        private static bool TryIntersectLines(ConstructionLineGeometry a, ConstructionLineGeometry b, out Vector2d point)
        {
            point = Vector2d.Zero;
            var cross = Cross(a.Direction, b.Direction);
            var tolerance = IntersectTolerance * Math.Max(1d, a.Direction.Magnitude * b.Direction.Magnitude);
            if (Math.Abs(cross) <= tolerance)
            {
                return false;
            }

            var delta = b.Anchor - a.Anchor;
            var t = Cross(delta, b.Direction) / cross;
            point = a.Anchor + a.Direction * t;
            return true;
        }

        private static bool ContainsEquivalentLine(List<ConstructionLineGeometry> lines, ConstructionLineGeometry candidate)
        {
            for (var i = 0; i < lines.Count; i++)
            {
                if (AreEquivalentLines(lines[i], candidate))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool AreEquivalentLines(ConstructionLineGeometry a, ConstructionLineGeometry b)
        {
            if (a.Direction.SqrMagnitude <= MinLengthSqr || b.Direction.SqrMagnitude <= MinLengthSqr)
            {
                return false;
            }

            var directionTolerance = IntersectTolerance * Math.Max(1d, a.Direction.Magnitude * b.Direction.Magnitude);
            if (Math.Abs(Cross(a.Direction, b.Direction)) > directionTolerance)
            {
                return false;
            }

            var delta = b.Anchor - a.Anchor;
            var anchorTolerance = IntersectTolerance * Math.Max(1d, a.Direction.Magnitude * Math.Max(1d, delta.Magnitude));
            return Math.Abs(Cross(delta, a.Direction)) <= anchorTolerance;
        }

        private static void AddLineCircleIntersections(ConstructionLineGeometry line, CircleGeometry circle, List<Vector2d> points)
        {
            var f = line.Anchor - circle.Center;
            var a = Vector2d.Dot(line.Direction, line.Direction);
            var b = 2d * Vector2d.Dot(f, line.Direction);
            var c = Vector2d.Dot(f, f) - circle.Radius * circle.Radius;
            var discriminant = b * b - 4d * a * c;
            if (discriminant < -IntersectTolerance)
            {
                return;
            }

            if (discriminant < 0d)
            {
                discriminant = 0d;
            }

            var sqrt = Math.Sqrt(discriminant);
            AddUnique(points, line.Anchor + line.Direction * ((-b - sqrt) / (2d * a)));
            if (sqrt > IntersectTolerance)
            {
                AddUnique(points, line.Anchor + line.Direction * ((-b + sqrt) / (2d * a)));
            }
        }

        private static void AddCircleCircleIntersections(CircleGeometry a, CircleGeometry b, List<Vector2d> points)
        {
            var delta = b.Center - a.Center;
            var distance = delta.Magnitude;
            if (distance <= IntersectTolerance ||
                distance > a.Radius + b.Radius + IntersectTolerance ||
                distance < Math.Abs(a.Radius - b.Radius) - IntersectTolerance)
            {
                return;
            }

            var unit = delta / distance;
            var along = (a.Radius * a.Radius - b.Radius * b.Radius + distance * distance) / (2d * distance);
            var heightSqr = a.Radius * a.Radius - along * along;
            if (heightSqr < -IntersectTolerance)
            {
                return;
            }

            var height = Math.Sqrt(Math.Max(0d, heightSqr));
            var basePoint = a.Center + unit * along;
            var perpendicular = new Vector2d(-unit.Y, unit.X);
            AddUnique(points, basePoint + perpendicular * height);
            if (height > IntersectTolerance)
            {
                AddUnique(points, basePoint - perpendicular * height);
            }
        }

        private static bool TryGetCircle(ConstructionShape shape, out CircleGeometry circle)
        {
            return TryGetCircle(shape, useDrawn: false, out circle);
        }

        private static bool TryGetDrawnCircle(ConstructionShape shape, out CircleGeometry circle)
        {
            if (!IsDrawn(shape))
            {
                circle = default;
                return false;
            }

            return TryGetCircle(shape, useDrawn: true, out circle);
        }

        private static bool TryGetCircle(ConstructionShape shape, bool useDrawn, out CircleGeometry circle)
        {
            circle = default;
            if (shape == null)
            {
                return false;
            }

            var type = useDrawn ? shape.DrawnType : shape.Type;
            if (type != ConstructionShapeType.Circle)
            {
                return false;
            }

            var center = GetPointWorld(shape, 0, useDrawn);
            var edge = GetPointWorld(shape, 1, useDrawn);
            var radius = Math.Sqrt((edge - center).SqrMagnitude);
            if (radius <= 0.000001d)
            {
                return false;
            }

            circle = new CircleGeometry(center, radius);
            return true;
        }

        private static void AddUnique(List<Vector2d> points, Vector2d point)
        {
            for (var i = 0; i < points.Count; i++)
            {
                if ((points[i] - point).SqrMagnitude <= DuplicatePointToleranceSqr)
                {
                    return;
                }
            }

            points.Add(point);
        }

        private static void InitializePoints(ConstructionShape shape, MeasureSnapshot measure)
        {
            if (TileSelectionOrderTracker.TryGetTileAtSelectionIndex(0, out var firstTile))
            {
                shape.First = FromTile(firstTile, GetTileWorld(firstTile));
                if (TileSelectionOrderTracker.TryGetTileAtSelectionIndex(1, out var secondTile))
                {
                    shape.Second = FromTile(secondTile, GetTileWorld(secondTile));
                }
                else if (measure.State == MeasureState.Ready || measure.State == MeasureState.SingleSelection)
                {
                    shape.Second = FromTile(measure.EndSeqId, measure.End);
                }
                else
                {
                    shape.Second = FromTile(firstTile + 1, GetTileWorld(firstTile + 1));
                }

                if (shape.First.Tile == shape.Second.Tile)
                {
                    shape.Second = FromTile(shape.First.Tile + 1, GetTileWorld(shape.First.Tile + 1));
                }

                return;
            }

            if (measure.State == MeasureState.Ready || measure.State == MeasureState.SingleSelection)
            {
                shape.First = FromTile(measure.StartSeqId, measure.Start);
                shape.Second = FromTile(measure.EndSeqId, measure.End);
                if (shape.First.Tile == shape.Second.Tile)
                {
                    shape.Second.Tile = shape.First.Tile + 1;
                    var secondWorld = GetTileWorld(shape.Second.Tile);
                    var secondCoord = WorldToTileUnits(secondWorld);
                    shape.Second.X = secondCoord.X;
                    shape.Second.Y = secondCoord.Y;
                }
                return;
            }

            shape.First = FromTile(0, GetTileWorld(0));
            shape.Second = FromTile(1, GetTileWorld(1));
        }

        private static ConstructionPointRef FromTile(int tile, Vector2 world)
        {
            var coord = WorldToTileUnits(world);
            return new ConstructionPointRef
            {
                HasTile = true,
                Tile = tile,
                X = coord.X,
                Y = coord.Y,
                SourceKind = ConstructionPointSourceKind.Tile,
                SourceShapeId = 0,
            };
        }

        private static void SetPointInternal(ConstructionShape shape, int index, ConstructionPointRef point)
        {
            if (index == 0)
            {
                shape.First = point;
            }
            else
            {
                shape.Second = point;
            }
        }

        private static Vector2d TileUnitsToWorld(double x, double y)
        {
            var tileSize = GetTileSize();
            return new Vector2d(x * tileSize, y * tileSize);
        }

        private static Vector2d WorldToTileUnits(Vector2 world)
        {
            var tileSize = GetTileSize();
            return new Vector2d(world.x / tileSize, world.y / tileSize);
        }

        private static Vector2 GetTileWorld(int tile)
        {
            try
            {
                var floors = GameCompat.GetFloors(scnEditor.instance);
                for (var i = 0; i < floors.Count; i++)
                {
                    var floor = floors[i];
                    if (floor != null && floor.seqID == tile)
                    {
                        var position = floor.transform.position;
                        return new Vector2(position.x, position.y);
                    }
                }

                if (tile >= 0 && tile < floors.Count && floors[tile] != null)
                {
                    var position = floors[tile].transform.position;
                    return new Vector2(position.x, position.y);
                }
            }
            catch (Exception)
            {
                // Fall back below.
            }

            return Vector2.zero;
        }

        private static double GetTileSize()
        {
            return GameCompat.GetTileSize(1.5f);
        }

        private static double Cross(Vector2d a, Vector2d b)
        {
            return a.X * b.Y - a.Y * b.X;
        }

        private readonly struct CircleGeometry
        {
            internal CircleGeometry(Vector2d center, double radius)
            {
                Center = center;
                Radius = radius;
            }

            internal Vector2d Center { get; }

            internal double Radius { get; }
        }

        private readonly struct LineCandidate
        {
            internal LineCandidate(Vector2d first, Vector2d second)
            {
                First = first;
                Second = second;
            }

            internal Vector2d First { get; }

            internal Vector2d Second { get; }
        }
    }
}
