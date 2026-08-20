using UnityModManagerNet;

namespace Euclid
{
    internal static class Startup
    {
        internal static void Load(UnityModManager.ModEntry modEntry)
        {
            EuclidMod.Load(modEntry);
            AllEffectMarkerSettings.Install(modEntry);
            PositionTrackFocusSync.Install();
            PositionTrackMarkerDragFocus.Install();
            PositionTrackSnapCommitSync.Install();
            SelectedShapeSnapSync.Install();
            CameraFrameTransformHandles.Install();
            AllEffectMarkerOverlayV2.Install();
        }
    }
}
