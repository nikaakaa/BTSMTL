using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BTSMTL.Authoring.Documents;
using ThirdPersonSimulation;

namespace BTSMTL.Compilation
{
    // 被引用内容的编译值读法：按条目种类把导出字段转为编译记录；Unity 资产来源与独立编译的身份表共用同一读法。
    public sealed class GraphCompileReferenceKinds
    {
        readonly Dictionary<Type, (string Kind, Func<GraphNodeFields, object> Read)> m_Readers = new();

        public GraphCompileReferenceKinds Add<T>(string kind, Func<GraphNodeFields, T> read) where T : class
        {
            m_Readers.Add(typeof(T), (kind, read));
            return this;
        }

        public T Read<T>(AuthoringReferenceEntry entry, IGraphCompileReferences references) where T : class
        {
            if (!m_Readers.TryGetValue(typeof(T), out var reader))
                throw new GraphNodeCompileException($"编译期引用类型 '{typeof(T).Name}' 没有登记读法。");
            if (!string.Equals(reader.Kind, entry.Kind, StringComparison.Ordinal))
                throw new GraphNodeCompileException($"引用 '{entry.Key}' 的种类是 '{entry.Kind}'，节点需要 '{reader.Kind}'。");
            return (T)reader.Read(new GraphNodeFields(entry.Fields, references));
        }
    }

    // 独立编译的引用来源：只认导出的身份表，表中没有的引用直接失败。
    public sealed class GraphCompileReferenceTable : IGraphCompileReferences
    {
        readonly Dictionary<string, AuthoringReferenceEntry> m_Entries = new(StringComparer.Ordinal);
        readonly GraphCompileReferenceKinds m_Kinds;

        public GraphCompileReferenceTable(AuthoringReferenceTable table, GraphCompileReferenceKinds kinds)
        {
            m_Kinds = kinds;
            foreach (AuthoringReferenceEntry entry in table.Entries.Span)
                if (!m_Entries.TryAdd(entry.Key, entry))
                    throw new GraphNodeCompileException($"身份表重复登记引用 '{entry.Key}'。");
        }

        public T Resolve<T>(GraphNodeField value) where T : class
        {
            string key = AuthoringReferenceEntry.KeyOf(value.Read<AuthoringValue>());
            return m_Entries.TryGetValue(key, out AuthoringReferenceEntry entry)
                ? m_Kinds.Read<T>(entry, this)
                : throw new GraphNodeCompileException($"引用 '{key}' 不在身份表中。");
        }

        public GraphCompileReference Reference(GraphNodeField value)
        {
            var reference = (AuthoringReferenceValue)value.Read<AuthoringValue>();
            return new GraphCompileReference(reference.Target.RootId.Value, reference.Target.DocumentId.Value,
                reference.Target.ElementId.Value);
        }
    }

    public sealed partial class GraphNodeFields
    {
        public GraphNodeFields(ReadOnlyMemory<AuthoringNamedValue> values, IGraphCompileReferences references)
            : this(new DocumentGraphNodeFieldReader(values), references) { }
    }

    sealed class DocumentGraphNodeFieldReader : IGraphNodeFieldReader
    {
        readonly Dictionary<string, AuthoringValue> m_Fields;
        readonly AuthoringValueCodec m_Values = new();

        internal DocumentGraphNodeFieldReader(ReadOnlyMemory<AuthoringNamedValue> values) =>
            m_Fields = values.ToArray().ToDictionary(value => value.FieldId.Value, value => value.Value, StringComparer.Ordinal);

        AuthoringValue Value(string fieldId) => m_Fields.TryGetValue(fieldId, out AuthoringValue value) ? value :
            throw new GraphNodeCompileException($"节点缺少作者字段 '{fieldId}'。");

        public bool IsNull(string fieldId) => Value(fieldId) is AuthoringNullValue;

        public T Read<T>(string fieldId)
        {
            if (typeof(T) == typeof(AuthoringValue)) return (T)(object)Value(fieldId);
            if (typeof(T) == typeof(string)) return (T)(object)(IsNull(fieldId) ? null : Scalar(fieldId));
            if (typeof(T).IsEnum) return (T)Enum.Parse(typeof(T), Scalar(fieldId), false);
            if (Value(fieldId) is AuthoringScalarValue scalar)
            {
                object number = Type.GetTypeCode(typeof(T)) switch
                {
                    TypeCode.Boolean => scalar.Value switch
                    {
                        "true" => true, "false" => false,
                        _ => throw new GraphNodeCompileException($"作者字段 '{fieldId}' 不是布尔值：'{scalar.Value}'。")
                    },
                    TypeCode.Int32 => int.Parse(scalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture),
                    TypeCode.Int64 => long.Parse(scalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture),
                    TypeCode.UInt32 => uint.Parse(scalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture),
                    TypeCode.UInt64 => ulong.Parse(scalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture),
                    TypeCode.Single => float.Parse(scalar.Value, NumberStyles.Float, CultureInfo.InvariantCulture),
                    TypeCode.Double => double.Parse(scalar.Value, NumberStyles.Float, CultureInfo.InvariantCulture),
                    _ => m_Values.Restore(typeof(T), scalar)
                };
                return (T)number;
            }
            if (typeof(T) == typeof(SemanticVector2Value))
            {
                IGraphNodeFieldReader vector = Object(fieldId);
                return (T)(object)new SemanticVector2Value(vector.Read<float>("x"), vector.Read<float>("y"));
            }
            if (typeof(T) == typeof(SemanticVector3Value))
            {
                IGraphNodeFieldReader vector = Object(fieldId);
                return (T)(object)new SemanticVector3Value(vector.Read<float>("x"), vector.Read<float>("y"), vector.Read<float>("z"));
            }
            if (typeof(T) == typeof(GameplayAbilityCurve<double>)) return (T)(object)Curve(fieldId);
            return (T)m_Values.Restore(typeof(T), Value(fieldId));
        }

        public IGraphNodeFieldReader Object(string fieldId) => Value(fieldId) switch
        {
            AuthoringNullValue => null,
            AuthoringObjectValue composite => new DocumentGraphNodeFieldReader(composite.Members),
            _ => throw new GraphNodeCompileException($"作者字段 '{fieldId}' 不是复合值。")
        };

        public IReadOnlyList<IGraphNodeFieldReader> Objects(string fieldId) => Value(fieldId) switch
        {
            AuthoringNullValue => Array.Empty<IGraphNodeFieldReader>(),
            AuthoringSequenceValue sequence => sequence.Items.ToArray().Select(item => item switch
            {
                AuthoringObjectValue composite => (IGraphNodeFieldReader)new DocumentGraphNodeFieldReader(composite.Members),
                _ => throw new GraphNodeCompileException($"作者字段 '{fieldId}' 的元素不是复合值。")
            }).ToArray(),
            _ => throw new GraphNodeCompileException($"作者字段 '{fieldId}' 不是序列。")
        };

        GameplayAbilityCurve<double> Curve(string fieldId)
        {
            var curve = (DocumentGraphNodeFieldReader)Object(fieldId);
            if (curve == null) return null;
            IReadOnlyList<IGraphNodeFieldReader> source = curve.Objects("keys");
            var keys = new GameplayAbilityCurveKey<double>[source.Count];
            for (int i = 0; i < keys.Length; i++)
            {
                IGraphNodeFieldReader key = source[i];
                float inTangent = key.Read<float>("inTangent"), outTangent = key.Read<float>("outTangent");
                keys[i] = new GameplayAbilityCurveKey<double>(key.Read<float>("time"), key.Read<float>("value"),
                    float.IsInfinity(inTangent) ? 0f : inTangent, float.IsInfinity(outTangent) ? 0f : outTangent,
                    key.Read<float>("inWeight"), key.Read<float>("outWeight"),
                    WeightedMode(((DocumentGraphNodeFieldReader)key).Scalar("weightedMode")),
                    i + 1 < source.Count && (float.IsInfinity(outTangent) || float.IsInfinity(source[i + 1].Read<float>("inTangent"))));
            }
            return new GameplayAbilityCurve<double>(WrapMode(curve.Scalar("preWrapMode")),
                WrapMode(curve.Scalar("postWrapMode")), keys);
        }

        string Scalar(string fieldId) => ((AuthoringScalarValue)Value(fieldId)).Value;

        static int WrapMode(string name) => name switch
        {
            "Default" => 0, "Once" or "Clamp" => 1, "Loop" => 2, "PingPong" => 4, "ClampForever" => 8,
            _ => throw new GraphNodeCompileException($"曲线循环方式 '{name}' 不属于正式曲线语义。")
        };

        static int WeightedMode(string name) => name switch
        {
            "None" => 0, "In" => 1, "Out" => 2, "Both" => 3,
            _ => throw new GraphNodeCompileException($"曲线权重方式 '{name}' 不属于正式曲线语义。")
        };
    }
}
