using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using ADOFAI;

namespace Euclid
{
    internal static class LevelEventCompat
    {
        private static readonly FieldInfo DataField = typeof(LevelEvent).GetField(
            "data",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        internal static bool TryGetRaw(LevelEvent ev, string key, out object value)
        {
            value = null;
            if (ev == null || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            if (TryGetData(ev, out var data) && data.TryGetValue(key, out value))
            {
                value = NormalizeKnownRawValue(key, value);
                return true;
            }

            try
            {
                value = NormalizeKnownRawValue(key, ev[key]);
                return value != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static bool ContainsKey(LevelEvent ev, string key)
        {
            if (ev == null || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            if (TryGetData(ev, out var data) && data.ContainsKey(key))
            {
                return true;
            }

            try
            {
                return ev[key] != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ADOFAI stores the per-property on/off switch in LevelEvent.disabled. Missing entries are
        // enabled by default. Keep this interpretation in one compatibility helper so marker,
        // snapping, and read-only overlay code all agree on the effective value of a property.
        internal static bool IsPropertyEnabled(LevelEvent ev, string key)
        {
            if (ev == null || string.IsNullOrWhiteSpace(key) || ev.disabled == null)
            {
                return true;
            }

            try
            {
                return !ev.disabled.TryGetValue(key, out var disabled) || !disabled;
            }
            catch (Exception)
            {
                return true;
            }
        }

        internal static IEnumerable<KeyValuePair<string, object>> EnumerateRaw(LevelEvent ev)
        {
            if (TryGetData(ev, out var data))
            {
                foreach (var pair in data)
                {
                    yield return pair;
                }
            }
        }

        internal static bool SetRaw(LevelEvent ev, string key, object value)
        {
            if (ev == null || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            try
            {
                EnsureData(ev);
                ev[key] = value;
                return true;
            }
            catch (Exception)
            {
                if (!TryGetData(ev, out var data))
                {
                    return false;
                }

                data[key] = value;
                return true;
            }
        }

        private static object NormalizeKnownRawValue(string key, object value)
        {
            if (!string.Equals(key, "relativeTo", StringComparison.OrdinalIgnoreCase) || value == null)
            {
                return value;
            }

            // ADOFAI 3.3.0 can store tile-relative enum values in a serialized wrapper such as
            // [0, "ThisTile"]. Feature code expects the semantic enum/string, not the wrapper.
            if (value is IList list && list.Count > 0)
            {
                if (list.Count >= 2 && list[1] != null)
                {
                    return list[1];
                }

                return list[0];
            }

            // Also tolerate Tuple/ValueTuple-like wrappers without depending on their generic types.
            try
            {
                var type = value.GetType();
                var item2Property = type.GetProperty("Item2", BindingFlags.Instance | BindingFlags.Public);
                if (item2Property != null)
                {
                    var item2 = item2Property.GetValue(value, null);
                    if (item2 != null)
                    {
                        return item2;
                    }
                }

                var item2Field = type.GetField("Item2", BindingFlags.Instance | BindingFlags.Public);
                if (item2Field != null)
                {
                    var item2 = item2Field.GetValue(value);
                    if (item2 != null)
                    {
                        return item2;
                    }
                }
            }
            catch (Exception)
            {
                // Leave unknown representations untouched so existing fallbacks can handle them.
            }

            return value;
        }

        private static bool TryGetData(LevelEvent ev, out Dictionary<string, object> data)
        {
            data = null;
            if (ev == null || DataField == null)
            {
                return false;
            }

            try
            {
                data = DataField.GetValue(ev) as Dictionary<string, object>;
                return data != null;
            }
            catch (Exception)
            {
                data = null;
                return false;
            }
        }

        private static void EnsureData(LevelEvent ev)
        {
            if (ev == null || DataField == null)
            {
                return;
            }

            if (TryGetData(ev, out _))
            {
                return;
            }

            try
            {
                DataField.SetValue(ev, new Dictionary<string, object>());
            }
            catch (Exception)
            {
                // The public indexer can still handle many event types without a backing field write.
            }
        }
    }
}
