using ThirdPersonSimulation;
using BTSMTL.Authoring.Blackboard;
using BTSMTL.Orchestration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace BTSMTL.Graphs
{
    public static class GraphHostInputIds
    {
        public const string DeltaSeconds = "host.delta-seconds";
    }

    public interface IGraphHostContext
    {
        GraphInvocationContext Invocation { get; }
        GraphHostValue ReadInput(int slot);
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
        Text = 8
    }

    public enum GraphVariableMutation : byte
    {
        Assign,
        Override,
        Release
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
            else if (type == typeof(float))
                kind = GraphHostValueKind.Float32;
            else if (type == typeof(UnityEngine.Vector2))
                kind = GraphHostValueKind.Vector2;
            else if (type == typeof(UnityEngine.Vector3))
                kind = GraphHostValueKind.Vector3;
            else if (type == typeof(UnityEngine.Quaternion))
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
            GraphHostValueKind.Float32 => typeof(float),
            GraphHostValueKind.Vector2 => typeof(UnityEngine.Vector2),
            GraphHostValueKind.Vector3 => typeof(UnityEngine.Vector3),
            GraphHostValueKind.Quaternion => typeof(UnityEngine.Quaternion),
            GraphHostValueKind.Enum => typeof(System.Enum),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        public static bool IsSupported(Type type) => TryGet(type, out _);
    }

    [ValueStorage]
    public readonly partial struct GraphHostValue
    {
        readonly string m_TextValue;
        readonly bool m_BoolValue;
        readonly int m_IntValue;
        readonly float m_FloatValue;
        readonly UnityEngine.Vector2 m_Vector2Value;
        readonly UnityEngine.Vector3 m_Vector3Value;
        readonly UnityEngine.Quaternion m_QuaternionValue;
        readonly Type m_EnumType;
        readonly int m_EnumValue;

        GraphHostValue(
            GraphHostValueKind kind,
            bool boolValue,
            int intValue,
            float floatValue,
            UnityEngine.Vector2 vector2Value,
            UnityEngine.Vector3 vector3Value)
            : this(
                kind,
                boolValue,
                intValue,
                floatValue,
                vector2Value,
                vector3Value,
                default,
                null,
                0)
        {
        }

        GraphHostValue(
            GraphHostValueKind kind,
            bool boolValue,
            int intValue,
            float floatValue,
            UnityEngine.Vector2 vector2Value,
            UnityEngine.Vector3 vector3Value,
            UnityEngine.Quaternion quaternionValue,
            Type enumType,
            int enumValue,
            string textValue = default)
        {
            m_TextValue = textValue;
            Kind = kind;
            m_BoolValue = boolValue;
            m_IntValue = intValue;
            m_FloatValue = floatValue;
            m_Vector2Value = vector2Value;
            m_Vector3Value = vector3Value;
            m_QuaternionValue = quaternionValue;
            m_EnumType = enumType;
            m_EnumValue = enumValue;
        }

        public GraphHostValueKind Kind { get; }

        public string TextValue
        {
            get { RequireKind(GraphHostValueKind.Text); return m_TextValue; }
        }

        public static GraphHostValue FromText(string value) =>
            new GraphHostValue(GraphHostValueKind.Text, false, 0, 0f, default, default, default, null, 0,
                value);

        public void WriteRecordedFrameState<TWriter>(ref TWriter writer)
            where TWriter : struct, IRecordedRuntimeStateWriter =>
            WriteRecordedFrameState(ref writer, Kind);

        internal static void WriteRecordedStateCapacity<TWriter>(ref TWriter writer, Type valueType)
            where TWriter : struct, IRecordedRuntimeStateWriter
        {
            GraphHostValueKinds.TryGet(valueType, out GraphHostValueKind kind);
            default(GraphHostValue).WriteRecordedFrameState(ref writer, kind);
        }

        void WriteRecordedFrameState<TWriter>(ref TWriter writer, GraphHostValueKind kind)
            where TWriter : struct, IRecordedRuntimeStateWriter
        {
            writer.Write(kind);
            switch (kind)
            {
                case GraphHostValueKind.Bool:
                    writer.Write(m_BoolValue);
                    break;
                case GraphHostValueKind.Int32:
                    writer.Write(m_IntValue);
                    break;
                case GraphHostValueKind.Float32:
                    writer.Write(m_FloatValue);
                    break;
                case GraphHostValueKind.Vector2:
                    writer.Write(m_Vector2Value);
                    break;
                case GraphHostValueKind.Vector3:
                    writer.Write(m_Vector3Value);
                    break;
                case GraphHostValueKind.Quaternion:
                    writer.Write(m_QuaternionValue);
                    break;
                case GraphHostValueKind.Enum:
                    writer.Write(m_EnumValue);
                    break;
                case GraphHostValueKind.Text:
                    writer.WriteText(m_TextValue);
                    break;
                default:
                    throw new InvalidOperationException("Event graph recorded value kind is not supported.");
            }
        }

        public static GraphHostValue ReadRecordedFrameState(
            ref RecordedRuntimeStateReader reader, Type valueType)
        {
            GraphHostValueKind kind = reader.Read<GraphHostValueKind>();
            switch (kind)
            {
                case GraphHostValueKind.Bool:
                    return new GraphHostValue(kind, reader.Read<bool>(), 0, 0f, default, default);
                case GraphHostValueKind.Int32:
                    return new GraphHostValue(kind, false, reader.Read<int>(), 0f, default, default);
                case GraphHostValueKind.Float32:
                    return new GraphHostValue(kind, false, 0, reader.Read<float>(), default, default);
                case GraphHostValueKind.Vector2:
                    return new GraphHostValue(kind, false, 0, 0f, reader.Read<UnityEngine.Vector2>(), default);
                case GraphHostValueKind.Vector3:
                    return new GraphHostValue(kind, false, 0, 0f, default, reader.Read<UnityEngine.Vector3>());
                case GraphHostValueKind.Quaternion:
                    return new GraphHostValue(kind, false, 0, 0f, default, default,
                        reader.Read<UnityEngine.Quaternion>(), null, 0);
                case GraphHostValueKind.Enum:
                    return new GraphHostValue(kind, false, 0, 0f, default, default,
                        default, valueType, reader.Read<int>());
                case GraphHostValueKind.Text:
                    return FromText(reader.ReadText());
                default:
                    throw new InvalidOperationException("Event graph recorded value kind is not supported.");
            }
        }
        public bool BoolValue
        {
            get
            {
                RequireKind(GraphHostValueKind.Bool);
                return m_BoolValue;
            }
        }

        public int Int32Value
        {
            get
            {
                RequireKind(GraphHostValueKind.Int32);
                return m_IntValue;
            }
        }

        public float Float32Value
        {
            get
            {
                RequireKind(GraphHostValueKind.Float32);
                return m_FloatValue;
            }
        }

        public UnityEngine.Vector2 Vector2Value
        {
            get
            {
                RequireKind(GraphHostValueKind.Vector2);
                return m_Vector2Value;
            }
        }

        public UnityEngine.Vector3 Vector3Value
        {
            get
            {
                RequireKind(GraphHostValueKind.Vector3);
                return m_Vector3Value;
            }
        }

        public UnityEngine.Quaternion QuaternionValue
        {
            get
            {
                RequireKind(GraphHostValueKind.Quaternion);
                return m_QuaternionValue;
            }
        }

        public Type EnumType
        {
            get
            {
                RequireKind(GraphHostValueKind.Enum);
                return m_EnumType;
            }
        }

        public int EnumValue
        {
            get
            {
                RequireKind(GraphHostValueKind.Enum);
                return m_EnumValue;
            }
        }

        public static GraphHostValue FromBool(bool value) =>
            new GraphHostValue(GraphHostValueKind.Bool, value, 0, 0f, default, default);

        public static GraphHostValue FromInt32(int value) =>
            new GraphHostValue(GraphHostValueKind.Int32, false, value, 0f, default, default);

        public static GraphHostValue FromFloat32(float value)
        {
            if (!float.IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            return new GraphHostValue(GraphHostValueKind.Float32, false, 0, value, default, default);
        }

        public static GraphHostValue FromVector2(UnityEngine.Vector2 value)
        {
            if (!float.IsFinite(value.x) || !float.IsFinite(value.y))
                throw new ArgumentOutOfRangeException(nameof(value));
            return new GraphHostValue(GraphHostValueKind.Vector2, false, 0, 0f, value, default);
        }

        public static GraphHostValue FromVector3(UnityEngine.Vector3 value)
        {
            if (!float.IsFinite(value.x) || !float.IsFinite(value.y) || !float.IsFinite(value.z))
                throw new ArgumentOutOfRangeException(nameof(value));
            return new GraphHostValue(GraphHostValueKind.Vector3, false, 0, 0f, default, value);
        }

        public static GraphHostValue FromQuaternion(UnityEngine.Quaternion value)
        {
            if (!float.IsFinite(value.x) || !float.IsFinite(value.y) ||
                !float.IsFinite(value.z) || !float.IsFinite(value.w))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            return new GraphHostValue(
                GraphHostValueKind.Quaternion,
                false,
                0,
                0f,
                default,
                default,
                value,
                null,
                0);
        }

        public static GraphHostValue FromEnum(Enum value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            return new GraphHostValue(
                GraphHostValueKind.Enum,
                false,
                0,
                0f,
                default,
                default,
                default,
                value.GetType(),
                Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture));
        }

        public static GraphHostValue FromEnum(Type type, int value) =>
            new GraphHostValue(GraphHostValueKind.Enum, false, 0, 0f, default, default, default, type, value);

        public static GraphHostValue FromObject(object value)
        {
            if (value is string || value == null)
                return FromText((string)value);
            if (value is bool boolValue)
                return FromBool(boolValue);
            if (value is int intValue)
                return FromInt32(intValue);
            if (value is float floatValue)
                return FromFloat32(floatValue);
            if (value is UnityEngine.Vector2 vector2Value)
                return FromVector2(vector2Value);
            if (value is UnityEngine.Vector3 vector3Value)
                return FromVector3(vector3Value);
            if (value is UnityEngine.Quaternion quaternionValue)
                return FromQuaternion(quaternionValue);
            if (value is Enum enumValue)
                return FromEnum(enumValue);
            throw new ArgumentException(
                $"Event graph value type '{value?.GetType().FullName ?? "null"}' is not supported.",
                nameof(value));
        }

        public object ToObject() => Kind switch
        {
            GraphHostValueKind.Text => m_TextValue,
            GraphHostValueKind.Bool => m_BoolValue,
            GraphHostValueKind.Int32 => m_IntValue,
            GraphHostValueKind.Float32 => m_FloatValue,
            GraphHostValueKind.Vector2 => m_Vector2Value,
            GraphHostValueKind.Vector3 => m_Vector3Value,
            GraphHostValueKind.Quaternion => m_QuaternionValue,
            GraphHostValueKind.Enum => Enum.ToObject(m_EnumType, m_EnumValue),
            _ => throw new InvalidOperationException("Event graph value kind is invalid.")
        };

        public T As<T>()
        {
            if (typeof(T) == typeof(string) && Kind == GraphHostValueKind.Text)
                return Reinterpret<string, T>(m_TextValue);
            if (typeof(T) == typeof(bool) && Kind == GraphHostValueKind.Bool)
                return Reinterpret<bool, T>(m_BoolValue);
            if (typeof(T) == typeof(int) && Kind == GraphHostValueKind.Int32)
                return Reinterpret<int, T>(m_IntValue);
            if (typeof(T) == typeof(float) && Kind == GraphHostValueKind.Float32)
                return Reinterpret<float, T>(m_FloatValue);
            if (typeof(T) == typeof(UnityEngine.Vector2) && Kind == GraphHostValueKind.Vector2)
                return Reinterpret<UnityEngine.Vector2, T>(m_Vector2Value);
            if (typeof(T) == typeof(UnityEngine.Vector3) && Kind == GraphHostValueKind.Vector3)
                return Reinterpret<UnityEngine.Vector3, T>(m_Vector3Value);
            if (typeof(T) == typeof(UnityEngine.Quaternion) && Kind == GraphHostValueKind.Quaternion)
                return Reinterpret<UnityEngine.Quaternion, T>(m_QuaternionValue);
            if (typeof(T).IsEnum && Kind == GraphHostValueKind.Enum && m_EnumType == typeof(T))
                return GraphEnumValue<T>.FromInt32(m_EnumValue);
            throw new InvalidOperationException(
                $"Event graph value kind '{Kind}' cannot be read as '{typeof(T).FullName}'.");
        }

        static TResult Reinterpret<TValue, TResult>(TValue value) =>
            Unsafe.As<TValue, TResult>(ref value);

        public static GraphHostValue FromTyped<T>(T value)
        {
            if (typeof(T) == typeof(bool)) return FromBool(Unsafe.As<T, bool>(ref value));
            if (typeof(T) == typeof(int)) return FromInt32(Unsafe.As<T, int>(ref value));
            if (typeof(T) == typeof(float)) return FromFloat32(Unsafe.As<T, float>(ref value));
            if (typeof(T) == typeof(UnityEngine.Vector2)) return FromVector2(Unsafe.As<T, UnityEngine.Vector2>(ref value));
            if (typeof(T) == typeof(UnityEngine.Vector3)) return FromVector3(Unsafe.As<T, UnityEngine.Vector3>(ref value));
            if (typeof(T) == typeof(UnityEngine.Quaternion)) return FromQuaternion(Unsafe.As<T, UnityEngine.Quaternion>(ref value));
            if (typeof(T) == typeof(string)) return FromText(Unsafe.As<T, string>(ref value));
            if (typeof(T).IsEnum)
                return new GraphHostValue(GraphHostValueKind.Enum, false, 0, 0f, default, default,
                    default, typeof(T), GraphEnumValue<T>.ToInt32(value));
            throw new InvalidOperationException($"Unsupported graph value type '{typeof(T).FullName}'.");
        }

        void RequireKind(GraphHostValueKind expected)
        {
            if (Kind != expected)
                throw new InvalidOperationException(
                    $"Event graph value kind '{Kind}' cannot be read as '{expected}'.");
        }
    }

    public sealed class GraphVariableDescriptor
    {
        public GraphVariableDescriptor(
            BtsmtlGraphVariableReference reference,
            string name,
            Type valueType,
            GraphHostValue initialValue,
            bool isExposedPublic = true)
        {
            if (!reference.IsValid)
                throw new ArgumentException("Event graph variable reference is invalid.", nameof(reference));
            if (!GraphHostValueKinds.TryGet(valueType, out GraphHostValueKind kind) ||
                kind != initialValue.Kind ||
                kind == GraphHostValueKind.Enum && initialValue.EnumType != valueType)
            {
                throw new ArgumentException("Event graph variable type and initial value do not match.", nameof(valueType));
            }
            Reference = reference;
            Name = string.IsNullOrWhiteSpace(name) ? reference.VariableId : name.Trim();
            ValueType = valueType;
            ValueKind = kind;
            InitialValue = initialValue;
            IsExposedPublic = isExposedPublic;
        }

        public BtsmtlGraphVariableReference Reference { get; }
        public string Name { get; }
        public Type ValueType { get; }
        public GraphHostValueKind ValueKind { get; }
        public GraphHostValue InitialValue { get; }
        public bool IsExposedPublic { get; }
    }

    public sealed class GraphVariableLayoutEntry
    {
        public GraphVariableLayoutEntry(GraphVariableDescriptor descriptor, int index)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(index));
            Index = index;
        }

        public GraphVariableDescriptor Descriptor { get; }
        public int Index { get; }
    }

    public readonly struct GraphVariableBinding
    {
        internal GraphVariableBinding(GraphVariableLayout layout, GraphVariableLayoutEntry entry)
        {
            Layout = layout;
            Index = entry.Index;
            VariableId = entry.Descriptor.Reference.VariableId;
        }

        internal GraphVariableLayout Layout { get; }
        internal int Index { get; }
        public string VariableId { get; }
    }

    public sealed class GraphVariableLayout
    {
        readonly GraphVariableLayoutEntry[] m_Entries;
        readonly Dictionary<string, GraphVariableLayoutEntry> m_EntriesById;

        public GraphVariableLayout(
            string graphId,
            string contractRevision,
            IReadOnlyList<GraphVariableDescriptor> descriptors)
        {
            GraphId = string.IsNullOrWhiteSpace(graphId)
                ? throw new ArgumentException("Event graph identity is missing.", nameof(graphId))
                : graphId.Trim();
            ContractRevision = string.IsNullOrWhiteSpace(contractRevision)
                ? throw new ArgumentException("Event graph contract revision is missing.", nameof(contractRevision))
                : contractRevision.Trim();
            GraphVariableDescriptor[] ordered = (descriptors ?? Array.Empty<GraphVariableDescriptor>())
                .Where(value => value == null || value.IsExposedPublic)
                .OrderBy(value => value?.Reference.VariableId, StringComparer.Ordinal)
                .ToArray();
            m_Entries = new GraphVariableLayoutEntry[ordered.Length];
            m_EntriesById = new Dictionary<string, GraphVariableLayoutEntry>(ordered.Length, StringComparer.Ordinal);
            for (int i = 0; i < ordered.Length; i++)
            {
                GraphVariableDescriptor descriptor = ordered[i] ??
                    throw new ArgumentException("Event graph variable descriptor is missing.", nameof(descriptors));
                m_Entries[i] = new GraphVariableLayoutEntry(descriptor, i);
                if (!m_EntriesById.TryAdd(descriptor.Reference.VariableId, m_Entries[i]))
                    throw new ArgumentException("Event graph variable identity is duplicated.", nameof(descriptors));
            }
            LayoutId = $"{GraphId}/variables/{ContractRevision}";
        }

        public string GraphId { get; }
        public string ContractRevision { get; }
        public string LayoutId { get; }
        public IReadOnlyList<GraphVariableLayoutEntry> Entries => m_Entries;

        public bool TryGet(string variableId, out GraphVariableLayoutEntry entry)
        {
            if (variableId != null)
                return m_EntriesById.TryGetValue(variableId, out entry);
            entry = null;
            return false;
        }

        public GraphVariableBinding Bind(string variableId) =>
            TryGet(variableId, out GraphVariableLayoutEntry entry)
                ? new GraphVariableBinding(this, entry)
                : throw new InvalidOperationException($"Event graph variable '{variableId}' is not published by layout '{LayoutId}'.");
    }

    public sealed class GraphVariableContract
    {
        readonly GraphVariableDescriptor[] m_Descriptors;
        readonly GraphVariableDescriptor[] m_PublishedDescriptors;

        public GraphVariableContract(
            string graphId,
            string revision,
            IReadOnlyList<GraphVariableDescriptor> descriptors)
        {
            GraphId = string.IsNullOrWhiteSpace(graphId)
                ? throw new ArgumentException("Event graph identity is missing.", nameof(graphId))
            : graphId.Trim();
            Revision = string.IsNullOrWhiteSpace(revision)
                ? throw new ArgumentException("Event graph revision is missing.", nameof(revision))
                : revision.Trim();
            m_Descriptors = (descriptors ?? Array.Empty<GraphVariableDescriptor>()).ToArray();
            m_PublishedDescriptors = m_Descriptors
                .Where(value => value.IsExposedPublic)
                .OrderBy(value => value.Reference.VariableId, StringComparer.Ordinal)
                .ToArray();
            Layout = new GraphVariableLayout(GraphId, Revision, m_Descriptors);
        }

        public string GraphId { get; }
        public string Revision { get; }
        public IReadOnlyList<GraphVariableDescriptor> Descriptors => m_Descriptors;
        public IReadOnlyList<GraphVariableDescriptor> PublishedDescriptors => m_PublishedDescriptors;
        public GraphVariableLayout Layout { get; }

        public GraphVariableDescriptor Require(string variableId)
        {
            for (int i = 0; i < m_Descriptors.Length; i++)
            {
                if (string.Equals(m_Descriptors[i].Reference.VariableId, variableId, StringComparison.Ordinal))
                    return m_Descriptors[i];
            }
            throw new InvalidOperationException($"Event graph variable '{variableId}' is not declared.");
        }
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

        public static GraphHostContract FromPublishedGraph<TValue>(BtsmtlGraphRoot graph,
            IOrchestrationGraphContentEncoding<TValue> encoding,
            string contractId, string revision, string updateEventId,
            IReadOnlyList<PipelineBlackboardVariableScope> blackboardScopes,
            IReadOnlyList<string> domains, string numericTarget) where TValue : unmanaged
        {
            OrchestrationGraphContent<TValue> content = graph.RequireExecutionData(encoding, numericTarget);
            var inputs = new List<GraphHostInputDescriptor>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (ref readonly OrchestrationGraphHostInput input in content.HostInputs.Span)
            {
                string inputId = content.Identities.Resolve(input.InputIdentityId);
                if (ids.Add(inputId))
                    inputs.Add(new GraphHostInputDescriptor(inputId,
                        GraphCompiledValues.ResolveTypeIdentity(content.Identities.Resolve(input.ValueTypeIdentityId))));
            }
            var outputs = new List<GraphHostOutputDescriptor>();
            foreach (GraphVariableDescriptor variable in graph.Variables.CreateContract().PublishedDescriptors)
                outputs.Add(new GraphHostOutputDescriptor(variable.Reference.VariableId, variable.ValueType));
            return new GraphHostContract(contractId, revision, updateEventId, inputs, outputs,
                blackboardScopes, domains, numericTarget);
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

    internal static class GraphEnumValue<T>
    {
        static readonly TypeCode s_Code = Type.GetTypeCode(Enum.GetUnderlyingType(typeof(T)));

        internal static int ToInt32(T value) => s_Code switch
        {
            TypeCode.SByte => Unsafe.As<T, sbyte>(ref value),
            TypeCode.Byte => Unsafe.As<T, byte>(ref value),
            TypeCode.Int16 => Unsafe.As<T, short>(ref value),
            TypeCode.UInt16 => Unsafe.As<T, ushort>(ref value),
            TypeCode.Int32 => Unsafe.As<T, int>(ref value),
            TypeCode.UInt32 => checked((int)Unsafe.As<T, uint>(ref value)),
            TypeCode.Int64 => checked((int)Unsafe.As<T, long>(ref value)),
            TypeCode.UInt64 => checked((int)Unsafe.As<T, ulong>(ref value)),
            _ => throw new InvalidOperationException("Unsupported enum storage.")
        };

        internal static T FromInt32(int value)
        {
            switch (s_Code)
            {
                case TypeCode.SByte: { sbyte v = checked((sbyte)value); return Unsafe.As<sbyte, T>(ref v); }
                case TypeCode.Byte: { byte v = checked((byte)value); return Unsafe.As<byte, T>(ref v); }
                case TypeCode.Int16: { short v = checked((short)value); return Unsafe.As<short, T>(ref v); }
                case TypeCode.UInt16: { ushort v = checked((ushort)value); return Unsafe.As<ushort, T>(ref v); }
                case TypeCode.Int32: return Unsafe.As<int, T>(ref value);
                case TypeCode.UInt32: { uint v = checked((uint)value); return Unsafe.As<uint, T>(ref v); }
                case TypeCode.Int64: { long v = value; return Unsafe.As<long, T>(ref v); }
                case TypeCode.UInt64: { ulong v = checked((ulong)value); return Unsafe.As<ulong, T>(ref v); }
                default: throw new InvalidOperationException("Unsupported enum storage.");
            }
        }
    }

    public readonly struct GraphVariableFrame
    {
        readonly GraphHostValueFrame m_Values;

        internal GraphVariableFrame(GraphVariableContract contract, GraphHostValueFrame values)
        {
            Contract = contract;
            m_Values = values;
        }

        public GraphVariableContract Contract { get; }
        public GraphInvocationIdentity Invocation => m_Values.Invocation;
        public ulong ResetGeneration => m_Values.ResetGeneration;
        public bool IsValid => m_Values.IsValid;
        public void Retain() => m_Values.Retain();
        public void Release() => m_Values.Release();
        public string GraphId => Contract.GraphId;
        public string ContractRevision => Contract.Revision;
        public string LayoutId => Contract.Layout.LayoutId;

        public bool TryRead(string variableId, out GraphHostValue value)
        {
            if (IsValid && Contract.Layout.TryGet(variableId, out GraphVariableLayoutEntry entry))
            {
                value = m_Values.Read(entry.Index);
                return true;
            }
            value = default;
            return false;
        }

        public bool TryRead(BtsmtlGraphVariableReference reference, out GraphHostValue value)
        {
            if (IsValid && reference.IsValid &&
                string.Equals(reference.GraphId, GraphId, StringComparison.Ordinal))
                return TryRead(reference.VariableId, out value);
            value = default;
            return false;
        }

        public bool TryRead(GraphVariableBinding binding, out GraphHostValue value)
        {
            if (IsValid && ReferenceEquals(Contract.Layout, binding.Layout))
            {
                value = m_Values.Read(binding.Index);
                return true;
            }
            value = default;
            return false;
        }

        public GraphHostValue Require(GraphVariableBinding binding)
        {
            if (TryRead(binding, out GraphHostValue value))
                return value;
            throw new InvalidOperationException(
                $"Event graph variable binding '{binding.VariableId}' is unavailable in frame '{m_Values}'.");
        }

        public GraphHostValue Require(string variableId)
        {
            if (TryRead(variableId, out GraphHostValue value))
                return value;
            throw new InvalidOperationException(
                $"Event graph variable '{variableId}' is unavailable in frame '{m_Values}'.");
        }
    }

    public readonly struct GraphExecutionResult
    {
        GraphExecutionResult(
            bool succeeded,
            GraphVariableFrame frame,
            GraphExecutionFailure failure)
        {
            Succeeded = succeeded;
            Frame = frame;
            Failure = failure;
        }

        public bool Succeeded { get; }
        public GraphVariableFrame Frame { get; }
        public GraphExecutionFailure Failure { get; }

        internal static GraphExecutionResult Success(GraphVariableFrame frame) =>
            new GraphExecutionResult(
                true,
                frame.IsValid ? frame : throw new ArgumentException("Event graph frame is invalid.", nameof(frame)),
                null);

        internal static GraphExecutionResult Failed(GraphExecutionFailure failure) =>
            new GraphExecutionResult(
                false,
                default,
                failure ?? throw new ArgumentNullException(nameof(failure)));
    }
}
