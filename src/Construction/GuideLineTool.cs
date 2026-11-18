using System;
using System.Globalization;
using UnityEngine;

namespace Euclid
{
    internal readonly struct GuideLineSnapshot
    {
        private const double MinDirectionSqrMagnitude = 0.000001d;

        internal GuideLineSnapshot(bool active, Vector2d anchor, Vector2d direction, int revision, string message)
        {
            Active = active;
            AnchorD = anchor;
            DirectionD = direction;
            Revision = revision;
            Message = message;
        }

        internal bool Active { get; }

        internal Vector2d AnchorD { get; }

        internal Vector2d DirectionD { get; }

        internal int Revision { get; }

        internal string Message { get; }

        internal Vector2 Anchor => AnchorD.ToVector2();

        internal Vector2 Direction => DirectionD.ToVector2();

        internal bool IsValid => Active && DirectionD.SqrMagnitude > MinDirectionSqrMagnitude;

        internal double DirectionLength => IsValid ? DirectionD.Magnitude : 1d;

        internal Vector2 Project(Vector2 point)
        {
            return PointAt(ParameterOf(point)).ToVector2();
        }

        internal double ParameterOf(Vector2 point)
        {
            if (!IsValid)
            {
                return 0d;
            }

            var delta = new Vector2d(point) - AnchorD;
            return Vector2d.Dot(delta, DirectionD) / Vector2d.Dot(DirectionD, DirectionD);
        }

        internal Vector2d PointAt(double t)
        {
            return AnchorD + DirectionD * t;
        }
    }

    internal readonly struct ConstructionCircleSnapshot
    {
        private const double MinRadius = 0.000001d;

        internal ConstructionCircleSnapshot(bool active, Vector2d center, double radius)
        {
            Active = active;
            CenterD = center;
            Radius = radius;
        }

        internal bool Active { get; }

        internal Vector2d CenterD { get; }

        internal double Radius { get; }

        internal bool IsValid => Active && Radius > MinRadius;

        internal Vector2 Center => CenterD.ToVector2();
    }

    // Pure-ish guide-line state/calculation layer used by both the overlay and coordinate snapper.
    // Keep Unity editor selection/event mutation out of this class where possible; that belongs in
    // CoordinateSnapTool. This separation makes geometry bugs testable independently of UI state.
    internal static class GuideLineTool
    {
        private const double ParallelCrossTolerance = 0.000000001d;

        internal static bool Active { get; set; }

        internal static bool SnapCameraDrag { get; set; } = true;

        internal static bool SnapSelectedShapeDrag { get; set; }

        internal static bool EnableCameraDrag { get; set; } = true;

        private static string coordinateKeyText = "position";
        private static string stepText = "1";
        private static Vector2d anchor = Vector2d.Zero;
        private static Vector2d direction = Vector2d.Right;
        private static int revision;
        private static string anchorXText = "0";
        private static string anchorYText = "0";
        private static string directionXText = "1";
        private static string directionYText = "0";
        private static string message = EuclidText.Get("message.noGuide");
        private static SavedGuideLine savedLine1;
        private static SavedGuideLine savedLine2;
        private static bool circleActive;
        private static Vector2d circleCenter = Vector2d.Zero;
        private static double circleRadius = 1d;
        private static string circleCenterXText = "0";
        private static string circleCenterYText = "0";
        private static string circleRadiusText = "1";

        internal static GuideLineSnapshot Snapshot => new GuideLineSnapshot(Active, anchor, direction, revision, message);

        internal static ConstructionCircleSnapshot CircleSnapshot => new ConstructionCircleSnapshot(circleActive, circleCenter, circleRadius);

        internal static Vector2 Anchor => anchor.ToVector2();

        internal static Vector2 Direction => direction.ToVector2();

        internal static Vector2 CircleCenter => circleCenter.ToVector2();

        internal static double CircleRadius => circleRadius;

        internal static string CoordinateKeyText
        {
            get => coordinateKeyText;
            set => coordinateKeyText = value ?? string.Empty;
        }

        internal static string StepText
        {
            get => stepText;
            set => stepText = value ?? string.Empty;
        }

        internal static string Message => message;

        internal static bool TryGetSavedLine(int slot, out GuideLineSnapshot snapshot)
        {
            var saved = slot == 1 ? savedLine1 : savedLine2;
            if (saved.HasValue)
            {
                snapshot = new GuideLineSnapshot(true, saved.Anchor, saved.Direction, 0, string.Empty);
                return true;
            }

            snapshot = default;
            return false;
        }

        internal static bool TryGetIntersection(out Vector2d point)
        {
            point = Vector2d.Zero;
            if (!savedLine1.HasValue || !savedLine2.HasValue)
            {
                return false;
            }

            var cross = Cross(savedLine1.Direction, savedLine2.Direction);
            var tolerance = ParallelCrossTolerance * Math.Max(1d, savedLine1.Direction.Magnitude * savedLine2.Direction.Magnitude);
            if (Math.Abs(cross) <= tolerance)
            {
                return false;
            }

            var delta = savedLine2.Anchor - savedLine1.Anchor;
            var t = Cross(delta, savedLine2.Direction) / cross;
            point = savedLine1.Anchor + savedLine1.Direction * t;
            return true;
        }

        internal static void UseSelectedLine(MeasureSnapshot measure)
        {
            if (measure.State != MeasureState.Ready)
            {
                message = EuclidText.Get("message.selectTwoTiles");
                return;
            }

            SetLine(measure.Start, measure.Delta, EuclidText.Format("message.lineThroughTiles", measure.StartSeqId, measure.EndSeqId));
        }

        internal static void UseSelectedPerpendicular(MeasureSnapshot measure)
        {
            if (measure.State != MeasureState.Ready)
            {
                message = EuclidText.Get("message.selectTwoTiles");
                return;
            }

            var perpendicular = new Vector2(-measure.Delta.y, measure.Delta.x);
            SetLine(measure.Midpoint, perpendicular, EuclidText.Format("message.perpendicularAtMidpoint", measure.StartSeqId, measure.EndSeqId));
        }

        internal static bool ApplyFields()
        {
            if (!TryParse(anchorXText, out var ax) ||
                !TryParse(anchorYText, out var ay) ||
                !TryParse(directionXText, out var dx) ||
                !TryParse(directionYText, out var dy))
            {
                message = EuclidText.Get("message.invalidLine");
                return false;
            }

            var tileSize = GetTileSize();
            return SetLine(
                new Vector2d(ax * tileSize, ay * tileSize),
                new Vector2d(dx * tileSize, dy * tileSize),
                EuclidText.Get("message.customGuide"));
        }

        internal static void SetFieldTexts(string ax, string ay, string dx, string dy)
        {
            anchorXText = ax ?? string.Empty;
            anchorYText = ay ?? string.Empty;
            directionXText = dx ?? string.Empty;
            directionYText = dy ?? string.Empty;
        }

        internal static void SetCircleFieldTexts(string centerX, string centerY, string radius)
        {
            circleCenterXText = centerX ?? string.Empty;
            circleCenterYText = centerY ?? string.Empty;
            circleRadiusText = radius ?? string.Empty;
        }

        internal static bool ApplyCircleFields()
        {
            if (!TryParse(circleCenterXText, out var cx) ||
                !TryParse(circleCenterYText, out var cy) ||
                !TryParse(circleRadiusText, out var radius))
            {
                message = EuclidText.Get("message.invalidCircle");
                return false;
            }

            if (radius <= 0d)
            {
                message = EuclidText.Get("message.invalidCircleRadius");
                return false;
            }

            var tileSize = GetTileSize();
            SetCircle(new Vector2d(cx * tileSize, cy * tileSize), radius * tileSize, EuclidText.Get("message.customCircle"));
            return true;
        }

        internal static void UseSelectedCircle(MeasureSnapshot measure)
        {
            if (measure.State != MeasureState.Ready)
            {
                message = EuclidText.Get("message.selectTwoTiles");
                return;
            }

            SetCircle(
                new Vector2d(measure.Start),
                measure.Distance,
                EuclidText.Format("message.circleFromTiles", measure.StartSeqId, measure.EndSeqId));
        }

        internal static bool SetLineFromValues(Vector2 newAnchor, Vector2 newDirection, string newMessage)
        {
            return SetLine(newAnchor, newDirection, newMessage);
        }

        internal static void SaveCurrentLine(int slot)
        {
            if (!Snapshot.IsValid)
            {
                message = EuclidText.Get("message.guideInactive");
                return;
            }

            var saved = new SavedGuideLine(anchor, direction);
            if (slot == 1)
            {
                savedLine1 = saved;
                message = EuclidText.Get("message.savedLine1");
                return;
            }

            savedLine2 = saved;
            message = EuclidText.Get("message.savedLine2");
        }

        internal static void SnapSelectedToIntersection(CameraFrameSnapshot cameraFrame)
        {
            if (!savedLine1.HasValue || !savedLine2.HasValue)
            {
                message = EuclidText.Get("message.needTwoSavedLines");
                return;
            }

            if (!TryGetIntersection(out var point))
            {
                message = EuclidText.Get("message.parallelLines");
                return;
            }

            if (CoordinateSnapTool.TrySnapToPoint(cameraFrame, point, coordinateKeyText, out var result))
            {
                message = result;
                return;
            }

            message = result;
        }

        internal static void SnapSelectedToShape(CameraFrameSnapshot cameraFrame)
        {
            if (CoordinateSnapTool.TrySnapSelectedTargetToSelectedShape(cameraFrame, coordinateKeyText, out var result))
            {
                message = result;
                return;
            }

            message = result;
        }

        internal static void ToggleSelectedShapeSnap(CameraFrameSnapshot cameraFrame)
        {
            if (SnapSelectedShapeDrag)
            {
                SnapSelectedShapeDrag = false;
                message = EuclidText.Get("message.shapeSnapOff");
                return;
            }

            if (CoordinateSnapTool.TrySnapSelectedTargetToSelectedShape(cameraFrame, coordinateKeyText, out var result))
            {
                SnapSelectedShapeDrag = true;
                message = result;
                return;
            }

            SnapSelectedShapeDrag = false;
            message = result;
        }

        internal static void Clear()
        {
            Active = false;
            revision++;
            message = EuclidText.Get("message.noGuide");
        }

        internal static void ClearCircle()
        {
            circleActive = false;
            message = EuclidText.Get("message.noCircle");
        }

        private static bool SetLine(Vector2 newAnchor, Vector2 newDirection, string newMessage)
        {
            return SetLine(new Vector2d(newAnchor), new Vector2d(newDirection), newMessage);
        }

        private static bool SetLine(Vector2d newAnchor, Vector2d newDirection, string newMessage)
        {
            if (newDirection.SqrMagnitude <= 0.000001d)
            {
                message = EuclidText.Get("message.zeroDirection");
                return false;
            }

            anchor = newAnchor;
            direction = newDirection;
            Active = true;
            revision++;
            message = newMessage;
            SyncFields();
            return true;
        }

        private static void SetCircle(Vector2d center, double radius, string newMessage)
        {
            circleCenter = center;
            circleRadius = radius;
            circleActive = true;
            message = newMessage;
            SyncCircleFields();
        }

        internal static void SnapSelectedToGuide(CameraFrameSnapshot cameraFrame)
        {
            if (CoordinateSnapTool.TrySnapToGuide(cameraFrame, Snapshot, coordinateKeyText, out var result))
            {
                message = result;
                return;
            }

            message = result;
        }

        internal static void MoveSelectedAlongGuide(CameraFrameSnapshot cameraFrame, double distance)
        {
            if (CoordinateSnapTool.TryMoveAlongGuide(cameraFrame, Snapshot, coordinateKeyText, distance, out var result))
            {
                message = result;
                return;
            }

            message = result;
        }

        private static double GetStep()
        {
            if (TryParse(stepText, out var step))
            {
                return step;
            }

            stepText = "1";
            return 1d;
        }

        internal static double GetStepValue()
        {
            return GetStep() * GetTileSize();
        }

        internal static string FormatValue(double value)
        {
            return Format(value / GetTileSize());
        }

        internal static string FormatRadius(double value)
        {
            return Format(value / GetTileSize());
        }

        private static void SyncFields()
        {
            var tileSize = GetTileSize();
            anchorXText = Format(anchor.X / tileSize);
            anchorYText = Format(anchor.Y / tileSize);
            directionXText = Format(direction.X / tileSize);
            directionYText = Format(direction.Y / tileSize);
        }

        private static void SyncCircleFields()
        {
            var tileSize = GetTileSize();
            circleCenterXText = Format(circleCenter.X / tileSize);
            circleCenterYText = Format(circleCenter.Y / tileSize);
            circleRadiusText = Format(circleRadius / tileSize);
        }

        private static double GetTileSize()
        {
            return GameCompat.GetTileSize(1.5f);
        }

        private static bool TryParse(string text, out double value)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static string Format(double value)
        {
            return value.ToString("0.#####", CultureInfo.InvariantCulture);
        }

        private static double Cross(Vector2d a, Vector2d b)
        {
            return a.X * b.Y - a.Y * b.X;
        }

        private readonly struct SavedGuideLine
        {
            internal SavedGuideLine(Vector2d anchor, Vector2d direction)
            {
                Anchor = anchor;
                Direction = direction;
                HasValue = true;
            }

            internal bool HasValue { get; }

            internal Vector2d Anchor { get; }

            internal Vector2d Direction { get; }
        }
    }
}
