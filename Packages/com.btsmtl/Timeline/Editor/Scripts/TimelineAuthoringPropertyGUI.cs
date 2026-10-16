#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BTSMTL.Timeline.Editor
{
    static class TimelineAuthoringPropertyGUI
    {
        public static object Draw(TimelineAuthoringPropertyAttribute property, string label,
            object value, Type valueType, IReadOnlyList<TimelineExternalBindingDeclaration> bindings,
            AnimationCurve editableCurve = null)
        {
            EditorGUI.BeginChangeCheck();
            object changed;
            switch (property.Kind)
            {
                case TimelineAuthoringPropertyKind.Text:
                    changed = property.BindingValueKinds.Length == 0
                        ? EditorGUILayout.TextField(label, value as string ?? string.Empty)
                        : DrawBinding(property, label, (string)value, bindings);
                    break;
                case TimelineAuthoringPropertyKind.Boolean:
                    changed = EditorGUILayout.Toggle(label, (bool)value);
                    break;
                case TimelineAuthoringPropertyKind.Integer:
                    changed = EditorGUILayout.IntField(label, (int)value);
                    break;
                case TimelineAuthoringPropertyKind.Float:
                    float number = EditorGUILayout.FloatField(label, (float)value);
                    if (property.HasMinimum)
                        number = Mathf.Max((float)property.Minimum, number);
                    if (property.HasMaximum)
                        number = Mathf.Min((float)property.Maximum, number);
                    changed = number;
                    break;
                case TimelineAuthoringPropertyKind.Enum:
                    changed = EditorGUILayout.EnumPopup(label, (Enum)value);
                    break;
                case TimelineAuthoringPropertyKind.Vector2:
                    changed = EditorGUILayout.Vector2Field(label, (Vector2)value);
                    break;
                case TimelineAuthoringPropertyKind.Object:
                    changed = valueType == typeof(AnimationCurve)
                        ? EditorGUILayout.CurveField(label, editableCurve ?? (AnimationCurve)value)
                        : EditorGUILayout.ObjectField(label, value as UnityEngine.Object, valueType, false);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(property.Kind));
            }
            if (!EditorGUI.EndChangeCheck())
                return value;
            return property.Trimmed && changed is string text ? text.Trim() : changed;
        }

        static string DrawBinding(TimelineAuthoringPropertyAttribute property, string label, string value,
            IReadOnlyList<TimelineExternalBindingDeclaration> bindings)
        {
            var options = new List<string> { "Select binding" };
            for (int index = 0; index < bindings.Count; index++)
            {
                TimelineExternalBindingDeclaration binding = bindings[index];
                if (binding.Access != property.BindingAccess || binding.Lifetime != property.BindingLifetime)
                    continue;
                for (int kindIndex = 0; kindIndex < property.BindingValueKinds.Length; kindIndex++)
                {
                    if (binding.ValueKind != property.BindingValueKinds[kindIndex])
                        continue;
                    options.Add(binding.BindingId);
                    break;
                }
            }
            int selected = Mathf.Max(0, options.IndexOf(value));
            int next = EditorGUILayout.Popup(label, selected, options.ToArray());
            return next > 0 ? options[next] : string.Empty;
        }
    }
}
#endif
