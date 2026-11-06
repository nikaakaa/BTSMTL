using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Authoring.Blackboard;
using BTSMTL.Orchestration;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#else
using System.Numerics;
#endif

namespace BTSMTL.Graphs
{
    public static class GraphHostInputIds
    {
        public const string DeltaSeconds = "host.delta-seconds";
    }

    public enum GraphHostValueKind : byte
    {
        Bool = 1,
        Int32 = 2,
        Float32 = 3,
        Vector2 = 4,
        Vector3 = 5,
        Quaternion = 6,
        Enum = 7,
        Text = 8,
        ObjectHandle = 9
    }

    public static class GraphHostValueKinds
    {
        public static bool TryGet(Type type, out GraphHostValueKind kind)
        {
            if (type == typeof(string))
                kind = GraphHostValueKind.Text;
            else if (type == typeof(bool))
                kind = GraphHostValueKind.Bool;
            else if (type == typeof(int))
                kind = GraphHostValueKind.Int32;
            else if (type == typeof(ObjectHandle))
                kind = GraphHostValueKind.ObjectHandle;
            else if (type == typeof(float))
                kind = GraphHostValueKind.Float32;
            else if (type == typeof(Vector2))
                kind = GraphHostValueKind.Vector2;
            else if (type == typeof(Vector3))
                kind = GraphHostValueKind.Vector3;
            else if (type == typeof(Quaternion))
                kind = GraphHostValueKind.Quaternion;
            else if (type != null && type.IsEnum)
                kind = GraphHostValueKind.Enum;
            else
            {
                kind = default;
                return false;
            }
            return true;
        }

        public static Type RequireType(GraphHostValueKind kind) => kind switch
        {
            GraphHostValueKind.Text => typeof(string),
            GraphHostValueKind.Bool => typeof(bool),
            GraphHostValueKind.Int32 => typeof(int),
            GraphHostValueKind.ObjectHandle => typeof(ObjectHandle),
            GraphHostValueKind.Float32 => typeof(float),
            GraphHostValueKind.Vector2 => typeof(Vector2),
            GraphHostValueKind.Vector3 => typeof(Vector3),
            GraphHostValueKind.Quaternion => typeof(Quaternion),
            GraphHostValueKind.Enum => typeof(System.Enum),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        public static bool IsSupported(Type type) => TryGet(type, out _);
    }

    public sealed class GraphHostInputDescriptor
    {
        public GraphHostInputDescriptor(string inputId, Type valueType)
        {
            InputId = string.IsNullOrWhiteSpace(inputId)
                ? throw new ArgumentException("Event graph input identity is missing.", nameof(inputId))
                : inputId.Trim();
            if (!GraphHostValueKinds.TryGet(valueType, out GraphHostValueKind valueKind))
                throw new ArgumentException("Event graph input type is not supported.", nameof(valueType));
            ValueType = valueType;
            ValueKind = valueKind;
        }

        public string InputId { get; }
        public Type ValueType { get; }
        public GraphHostValueKind ValueKind { get; }
    }

    public sealed class GraphHostOutputDescriptor
    {
        public GraphHostOutputDescriptor(string variableId, Type valueType)
        {
            VariableId = string.IsNullOrWhiteSpace(variableId)
                ? throw new ArgumentException("Event graph output variable identity is missing.", nameof(variableId))
                : variableId.Trim();
            if (!GraphHostValueKinds.TryGet(valueType, out GraphHostValueKind valueKind))
                throw new ArgumentException("Event graph output type is not supported.", nameof(valueType));
            ValueType = valueType;
            ValueKind = valueKind;
        }

        public string VariableId { get; }
        public Type ValueType { get; }
        public GraphHostValueKind ValueKind { get; }
    }

    public sealed class GraphHostContract
    {
        readonly GraphHostInputDescriptor[] m_Inputs;
        readonly GraphHostOutputDescriptor[] m_Outputs;

        public GraphHostContract(
            string contractId,
            string revision,
            string updateEventId,
            IReadOnlyList<GraphHostInputDescriptor> inputs,
            IReadOnlyList<GraphHostOutputDescriptor> outputs,
            IReadOnlyList<PipelineBlackboardVariableScope> blackboardScopes,
            IReadOnlyList<string> domains,
            string numericTarget)
        {
            ContractId = RequireIdentity(contractId, nameof(contractId));
            Revision = RequireIdentity(revision, nameof(revision));
            UpdateEventId = RequireIdentity(updateEventId, nameof(updateEventId));
            NumericTarget = RequireIdentity(numericTarget, nameof(numericTarget));
            BlackboardScopes = blackboardScopes?.Distinct().ToArray() ?? throw new ArgumentNullException(nameof(blackboardScopes));
            Domains = (domains ?? throw new ArgumentNullException(nameof(domains)))
                .Select(domain => RequireIdentity(domain, nameof(domains))).Distinct(StringComparer.Ordinal)
                .OrderBy(domain => domain, StringComparer.Ordinal).ToArray();
            m_Inputs = (inputs ?? Array.Empty<GraphHostInputDescriptor>())
                .OrderBy(value => value?.InputId, StringComparer.Ordinal)
                .ToArray();
            m_Outputs = (outputs ?? Array.Empty<GraphHostOutputDescriptor>())
                .OrderBy(value => value?.VariableId, StringComparer.Ordinal)
                .ToArray();
            RequireUniqueInputs();
            RequireUniqueOutputs();
        }

        public string ContractId { get; }
        public string Revision { get; }
        public string UpdateEventId { get; }
        public IReadOnlyList<GraphHostInputDescriptor> Inputs => m_Inputs;
        public IReadOnlyList<GraphHostOutputDescriptor> Outputs => m_Outputs;
        // 宿主为图变量提供的黑板作用域；图声明了其它作用域时编译失败。
        public IReadOnlyList<PipelineBlackboardVariableScope> BlackboardScopes { get; }
        // 宿主提供的领域：扩展领域由宿主运行时传入的扩展实现执行，输出领域由宿主读取编排输出后消费。
        public IReadOnlyList<string> Domains { get; }
        // 宿主选择的数值目标；编译写入内容身份，运行准备时核对，须与宿主运行时的数值类型参数一致。
        public string NumericTarget { get; }

        public bool TryGetInput(string inputId, out GraphHostInputDescriptor descriptor)
        {
            for (int i = 0; i < m_Inputs.Length; i++)
            {
                if (string.Equals(m_Inputs[i].InputId, inputId, StringComparison.Ordinal))
                {
                    descriptor = m_Inputs[i];
                    return true;
                }
            }
            descriptor = null;
            return false;
        }

        public bool TryGetOutput(string variableId, out GraphHostOutputDescriptor descriptor)
        {
            for (int i = 0; i < m_Outputs.Length; i++)
            {
                if (string.Equals(m_Outputs[i].VariableId, variableId, StringComparison.Ordinal))
                {
                    descriptor = m_Outputs[i];
                    return true;
                }
            }
            descriptor = null;
            return false;
        }

        void RequireUniqueInputs()
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < m_Inputs.Length; i++)
            {
                GraphHostInputDescriptor descriptor = m_Inputs[i] ??
                    throw new ArgumentException("Event graph input descriptor is missing.", nameof(m_Inputs));
                if (!ids.Add(descriptor.InputId))
                    throw new ArgumentException("Event graph input identity is duplicated.", nameof(m_Inputs));
            }
        }

        void RequireUniqueOutputs()
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < m_Outputs.Length; i++)
            {
                GraphHostOutputDescriptor descriptor = m_Outputs[i] ??
                    throw new ArgumentException("Event graph output descriptor is missing.", nameof(m_Outputs));
                if (!ids.Add(descriptor.VariableId))
                    throw new ArgumentException("Event graph output identity is duplicated.", nameof(m_Outputs));
            }
        }

        static string RequireIdentity(string value, string parameterName) =>
            string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("Event graph contract identity is missing.", parameterName)
                : value.Trim();
    }

}
