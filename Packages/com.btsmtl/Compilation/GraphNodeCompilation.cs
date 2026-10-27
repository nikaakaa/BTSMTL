using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BTSMTL.Authoring.Documents;
using BTSMTL.Authoring.Graph;
using ThirdPersonSimulation;

namespace BTSMTL.Compilation
{
    // 节点编译描述按能力的 CompilerBindingId 登记，只读文档作者值和编译期引用，不读平台节点对象。
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class GraphNodeCompilerAttribute : Attribute
    {
        public GraphNodeCompilerAttribute(params string[] bindingIds) => BindingIds = bindingIds;
        public IReadOnlyList<string> BindingIds { get; }
    }

    public interface IGraphNodeCompiler
    {
        GraphNodeCompileDescription Describe(GraphNodeFields fields);
    }

    // 平台按引用身份提供被引用内容的编译所需值：Unity 读资产，.NET 读导出的身份表；缺失时抛出带引用身份的异常。
    public interface IGraphCompileReferences
    {
        T Resolve<T>(AuthoringValue value) where T : class;
    }

    public sealed class GraphNodeCompileDescription
    {
        public static readonly GraphNodeCompileDescription Empty = new();

        public GraphNodeCompileDescription(
            IEnumerable<KeyValuePair<string, object>> payload = null,
            IEnumerable<GraphOrchestrationBlackboardPort> readBindings = null,
            IEnumerable<GraphOrchestrationBlackboardPort> writeBindings = null,
            IEnumerable<GraphOrchestrationConstantPort> constantInputs = null,
            IEnumerable<GraphOrchestrationConstantPort> constantOutputs = null,
            IEnumerable<GraphOrchestrationDerivedInput> derivedInputs = null,
            IEnumerable<string> requiredInputConnections = null)
        {
            Payload = payload?.ToArray() ?? Array.Empty<KeyValuePair<string, object>>();
            ReadBindings = readBindings?.ToArray() ?? Array.Empty<GraphOrchestrationBlackboardPort>();
            WriteBindings = writeBindings?.ToArray() ?? Array.Empty<GraphOrchestrationBlackboardPort>();
            ConstantInputs = constantInputs?.ToArray() ?? Array.Empty<GraphOrchestrationConstantPort>();
            ConstantOutputs = constantOutputs?.ToArray() ?? Array.Empty<GraphOrchestrationConstantPort>();
            DerivedInputs = derivedInputs?.ToArray() ?? Array.Empty<GraphOrchestrationDerivedInput>();
            RequiredInputConnections = requiredInputConnections?.ToArray() ?? Array.Empty<string>();
        }

        public static GraphNodeCompileDescription Fields(params (string Name, object Value)[] payload) =>
            new(SimulationNodeEmissionFields.Fields(payload));

        public IReadOnlyList<KeyValuePair<string, object>> Payload { get; }
        public IReadOnlyList<GraphOrchestrationBlackboardPort> ReadBindings { get; }
        public IReadOnlyList<GraphOrchestrationBlackboardPort> WriteBindings { get; }
        public IReadOnlyList<GraphOrchestrationConstantPort> ConstantInputs { get; }
        public IReadOnlyList<GraphOrchestrationConstantPort> ConstantOutputs { get; }
        public IReadOnlyList<GraphOrchestrationDerivedInput> DerivedInputs { get; }
        public IReadOnlyList<string> RequiredInputConnections { get; }
    }

    public sealed class GraphNodeCompilers
    {
        readonly Dictionary<string, IGraphNodeCompiler> m_Compilers;

        GraphNodeCompilers(Dictionary<string, IGraphNodeCompiler> compilers) => m_Compilers = compilers;

        public static GraphNodeCompilers From(IEnumerable<Assembly> assemblies)
        {
            var compilers = new Dictionary<string, IGraphNodeCompiler>(StringComparer.Ordinal);
            foreach (Type type in assemblies.SelectMany(assembly => assembly.GetTypes()))
            {
                GraphNodeCompilerAttribute attribute = type.GetCustomAttribute<GraphNodeCompilerAttribute>();
                if (attribute == null)
                    continue;
                var compiler = (IGraphNodeCompiler)Activator.CreateInstance(type);
                foreach (string bindingId in attribute.BindingIds)
                    if (!compilers.TryAdd(bindingId, compiler))
                        throw new InvalidOperationException($"节点编译描述 '{bindingId}' 重复登记：{type.FullName}。");
            }
            return new GraphNodeCompilers(compilers);
        }

        public GraphNodeCompileDescription Describe(string bindingId, GraphNodeFields fields)
        {
            if (!m_Compilers.TryGetValue(bindingId, out IGraphNodeCompiler compiler))
                return GraphNodeCompileDescription.Empty;
            try
            {
                return compiler.Describe(fields);
            }
            catch (Exception exception) when (exception is not GraphNodeCompileException)
            {
                throw new GraphNodeCompileException($"节点编译描述 '{bindingId}' 失败：{exception.Message}", exception);
            }
        }
    }

    public sealed class GraphNodeCompileException : Exception
    {
        public GraphNodeCompileException(string message, Exception inner = null) : base(message, inner) { }
    }

    // 节点文档字段的类型化读取；数值按文档无损文本还原为原字段的 CLR 类型。
    public sealed class GraphNodeFields
    {
        readonly Dictionary<string, AuthoringValue> m_Values;
        readonly IGraphCompileReferences m_References;

        public GraphNodeFields(ReadOnlyMemory<AuthoringNamedValue> values, IGraphCompileReferences references)
        {
            m_Values = new Dictionary<string, AuthoringValue>(StringComparer.Ordinal);
            foreach (AuthoringNamedValue value in values.Span)
                m_Values.Add(value.FieldId.Value, value.Value);
            m_References = references;
        }

        public AuthoringValue Value(string fieldId) => m_Values.TryGetValue(fieldId, out AuthoringValue value)
            ? value
            : throw new GraphNodeCompileException($"节点缺少作者字段 '{fieldId}'。");

        public bool IsNull(string fieldId) => Value(fieldId) is AuthoringNullValue;
        public string String(string fieldId) => Value(fieldId) is AuthoringNullValue ? null : Scalar(fieldId);
        public bool Boolean(string fieldId) => Scalar(fieldId) switch
        {
            "true" => true,
            "false" => false,
            string text => throw new GraphNodeCompileException($"作者字段 '{fieldId}' 不是布尔值：'{text}'。")
        };
        public int Int32(string fieldId) => int.Parse(Scalar(fieldId), NumberStyles.Integer, CultureInfo.InvariantCulture);
        public long Int64(string fieldId) => long.Parse(Scalar(fieldId), NumberStyles.Integer, CultureInfo.InvariantCulture);
        public uint UInt32(string fieldId) => uint.Parse(Scalar(fieldId), NumberStyles.Integer, CultureInfo.InvariantCulture);
        public ulong UInt64(string fieldId) => ulong.Parse(Scalar(fieldId), NumberStyles.Integer, CultureInfo.InvariantCulture);
        public float Single(string fieldId) => float.Parse(Scalar(fieldId), NumberStyles.Float, CultureInfo.InvariantCulture);
        public double Double(string fieldId) => double.Parse(Scalar(fieldId), NumberStyles.Float, CultureInfo.InvariantCulture);
        public TEnum Enum<TEnum>(string fieldId) where TEnum : struct, Enum => (TEnum)System.Enum.Parse(typeof(TEnum), Scalar(fieldId), false);

        public SemanticVector2Value Vector2(string fieldId)
        {
            GraphNodeFields vector = Object(fieldId);
            return new SemanticVector2Value(vector.Single("x"), vector.Single("y"));
        }

        public SemanticVector3Value Vector3(string fieldId)
        {
            GraphNodeFields vector = Object(fieldId);
            return new SemanticVector3Value(vector.Single("x"), vector.Single("y"), vector.Single("z"));
        }

        public GraphNodeFields Object(string fieldId) => Value(fieldId) switch
        {
            AuthoringNullValue => null,
            AuthoringObjectValue composite => new GraphNodeFields(composite.Members, m_References),
            _ => throw new GraphNodeCompileException($"作者字段 '{fieldId}' 不是复合值。")
        };

        public IReadOnlyList<GraphNodeFields> Objects(string fieldId) => Value(fieldId) switch
        {
            AuthoringNullValue => Array.Empty<GraphNodeFields>(),
            AuthoringSequenceValue sequence => sequence.Items.ToArray().Select(item => item switch
            {
                AuthoringObjectValue composite => new GraphNodeFields(composite.Members, m_References),
                _ => throw new GraphNodeCompileException($"作者字段 '{fieldId}' 的元素不是复合值。")
            }).ToArray(),
            _ => throw new GraphNodeCompileException($"作者字段 '{fieldId}' 不是序列。")
        };

        public AuthoringContentReference? Reference(string fieldId) => Value(fieldId) switch
        {
            AuthoringNullValue => null,
            AuthoringReferenceValue reference => reference.Target,
            _ => throw new GraphNodeCompileException($"作者字段 '{fieldId}' 不是引用。")
        };

        // 被引用内容的编译值；字段为空时返回 null。
        public T Resolve<T>(string fieldId) where T : class
        {
            AuthoringValue value = Value(fieldId);
            if (value is AuthoringNullValue)
                return null;
            return m_References.Resolve<T>(value) ??
                throw new GraphNodeCompileException($"作者字段 '{fieldId}' 的引用没有 '{typeof(T).Name}' 编译值。");
        }

        // 文档中的曲线值：键序列与前后循环方式，按平台曲线语义转为编译曲线。
        public GameplayAbilityCurve<double> Curve(string fieldId)
        {
            GraphNodeFields curve = Object(fieldId);
            if (curve == null)
                return null;
            IReadOnlyList<GraphNodeFields> source = curve.Objects("keys");
            var keys = new GameplayAbilityCurveKey<double>[source.Count];
            for (int i = 0; i < keys.Length; i++)
            {
                GraphNodeFields key = source[i];
                float inTangent = key.Single("inTangent");
                float outTangent = key.Single("outTangent");
                keys[i] = new GameplayAbilityCurveKey<double>(key.Single("time"), key.Single("value"),
                    float.IsInfinity(inTangent) ? 0f : inTangent,
                    float.IsInfinity(outTangent) ? 0f : outTangent,
                    key.Single("inWeight"), key.Single("outWeight"), CurveWeightedMode(key.Scalar("weightedMode")),
                    i + 1 < source.Count && (float.IsInfinity(outTangent) || float.IsInfinity(source[i + 1].Single("inTangent"))));
            }
            return new GameplayAbilityCurve<double>(CurveWrapMode(curve.Scalar("preWrapMode")),
                CurveWrapMode(curve.Scalar("postWrapMode")), keys);
        }

        public bool CurveIsEmpty(string fieldId) => Object(fieldId) is not { } curve || curve.Objects("keys").Count == 0;

        string Scalar(string fieldId) => Value(fieldId) is AuthoringScalarValue scalar
            ? scalar.Value
            : throw new GraphNodeCompileException($"作者字段 '{fieldId}' 不是标量。");

        static int CurveWrapMode(string name) => name switch
        {
            "Default" => 0,
            "Once" or "Clamp" => 1,
            "Loop" => 2,
            "PingPong" => 4,
            "ClampForever" => 8,
            _ => throw new GraphNodeCompileException($"曲线循环方式 '{name}' 不属于正式曲线语义。")
        };

        static int CurveWeightedMode(string name) => name switch
        {
            "None" => 0,
            "In" => 1,
            "Out" => 2,
            "Both" => 3,
            _ => throw new GraphNodeCompileException($"曲线权重方式 '{name}' 不属于正式曲线语义。")
        };
    }
}
