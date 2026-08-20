using System;
using UnityEngine;

namespace Euclid
{
    // The editor exposes several different effects whose editable position is expressed as an
    // offset from a tile/reference point. Keeping the visual model in one small struct prevents
    // each renderer from re-deriving the same reference/target semantics differently.
    internal enum EffectOverlayKind
    {
        CameraMove,
        TrackMove,
        TrackPosition,
        FreeRoam,
        DecorationMove,
    }

    internal readonly struct EffectOverlayVisual
    {
        internal EffectOverlayVisual(
            EffectOverlayKind kind,
            Vector2 referenceWorld,
            Vector2 targetWorld,
            string label)
        {
            var resolvedLabel = label ?? string.Empty;
            var resolvedKind = ResolveKind(kind, resolvedLabel);

            Kind = resolvedKind;
            ReferenceWorld = referenceWorld;
            TargetWorld = targetWorld;
            Label = resolvedKind == EffectOverlayKind.DecorationMove
                ? GetDecorationMoveLabel()
                : resolvedLabel;
            IsValid = true;
        }

        internal bool IsValid { get; }
        internal EffectOverlayKind Kind { get; }
        internal Vector2 ReferenceWorld { get; }
        internal Vector2 TargetWorld { get; }
        internal string Label { get; }

        private static EffectOverlayKind ResolveKind(EffectOverlayKind kind, string label)
        {
            if (kind != EffectOverlayKind.TrackMove)
            {
                return kind;
            }

            // The all-effect overlay currently passes the raw event type as its label, so the
            // unfocused decoration case can be identified without any editor lookup.
            if (string.Equals(label, "MoveDecorations", StringComparison.OrdinalIgnoreCase))
            {
                return EffectOverlayKind.DecorationMove;
            }

            // The focused overlay historically grouped MoveDecorations under the localized
            // MoveTrack label. Resolve that one ambiguous foreground case from the selected event.
            if (!string.Equals(label, EuclidText.Get("effect.moveTrack"), StringComparison.Ordinal))
            {
                return kind;
            }

            try
            {
                var editor = scnEditor.instance;
                var panel = GameCompat.GetLevelEventsPanel(editor);
                var selectedEvent = GameCompat.GetSelectedEvent(panel);
                if (selectedEvent != null &&
                    string.Equals(selectedEvent.eventType.ToString(), "MoveDecorations", StringComparison.Ordinal))
                {
                    return EffectOverlayKind.DecorationMove;
                }
            }
            catch (Exception)
            {
                // Keep the original TrackMove kind when the editor is rebuilding.
            }

            return kind;
        }

        private static string GetDecorationMoveLabel()
        {
            return string.Equals(EuclidText.CurrentLocaleCode, "ko", StringComparison.OrdinalIgnoreCase)
                ? "장식 이동"
                : "Move Decorations";
        }
    }

    internal readonly struct EffectOverlayColors
    {
        internal EffectOverlayColors(Color tileMarker, Color positionMarker, Color segment, Color label)
        {
            TileMarker = tileMarker;
            PositionMarker = positionMarker;
            Segment = segment;
            Label = label;
        }

        internal Color TileMarker { get; }
        internal Color PositionMarker { get; }
        internal Color Segment { get; }
        internal Color Label { get; }
    }
}
