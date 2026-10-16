using ThirdPersonSimulation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace BTSMTL.EventGraphs
{
    public interface IEventGraphHostContext
    {
        EventGraphInvocationContext Invocation { get; }
        EventGraphValue ReadInput(int slot);
    }

    public enum EventGraphValueKind : byte
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

    public enum EventGraphVariableMutation : byte
    {
        Assign,
        Override,
        Release
    }

    public static class EventGraphValueKinds
    {
        public static bool TryGet(Type type, out EventGraphValueKind kind)
        {
            if (type == typeof(string))
                kind = EventGraphValueKind.Text;
            else if (type == typeof(bool))
                kind = EventGraphValueKind.Bool;
            else if (type == typeof(int))
                kind = EventGraphValueKind.Int32;
            else if (type == typeof(float))
                kind = EventGraphValueKind.Float32;
            else if (type == typeof(UnityEngine.Vector2))
                kind = EventGraphValueKind.Vector2;
            else if (type == typeof(UnityEngine.Vector3))
                kind = EventGraphValueKind.Vector3;
            else if (type == typeof(UnityEngine.Quaternion))
                kind = EventGraphValueKind.Quaternion;
            else if (type != null && type.IsEnum)
                kind = EventGraphValueKind.Enum;
            else
            {
                kind = default;
                return false;
            }
            return true;
        }

        public static Type RequireType(EventGraphValueKind kind) => kind switch
        {
            EventGraphValueKind.Text => typeof(string),
            EventGraphValueKind.Bool => typeof(bool),
            EventGraphValueKind.Int32 => typeof(int),
            EventGraphValueKind.Float32 => typeof(float),
            EventGraphValueKind.Vector2 => typeof(UnityEngine.Vector2),
            EventGraphValueKind.Vector3 => typeof(UnityEngine.Vector3),
            EventGraphValueKind.Quaternion => typeof(UnityEngine.Quaternion),
            EventGraphValueKind.Enum => typeof(System.Enum),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        public static bool IsSupported(Type type) => TryGet(type, out _);
    }

    [ValueStorage]
    public readonly partial struct EventGraphValue
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

        EventGraphValue(
            EventGraphValueKind kind,
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

        EventGraphValue(
            EventGraphValueKind kind,
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

        public EventGraphValueKind Kind { get; }

        public string TextValue
        {
            get { RequireKind(EventGraphValueKind.Text); return m_TextValue; }
        }

        public static EventGraphValue FromText(string value) =>
            new EventGraphValue(EventGraphValueKind.Text, false, 0, 0f, default, default, default, null, 0,
                value);

#if UNITY_EDITOR
        public void WriteRecordedFrameState<TWriter>(ref TWriter writer)
            where TWriter : struct, IRecordedRuntimeStateWriter
        {
            writer.Write(Kind);
            switch (Kind)
            {
                case EventGraphValueKind.Bool:
                    writer.Write(m_BoolValue);
                    break;
                case EventGraphValueKind.Int32:
                    writer.Write(m_IntValue);
                    break;
                case EventGraphValueKind.Float32:
                    writer.Write(m_FloatValue);
                    break;
                case EventGraphValueKind.Vector2:
                    writer.Write(m_Vector2Value);
                    break;
                case EventGraphValueKind.Vector3:
                    writer.Write(m_Vector3Value);
                    break;
                case EventGraphValueKind.Quaternion:
                    writer.Write(m_QuaternionValue);
                    break;
                case EventGraphValueKind.Enum:
                    writer.Write(m_EnumValue);
                    break;
                case EventGraphValueKind.Text:
                    writer.WriteText(m_TextValue);
                    break;
                default:
                    throw new InvalidOperationException("Event graph recorded value kind is not supported.");
            }
        }

        public static EventGraphValue ReadRecordedFrameState(
            ref RecordedRuntimeStateReader reader, Type valueType)
        {
            EventGraphValueKind kind = reader.Read<EventGraphValueKind>();
            switch (kind)
            {
                case EventGraphValueKind.Bool:
                    return new EventGraphValue(kind, reader.Read<bool>(), 0, 0f, default, default);
                case EventGraphValueKind.Int32:
                    return new EventGraphValue(kind, false, reader.Read<int>(), 0f, default, default);
                case EventGraphValueKind.Float32:
                    return new EventGraphValue(kind, false, 0, reader.Read<float>(), default, default);
                case EventGraphValueKind.Vector2:
                    return new EventGraphValue(kind, false, 0, 0f, reader.Read<UnityEngine.Vector2>(), default);
                case EventGraphValueKind.Vector3:
                    return new EventGraphValue(kind, false, 0, 0f, default, reader.Read<UnityEngine.Vector3>());
                case EventGraphValueKind.Quaternion:
                    return new EventGraphValue(kind, false, 0, 0f, default, default,
                        reader.Read<UnityEngine.Quaternion>(), null, 0);
                case EventGraphValueKind.Enum:
                    return new EventGraphValue(kind, false, 0, 0f, default, default,
                        default, valueType, reader.Read<int>());
                case EventGraphValueKind.Text:
                    return FromText(reader.ReadText());
                default:
                    throw new InvalidOperationException("Event graph recorded value kind is not supported.");
            }
        }
#endif

        public bool BoolValue
        {
            get
            {
                RequireKind(EventGraphValueKind.Bool);
                return m_BoolValue;
            }
        }

        public int Int32Value
        {
            get
            {
                RequireKind(EventGraphValueKind.Int32);
                return m_IntValue;
            }
        }

        public float Float32Value
        {
            get
            {
                RequireKind(EventGraphValueKind.Float32);
                return m_FloatValue;
            }
        }

        public UnityEngine.Vector2 Vector2Value
        {
            get
            {
                RequireKind(EventGraphValueKind.Vector2);
                return m_Vector2Value;
            }
        }

        public UnityEngine.Vector3 Vector3Value
        {
            get
            {
                RequireKind(EventGraphValueKind.Vector3);
                return m_Vector3Value;
            }
        }

        public UnityEngine.Quaternion QuaternionValue
        {
            get
            {
                RequireKind(EventGraphValueKind.Quaternion);
                return m_QuaternionValue;
            }
        }

        public Type EnumType
        {
            get
            {
                RequireKind(EventGraphValueKind.Enum);
                return m_EnumType;
            }
        }

        public int EnumValue
        {
            get
            {
                RequireKind(EventGraphValueKind.Enum);
                return m_EnumValue;
            }
        }

        public static EventGraphValue FromBool(bool value) =>
            new EventGraphValue(EventGraphValueKind.Bool, value, 0, 0f, default, default);

        public static EventGraphValue FromInt32(int value) =>
            new EventGraphValue(EventGraphValueKind.Int32, false, value, 0f, default, default);

        public static EventGraphValue FromFloat32(float value)
        {
            if (!float.IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            return new EventGraphValue(EventGraphValueKind.Float32, false, 0, value, default, default);
        }

        public static EventGraphValue FromVector2(UnityEngine.Vector2 value)
        {
            if (!float.IsFinite(value.x) || !float.IsFinite(value.y))
                throw new ArgumentOutOfRangeException(nameof(value));
            return new EventGraphValue(EventGraphValueKind.Vector2, false, 0, 0f, value, default);
        }

        public static EventGraphValue FromVector3(UnityEngine.Vector3 value)
        {
            if (!float.IsFinite(value.x) || !float.IsFinite(value.y) || !float.IsFinite(value.z))
                throw new ArgumentOutOfRangeException(nameof(value));
            return new EventGraphValue(EventGraphValueKind.Vector3, false, 0, 0f, default, value);
        }

        public static EventGraphValue FromQuaternion(UnityEngine.Quaternion value)
        {
            if (!float.IsFinite(value.x) || !float.IsFinite(value.y) ||
                !float.IsFinite(value.z) || !float.IsFinite(value.w))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            return new EventGraphValue(
                EventGraphValueKind.Quaternion,
                false,
                0,
                0f,
                default,
                default,
                value,
                null,
                0);
        }

        public static EventGraphValue FromEnum(Enum value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            return new EventGraphValue(
                EventGraphValueKind.Enum,
                false,
                0,
                0f,
                default,
                default,
                default,
                value.GetType(),
                Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture));
        }

        public static EventGraphValue FromEnum(Type type, int value) =>
            new EventGraphValue(EventGraphValueKind.Enum, false, 0, 0f, default, default, default, type, value);

        public static EventGraphValue FromObject(object value)
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
            EventGraphValueKind.Text => m_TextValue,
            EventGraphValueKind.Bool => m_BoolValue,
            EventGraphValueKind.Int32 => m_IntValue,
            EventGraphValueKind.Float32 => m_FloatValue,
            EventGraphValueKind.Vector2 => m_Vector2Value,
            EventGraphValueKind.Vector3 => m_Vector3Value,
            EventGraphValueKind.Quaternion => m_QuaternionValue,
            EventGraphValueKind.Enum => Enum.ToObject(m_EnumType, m_EnumValue),
            _ => throw new InvalidOperationException("Event graph value kind is invalid.")
        };

        public T As<T>()
        {
            if (typeof(T) == typeof(string) && Kind == EventGraphValueKind.Text)
                return Reinterpret<string, T>(m_TextValue);
            if (typeof(T) == typeof(bool) && Kind == EventGraphValueKind.Bool)
                return Reinterpret<bool, T>(m_BoolValue);
            if (typeof(T) == typeof(int) && Kind == EventGraphValueKind.Int32)
                return Reinterpret<int, T>(m_IntValue);
            if (typeof(T) == typeof(float) && Kind == EventGraphValueKind.Float32)
                return Reinterpret<float, T>(m_FloatValue);
            if (typeof(T) == typeof(UnityEngine.Vector2) && Kind == EventGraphValueKind.Vector2)
                return Reinterpret<UnityEngine.Vector2, T>(m_Vector2Value);
            if (typeof(T) == typeof(UnityEngine.Vector3) && Kind == EventGraphValueKind.Vector3)
                return Reinterpret<UnityEngine.Vector3, T>(m_Vector3Value);
            if (typeof(T) == typeof(UnityEngine.Quaternion) && Kind == EventGraphValueKind.Quaternion)
                return Reinterpret<UnityEngine.Quaternion, T>(m_QuaternionValue);
            if (typeof(T).IsEnum && Kind == EventGraphValueKind.Enum && m_EnumType == typeof(T))
                return EventGraphEnumValue<T>.FromInt32(m_EnumValue);
            throw new InvalidOperationException(
                $"Event graph value kind '{Kind}' cannot be read as '{typeof(T).FullName}'.");
        }

        static TResult Reinterpret<TValue, TResult>(TValue value) =>
            Unsafe.As<TValue, TResult>(ref value);

        public static EventGraphValue FromTyped<T>(T value)
        {
            if (typeof(T) == typeof(bool)) return FromBool(Unsafe.As<T, bool>(ref value));
            if (typeof(T) == typeof(int)) return FromInt32(Unsafe.As<T, int>(ref value));
            if (typeof(T) == typeof(float)) return FromFloat32(Unsafe.As<T, float>(ref value));
            if (typeof(T) == typeof(UnityEngine.Vector2)) return FromVector2(Unsafe.As<T, UnityEngine.Vector2>(ref value));
            if (typeof(T) == typeof(UnityEngine.Vector3)) return FromVector3(Unsafe.As<T, UnityEngine.Vector3>(ref value));
            if (typeof(T) == typeof(UnityEngine.Quaternion)) return FromQuaternion(Unsafe.As<T, UnityEngine.Quaternion>(ref value));
            if (typeof(T) == typeof(string)) return FromText(Unsafe.As<T, string>(ref value));
            if (typeof(T).IsEnum)
                return new EventGraphValue(EventGraphValueKind.Enum, false, 0, 0f, default, default,
                    default, typeof(T), EventGraphEnumValue<T>.ToInt32(value));
            throw new InvalidOperationException($"Unsupported EventGraph value type '{typeof(T).FullName}'.");
        }

        void RequireKind(EventGraphValueKind expected)
        {
            if (Kind != expected)
                throw new InvalidOperationException(
                    $"Event graph value kind '{Kind}' cannot be read as '{expected}'.");
        }
    }

    public sealed class EventGraphVariableDescriptor
    {
        public EventGraphVariableDescriptor(
            BtsmtlGraphVariableReference reference,
            string name,
            Type valueType,
            EventGraphValue initialValue,
            bool isExposedPublic = true)
        {
            if (!reference.IsValid)
                throw new ArgumentException("Event graph variable reference is invalid.", nameof(reference));
            if (!EventGraphValueKinds.TryGet(valueType, out EventGraphValueKind kind) ||
                kind != initialValue.Kind ||
                kind == EventGraphValueKind.Enum && initialValue.EnumType != valueType)
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
        public EventGraphValueKind ValueKind { get; }
        public EventGraphValue InitialValue { get; }
        public bool IsExposedPublic { get; }
    }

    public sealed class EventGraphVariableLayoutEntry
    {
        public EventGraphVariableLayoutEntry(EventGraphVariableDescriptor descriptor, int index)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(index));
            Index = index;
        }

        public EventGraphVariableDescriptor Descriptor { get; }
        public int Index { get; }
    }

    public readonly struct EventGraphVariableBinding
    {
        internal EventGraphVariableBinding(EventGraphVariableLayout layout, EventGraphVariableLayoutEntry entry)
        {
            Layout = layout;
            Index = entry.Index;
            VariableId = entry.Descriptor.Reference.VariableId;
        }

        internal EventGraphVariableLayout Layout { get; }
        internal int Index { get; }
        public string VariableId { get; }
    }

    public sealed class EventGraphVariableLayout
    {
        readonly EventGraphVariableLayoutEntry[] m_Entries;
        readonly Dictionary<string, EventGraphVariableLayoutEntry> m_EntriesById;

        public EventGraphVariableLayout(
            string graphId,
            string contractRevision,
            IReadOnlyList<EventGraphVariableDescriptor> descriptors)
        {
            GraphId = string.IsNullOrWhiteSpace(graphId)
                ? throw new ArgumentException("Event graph identity is missing.", nameof(graphId))
                : graphId.Trim();
            ContractRevision = string.IsNullOrWhiteSpace(contractRevision)
                ? throw new ArgumentException("Event graph contract revision is missing.", nameof(contractRevision))
                : contractRevision.Trim();
            EventGraphVariableDescriptor[] ordered = (descriptors ?? Array.Empty<EventGraphVariableDescriptor>())
                .Where(value => value == null || value.IsExposedPublic)
                .OrderBy(value => value?.Reference.VariableId, StringComparer.Ordinal)
                .ToArray();
            m_Entries = new EventGraphVariableLayoutEntry[ordered.Length];
            m_EntriesById = new Dictionary<string, EventGraphVariableLayoutEntry>(ordered.Length, StringComparer.Ordinal);
            for (int i = 0; i < ordered.Length; i++)
            {
                EventGraphVariableDescriptor descriptor = ordered[i] ??
                    throw new ArgumentException("Event graph variable descriptor is missing.", nameof(descriptors));
                m_Entries[i] = new EventGraphVariableLayoutEntry(descriptor, i);
                if (!m_EntriesById.TryAdd(descriptor.Reference.VariableId, m_Entries[i]))
                    throw new ArgumentException("Event graph variable identity is duplicated.", nameof(descriptors));
            }
            LayoutId = $"{GraphId}/variables/{ContractRevision}";
        }

        public string GraphId { get; }
        public string ContractRevision { get; }
        public string LayoutId { get; }
        public IReadOnlyList<EventGraphVariableLayoutEntry> Entries => m_Entries;

        public bool TryGet(string variableId, out EventGraphVariableLayoutEntry entry)
        {
            if (variableId != null)
                return m_EntriesById.TryGetValue(variableId, out entry);
            entry = null;
            return false;
        }

        public EventGraphVariableBinding Bind(string variableId) =>
            TryGet(variableId, out EventGraphVariableLayoutEntry entry)
                ? new EventGraphVariableBinding(this, entry)
                : throw new InvalidOperationException($"Event graph variable '{variableId}' is not published by layout '{LayoutId}'.");
    }

    public sealed class EventGraphVariableContract
    {
        readonly EventGraphVariableDescriptor[] m_Descriptors;
        readonly EventGraphVariableDescriptor[] m_PublishedDescriptors;

        public EventGraphVariableContract(
            string graphId,
            string revision,
            IReadOnlyList<EventGraphVariableDescriptor> descriptors)
        {
            GraphId = string.IsNullOrWhiteSpace(graphId)
                ? throw new ArgumentException("Event graph identity is missing.", nameof(graphId))
            : graphId.Trim();
            Revision = string.IsNullOrWhiteSpace(revision)
                ? throw new ArgumentException("Event graph revision is missing.", nameof(revision))
                : revision.Trim();
            m_Descriptors = (descriptors ?? Array.Empty<EventGraphVariableDescriptor>()).ToArray();
            m_PublishedDescriptors = m_Descriptors
                .Where(value => value.IsExposedPublic)
                .OrderBy(value => value.Reference.VariableId, StringComparer.Ordinal)
                .ToArray();
            Layout = new EventGraphVariableLayout(GraphId, Revision, m_Descriptors);
        }

        public string GraphId { get; }
        public string Revision { get; }
        public IReadOnlyList<EventGraphVariableDescriptor> Descriptors => m_Descriptors;
        public IReadOnlyList<EventGraphVariableDescriptor> PublishedDescriptors => m_PublishedDescriptors;
        public EventGraphVariableLayout Layout { get; }

        public EventGraphVariableDescriptor Require(string variableId)
        {
            for (int i = 0; i < m_Descriptors.Length; i++)
            {
                if (string.Equals(m_Descriptors[i].Reference.VariableId, variableId, StringComparison.Ordinal))
                    return m_Descriptors[i];
            }
            throw new InvalidOperationException($"Event graph variable '{variableId}' is not declared.");
        }
    }

    public sealed class EventGraphInputDescriptor
    {
        public EventGraphInputDescriptor(string inputId, Type valueType)
        {
            InputId = string.IsNullOrWhiteSpace(inputId)
                ? throw new ArgumentException("Event graph input identity is missing.", nameof(inputId))
                : inputId.Trim();
            if (!EventGraphValueKinds.TryGet(valueType, out EventGraphValueKind valueKind))
                throw new ArgumentException("Event graph input type is not supported.", nameof(valueType));
            ValueType = valueType;
            ValueKind = valueKind;
        }

        public string InputId { get; }
        public Type ValueType { get; }
        public EventGraphValueKind ValueKind { get; }
    }

    public sealed class EventGraphOutputDescriptor
    {
        public EventGraphOutputDescriptor(string variableId, Type valueType)
        {
            VariableId = string.IsNullOrWhiteSpace(variableId)
                ? throw new ArgumentException("Event graph output variable identity is missing.", nameof(variableId))
                : variableId.Trim();
            if (!EventGraphValueKinds.TryGet(valueType, out EventGraphValueKind valueKind))
                throw new ArgumentException("Event graph output type is not supported.", nameof(valueType));
            ValueType = valueType;
            ValueKind = valueKind;
        }

        public string VariableId { get; }
        public Type ValueType { get; }
        public EventGraphValueKind ValueKind { get; }
    }

    public sealed class EventGraphHostContract
    {
        readonly EventGraphInputDescriptor[] m_Inputs;
        readonly EventGraphOutputDescriptor[] m_Outputs;

        public EventGraphHostContract(
            string contractId,
            string revision,
            string updateEventId,
            IReadOnlyList<EventGraphInputDescriptor> inputs,
            IReadOnlyList<EventGraphOutputDescriptor> outputs)
        {
            ContractId = RequireIdentity(contractId, nameof(contractId));
            Revision = RequireIdentity(revision, nameof(revision));
            UpdateEventId = RequireIdentity(updateEventId, nameof(updateEventId));
            m_Inputs = (inputs ?? Array.Empty<EventGraphInputDescriptor>())
                .OrderBy(value => value?.InputId, StringComparer.Ordinal)
                .ToArray();
            m_Outputs = (outputs ?? Array.Empty<EventGraphOutputDescriptor>())
                .OrderBy(value => value?.VariableId, StringComparer.Ordinal)
                .ToArray();
            RequireUniqueInputs();
            RequireUniqueOutputs();
        }

        public string ContractId { get; }
        public string Revision { get; }
        public string UpdateEventId { get; }
        public IReadOnlyList<EventGraphInputDescriptor> Inputs => m_Inputs;
        public IReadOnlyList<EventGraphOutputDescriptor> Outputs => m_Outputs;

        public bool TryGetInput(string inputId, out EventGraphInputDescriptor descriptor)
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

        public bool TryGetOutput(string variableId, out EventGraphOutputDescriptor descriptor)
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
                EventGraphInputDescriptor descriptor = m_Inputs[i] ??
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
                EventGraphOutputDescriptor descriptor = m_Outputs[i] ??
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

    internal static class EventGraphEnumValue<T>
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

    public readonly struct EventGraphVariableFrame
    {
        readonly EventGraphValueFrame m_Values;

        internal EventGraphVariableFrame(EventGraphVariableContract contract, EventGraphValueFrame values)
        {
            Contract = contract;
            m_Values = values;
        }

        public EventGraphVariableContract Contract { get; }
        public EventGraphInvocationIdentity Invocation => m_Values.Invocation;
        public ulong ResetGeneration => m_Values.ResetGeneration;
        public bool IsValid => m_Values.IsValid;
        public void Retain() => m_Values.Retain();
        public void Release() => m_Values.Release();
        public string GraphId => Contract.GraphId;
        public string ContractRevision => Contract.Revision;
        public string LayoutId => Contract.Layout.LayoutId;

        public bool TryRead(string variableId, out EventGraphValue value)
        {
            if (IsValid && Contract.Layout.TryGet(variableId, out EventGraphVariableLayoutEntry entry))
            {
                value = m_Values.Read(entry.Index);
                return true;
            }
            value = default;
            return false;
        }

        public bool TryRead(BtsmtlGraphVariableReference reference, out EventGraphValue value)
        {
            if (IsValid && reference.IsValid &&
                string.Equals(reference.GraphId, GraphId, StringComparison.Ordinal))
                return TryRead(reference.VariableId, out value);
            value = default;
            return false;
        }

        public bool TryRead(EventGraphVariableBinding binding, out EventGraphValue value)
        {
            if (IsValid && ReferenceEquals(Contract.Layout, binding.Layout))
            {
                value = m_Values.Read(binding.Index);
                return true;
            }
            value = default;
            return false;
        }

        public EventGraphValue Require(EventGraphVariableBinding binding)
        {
            if (TryRead(binding, out EventGraphValue value))
                return value;
            throw new InvalidOperationException(
                $"Event graph variable binding '{binding.VariableId}' is unavailable in frame '{m_Values}'.");
        }

        public EventGraphValue Require(string variableId)
        {
            if (TryRead(variableId, out EventGraphValue value))
                return value;
            throw new InvalidOperationException(
                $"Event graph variable '{variableId}' is unavailable in frame '{m_Values}'.");
        }
    }

    public readonly struct EventGraphExecutionResult
    {
        EventGraphExecutionResult(
            bool succeeded,
            EventGraphVariableFrame frame,
            EventGraphExecutionFailure failure)
        {
            Succeeded = succeeded;
            Frame = frame;
            Failure = failure;
        }

        public bool Succeeded { get; }
        public EventGraphVariableFrame Frame { get; }
        public EventGraphExecutionFailure Failure { get; }

        internal static EventGraphExecutionResult Success(EventGraphVariableFrame frame) =>
            new EventGraphExecutionResult(
                true,
                frame.IsValid ? frame : throw new ArgumentException("Event graph frame is invalid.", nameof(frame)),
                null);

        internal static EventGraphExecutionResult Failed(EventGraphExecutionFailure failure) =>
            new EventGraphExecutionResult(
                false,
                default,
                failure ?? throw new ArgumentNullException(nameof(failure)));
    }
}
