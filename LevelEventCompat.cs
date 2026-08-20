using System;
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
                return true;
            }

            try
            {
                value = ev[key];
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
