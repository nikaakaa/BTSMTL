#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BTSMTL.Timeline.Editor
{
    sealed class TimelineTrackCreationPopup : PopupWindowContent
    {
        static readonly string[] s_ExecutionDomains = { "Logic", "Presentation" };

        readonly string m_Kind;
        readonly IReadOnlyList<TimelineAuthoringTrackFieldAttribute> m_Fields;
        readonly Func<string, IReadOnlyDictionary<string, string>, string> m_Create;
        readonly Dictionary<string, string> m_Values = new Dictionary<string, string>(StringComparer.Ordinal);
        string m_Name;
        string m_Error;
        string m_SubmitError;

        public TimelineTrackCreationPopup(
            string kind,
            string displayName,
            IReadOnlyList<TimelineAuthoringTrackFieldAttribute> fields,
            Func<string, IReadOnlyDictionary<string, string>, string> create)
        {
            m_Kind = kind;
            m_Fields = fields ?? Array.Empty<TimelineAuthoringTrackFieldAttribute>();
            m_Create = create;
            m_Name = displayName;
        }

        public override Vector2 GetWindowSize() => new Vector2(340, 96f + m_Fields.Count * 24f);

        public override void OnGUI(Rect rect)
        {
            EditorGUILayout.LabelField("Add Track", EditorStyles.boldLabel);
            m_Name = EditorGUILayout.TextField("Name", m_Name);
            for (int index = 0; index < m_Fields.Count; index++)
            {
                TimelineAuthoringTrackFieldAttribute field = m_Fields[index];
                string value = m_Values.TryGetValue(field.FieldId, out string current) ? current : string.Empty;
                if (string.Equals(field.FieldId, "executionDomain", StringComparison.Ordinal))
                {
                    int selected = Array.IndexOf(s_ExecutionDomains, value);
                    m_Values[field.FieldId] = s_ExecutionDomains[EditorGUILayout.Popup(
                        DisplayName(field.FieldId),
                        selected < 0 ? 0 : selected,
                        s_ExecutionDomains)];
                }
                else
                    m_Values[field.FieldId] = EditorGUILayout.TextField(DisplayName(field.FieldId), value);
            }
            IReadOnlyList<TimelineAuthoringTrackIssue> issues = TimelineAuthoringTrackBinding.Validate(m_Kind, m_Values);
            m_Error = issues.Count == 0 ? string.Empty : issues[0].ErrorMessage;
            if (!string.IsNullOrEmpty(m_Error))
                EditorGUILayout.HelpBox(m_Error, MessageType.Error);
            if (!string.IsNullOrEmpty(m_SubmitError))
                EditorGUILayout.HelpBox(m_SubmitError, MessageType.Error);
            using (new EditorGUI.DisabledScope(!string.IsNullOrEmpty(m_Error)))
            {
                if (GUILayout.Button("Create"))
                {
                    string error = m_Create(m_Name, m_Values);
                    if (string.IsNullOrEmpty(error))
                        editorWindow.Close();
                    else
                        m_SubmitError = error;
                }
            }
        }

        static string DisplayName(string fieldId)
        {
            if (string.IsNullOrEmpty(fieldId))
                return string.Empty;
            string value = fieldId.Replace("Id", " ID").Replace("animation", "Animation");
            return char.ToUpperInvariant(value[0]) + value.Substring(1);
        }
    }
}
#endif
