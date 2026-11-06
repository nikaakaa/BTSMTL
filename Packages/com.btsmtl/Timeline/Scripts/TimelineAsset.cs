using System.Collections.Generic;
using BTSMTL.Authoring.Graph;
using UnityEngine;

namespace BTSMTL.Timeline
{
    public sealed class TimelineAsset : ScriptableObject
    {
        [System.NonSerialized]
        TimelineData m_Data = new TimelineData();

        [SerializeField]
        string m_AuthoringScope;

        public TimelineData Data => m_Data;
        public string AuthoringScope => m_AuthoringScope;

        [GraphAuthoringConfigure]
        public void ConfigureAuthoringScope(string scope) => m_AuthoringScope = scope;

        public bool ValidateContent(TimelineContractCatalog catalog, List<string> errors)
        {
            if (catalog == null)
                throw new System.ArgumentNullException(nameof(catalog));
            if (m_Data == null)
            {
                errors?.Add($"Timeline asset '{name}' has no TimelineData.");
                return false;
            }
            return m_Data.ValidateContent(catalog, errors);
        }

        public void SetData(TimelineData data)
        {
            m_Data = data;
        }

#if UNITY_EDITOR
        [SerializeField] string m_UndoSnapshot;
        [System.NonSerialized] System.Func<string, TimelineData> m_ReadUndoSnapshot;
        [System.NonSerialized] System.Func<TimelineData, string> m_WriteUndoSnapshot;

        public void BindUndoSnapshot(System.Func<string, TimelineData> read,
            System.Func<TimelineData, string> write)
        {
            m_ReadUndoSnapshot = read;
            m_WriteUndoSnapshot = write;
            CaptureUndoSnapshot();
        }

        public void CaptureUndoSnapshot() => m_UndoSnapshot = m_WriteUndoSnapshot(m_Data);
        public void RestoreUndoSnapshot() => m_Data = m_ReadUndoSnapshot(m_UndoSnapshot);
#endif
    }
}
