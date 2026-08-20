using System;
using System.IO;
using UnityEngine;
using UnityModManagerNet;

namespace Euclid
{
    // Owns the selection-independent all-effect-marker toggle and persists it beside Euclid's
    // normal overlay settings. EuclidMod draws the toggle explicitly so it can sit above the
    // color configuration instead of being appended after every other option.
    internal static class AllEffectMarkerSettings
    {
        private const string SettingsFileName = "Settings.json";
        private const string Key = "ShowAllEffectMarkers";
        private static bool installed;
        private static UnityModManager.ModEntry modEntry;

        internal static bool Enabled { get; private set; }

        internal static void Install(UnityModManager.ModEntry entry)
        {
            if (installed || entry == null)
            {
                return;
            }

            installed = true;
            modEntry = entry;
            Load();

            var previousSaveGui = entry.OnSaveGUI;
            entry.OnSaveGUI = currentEntry =>
            {
                previousSaveGui?.Invoke(currentEntry);
                Save();
            };
        }

        internal static void DrawGui()
        {
            var label = string.Equals(EuclidText.CurrentLocaleCode, "ko", StringComparison.OrdinalIgnoreCase)
                ? "모든 이펙트 마크 표시"
                : "Show all effect markers";
            Enabled = GUILayout.Toggle(Enabled, label);
        }

        private static void Load()
        {
            Enabled = false;
            try
            {
                var path = GetSettingsPath();
                if (!File.Exists(path))
                {
                    return;
                }

                var json = File.ReadAllText(path);
                if (TryReadBool(json, Key, out var value))
                {
                    Enabled = value;
                }
            }
            catch (Exception ex)
            {
                EuclidMod.Logger?.Log("Could not load all-effect marker setting: " + ex.Message);
            }
        }

        private static void Save()
        {
            try
            {
                var path = GetSettingsPath();
                var json = File.Exists(path) ? File.ReadAllText(path) : "{\n}\n";
                json = SetBool(json, Key, Enabled);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                EuclidMod.Logger?.Log("Could not save all-effect marker setting: " + ex.Message);
            }
        }

        private static bool TryReadBool(string json, string key, out bool value)
        {
            value = false;
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key))
            {
                return false;
            }

            var token = "\"" + key + "\"";
            var keyIndex = json.IndexOf(token, StringComparison.Ordinal);
            if (keyIndex < 0)
            {
                return false;
            }

            var colon = json.IndexOf(':', keyIndex + token.Length);
            if (colon < 0)
            {
                return false;
            }

            var start = colon + 1;
            while (start < json.Length && char.IsWhiteSpace(json[start])) start++;
            if (start >= json.Length)
            {
                return false;
            }

            if (json.IndexOf("true", start, StringComparison.OrdinalIgnoreCase) == start)
            {
                value = true;
                return true;
            }

            if (json.IndexOf("false", start, StringComparison.OrdinalIgnoreCase) == start)
            {
                value = false;
                return true;
            }

            return false;
        }

        private static string SetBool(string json, string key, bool value)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                json = "{\n}\n";
            }

            var token = "\"" + key + "\"";
            var keyIndex = json.IndexOf(token, StringComparison.Ordinal);
            var valueText = value ? "true" : "false";
            if (keyIndex >= 0)
            {
                var colon = json.IndexOf(':', keyIndex + token.Length);
                if (colon >= 0)
                {
                    var start = colon + 1;
                    while (start < json.Length && char.IsWhiteSpace(json[start])) start++;
                    var end = start;
                    while (end < json.Length && char.IsLetter(json[end])) end++;
                    return json.Substring(0, start) + valueText + json.Substring(end);
                }
            }

            var close = json.LastIndexOf('}');
            if (close < 0)
            {
                return "{\n  \"" + key + "\": " + valueText + "\n}\n";
            }

            var before = json.Substring(0, close).TrimEnd();
            var needsComma = before.Length > 0 && before[before.Length - 1] != '{';
            return before + (needsComma ? ",\n" : "\n") +
                   "  \"" + key + "\": " + valueText + "\n" +
                   json.Substring(close);
        }

        private static string GetSettingsPath()
        {
            return Path.Combine(modEntry?.Path ?? string.Empty, SettingsFileName);
        }
    }
}
