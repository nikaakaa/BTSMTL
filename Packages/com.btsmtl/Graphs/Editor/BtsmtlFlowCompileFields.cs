using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BTSMTL.Authoring.Graph;
using BTSMTL.Compilation;
using BTSMTL.Orchestration;
using FlowCanvas;
using ThirdPersonSimulation;
using UnityEngine;

namespace BTSMTL.Graphs.Editor
{
    public sealed class BtsmtlFlowCompileFields : IGraphNodeFieldReader
    {
        readonly object m_Source;

        public BtsmtlFlowCompileFields(object source) => m_Source = source;

        public Type NodeType => m_Source.GetType();

        public T Read<T>(string fieldId)
        {
            object value = Value(fieldId);
            return typeof(T) == typeof(long) && value is OrchestrationTime time
                ? (T)(object)time.RawValue : (T)SemanticValue(value);
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

        static object SemanticValue(object value) => value switch
        {
            Vector2 vector => new SemanticVector2Value(vector.x, vector.y),
            Vector3 vector => new SemanticVector3Value(vector.x, vector.y, vector.z),
            Quaternion rotation => new SemanticQuaternionValue(rotation.x, rotation.y, rotation.z, rotation.w),
            AnimationCurve curve => Curve(curve),
            _ => value
        };

        static GameplayAbilityCurve<double> Curve(AnimationCurve source)
        {
            if (source == null) return null;
            Keyframe[] sourceKeys = source.keys;
            var keys = new GameplayAbilityCurveKey<double>[sourceKeys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                Keyframe key = sourceKeys[i];
                keys[i] = new GameplayAbilityCurveKey<double>(key.time, key.value,
                    float.IsInfinity(key.inTangent) ? 0f : key.inTangent,
                    float.IsInfinity(key.outTangent) ? 0f : key.outTangent,
                    key.inWeight, key.outWeight, (int)key.weightedMode,
                    i + 1 < keys.Length && (float.IsInfinity(key.outTangent) || float.IsInfinity(sourceKeys[i + 1].inTangent)));
            }
            return new GameplayAbilityCurve<double>((int)source.preWrapMode, (int)source.postWrapMode, keys);
        }
    }
}
