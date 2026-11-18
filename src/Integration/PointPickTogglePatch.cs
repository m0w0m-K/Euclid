using System;
using System.Reflection;
using HarmonyLib;

namespace Euclid
{
    internal sealed partial class EuclidPanel
    {
        private const string PointPickToggleHarmonyId = "m0w0m.euclid.point-pick-toggle";
        private static Harmony pointPickToggleHarmony;
        private static bool pointPickToggleInstalled;

        internal static void InstallPointPickToggle()
        {
            if (pointPickToggleInstalled)
            {
                return;
            }

            try
            {
                var target = AccessTools.Method(
                    typeof(EuclidPanel),
                    "BeginPointPick",
                    new[] { typeof(ConstructionShape), typeof(int) });
                if (target == null)
                {
                    EuclidMod.Logger?.Log("Could not find EuclidPanel.BeginPointPick for toggle behavior.");
                    return;
                }

                var prefix = typeof(EuclidPanel).GetMethod(
                    nameof(BeforeBeginPointPick),
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (prefix == null)
                {
                    EuclidMod.Logger?.Log("Could not install point-pick toggle: prefix method was not found.");
                    return;
                }

                pointPickToggleHarmony = new Harmony(PointPickToggleHarmonyId);
                pointPickToggleHarmony.Patch(target, prefix: new HarmonyMethod(prefix));
                pointPickToggleInstalled = true;
            }
            catch (Exception ex)
            {
                EuclidMod.Logger?.Log("Could not install point-pick toggle: " + ex.Message);
            }
        }

        private static bool BeforeBeginPointPick(EuclidPanel __instance, ConstructionShape __0, int __1)
        {
            if (__instance == null ||
                __instance.pendingPointPickShape != __0 ||
                __instance.pendingPointPickIndex != __1)
            {
                return true;
            }

            // Pressing the already-active Select button cancels the pending pick instead of
            // re-arming it. Pressing the other endpoint's Select button still switches the target.
            __instance.ClearPointPick();
            __instance.RefreshPointBindingButtons();
            return false;
        }
    }
}
