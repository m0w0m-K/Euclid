using UnityModManagerNet;

namespace Euclid
{
    internal static class Startup
    {
        internal static void Load(UnityModManager.ModEntry modEntry)
        {
            EuclidMod.Load(modEntry);
        }
    }
}
