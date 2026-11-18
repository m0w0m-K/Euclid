using System;
using System.Collections.Generic;
using UnityEngine;

namespace Euclid
{
    internal enum MeasureState
    {
        Ready,
        NoEditor,
        EmptySelection,
        SingleSelection,
        Unavailable
    }

    internal readonly struct MeasureSnapshot
    {
        private MeasureSnapshot(
            MeasureState state,
            string message,
            int count,
            int startSeqId,
            int endSeqId,
            Vector2 start,
            Vector2 end)
        {
            State = state;
            Message = message;
            Count = count;
            StartSeqId = startSeqId;
            EndSeqId = endSeqId;
            Start = start;
            End = end;
        }

        internal MeasureState State { get; }

        internal string Message { get; }

        internal int Count { get; }

        internal int StartSeqId { get; }

        internal int EndSeqId { get; }

        internal Vector2 Start { get; }

        internal Vector2 End { get; }

        internal Vector2 Delta => End - Start;

        internal float Distance => Delta.magnitude;

        internal Vector2 Midpoint => (Start + End) * 0.5f;

        internal Vector2 MidpointOffsetFromStart => Midpoint - Start;

        internal Vector2 MidpointOffsetFromEnd => Midpoint - End;

        internal float AngleDegrees
        {
            get
            {
                var angle = Mathf.Atan2(Delta.y, Delta.x) * Mathf.Rad2Deg;
                return angle < 0f ? angle + 360f : angle;
            }
        }

        internal static MeasureSnapshot Unavailable(string message)
        {
            return new MeasureSnapshot(MeasureState.Unavailable, message, 0, -1, -1, Vector2.zero, Vector2.zero);
        }

        internal static MeasureSnapshot Capture()
        {
            try
            {
                var editor = scnEditor.instance;
                if (editor == null)
                {
                    return new MeasureSnapshot(MeasureState.NoEditor, EuclidText.Get("message.openEditor"), 0, -1, -1, Vector2.zero, Vector2.zero);
                }

                var selected = GameCompat.GetSelectedFloors(editor);
                if (selected == null || selected.Count == 0)
                {
                    return new MeasureSnapshot(MeasureState.EmptySelection, EuclidText.Get("message.selectTwoTiles"), 0, -1, -1, Vector2.zero, Vector2.zero);
                }

                if (selected.Count == 1)
                {
                    var floor = selected[0];
                    var pos = ToVector2(floor.transform.position);
                    return new MeasureSnapshot(
                        MeasureState.SingleSelection,
                        EuclidText.Get("message.selectOneMoreTile"),
                        1,
                        floor.seqID,
                        floor.seqID,
                        pos,
                        pos);
                }

                var first = selected[0];
                var last = selected[selected.Count - 1];
                return new MeasureSnapshot(
                    MeasureState.Ready,
                    string.Empty,
                    selected.Count,
                    first.seqID,
                    last.seqID,
                    ToVector2(first.transform.position),
                    ToVector2(last.transform.position));
            }
            catch (Exception ex)
            {
                EuclidMod.Logger?.Error(ex.ToString());
                return Unavailable(EuclidText.Get("message.measureFailed"));
            }
        }

        private static Vector2 ToVector2(Vector3 value)
        {
            return new Vector2(value.x, value.y);
        }

        internal string ToClipboardText()
        {
            if (State != MeasureState.Ready)
            {
                return Message;
            }

            var tileSize = GetTileSize();
            var distance = Distance / tileSize;
            var midpoint = Midpoint / tileSize;
            var midpointFromStart = MidpointOffsetFromStart / tileSize;
            var midpointFromEnd = MidpointOffsetFromEnd / tileSize;
            var delta = Delta / tileSize;

            return string.Format(
                "Tile {0} -> Tile {1}\nDistance: {2:0.#####}\nMidpoint: ({3:0.#####}, {4:0.#####})\nMidpoint from {0}: ({5:0.#####}, {6:0.#####})\nMidpoint from {1}: ({7:0.#####}, {8:0.#####})\nAngle: {9:0.#####} deg\nDelta {0}->{1}: ({10:0.#####}, {11:0.#####})\nDelta {1}->{0}: ({12:0.#####}, {13:0.#####})",
                StartSeqId,
                EndSeqId,
                distance,
                midpoint.x,
                midpoint.y,
                midpointFromStart.x,
                midpointFromStart.y,
                midpointFromEnd.x,
                midpointFromEnd.y,
                AngleDegrees,
                delta.x,
                delta.y,
                -delta.x,
                -delta.y);
        }

        private static float GetTileSize()
        {
            return GameCompat.GetTileSize(1.5f);
        }
    }
}
