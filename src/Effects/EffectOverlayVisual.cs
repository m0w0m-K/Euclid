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
    }

    internal readonly struct EffectOverlayVisual
    {
        internal EffectOverlayVisual(
            EffectOverlayKind kind,
            Vector2 referenceWorld,
            Vector2 targetWorld,
            string label)
        {
            Kind = kind;
            ReferenceWorld = referenceWorld;
            TargetWorld = targetWorld;
            Label = label ?? string.Empty;
            IsValid = true;
        }

        internal bool IsValid { get; }
        internal EffectOverlayKind Kind { get; }
        internal Vector2 ReferenceWorld { get; }
        internal Vector2 TargetWorld { get; }
        internal string Label { get; }
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
