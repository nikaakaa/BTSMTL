using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BTSMTL.Authoring.Graph;
using ThirdPersonSimulation;

namespace BTSMTL.Compilation
{
    // 节点编译描述按能力的 CompilerBindingId 登记，由来源读取器提供作者字段及编译期引用。
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
        T Resolve<T>(GraphNodeField value) where T : class;
        GraphCompileReference Reference(GraphNodeField value);
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

    public interface IGraphNodeFieldReader
    {
        T Read<T>(string fieldId);
        bool IsNull(string fieldId);
        IGraphNodeFieldReader Object(string fieldId);
        IReadOnlyList<IGraphNodeFieldReader> Objects(string fieldId);
    }

    public readonly struct GraphNodeField
    {
        readonly IGraphNodeFieldReader m_Reader;
        readonly string m_FieldId;

        public GraphNodeField(IGraphNodeFieldReader reader, string fieldId)
        {
            m_Reader = reader;
            m_FieldId = fieldId;
        }

        public bool IsNull => m_Reader.IsNull(m_FieldId);
        public T Read<T>() => m_Reader.Read<T>(m_FieldId);
    }

    public readonly struct GraphCompileReference
    {
        public GraphCompileReference(string assetId, string contentId, string elementId)
        {
            AssetId = assetId;
            ContentId = contentId;
            ElementId = elementId;
        }

        public string AssetId { get; }
        public string ContentId { get; }
        public string ElementId { get; }
    }

    public sealed partial class GraphNodeFields
    {
        readonly IGraphNodeFieldReader m_Reader;
        readonly IGraphCompileReferences m_References;

        public GraphNodeFields(IGraphNodeFieldReader reader, IGraphCompileReferences references)
        {
            m_Reader = reader;
            m_References = references;
        }

        public GraphNodeField Value(string fieldId) => new(m_Reader, fieldId);
        public bool IsNull(string fieldId) => m_Reader.IsNull(fieldId);
        public string String(string fieldId) => m_Reader.Read<string>(fieldId);
        public bool Boolean(string fieldId) => m_Reader.Read<bool>(fieldId);
        public int Int32(string fieldId) => m_Reader.Read<int>(fieldId);
        public long Int64(string fieldId) => m_Reader.Read<long>(fieldId);
        public uint UInt32(string fieldId) => m_Reader.Read<uint>(fieldId);
        public ulong UInt64(string fieldId) => m_Reader.Read<ulong>(fieldId);
        public float Single(string fieldId) => m_Reader.Read<float>(fieldId);
        public double Double(string fieldId) => m_Reader.Read<double>(fieldId);
        public TEnum Enum<TEnum>(string fieldId) where TEnum : struct, Enum => m_Reader.Read<TEnum>(fieldId);
        public SemanticVector2Value Vector2(string fieldId) => m_Reader.Read<SemanticVector2Value>(fieldId);
        public SemanticVector3Value Vector3(string fieldId) => m_Reader.Read<SemanticVector3Value>(fieldId);

        public GraphNodeFields Object(string fieldId) => m_Reader.Object(fieldId) is { } reader
            ? new GraphNodeFields(reader, m_References) : null;

        public IReadOnlyList<GraphNodeFields> Objects(string fieldId) => m_Reader.Objects(fieldId)
            .Select(reader => new GraphNodeFields(reader, m_References)).ToArray();

        public GraphCompileReference? Reference(string fieldId)
        {
            GraphNodeField value = Value(fieldId);
            return value.IsNull ? null : m_References.Reference(value);
        }

        public T Resolve<T>(string fieldId) where T : class
        {
            GraphNodeField value = Value(fieldId);
            return value.IsNull ? null : m_References.Resolve<T>(value);
        }

        public GameplayAbilityCurve<double> Curve(string fieldId) => m_Reader.Read<GameplayAbilityCurve<double>>(fieldId);
        public bool CurveIsEmpty(string fieldId) => Curve(fieldId) is not { } curve || curve.Keys.Count == 0;
    }
}
