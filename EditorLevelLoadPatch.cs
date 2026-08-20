using System;
using System.Reflection;
using HarmonyLib;

namespace Euclid
{
    // Construction shapes are editor-local helper state and are intentionally not serialized into
    // .adofai files. Hook the actual file-open overload instead of trying to infer a map switch from
    // levelData/floor/path identities, all of which ADOFAI can reuse while loading another chart.
    internal static class EditorLevelLoadPatch
    {
        private const string HarmonyId = "m0w0m.euclid.editor-level-load";
        private static Harmony harmony;
        private static bool installed;

        internal static void Install()
        {
            if (installed)
            {
                return;
            }

            try
            {
                var target = AccessTools.Method(typeof(scnEditor), "OpenLevel", new[] { typeof(string) });
                if (target == null)
                {
                    EuclidMod.Logger?.Log("Could not find scnEditor.OpenLevel(string); map-change fallback detection remains active.");
                    return;
                }

                var prefixMethod = typeof(EditorLevelLoadPatch).GetMethod(
                    nameof(BeforeOpenLevel),
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (prefixMethod == null)
                {
                    EuclidMod.Logger?.Log("Could not install level-open patch: prefix method was not found.");
                    return;
                }

                harmony = new Harmony(HarmonyId);
                harmony.Patch(target, prefix: new HarmonyMethod(prefixMethod));
                installed = true;
                EuclidMod.Logger?.Log("Installed scnEditor.OpenLevel(string) map-change hook.");
            }
            catch (Exception ex)
            {
                EuclidMod.Logger?.Log("Could not install level-open patch: " + ex.Message);
            }
        }

        private static void BeforeOpenLevel(string __0)
        {
            // This overload is reached only after a concrete level path has been selected. The
            // no-argument OpenLevel() may merely open the file picker, so it is deliberately not
            // patched; cancelling the picker therefore cannot clear construction state.
            if (string.IsNullOrWhiteSpace(__0))
            {
                return;
            }

            GuideLineTool.SnapSelectedShapeDrag = false;
            CoordinateSnapTool.ResetPositionTrackReference();
            ConstructionShapeTool.ClearAll();
            ConstructionShapeCanvasOverlay.Refresh();

            var behaviour = EuclidMod.Behaviour;
            if (behaviour != null)
            {
                var panel = behaviour.GetComponent<EuclidPanel>();
                panel?.HandleEditorMapChanged();
            }
        }
    }
}
