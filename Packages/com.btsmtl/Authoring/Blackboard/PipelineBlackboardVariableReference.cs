using System;
using UnityEngine;

namespace BTSMTL.Authoring.Blackboard
{
    [Serializable]
    public struct PipelineBlackboardVariableReference
    {
        [SerializeField]
        string m_DeclarationId;

        [SerializeField]
        string m_DeclarationOwnerId;

        [SerializeField]
        string m_DisplayKey;

        [SerializeField]
        string m_ValueTypeName;

        public string DeclarationId => m_DeclarationId ?? string.Empty;
        public string DeclarationOwnerId => m_DeclarationOwnerId ?? string.Empty;
        public string DisplayKey => m_DisplayKey ?? string.Empty;
        public string ValueTypeName => m_ValueTypeName ?? string.Empty;
        public bool IsValid => !string.IsNullOrEmpty(DeclarationId) && !string.IsNullOrEmpty(DeclarationOwnerId);

        public PipelineBlackboardVariableReference(string declarationId, string declarationOwnerId, string displayKey, string valueTypeName)
        {
            m_DeclarationId = declarationId ?? string.Empty;
            m_DeclarationOwnerId = declarationOwnerId ?? string.Empty;
            m_DisplayKey = displayKey ?? string.Empty;
            m_ValueTypeName = valueTypeName ?? string.Empty;
        }

        public bool MatchesValueType(Type type)
        {
            if (type == null || string.IsNullOrEmpty(ValueTypeName))
                return false;

            return string.Equals(ValueTypeName, type.AssemblyQualifiedName, StringComparison.Ordinal) ||
                   string.Equals(ValueTypeName, type.FullName, StringComparison.Ordinal);
        }

        public static PipelineBlackboardVariableReference None => default;
    }
}
