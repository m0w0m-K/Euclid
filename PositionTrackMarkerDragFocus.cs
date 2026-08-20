using System;
using System.Globalization;
using System.Reflection;
using System.Text;
using ADOFAI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Euclid
{
    // Bridges Euclid's scene-marker drag to ADOFAI's normal inspector commit path.
    //
    // PositionTrack and FreeRoam both move an editor floor through positionOffset. Re-applying the
    // host floor state on every mouse-move is unnecessarily expensive and can make the marker/floor
    // fight each other while dragging. ADOFAI already defers these edits while the real coordinate
    // input is focused and commits when the field loses focus, so Euclid mirrors that lifecycle:
    // focus the real positionOffset input, keep only the raw value live during the drag, then release
    // that exact field on mouse-up so the host applies the floor once.
    internal sealed class PositionTrackMarkerDragFocus : MonoBehaviour
    {
        private static readonly FieldInfo PositionOffsetDraggingField = typeof(CameraFrameOverlay).GetField(
            "draggingPositionOffset",
            BindingFlags.Static | BindingFlags.NonPublic);

        private static Component ownedInput;
        private static bool focusEstablished;
        private bool wasDragging;

        internal static void Install()
        {
            var behaviour = EuclidMod.Behaviour;
            if (behaviour == null || behaviour.GetComponent<PositionTrackMarkerDragFocus>() != null)
            {
                return;
            }

            behaviour.gameObject.AddComponent<PositionTrackMarkerDragFocus>();
        }

        internal static bool ShouldDeferApply(LevelEvent ev, string key)
        {
            if (!UsesDeferredFloorPositionOffset(ev) ||
                !string.Equals(key, "positionOffset", StringComparison.OrdinalIgnoreCase) ||
                !IsMarkerDragging())
            {
                return false;
            }

            // LateUpdate normally establishes focus on the MouseDown frame. If Unity's IMGUI order
            // delays that transition, make one more attempt here before the first expensive apply.
            if (!HasOwnedFocus())
            {
                TryFocusPositionOffsetInput(scnEditor.instance, ev);
            }

            // If the real inspector field cannot be found, fall back to immediate apply rather than
            // leaving the level in an uncommitted state.
            return focusEstablished && HasOwnedFocus();
        }

        private void LateUpdate()
        {
            if (!EuclidMod.Enabled)
            {
                ReleaseOwnedInput();
                wasDragging = false;
                return;
            }

            var dragging = IsMarkerDragging();
            var editor = scnEditor.instance;
            var panel = GameCompat.GetLevelEventsPanel(editor);
            var ev = GameCompat.GetSelectedEvent(panel);

            if (dragging)
            {
                if (UsesDeferredFloorPositionOffset(ev) && !HasOwnedFocus())
                {
                    TryFocusPositionOffsetInput(editor, ev);
                }
            }
            else if (wasDragging)
            {
                // DeactivateInputField invokes the same end-edit/deselect path that ADOFAI uses
                // when the user leaves the coordinate field. Do not call ApplyPropertiesToRealEvents
                // here; the host editor owns the commit boundary.
                ReleaseOwnedInput();
            }

            wasDragging = dragging;
        }

        private void OnDisable()
        {
            ReleaseOwnedInput();
            wasDragging = false;
        }

        private void OnDestroy()
        {
            ReleaseOwnedInput();
        }

        private static bool TryFocusPositionOffsetInput(scnEditor editor, LevelEvent ev)
        {
            if (editor == null || !UsesDeferredFloorPositionOffset(ev))
            {
                return false;
            }

            if (HasOwnedFocus())
            {
                focusEstablished = true;
                return true;
            }

            var root = ResolveSelectedEventPropertyRoot(editor, ev);
            if (root == null)
            {
                focusEstablished = false;
                ownedInput = null;
                return false;
            }

            TryGetPositionOffset(ev, out var rawOffset);

            Component best = null;
            var bestScore = int.MinValue;

            var tmpInputs = root.GetComponentsInChildren<TMP_InputField>(true);
            for (var i = 0; i < tmpInputs.Length; i++)
            {
                var input = tmpInputs[i];
                if (input == null || !input.gameObject.activeInHierarchy || !input.interactable)
                {
                    continue;
                }

                var score = ScoreInput(input, root.transform, input.text, rawOffset);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = input;
                }
            }

            var legacyInputs = root.GetComponentsInChildren<InputField>(true);
            for (var i = 0; i < legacyInputs.Length; i++)
            {
                var input = legacyInputs[i];
                if (input == null || !input.gameObject.activeInHierarchy || !input.interactable)
                {
                    continue;
                }

                var score = ScoreInput(input, root.transform, input.text, rawOffset);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = input;
                }
            }

            // ADOFAI normally names the property row after the JSON key, so a context hit scores
            // several hundred points. Refuse an unrelated numeric field if that semantic match is
            // absent; immediate apply is safer than committing through the wrong inspector control.
            if (best == null || bestScore < 250)
            {
                focusEstablished = false;
                ownedInput = null;
                EuclidMod.Logger?.Log(
                    $"{ev.eventType} marker drag: could not resolve the positionOffset input field; using immediate apply.");
                return false;
            }

            ownedInput = best;
            focusEstablished = ActivateInput(best);
            if (!focusEstablished)
            {
                ownedInput = null;
            }
            return focusEstablished;
        }

        private static Component ResolveSelectedEventPropertyRoot(scnEditor editor, LevelEvent ev)
        {
            var panel = GameCompat.GetLevelEventsPanel(editor);
            if (panel == null)
            {
                return null;
            }

            try
            {
                var methods = panel.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                for (var i = 0; i < methods.Length; i++)
                {
                    var method = methods[i];
                    if (!string.Equals(method.Name, "GetPanelOfType", StringComparison.Ordinal) ||
                        method.GetParameters().Length != 1)
                    {
                        continue;
                    }

                    object result;
                    try
                    {
                        result = method.Invoke(panel, new object[] { ev });
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    if (result is Component component)
                    {
                        return component;
                    }

                    if (result is GameObject gameObject)
                    {
                        return gameObject.transform;
                    }
                }
            }
            catch (Exception)
            {
                // Fall back to the complete level-events panel below.
            }

            return panel as Component;
        }

        private static int ScoreInput(Component input, Transform propertyRoot, string text, Vector2 rawOffset)
        {
            var context = BuildLocalContext(input.transform, propertyRoot);
            var normalized = NormalizeContext(context);
            var score = 0;

            if (normalized.Contains("positionoffset")) score += 1000;
            if (normalized.Contains("위치오프셋")) score += 1000;
            if (normalized.Contains("位置オフセット")) score += 1000;
            if (normalized.Contains("位置偏移") || normalized.Contains("位置偏移量")) score += 900;
            if (normalized.Contains("position") && normalized.Contains("offset")) score += 500;

            var objectName = NormalizeContext(input.gameObject.name);
            if (objectName.Contains("positionoffset")) score += 600;

            if (TryParseFloat(text, out var numeric))
            {
                if (Mathf.Abs(numeric - rawOffset.x) <= 0.0001f) score += 30;
                if (Mathf.Abs(numeric - rawOffset.y) <= 0.0001f) score += 25;
            }

            return score;
        }

        private static string BuildLocalContext(Transform input, Transform propertyRoot)
        {
            var builder = new StringBuilder();
            var current = input;
            var depth = 0;
            while (current != null && depth < 5)
            {
                builder.Append(' ').Append(current.name);

                var ownTmp = current.GetComponent<TMP_Text>();
                if (ownTmp != null) builder.Append(' ').Append(ownTmp.text);
                var ownLegacy = current.GetComponent<Text>();
                if (ownLegacy != null) builder.Append(' ').Append(ownLegacy.text);

                var parent = current.parent;
                if (parent != null)
                {
                    for (var i = 0; i < parent.childCount; i++)
                    {
                        var sibling = parent.GetChild(i);
                        if (sibling == current)
                        {
                            continue;
                        }

                        builder.Append(' ').Append(sibling.name);
                        var siblingTmp = sibling.GetComponent<TMP_Text>();
                        if (siblingTmp != null) builder.Append(' ').Append(siblingTmp.text);
                        var siblingLegacy = sibling.GetComponent<Text>();
                        if (siblingLegacy != null) builder.Append(' ').Append(siblingLegacy.text);
                    }
                }

                if (current == propertyRoot)
                {
                    break;
                }

                current = parent;
                depth++;
            }

            return builder.ToString();
        }

        private static string NormalizeContext(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (char.IsLetterOrDigit(c))
                {
                    builder.Append(char.ToLowerInvariant(c));
                }
            }
            return builder.ToString();
        }

        private static bool ActivateInput(Component input)
        {
            try
            {
                var eventSystem = EventSystem.current;
                if (input is TMP_InputField tmp)
                {
                    eventSystem?.SetSelectedGameObject(tmp.gameObject);
                    tmp.Select();
                    tmp.ActivateInputField();
                    return tmp.isFocused;
                }

                if (input is InputField legacy)
                {
                    eventSystem?.SetSelectedGameObject(legacy.gameObject);
                    legacy.Select();
                    legacy.ActivateInputField();
                    return legacy.isFocused;
                }
            }
            catch (Exception ex)
            {
                EuclidMod.Logger?.Log("positionOffset marker drag focus failed: " + ex.Message);
            }

            return false;
        }

        private static bool HasOwnedFocus()
        {
            try
            {
                if (ownedInput is TMP_InputField tmp)
                {
                    return tmp != null && tmp.isFocused;
                }

                if (ownedInput is InputField legacy)
                {
                    return legacy != null && legacy.isFocused;
                }
            }
            catch (Exception)
            {
                return false;
            }

            return false;
        }

        private static void ReleaseOwnedInput()
        {
            try
            {
                if (ownedInput is TMP_InputField tmp && tmp != null)
                {
                    tmp.DeactivateInputField();
                }
                else if (ownedInput is InputField legacy && legacy != null)
                {
                    legacy.DeactivateInputField();
                }

                var eventSystem = EventSystem.current;
                if (eventSystem != null)
                {
                    eventSystem.SetSelectedGameObject(null);
                }
            }
            catch (Exception ex)
            {
                EuclidMod.Logger?.Log("positionOffset marker drag release failed: " + ex.Message);
            }
            finally
            {
                ownedInput = null;
                focusEstablished = false;
            }
        }

        private static bool IsMarkerDragging()
        {
            if (PositionOffsetDraggingField == null)
            {
                return false;
            }

            try
            {
                return PositionOffsetDraggingField.GetValue(null) is bool active && active;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool UsesDeferredFloorPositionOffset(LevelEvent ev)
        {
            if (ev == null)
            {
                return false;
            }

            if (ev.eventType == LevelEventType.PositionTrack)
            {
                return true;
            }

            return string.Equals(ev.eventType.ToString(), "FreeRoam", StringComparison.Ordinal);
        }

        private static bool TryGetPositionOffset(LevelEvent ev, out Vector2 value)
        {
            value = Vector2.zero;
            if (ev == null)
            {
                return false;
            }

            if (LevelEventCompat.TryGetRaw(ev, "positionOffset", out var raw))
            {
                if (raw is Vector2 vector)
                {
                    value = vector;
                    return true;
                }

                if (raw is Tuple<float, float> pair)
                {
                    value = new Vector2(pair.Item1, pair.Item2);
                    return true;
                }
            }

            try
            {
                value = ev.Get<Vector2>("positionOffset");
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool TryParseFloat(string text, out float value)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
                   float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }
    }
}
