using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BTSMTL.Authoring.Graph;
using BTSMTL.Compilation;
using BTSMTL.Orchestration;
using FlowCanvas;

namespace BTSMTL.Graphs.Editor
{
    public sealed class BtsmtlFlowCompileFields : IGraphNodeFieldReader
    {
        readonly object m_Source;

        public BtsmtlFlowCompileFields(object source) => m_Source = source;

        public T Read<T>(string fieldId)
        {
            object value = Value(fieldId);
            return typeof(T) == typeof(long) && value is OrchestrationTime time
                ? (T)(object)time.RawValue : (T)BtsmtlFlowCompileGraphs.SemanticValue(value);
        }

        public bool IsNull(string fieldId) => Value(fieldId) == null;

        public IGraphNodeFieldReader Object(string fieldId) => Value(fieldId) is { } value
            ? new BtsmtlFlowCompileFields(value) : null;

        public IReadOnlyList<IGraphNodeFieldReader> Objects(string fieldId)
        {
            object value = Value(fieldId);
            return value == null ? Array.Empty<IGraphNodeFieldReader>() :
                ((IEnumerable)value).Cast<object>().Select(item =>
                    (IGraphNodeFieldReader)new BtsmtlFlowCompileFields(item)).ToArray();
        }

        object Value(string fieldId)
        {
            if (m_Source is FlowNode node && fieldId.StartsWith("input:", StringComparison.Ordinal))
                return node.GetInputValuePorts().Single(port => port.ID == fieldId.Substring(6)).serializedValue;
            if (m_Source is IGraphAuthoringFieldSource declared)
                return declared.AuthoringFields[fieldId].Read(m_Source);
            MemberInfo member = m_Source.GetType().GetMember(fieldId,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                .Single(value => value is FieldInfo or PropertyInfo);
            return member is FieldInfo field ? field.GetValue(m_Source) : ((PropertyInfo)member).GetValue(m_Source);
        }
    }
}
