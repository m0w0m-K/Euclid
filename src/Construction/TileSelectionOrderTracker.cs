using System;
using System.Collections.Generic;

namespace Euclid
{
    // Tracks selection order because scnEditor.selectedFloors does not reliably encode which
    // floor was clicked most recently. Version increments whenever the effective selection changes.
    // The construction point picker relies on this Version value to distinguish a new click from
    // the tile that was already selected before the user pressed "select position".
    internal static class TileSelectionOrderTracker
    {
        private static readonly List<int> orderedSelection = new List<int>();

        internal static int Version { get; private set; }

        internal static void Refresh()
        {
            try
            {
                var changed = false;
                var selectedFloors = GameCompat.GetSelectedFloors(scnEditor.instance);
                if (selectedFloors.Count == 0)
                {
                    if (orderedSelection.Count > 0)
                    {
                        orderedSelection.Clear();
                        Version++;
                    }

                    return;
                }

                var active = new HashSet<int>();
                for (var i = 0; i < selectedFloors.Count; i++)
                {
                    var floor = selectedFloors[i];
                    if (floor != null)
                    {
                        active.Add(floor.seqID);
                    }
                }

                changed |= orderedSelection.RemoveAll(tile => !active.Contains(tile)) > 0;

                for (var i = 0; i < selectedFloors.Count; i++)
                {
                    var floor = selectedFloors[i];
                    if (floor == null || orderedSelection.Contains(floor.seqID))
                    {
                        continue;
                    }

                    orderedSelection.Add(floor.seqID);
                    changed = true;
                }

                if (changed)
                {
                    Version++;
                }
            }
            catch (Exception)
            {
                if (orderedSelection.Count > 0)
                {
                    orderedSelection.Clear();
                    Version++;
                }
            }
        }

        internal static bool TryGetTileForPoint(int pointIndex, out int tile)
        {
            return TryGetTileAtSelectionIndex(pointIndex <= 0 ? 0 : 1, out tile);
        }

        internal static bool TryGetMostRecentTile(out int tile)
        {
            Refresh();
            tile = 0;
            if (orderedSelection.Count == 0)
            {
                return false;
            }

            tile = orderedSelection[orderedSelection.Count - 1];
            return tile >= 0;
        }

        internal static bool TryGetTileAtSelectionIndex(int selectionIndex, out int tile)
        {
            Refresh();
            tile = 0;
            if (orderedSelection.Count == 0)
            {
                return false;
            }

            if (selectionIndex <= 0)
            {
                tile = orderedSelection[0];
                return tile >= 0;
            }

            if (TryGetOppositeEndpoint(orderedSelection[0], out tile))
            {
                return tile >= 0;
            }

            if (orderedSelection.Count < 2)
            {
                return false;
            }

            tile = orderedSelection[1];
            return tile >= 0;
        }

        private static bool TryGetOppositeEndpoint(int firstTile, out int tile)
        {
            tile = 0;
            try
            {
                var selectedFloors = GameCompat.GetSelectedFloors(scnEditor.instance);
                if (selectedFloors.Count < 2)
                {
                    return false;
                }

                var found = false;
                var min = int.MaxValue;
                var max = int.MinValue;
                for (var i = 0; i < selectedFloors.Count; i++)
                {
                    var floor = selectedFloors[i];
                    if (floor == null)
                    {
                        continue;
                    }

                    found = true;
                    min = Math.Min(min, floor.seqID);
                    max = Math.Max(max, floor.seqID);
                }

                if (!found || min == max)
                {
                    return false;
                }

                if (firstTile == min)
                {
                    tile = max;
                    return true;
                }

                if (firstTile == max)
                {
                    tile = min;
                    return true;
                }

                tile = Math.Abs(max - firstTile) >= Math.Abs(firstTile - min) ? max : min;
                return tile != firstTile;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
