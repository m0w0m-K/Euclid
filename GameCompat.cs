using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ADOFAI;
using System.Reflection;
using UnityEngine;

namespace Euclid
{
    // Version boundary between Euclid and ADOFAI internals.
    //
    // When a future game update renames/moves editor fields or methods, add the compatibility
    // fallback here instead of scattering reflection throughout feature code. Public helpers in
    // this class should return safe defaults when the editor is temporarily unavailable.
    internal static class GameCompat
    {
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        internal static bool IsEditorPlaying(scnEditor editor)
        {
            return TryGetMember(editor, "playMode", out bool playing) && playing;
        }

        internal static bool IsEditorLoading(scnEditor editor)
        {
            return TryGetMember(editor, "isLoading", out bool loading) && loading;
        }

        internal static InspectorPanel GetSettingsPanel(scnEditor editor)
        {
            return TryGetMember(editor, "settingsPanel", out InspectorPanel panel) ? panel : null;
        }


        internal static IList<scrFloor> GetSelectedFloors(scnEditor editor)
        {
            return GetFloorList(editor, "selectedFloors");
        }

        internal static IList<scrFloor> GetFloors(scnEditor editor)
        {
            return GetFloorList(editor, "floors");
        }

        internal static IList<LevelEvent> GetSelectedFloorEvents(scnEditor editor, LevelEventType eventType)
        {
            if (!TryInvoke(editor, "GetSelectedFloorEvents", out var raw, eventType) || raw == null)
            {
                return Array.Empty<LevelEvent>();
            }

            if (raw is IList<LevelEvent> typed)
            {
                return typed;
            }

            if (raw is IEnumerable enumerable)
            {
                return enumerable.Cast<object>().OfType<LevelEvent>().ToList();
            }

            return Array.Empty<LevelEvent>();
        }

        internal static Camera GetEditorCamera(scnEditor editor)
        {
            return TryGetMember(editor, "camera", out Camera camera) ? camera : null;
        }

        // Identity token for the currently opened editor map. ADOFAI normally replaces levelData
        // when another map is opened, even if the scnEditor object itself survives. Keep this as
        // an object token rather than reading a specific path field so it remains tolerant of
        // path/member renames between game versions.
        internal static object GetEditorLevelIdentity(scnEditor editor)
        {
            if (editor == null)
            {
                return null;
            }

            return TryGetMember(editor, "levelData", out object levelData) && levelData != null
                ? levelData
                : editor;
        }

        internal static bool TryGetLevelSetting<T>(scnEditor editor, string name, out T value)
        {
            value = default;
            return TryGetMember(editor, "levelData", out object levelData)
                && levelData != null
                && TryGetMember(levelData, name, out value);
        }

        internal static float GetTileSize(float fallback = 1f)
        {
            try
            {
                var controller = scrController.instance;
                if (controller != null && TryGetMember(controller, "tileSize", out float tileSize) && tileSize > 0.000001f)
                {
                    return tileSize;
                }
            }
            catch (Exception)
            {
                // The controller may be absent while the editor is rebuilding.
            }

            return fallback;
        }

        private static IList<scrFloor> GetFloorList(scnEditor editor, string memberName)
        {
            if (!TryGetMember(editor, memberName, out object raw) || raw == null)
            {
                return Array.Empty<scrFloor>();
            }

            if (raw is IList<scrFloor> typed)
            {
                return typed;
            }

            if (raw is IEnumerable enumerable)
            {
                return enumerable.Cast<object>().OfType<scrFloor>().ToList();
            }

            return Array.Empty<scrFloor>();
        }

        internal static object GetLevelEventsPanel(scnEditor editor)
        {
            return TryGetMember(editor, "levelEventsPanel", out object panel) ? panel : null;
        }

        internal static bool TrySetInspectorVisible(object panel, bool visible)
        {
            if (panel == null)
            {
                return false;
            }

            if (TryInvoke(panel, "ShowInspector", out _, visible, false))
            {
                return true;
            }

            if (TrySetMember(panel, "showInspector", visible))
            {
                return true;
            }

            // Unity/ADOFAI versions differ in how the inspector visibility flag is exposed.
            // Activating the component is a safe last-resort for opening; we intentionally
            // do not deactivate the whole InspectorPanel when closing because it owns built-in tabs.
            if (visible && panel is Component component && !component.gameObject.activeSelf)
            {
                component.gameObject.SetActive(true);
                return true;
            }

            return false;
        }

        internal static LevelEvent GetSelectedEvent(object panel)
        {
            return TryGetMember(panel, "selectedEvent", out LevelEvent ev) ? ev : null;
        }

        internal static bool TryGetSelectedEventType(object panel, out LevelEventType eventType)
        {
            return TryGetMember(panel, "selectedEventType", out eventType);
        }

        internal static bool TrySaveState(scnEditor editor)
        {
            return TryInvoke(editor, "SaveState", out _, true, false);
        }

        internal static bool TryApplyPropertiesToRealEvents(LevelEvent ev)
        {
            return TryInvoke(ev, "ApplyPropertiesToRealEvents", out _);
        }

        internal static bool TryUpdatePropertyText(scnEditor editor, LevelEvent ev, string key)
        {
            var panel = GetLevelEventsPanel(editor);
            return TryInvoke(panel, "UpdatePropertyText", out _, ev, key);
        }

        internal static bool TryRefreshEventPanel(scnEditor editor, LevelEvent ev)
        {
            var levelEventsPanel = GetLevelEventsPanel(editor);
            if (!TryInvoke(levelEventsPanel, "GetPanelOfType", out var propertiesPanel, ev) || propertiesPanel == null)
            {
                return false;
            }

            return TryInvoke(propertiesPanel, "SetProperties", out _, ev, false);
        }

        internal static IEnumerable<LevelEvent> GetEditorEvents(scnEditor editor)
        {
            if (!TryGetMember(editor, "events", out object raw) || raw == null)
            {
                yield break;
            }

            if (raw is IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    if (item is LevelEvent ev)
                    {
                        yield return ev;
                    }
                }
            }
        }


        internal static Transform GetInspectorTabs(InspectorPanel panel)
        {
            return TryGetMember(panel, "tabs", out Transform tabs) ? tabs : null;
        }

        internal static Transform GetInspectorPanels(InspectorPanel panel)
        {
            if (TryGetMember(panel, "panels", out Transform panels) && panels != null)
            {
                return panels;
            }

            if (TryGetMember(panel, "panelsList", out object raw) && raw is IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    if (item is Component component && component.transform.parent != null)
                    {
                        return component.transform.parent;
                    }
                }
            }

            return null;
        }

        internal static void SetInspectorChrome(InspectorPanel panel, bool titleVisible, bool messageVisible, string titleText)
        {
            if (panel == null)
            {
                return;
            }

            SetGameObjectMemberActive(panel, "titleCanvas", titleVisible);
            SetGameObjectMemberActive(panel, "messageCanvas", messageVisible);

            if (TryGetMember(panel, "title", out object title) && title != null)
            {
                TrySetMember(title, "text", titleText ?? string.Empty);
            }
        }

        internal static void ClearInspectorSelection(InspectorPanel panel)
        {
            if (panel == null)
            {
                return;
            }

            TrySetMember(panel, "selectedEventType", default(LevelEventType));
            TrySetMember(panel, "selectedEvent", null);
            TrySetMember(panel, "cacheEventIndex", 0);
        }

        internal static bool TryGetMember<T>(object target, string name, out T value)
        {
            value = default;
            if (target == null || string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            try
            {
                var type = target.GetType();
                var property = type.GetProperty(name, InstanceFlags);
                if (property != null)
                {
                    var raw = property.GetValue(target, null);
                    if (TryConvert(raw, out value))
                    {
                        return true;
                    }
                }

                var field = type.GetField(name, InstanceFlags);
                if (field != null)
                {
                    var raw = field.GetValue(target);
                    if (TryConvert(raw, out value))
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                // Caller has a fallback path.
            }

            return false;
        }

        internal static bool TrySetMember(object target, string name, object value)
        {
            if (target == null || string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            try
            {
                var type = target.GetType();
                var property = type.GetProperty(name, InstanceFlags);
                if (property != null && property.CanWrite)
                {
                    property.SetValue(target, value, null);
                    return true;
                }

                var field = type.GetField(name, InstanceFlags);
                if (field != null)
                {
                    field.SetValue(target, value);
                    return true;
                }
            }
            catch (Exception)
            {
                // Caller has a fallback path.
            }

            return false;
        }

        internal static bool TryInvoke(object target, string name, out object result, params object[] args)
        {
            result = null;
            if (target == null || string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            args = args ?? Array.Empty<object>();

            try
            {
                var methods = target.GetType()
                    .GetMethods(InstanceFlags)
                    .Where(method => method.Name == name && method.GetParameters().Length == args.Length);

                foreach (var method in methods)
                {
                    var parameters = method.GetParameters();
                    var invokeArgs = new object[args.Length];
                    var compatible = true;

                    for (var i = 0; i < args.Length; i++)
                    {
                        if (!TryPrepareArgument(args[i], parameters[i].ParameterType, out invokeArgs[i]))
                        {
                            compatible = false;
                            break;
                        }
                    }

                    if (!compatible)
                    {
                        continue;
                    }

                    result = method.Invoke(target, invokeArgs);
                    return true;
                }
            }
            catch (Exception)
            {
                // Caller has a fallback path.
            }

            return false;
        }


        private static void SetGameObjectMemberActive(object target, string name, bool active)
        {
            if (!TryGetMember(target, name, out object raw) || raw == null)
            {
                return;
            }

            if (raw is GameObject gameObject)
            {
                gameObject.SetActive(active);
                return;
            }

            if (raw is Component component)
            {
                component.gameObject.SetActive(active);
            }
        }

        private static bool TryPrepareArgument(object value, Type parameterType, out object converted)
        {
            converted = value;
            var effectiveType = parameterType.IsByRef ? parameterType.GetElementType() : parameterType;
            if (effectiveType == null)
            {
                return false;
            }

            if (value == null)
            {
                return !effectiveType.IsValueType || Nullable.GetUnderlyingType(effectiveType) != null;
            }

            if (effectiveType.IsInstanceOfType(value))
            {
                return true;
            }

            try
            {
                if (effectiveType.IsEnum)
                {
                    if (value is string text)
                    {
                        converted = Enum.Parse(effectiveType, text, true);
                        return true;
                    }

                    converted = Enum.ToObject(effectiveType, value);
                    return true;
                }

                converted = Convert.ChangeType(value, effectiveType);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool TryConvert<T>(object raw, out T value)
        {
            if (raw is T typed)
            {
                value = typed;
                return true;
            }

            if (raw == null)
            {
                value = default;
                return !typeof(T).IsValueType || Nullable.GetUnderlyingType(typeof(T)) != null;
            }

            try
            {
                value = (T)Convert.ChangeType(raw, typeof(T));
                return true;
            }
            catch (Exception)
            {
                value = default;
                return false;
            }
        }
    }
}
