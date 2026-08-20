using System;
using System.IO;
using UnityEngine;
using UnityModManagerNet;

namespace Euclid
{
    internal static class EuclidMod
    {
        private const string SettingsFileName = "Settings.json";
        private static readonly Color DefaultCameraFrameColor = new Color(1f, 0.83f, 0.18f, 0.95f);

        private static EuclidBehaviour behaviour;
        private static UnityModManager.ModEntry modEntry;
        private static OverlaySettings settings = OverlaySettings.CreateDefault();

        internal static bool Enabled { get; private set; }

        internal static bool ShowOverlay { get; private set; } = true;

        // Overlay settings live here, not in the editor panel, because Ctrl+F10 must be able to
        // configure them even while the Euclid tab is closed.
        internal static bool ShowCameraFrame => settings?.ShowCameraFrame ?? true;

        internal static Color CameraFrameColor => ResolveColor(settings?.CameraFrameColor, DefaultCameraFrameColor);

        internal static EffectOverlayColors GetEffectOverlayColors(EffectOverlayKind kind)
        {
            if (settings == null)
            {
                settings = OverlaySettings.CreateDefault();
            }

            switch (kind)
            {
                case EffectOverlayKind.CameraMove:
                    return ResolvePalette(
                        settings.CameraMoveTileMarkerColor,
                        settings.CameraMovePositionMarkerColor,
                        settings.CameraMoveSegmentColor,
                        settings.CameraMoveNameColor,
                        OverlaySettings.DefaultCameraMovePalette);
                case EffectOverlayKind.TrackPosition:
                    return ResolvePalette(
                        settings.TrackPositionTileMarkerColor,
                        settings.TrackPositionPositionMarkerColor,
                        settings.TrackPositionSegmentColor,
                        settings.TrackPositionNameColor,
                        OverlaySettings.DefaultTrackPositionPalette);
                case EffectOverlayKind.FreeRoam:
                    return ResolvePalette(
                        settings.FreeRoamTileMarkerColor,
                        settings.FreeRoamPositionMarkerColor,
                        settings.FreeRoamSegmentColor,
                        settings.FreeRoamNameColor,
                        OverlaySettings.DefaultFreeRoamPalette);
                default:
                    return ResolvePalette(
                        settings.TrackMoveTileMarkerColor,
                        settings.TrackMovePositionMarkerColor,
                        settings.TrackMoveSegmentColor,
                        settings.TrackMoveNameColor,
                        OverlaySettings.DefaultTrackMovePalette);
            }
        }

        internal static EuclidBehaviour Behaviour => behaviour;

        internal static UnityModManager.ModEntry.ModLogger Logger { get; private set; }

        internal static void Load(UnityModManager.ModEntry entry)
        {
            modEntry = entry;
            Logger = entry.Logger;
            LoadSettings();

            entry.OnToggle = OnToggle;
            entry.OnGUI = OnGui;
            entry.OnSaveGUI = OnSaveGui;

            var obj = new GameObject("Euclid");
            UnityEngine.Object.DontDestroyOnLoad(obj);
            behaviour = obj.AddComponent<EuclidBehaviour>();

            Logger.Log($"Euclid loaded. ADOFAI {Application.version}, Unity {Application.unityVersion}.");
            Logger.Log("Standalone editor tab mode: no EditorTabLib/Localizations dependency.");
        }

        private static bool OnToggle(UnityModManager.ModEntry entry, bool value)
        {
            Enabled = value;
            if (behaviour != null)
            {
                behaviour.enabled = value;
            }

            if (!value)
            {
                behaviour?.HidePanel();
            }

            return true;
        }

        private static void OnGui(UnityModManager.ModEntry entry)
        {
            if (settings == null)
            {
                settings = OverlaySettings.CreateDefault();
            }

            settings.ShowCameraFrame = GUILayout.Toggle(
                settings.ShowCameraFrame,
                EuclidText.Get("settings.showCameraFrame"));

            GUILayout.Space(4f);
            DrawColorOption(
                EuclidText.Get("settings.cameraFrameColor"),
                ref settings.CameraFrameColor,
                DefaultCameraFrameColor);

            GUILayout.Space(10f);
            DrawEffectPalette(
                EuclidText.Get("effect.moveCamera"),
                ref settings.CameraMoveTileMarkerColor,
                ref settings.CameraMovePositionMarkerColor,
                ref settings.CameraMoveSegmentColor,
                ref settings.CameraMoveNameColor,
                OverlaySettings.DefaultCameraMovePalette);

            DrawEffectPalette(
                EuclidText.Get("effect.moveTrack"),
                ref settings.TrackMoveTileMarkerColor,
                ref settings.TrackMovePositionMarkerColor,
                ref settings.TrackMoveSegmentColor,
                ref settings.TrackMoveNameColor,
                OverlaySettings.DefaultTrackMovePalette);

            DrawEffectPalette(
                EuclidText.Get("effect.positionTrack"),
                ref settings.TrackPositionTileMarkerColor,
                ref settings.TrackPositionPositionMarkerColor,
                ref settings.TrackPositionSegmentColor,
                ref settings.TrackPositionNameColor,
                OverlaySettings.DefaultTrackPositionPalette);

            DrawEffectPalette(
                EuclidText.Get("effect.freeRoam"),
                ref settings.FreeRoamTileMarkerColor,
                ref settings.FreeRoamPositionMarkerColor,
                ref settings.FreeRoamSegmentColor,
                ref settings.FreeRoamNameColor,
                OverlaySettings.DefaultFreeRoamPalette);

            GUILayout.Label(EuclidText.Get("settings.colorHint"));

            // UMM invokes this every frame while Options is open. Dirtying the lower Canvas here
            // makes both the geometry AND the preview swatches react immediately while typing.
            ConstructionShapeCanvasOverlay.Refresh();
        }

        private static void OnSaveGui(UnityModManager.ModEntry entry)
        {
            NormalizeSettings();
            SaveSettings();
        }

        private static void DrawEffectPalette(
            string title,
            ref string tileMarker,
            ref string positionMarker,
            ref string segment,
            ref string name,
            EffectOverlayColors fallback)
        {
            GUILayout.Label(title);
            DrawColorOption(EuclidText.Get("settings.tileMarkerColor"), ref tileMarker, fallback.TileMarker);
            DrawColorOption(EuclidText.Get("settings.positionMarkerColor"), ref positionMarker, fallback.PositionMarker);
            DrawColorOption(EuclidText.Get("settings.segmentColor"), ref segment, fallback.Segment);
            DrawColorOption(EuclidText.Get("settings.effectNameColor"), ref name, fallback.Label);
            GUILayout.Space(8f);
        }

        private static void DrawColorOption(string label, ref string hex, Color fallback)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(180f));
            hex = GUILayout.TextField(hex ?? string.Empty, GUILayout.Width(110f));

            // GUI.backgroundColor does not reliably tint UMM's skinned Box, which is why the old
            // preview could stay visually unchanged even though the overlay itself updated.
            // Draw the swatch texture directly so its pixels always reflect the current text value.
            var swatch = GUILayoutUtility.GetRect(34f, 20f, GUILayout.Width(34f), GUILayout.Height(20f));
            var oldColor = GUI.color;
            GUI.color = ResolveColor(hex, fallback);
            GUI.DrawTexture(swatch, Texture2D.whiteTexture, ScaleMode.StretchToFill, true);
            GUI.color = oldColor;
            GUILayout.EndHorizontal();
        }

        private static EffectOverlayColors ResolvePalette(
            string tileMarker,
            string positionMarker,
            string segment,
            string label,
            EffectOverlayColors fallback)
        {
            return new EffectOverlayColors(
                ResolveColor(tileMarker, fallback.TileMarker),
                ResolveColor(positionMarker, fallback.PositionMarker),
                ResolveColor(segment, fallback.Segment),
                ResolveColor(label, fallback.Label));
        }

        private static Color ResolveColor(string raw, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return fallback;
            }

            var text = raw.Trim();
            if (!text.StartsWith("#", StringComparison.Ordinal))
            {
                text = "#" + text;
            }

            if ((text.Length == 7 || text.Length == 9) && ColorUtility.TryParseHtmlString(text, out var color))
            {
                return color;
            }

            return fallback;
        }

        private static void LoadSettings()
        {
            settings = OverlaySettings.CreateDefault();
            try
            {
                var path = GetSettingsPath();
                if (!File.Exists(path))
                {
                    return;
                }

                var json = File.ReadAllText(path);
                settings.ShowCameraFrame = ReadJsonBool(json, nameof(OverlaySettings.ShowCameraFrame), settings.ShowCameraFrame);
                ReadColor(json, nameof(OverlaySettings.CameraFrameColor), ref settings.CameraFrameColor);

                ReadColor(json, nameof(OverlaySettings.CameraMoveTileMarkerColor), ref settings.CameraMoveTileMarkerColor);
                ReadColor(json, nameof(OverlaySettings.CameraMovePositionMarkerColor), ref settings.CameraMovePositionMarkerColor);
                ReadColor(json, nameof(OverlaySettings.CameraMoveSegmentColor), ref settings.CameraMoveSegmentColor);
                ReadColor(json, nameof(OverlaySettings.CameraMoveNameColor), ref settings.CameraMoveNameColor);

                ReadColor(json, nameof(OverlaySettings.TrackMoveTileMarkerColor), ref settings.TrackMoveTileMarkerColor);
                ReadColor(json, nameof(OverlaySettings.TrackMovePositionMarkerColor), ref settings.TrackMovePositionMarkerColor);
                ReadColor(json, nameof(OverlaySettings.TrackMoveSegmentColor), ref settings.TrackMoveSegmentColor);
                ReadColor(json, nameof(OverlaySettings.TrackMoveNameColor), ref settings.TrackMoveNameColor);

                ReadColor(json, nameof(OverlaySettings.TrackPositionTileMarkerColor), ref settings.TrackPositionTileMarkerColor);
                ReadColor(json, nameof(OverlaySettings.TrackPositionPositionMarkerColor), ref settings.TrackPositionPositionMarkerColor);
                ReadColor(json, nameof(OverlaySettings.TrackPositionSegmentColor), ref settings.TrackPositionSegmentColor);
                ReadColor(json, nameof(OverlaySettings.TrackPositionNameColor), ref settings.TrackPositionNameColor);

                ReadColor(json, nameof(OverlaySettings.FreeRoamTileMarkerColor), ref settings.FreeRoamTileMarkerColor);
                ReadColor(json, nameof(OverlaySettings.FreeRoamPositionMarkerColor), ref settings.FreeRoamPositionMarkerColor);
                ReadColor(json, nameof(OverlaySettings.FreeRoamSegmentColor), ref settings.FreeRoamSegmentColor);
                ReadColor(json, nameof(OverlaySettings.FreeRoamNameColor), ref settings.FreeRoamNameColor);

                // 0.7.48/0.7.49 stored one generic effect color. If present, use it as a sensible
                // migration fallback for target markers only; all new per-effect fields remain
                // independently editable afterwards.
                var legacy = ReadJsonString(json, "EffectMarkerColor", null);
                if (!string.IsNullOrWhiteSpace(legacy))
                {
                    if (ReadJsonValue(json, nameof(OverlaySettings.CameraMovePositionMarkerColor)) == null) settings.CameraMovePositionMarkerColor = legacy;
                    if (ReadJsonValue(json, nameof(OverlaySettings.TrackMovePositionMarkerColor)) == null) settings.TrackMovePositionMarkerColor = legacy;
                    if (ReadJsonValue(json, nameof(OverlaySettings.TrackPositionPositionMarkerColor)) == null) settings.TrackPositionPositionMarkerColor = legacy;
                    if (ReadJsonValue(json, nameof(OverlaySettings.FreeRoamPositionMarkerColor)) == null) settings.FreeRoamPositionMarkerColor = legacy;
                }

                NormalizeSettings();
            }
            catch (Exception ex)
            {
                Logger?.Log("Could not load overlay settings: " + ex.Message);
                settings = OverlaySettings.CreateDefault();
            }
        }

        private static void ReadColor(string json, string key, ref string value)
        {
            value = ReadJsonString(json, key, value);
        }

        private static void SaveSettings()
        {
            try
            {
                var lines = new[]
                {
                    $"  \"{nameof(OverlaySettings.ShowCameraFrame)}\": {(settings.ShowCameraFrame ? "true" : "false")}",
                    JsonColor(nameof(OverlaySettings.CameraFrameColor), settings.CameraFrameColor),
                    JsonColor(nameof(OverlaySettings.CameraMoveTileMarkerColor), settings.CameraMoveTileMarkerColor),
                    JsonColor(nameof(OverlaySettings.CameraMovePositionMarkerColor), settings.CameraMovePositionMarkerColor),
                    JsonColor(nameof(OverlaySettings.CameraMoveSegmentColor), settings.CameraMoveSegmentColor),
                    JsonColor(nameof(OverlaySettings.CameraMoveNameColor), settings.CameraMoveNameColor),
                    JsonColor(nameof(OverlaySettings.TrackMoveTileMarkerColor), settings.TrackMoveTileMarkerColor),
                    JsonColor(nameof(OverlaySettings.TrackMovePositionMarkerColor), settings.TrackMovePositionMarkerColor),
                    JsonColor(nameof(OverlaySettings.TrackMoveSegmentColor), settings.TrackMoveSegmentColor),
                    JsonColor(nameof(OverlaySettings.TrackMoveNameColor), settings.TrackMoveNameColor),
                    JsonColor(nameof(OverlaySettings.TrackPositionTileMarkerColor), settings.TrackPositionTileMarkerColor),
                    JsonColor(nameof(OverlaySettings.TrackPositionPositionMarkerColor), settings.TrackPositionPositionMarkerColor),
                    JsonColor(nameof(OverlaySettings.TrackPositionSegmentColor), settings.TrackPositionSegmentColor),
                    JsonColor(nameof(OverlaySettings.TrackPositionNameColor), settings.TrackPositionNameColor),
                    JsonColor(nameof(OverlaySettings.FreeRoamTileMarkerColor), settings.FreeRoamTileMarkerColor),
                    JsonColor(nameof(OverlaySettings.FreeRoamPositionMarkerColor), settings.FreeRoamPositionMarkerColor),
                    JsonColor(nameof(OverlaySettings.FreeRoamSegmentColor), settings.FreeRoamSegmentColor),
                    JsonColor(nameof(OverlaySettings.FreeRoamNameColor), settings.FreeRoamNameColor),
                };

                File.WriteAllText(GetSettingsPath(), "{\n" + string.Join(",\n", lines) + "\n}\n");
            }
            catch (Exception ex)
            {
                Logger?.Log("Could not save overlay settings: " + ex.Message);
            }
        }

        private static string JsonColor(string key, string value)
        {
            return $"  \"{key}\": \"{EscapeJson(value)}\"";
        }

        private static bool ReadJsonBool(string json, string key, bool fallback)
        {
            var raw = ReadJsonValue(json, key);
            return bool.TryParse(raw, out var value) ? value : fallback;
        }

        private static string ReadJsonString(string json, string key, string fallback)
        {
            var raw = ReadJsonValue(json, key);
            if (string.IsNullOrEmpty(raw) || raw.Length < 2 || raw[0] != '"' || raw[raw.Length - 1] != '"')
            {
                return fallback;
            }

            return raw.Substring(1, raw.Length - 2)
                .Replace("\\\"", "\"")
                .Replace("\\\\", "\\");
        }

        private static string ReadJsonValue(string json, string key)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key))
            {
                return null;
            }

            var token = "\"" + key + "\"";
            var keyIndex = json.IndexOf(token, StringComparison.Ordinal);
            if (keyIndex < 0)
            {
                return null;
            }

            var colonIndex = json.IndexOf(':', keyIndex + token.Length);
            if (colonIndex < 0)
            {
                return null;
            }

            var valueStart = colonIndex + 1;
            while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart])) valueStart++;
            if (valueStart >= json.Length) return null;

            if (json[valueStart] == '"')
            {
                var valueEnd = valueStart + 1;
                var escaped = false;
                while (valueEnd < json.Length)
                {
                    var c = json[valueEnd];
                    if (c == '"' && !escaped)
                    {
                        return json.Substring(valueStart, valueEnd - valueStart + 1);
                    }

                    escaped = c == '\\' && !escaped;
                    if (c != '\\') escaped = false;
                    valueEnd++;
                }
                return null;
            }

            var end = valueStart;
            while (end < json.Length && json[end] != ',' && json[end] != '}' && json[end] != '\r' && json[end] != '\n') end++;
            return json.Substring(valueStart, end - valueStart).Trim();
        }

        private static string EscapeJson(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string GetSettingsPath()
        {
            return Path.Combine(modEntry?.Path ?? string.Empty, SettingsFileName);
        }

        private static void NormalizeSettings()
        {
            if (settings == null)
            {
                settings = OverlaySettings.CreateDefault();
                return;
            }

            settings.CameraFrameColor = NormalizeColorText(settings.CameraFrameColor, DefaultCameraFrameColor);
            NormalizePalette(
                ref settings.CameraMoveTileMarkerColor,
                ref settings.CameraMovePositionMarkerColor,
                ref settings.CameraMoveSegmentColor,
                ref settings.CameraMoveNameColor,
                OverlaySettings.DefaultCameraMovePalette);
            NormalizePalette(
                ref settings.TrackMoveTileMarkerColor,
                ref settings.TrackMovePositionMarkerColor,
                ref settings.TrackMoveSegmentColor,
                ref settings.TrackMoveNameColor,
                OverlaySettings.DefaultTrackMovePalette);
            NormalizePalette(
                ref settings.TrackPositionTileMarkerColor,
                ref settings.TrackPositionPositionMarkerColor,
                ref settings.TrackPositionSegmentColor,
                ref settings.TrackPositionNameColor,
                OverlaySettings.DefaultTrackPositionPalette);
            NormalizePalette(
                ref settings.FreeRoamTileMarkerColor,
                ref settings.FreeRoamPositionMarkerColor,
                ref settings.FreeRoamSegmentColor,
                ref settings.FreeRoamNameColor,
                OverlaySettings.DefaultFreeRoamPalette);
        }

        private static void NormalizePalette(
            ref string tile,
            ref string position,
            ref string segment,
            ref string name,
            EffectOverlayColors fallback)
        {
            tile = NormalizeColorText(tile, fallback.TileMarker);
            position = NormalizeColorText(position, fallback.PositionMarker);
            segment = NormalizeColorText(segment, fallback.Segment);
            name = NormalizeColorText(name, fallback.Label);
        }

        private static string NormalizeColorText(string raw, Color fallback)
        {
            return ColorUtility.ToHtmlStringRGBA(ResolveColor(raw, fallback));
        }

        [Serializable]
        private sealed class OverlaySettings
        {
            internal static readonly EffectOverlayColors DefaultCameraMovePalette = new EffectOverlayColors(
                new Color(0.35f, 0.75f, 1f, 0.95f),
                new Color(1f, 0.83f, 0.18f, 0.98f),
                new Color(0.2f, 1f, 0.5f, 0.9f),
                new Color(1f, 0.83f, 0.18f, 1f));
            internal static readonly EffectOverlayColors DefaultTrackMovePalette = new EffectOverlayColors(
                new Color(0.35f, 0.75f, 1f, 0.95f),
                new Color(1f, 0.55f, 0.2f, 0.96f),
                new Color(1f, 0.55f, 0.2f, 0.78f),
                new Color(1f, 0.68f, 0.3f, 1f));
            internal static readonly EffectOverlayColors DefaultTrackPositionPalette = new EffectOverlayColors(
                new Color(0.35f, 0.8f, 1f, 0.95f),
                new Color(0.35f, 1f, 0.62f, 0.96f),
                new Color(0.35f, 1f, 0.62f, 0.78f),
                new Color(0.5f, 1f, 0.7f, 1f));
            internal static readonly EffectOverlayColors DefaultFreeRoamPalette = new EffectOverlayColors(
                new Color(0.55f, 0.7f, 1f, 0.95f),
                new Color(0.9f, 0.45f, 1f, 0.96f),
                new Color(0.9f, 0.45f, 1f, 0.78f),
                new Color(0.95f, 0.6f, 1f, 1f));

            public bool ShowCameraFrame = true;
            public string CameraFrameColor = "FFD42EF2";

            public string CameraMoveTileMarkerColor = "59BFFFF2";
            public string CameraMovePositionMarkerColor = "FFD42EFA";
            public string CameraMoveSegmentColor = "33FF80E6";
            public string CameraMoveNameColor = "FFD42EFF";

            public string TrackMoveTileMarkerColor = "59BFFFF2";
            public string TrackMovePositionMarkerColor = "FF8C33F5";
            public string TrackMoveSegmentColor = "FF8C33C7";
            public string TrackMoveNameColor = "FFAD4DFF";

            public string TrackPositionTileMarkerColor = "59CCFFF2";
            public string TrackPositionPositionMarkerColor = "59FF9EF5";
            public string TrackPositionSegmentColor = "59FF9EC7";
            public string TrackPositionNameColor = "80FFB3FF";

            public string FreeRoamTileMarkerColor = "8CB3FFF2";
            public string FreeRoamPositionMarkerColor = "E673FFF5";
            public string FreeRoamSegmentColor = "E673FFC7";
            public string FreeRoamNameColor = "F299FFFF";

            internal static OverlaySettings CreateDefault() => new OverlaySettings();
        }
    }
}
