using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BTSMTL.Authoring.Graph;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;

namespace BTSMTL.Compilation
{
    // 节点编译描述按作者节点类型登记，由来源读取器提供作者字段及编译期引用。
    public interface IGraphNodeCompiler
    {
        Type NodeType { get; }
        GraphNodeCompileDescription Describe(GraphNodeFields fields);
    }

    public abstract class GraphNodeCompiler<TNode> : IGraphNodeCompiler where TNode : class
    {
        public Type NodeType => typeof(TNode);
        public abstract GraphNodeCompileDescription Describe(GraphNodeFields fields);
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
            IEnumerable<string> requiredInputConnections = null,
            IEnumerable<OrchestrationExtensionPort> additionalPorts = null)
        {
            Payload = payload?.ToArray() ?? Array.Empty<KeyValuePair<string, object>>();
            ReadBindings = readBindings?.ToArray() ?? Array.Empty<GraphOrchestrationBlackboardPort>();
            WriteBindings = writeBindings?.ToArray() ?? Array.Empty<GraphOrchestrationBlackboardPort>();
            ConstantInputs = constantInputs?.ToArray() ?? Array.Empty<GraphOrchestrationConstantPort>();
            ConstantOutputs = constantOutputs?.ToArray() ?? Array.Empty<GraphOrchestrationConstantPort>();
            DerivedInputs = derivedInputs?.ToArray() ?? Array.Empty<GraphOrchestrationDerivedInput>();
            RequiredInputConnections = requiredInputConnections?.ToArray() ?? Array.Empty<string>();
            AdditionalPorts = additionalPorts?.ToArray() ?? Array.Empty<OrchestrationExtensionPort>();
        }

        public static GraphNodeCompileDescription Fields(params (OrchestrationExtensionContentField Field, object Value)[] payload) =>
            new(PayloadFields(payload));

        public static GraphNodeCompileDescription Fields(params (OrchestrationOutputPort Port, object Value)[] payload) =>
            new(PayloadFields(payload));

        public static KeyValuePair<string, object>[] PayloadFields(params (OrchestrationExtensionContentField Field, object Value)[] payload) =>
            SimulationNodeEmissionFields.Fields(payload.Select(item => (item.Field.Id, item.Value)).ToArray());

        public static KeyValuePair<string, object>[] PayloadFields(params (OrchestrationOutputPort Port, object Value)[] payload) =>
            SimulationNodeEmissionFields.Fields(payload.Select(item => (item.Port.Id, item.Value)).ToArray());

        internal GraphNodeCompileDescription WithPayload(IEnumerable<KeyValuePair<string, object>> payload) =>
            new(payload, ReadBindings, WriteBindings, ConstantInputs, ConstantOutputs, DerivedInputs,
                RequiredInputConnections, AdditionalPorts);

        public IReadOnlyList<KeyValuePair<string, object>> Payload { get; }
        public IReadOnlyList<GraphOrchestrationBlackboardPort> ReadBindings { get; }
        public IReadOnlyList<GraphOrchestrationBlackboardPort> WriteBindings { get; }
        public IReadOnlyList<GraphOrchestrationConstantPort> ConstantInputs { get; }
        public IReadOnlyList<GraphOrchestrationConstantPort> ConstantOutputs { get; }
        public OrchestrationExtensionPort[] AdditionalPorts { get; }
        public IReadOnlyList<GraphOrchestrationDerivedInput> DerivedInputs { get; }
        public IReadOnlyList<string> RequiredInputConnections { get; }
    }

    public sealed class GraphNodeCompilers
    {
        readonly Dictionary<Type, IGraphNodeCompiler> m_Compilers;

        GraphNodeCompilers(Dictionary<Type, IGraphNodeCompiler> compilers) => m_Compilers = compilers;

        public static GraphNodeCompilers From(IEnumerable<Assembly> assemblies)
        {
            var compilers = new Dictionary<Type, IGraphNodeCompiler>();
            foreach (Type type in assemblies.SelectMany(assembly => assembly.GetTypes()))
            {
                if (type.IsAbstract || type.ContainsGenericParameters || !typeof(IGraphNodeCompiler).IsAssignableFrom(type))
                    continue;
                var compiler = (IGraphNodeCompiler)Activator.CreateInstance(type, true);
                if (!compilers.TryAdd(compiler.NodeType, compiler))
                    throw new InvalidOperationException($"节点类型 '{compiler.NodeType.FullName}' 的编译描述重复登记：{type.FullName}。");
            }
            return new GraphNodeCompilers(compilers);
        }

        // 标了 ContractField 的作者字段原样写入合同字段；其余载荷与端口描述按节点类型及其基类查找编译描述。
        public GraphNodeCompileDescription Describe(GraphNodeFields fields)
        {
            IGraphNodeCompiler compiler = null;
            for (Type type = fields.NodeType; type != null && !m_Compilers.TryGetValue(type, out compiler); type = type.BaseType) { }
            try
            {
                GraphNodeCompileDescription description = compiler?.Describe(fields) ?? GraphNodeCompileDescription.Empty;
                IReadOnlyList<GraphNodeContractField> copied = GraphNodeContracts.Fields(fields.NodeType);
                if (copied.Count == 0)
                    return description;
                var payload = new List<KeyValuePair<string, object>>(copied.Count + description.Payload.Count);
                foreach (GraphNodeContractField field in copied)
                    payload.Add(new KeyValuePair<string, object>(field.Contract.Id, fields.Read(field.AuthoringFieldId, field.ValueType)));
                foreach (KeyValuePair<string, object> item in description.Payload)
                {
                    if (payload.Any(existing => existing.Key == item.Key))
                        throw new GraphNodeCompileException($"节点 '{fields.NodeType.Name}' 的合同字段 '{item.Key}' 已由作者字段直接写入，编译描述不能再写。");
                    payload.Add(item);
                }
                return description.WithPayload(payload);
            }
            catch (Exception exception) when (exception is not GraphNodeCompileException)
            {
                throw new GraphNodeCompileException($"节点 '{fields.NodeType.Name}' 编译描述失败：{exception.Message}", exception);
            }
        }
    }

    public sealed class GraphNodeCompileException : Exception
    {
        public GraphNodeCompileException(string message, Exception inner = null) : base(message, inner) { }
    }

    public interface IGraphNodeFieldReader
    {
        // 作者节点或复合值的 CLR 类型；没有对应类型（原生数值、宏端点）时为 null。
        Type NodeType { get; }
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

        static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, MethodInfo> s_Reads = new();

        public Type NodeType => m_Reader.NodeType;
        public GraphNodeField Value(string fieldId) => new(m_Reader, fieldId);

        internal object Read(string fieldId, Type type)
        {
            MethodInfo read = s_Reads.GetOrAdd(type, value =>
                typeof(IGraphNodeFieldReader).GetMethod(nameof(IGraphNodeFieldReader.Read)).MakeGenericMethod(value));
            try
            {
                return read.Invoke(m_Reader, new object[] { fieldId });
            }
            catch (TargetInvocationException exception)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }
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
