using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace Euclid
{
    // Construction shapes are editor-local helper state and are intentionally not serialized into
    // .adofai files. Patch the coroutine that actually receives the chosen level path rather than
    // the file-picker entry point or editor-state identities.
    internal static class EditorLevelLoadPatch
    {
        private const string HarmonyId = "m0w0m.euclid.editor-level-load";
        private const BindingFlags InstanceMethodFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

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
                var prefixMethod = typeof(EditorLevelLoadPatch).GetMethod(
                    nameof(BeforeOpenLevelCo),
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (prefixMethod == null)
                {
                    EuclidMod.Logger?.Log("Could not install level-load patch: prefix method was not found.");
                    return;
                }

                // Native editor loading eventually enters OpenLevelCo(path). Discover by name and
                // first string parameter instead of assuming one exact signature so minor ADOFAI
                // version changes/optional parameters do not silently disable the reset hook.
                var targets = typeof(scnEditor)
                    .GetMethods(InstanceMethodFlags)
                    .Where(method => string.Equals(method.Name, "OpenLevelCo", StringComparison.Ordinal))
                    .Where(method =>
                    {
                        var parameters = method.GetParameters();
                        return parameters.Length > 0 && parameters[0].ParameterType == typeof(string);
                    })
                    .ToArray();

                if (targets.Length == 0)
                {
                    EuclidMod.Logger?.Log("Could not find scnEditor.OpenLevelCo(path); map-change fallback detection remains active.");
                    return;
                }

                harmony = new Harmony(HarmonyId);
                for (var i = 0; i < targets.Length; i++)
                {
                    harmony.Patch(targets[i], prefix: new HarmonyMethod(prefixMethod));
                }

                installed = true;
                EuclidMod.Logger?.Log($"Installed {targets.Length} scnEditor.OpenLevelCo map-change hook(s).");
            }
            catch (Exception ex)
            {
                EuclidMod.Logger?.Log("Could not install level-load patch: " + ex.Message);
            }
        }

        private static void BeforeOpenLevelCo(object[] __args)
        {
            if (__args == null || __args.Length == 0 || !(__args[0] is string path) || string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            // OpenLevelCo is entered only after a concrete file path has been chosen, so ordinary
            // Save/Save As does not pass here and cannot erase construction shapes.
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

            EuclidMod.Logger?.Log("Cleared construction shapes for loaded level: " + path);
        }
    }
}
