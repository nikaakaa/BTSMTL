using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;

namespace BTSMTL.Compilation
{
    // 扩展操作的作者内容由领域决定编码格式；编译器只负责把作者常量降为目标数值后逐字段写入。
    public interface IBtsmtlOrchestrationContentEncoder<TValue, TScalar>
        where TValue : unmanaged
        where TScalar : struct, IComparable<TScalar>, IEquatable<TScalar>
    {
        string ContractIdentity { get; }
        IBtsmtlOrchestrationContentBuffer<TValue, TScalar> Begin(ReadOnlyMemory<OrchestrationExtensionContentField> fields);
    }

    public interface IBtsmtlOrchestrationContentBuffer<TValue, TScalar> : IOrchestrationExtensionContentWriter<TValue>
        where TValue : unmanaged
        where TScalar : struct, IComparable<TScalar>, IEquatable<TScalar>
    {
        OrchestrationExtensionContentField GetField(string id);
        void WriteEnum(in OrchestrationExtensionContentField field, string typeIdentity, long value);
        void WriteCurve(in OrchestrationExtensionContentField field, GameplayAbilityCurve<TScalar> curve);
        byte[] Complete();
    }

    public sealed class BtsmtlOrchestrationCompilationDomain<TValue, TScalar>
        where TValue : unmanaged
        where TScalar : struct, IComparable<TScalar>, IEquatable<TScalar>
    {
        public BtsmtlOrchestrationCompilationDomain(ProgramStateSchema stateSchema,
            IBtsmtlOrchestrationContentEncoder<TValue, TScalar> contentEncoder,
            OrchestrationExtensionDefinition? actionInstanceBlackboard)
        {
            StateSchema = stateSchema;
            ContentEncoder = contentEncoder;
            ActionInstanceBlackboard = actionInstanceBlackboard;
        }

        public ProgramStateSchema StateSchema { get; }
        public IBtsmtlOrchestrationContentEncoder<TValue, TScalar> ContentEncoder { get; }
        // ActionInstance 作用域黑板的读写由该扩展向宿主提供实例数据；没有此类黑板的图可以不提供。
        public OrchestrationExtensionDefinition? ActionInstanceBlackboard { get; }
    }

    public interface IBtsmtlOrchestrationNumericLowering<TScalar, TVector2, TVector3, TYaw, TQuaternion>
        where TScalar : unmanaged, IComparable<TScalar>, IEquatable<TScalar>
        where TVector2 : unmanaged
        where TVector3 : unmanaged
        where TYaw : unmanaged
        where TQuaternion : unmanaged
    {
        SimulationNumericProfile Profile { get; }
        TScalar Number(double value, string sourceIdentity, SemanticNumericPrecision precision);
        TVector2 Vector2(TScalar x, TScalar y);
        TVector3 Vector3(TScalar x, TScalar y, TScalar z);
        TYaw Yaw(TScalar degrees);
        TQuaternion Quaternion(TScalar x, TScalar y, TScalar z, TScalar w);
        TQuaternion QuaternionIdentity { get; }
    }

    public readonly struct BtsmtlFloat32OrchestrationLowering :
        IBtsmtlOrchestrationNumericLowering<Float32Scalar, Float32Vector2, Float32Vector3, Float32Yaw, Float32Quaternion>
    {
        readonly List<Float32ScalarConversion> m_Conversions;
        public BtsmtlFloat32OrchestrationLowering(List<Float32ScalarConversion> conversions) => m_Conversions = conversions;
        public SimulationNumericProfile Profile => Float32SimulationNumericProfile.Value;
        public Float32Scalar Number(double value, string sourceIdentity, SemanticNumericPrecision precision)
        {
            Float32ScalarConversion conversion = Float32ScalarBoundary.LowerAuthoring(value, sourceIdentity);
            if (precision == SemanticNumericPrecision.Exact && conversion.WasRounded)
                throw new InvalidOperationException($"语义数值 '{sourceIdentity}' 无法以 Float32 精确表示。");
            m_Conversions.Add(conversion);
            return conversion.Value;
        }
        public Float32Vector2 Vector2(Float32Scalar x, Float32Scalar y) => new Float32Vector2(x, y);
        public Float32Vector3 Vector3(Float32Scalar x, Float32Scalar y, Float32Scalar z) => new Float32Vector3(x, y, z);
        public Float32Yaw Yaw(Float32Scalar degrees) => new Float32Yaw(degrees);
        public Float32Quaternion Quaternion(Float32Scalar x, Float32Scalar y, Float32Scalar z, Float32Scalar w) =>
            new Float32Quaternion(x, y, z, w);
        public Float32Quaternion QuaternionIdentity => Float32Quaternion.Identity;
    }

    public static class BtsmtlOrchestrationGraphCompiler
    {
        static readonly ConditionalWeakTable<ProgramExecutionSource,
            ConcurrentDictionary<(string GraphId, string TemplateIdentity), string>> s_ExecutionIdentities = new();
        static readonly ConditionalWeakTable<IReadOnlyList<ProgramSourceMapEntry>,
            StrongBox<OrchestrationIdentityTable>> s_IdentityTables = new();

        static string ExecutionIdentity(ProgramExecutionSource source, string graphId, string templateIdentity) =>
            s_ExecutionIdentities.GetValue(source, static _ => new()).GetOrAdd((graphId, templateIdentity),
                static (identity, input) => input.TargetKind == ProgramSourceTargetKind.Operation
                    ? StableHash.Compute("orchestration-operation", identity.GraphId,
                        input.GraphInvocationPath, identity.TemplateIdentity).ToString()
                    : StableHash.Compute("orchestration-invocation", identity.GraphId,
                        input.SourceInvocationPath, input.GraphInvocationPath, input.GraphId,
                        input.TimelineId, input.TrackId, input.ClipId,
                        input.InvocationCallerKind.ToString(), input.InvocationCallerId,
                        input.InvocationCallerClipId).ToString(), source);

        public static OrchestrationGraphContent<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>>
            Compile<TScalar, TVector2, TVector3, TYaw, TQuaternion, TLowering>(
                IReadOnlyList<SemanticOperation> operations, IReadOnlyList<SemanticLiteral> literals,
                IReadOnlyList<SemanticConstantInputBinding> constantInputs,
                IReadOnlyList<ProgramControlFlowEdge> controlFlow, IReadOnlyList<ProgramReference> references,
                IReadOnlyList<ProgramGraphCallFrame> graphCalls, IReadOnlyList<ProgramStateSlot> stateSlots,
                IReadOnlyList<ProgramScopeLayout> scopes, IReadOnlyList<ProgramCatalogEntry> catalog,
                IReadOnlyList<ProgramSourceMapEntry> sourceMap, OperationHandle entry,
                GraphExecutionIdentity identity, OrchestrationExecutionContract contract,
                IReadOnlyList<OrchestrationExtensionReference> stateTransitionHooks,
                TLowering lowering,
                BtsmtlOrchestrationCompilationDomain<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>, TScalar> domain,
                IReadOnlyDictionary<string, OrchestrationExecutionContract> entryContracts = null)
            where TScalar : unmanaged, IComparable<TScalar>, IEquatable<TScalar>
            where TVector2 : unmanaged
            where TVector3 : unmanaged
            where TYaw : unmanaged
            where TQuaternion : unmanaged
            where TLowering : struct, IBtsmtlOrchestrationNumericLowering<TScalar, TVector2, TVector3, TYaw, TQuaternion> =>
            new Compilation<TScalar, TVector2, TVector3, TYaw, TQuaternion, TLowering>(
                operations, literals, constantInputs, controlFlow, references, graphCalls, stateSlots,
                scopes, catalog, sourceMap, entry, identity, contract, stateTransitionHooks, lowering,
                domain, entryContracts).Build();

        sealed class Compilation<TScalar, TVector2, TVector3, TYaw, TQuaternion, TLowering>
            where TScalar : unmanaged, IComparable<TScalar>, IEquatable<TScalar>
            where TVector2 : unmanaged
            where TVector3 : unmanaged
            where TYaw : unmanaged
            where TQuaternion : unmanaged
            where TLowering : struct, IBtsmtlOrchestrationNumericLowering<TScalar, TVector2, TVector3, TYaw, TQuaternion>
        {
            sealed class Port
            {
                internal string Identity;
                internal int Index;
                internal OrchestrationValueKind Kind;
                internal string ValueTypeIdentity;
                internal OperationValuePortDefinition AuthorDefinition;
                internal int OperationIndex;
                internal int ConstantIndex = -1;
            }

            sealed class Ports
            {
                internal Port[] Inputs;
                internal Port[] Outputs;
                internal Port Input(string identity) => Find(Inputs, identity);
                internal Port Output(string identity) => Find(Outputs, identity);

                static Port Find(Port[] ports, string identity)
                {
                    for (int i = 0; i < ports.Length; i++)
                        if (ports[i].Identity == identity)
                            return ports[i];
                    throw new InvalidOperationException($"图值端口 '{identity}' 未在正式合同中声明。");
                }
            }

            sealed class GraphInvocationFrame
            {
                internal string ParentPath;
                internal string DisplayPath;
                internal bool IsTimelineOccurrence;
                internal int CanonicalRootOperationIndex = -1;
                internal int OwnerOperationIndex = -1;
            }

            readonly IReadOnlyList<SemanticOperation> m_Operations;
            readonly IReadOnlyList<SemanticLiteral> m_Literals;
            readonly IReadOnlyList<SemanticConstantInputBinding> m_ConstantInputs;
            readonly IReadOnlyList<ProgramControlFlowEdge> m_ControlFlow;
            readonly IReadOnlyList<ProgramReference> m_References;
            readonly IReadOnlyList<ProgramGraphCallFrame> m_GraphCalls;
            readonly IReadOnlyList<ProgramStateSlot> m_StateSlots;
            readonly IReadOnlyList<ProgramScopeLayout> m_Scopes;
            readonly IReadOnlyList<ProgramCatalogEntry> m_Catalog;
            readonly IReadOnlyList<ProgramSourceMapEntry> m_SourceMap;
            readonly OperationHandle m_Entry;
            readonly GraphExecutionIdentity m_Identity;
            readonly OrchestrationExecutionContract m_Contract;
            readonly IReadOnlyDictionary<string, OrchestrationExecutionContract> m_EntryContracts;
            readonly IReadOnlyList<OrchestrationExtensionReference> m_StateTransitionHooks;
            readonly TLowering m_Lowering;
            readonly BtsmtlOrchestrationCompilationDomain<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>, TScalar> m_Domain;
            readonly List<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>> m_Constants = new();
            readonly List<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>> m_DefaultValues = new();
            readonly List<OrchestrationBlackboardSlot> m_Blackboards = new();
            readonly List<OrchestrationValueInputBinding> m_Inputs = new();
            readonly List<OrchestrationExtensionReference> m_Extensions = new();
            readonly List<OrchestrationHookBinding> m_LifecycleHooks = new();
            readonly List<OrchestrationBlackboardOutputBinding> m_BlackboardOutputs = new();
            readonly List<OrchestrationGraphHostInput> m_HostInputs = new();
            readonly List<OrchestrationOutputTemplate> m_OutputTemplates = new();
            readonly Dictionary<int, int> m_ConstantIndexes = new();
            readonly HashSet<int> m_ParameterSlots = new();
            readonly Dictionary<string, GraphInvocationFrame> m_GraphInvocationFrames = new(StringComparer.Ordinal);
            OrchestrationIdentityTable m_Identities;
            Dictionary<string, int> m_IdentityIds;
            string[] m_OperationIdentities;
            string[] m_GraphInvocationIdentities;
            Ports[] m_Ports;
            int[] m_ExecutionFrameOwners;
            int[] m_StateValueSlots;
            int[] m_InputSlots;
            int[] m_ExtensionIndexes;
            int[] m_BlackboardFactsIndexes;
            int[] m_OutputOffsets;
            int m_RuntimeOperationCount;
            int[] m_EvaluationOffsets;
            OrchestrationRange[] m_InputRanges;
            OrchestrationRange[] m_OutputAssignmentRanges;
            OrchestrationRange[] m_LifecycleHookRanges;

            internal Compilation(IReadOnlyList<SemanticOperation> operations, IReadOnlyList<SemanticLiteral> literals,
                IReadOnlyList<SemanticConstantInputBinding> constantInputs,
                IReadOnlyList<ProgramControlFlowEdge> controlFlow, IReadOnlyList<ProgramReference> references,
                IReadOnlyList<ProgramGraphCallFrame> graphCalls, IReadOnlyList<ProgramStateSlot> stateSlots,
                IReadOnlyList<ProgramScopeLayout> scopes, IReadOnlyList<ProgramCatalogEntry> catalog,
                IReadOnlyList<ProgramSourceMapEntry> sourceMap, OperationHandle entry,
                GraphExecutionIdentity identity, OrchestrationExecutionContract contract,
                IReadOnlyList<OrchestrationExtensionReference> stateTransitionHooks,
                TLowering lowering,
                BtsmtlOrchestrationCompilationDomain<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>, TScalar> domain,
                IReadOnlyDictionary<string, OrchestrationExecutionContract> entryContracts)
            {
                m_Operations = operations;
                m_Literals = literals;
                m_ConstantInputs = constantInputs;
                m_ControlFlow = controlFlow;
                m_References = references;
                m_GraphCalls = graphCalls;
                m_StateSlots = stateSlots;
                m_Scopes = scopes;
                m_Catalog = catalog;
                m_SourceMap = sourceMap;
                m_Entry = entry;
                m_Identity = identity;
                m_Contract = contract;
                m_EntryContracts = entryContracts;
                m_StateTransitionHooks = stateTransitionHooks;
                m_Lowering = lowering;
                m_Domain = domain;
            }

            internal OrchestrationGraphContent<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>> Build()
            {
                BuildExecutionIdentities();
                var publication = s_IdentityTables.GetValue(m_SourceMap, static _ => new());
                m_Identities = OrchestrationIdentityPublication.Create(IdentityTexts(), Volatile.Read(ref publication.Value));
                Volatile.Write(ref publication.Value, m_Identities);
                m_IdentityIds = new Dictionary<string, int>(m_Identities.Count, StringComparer.Ordinal);
                for (int i = 0; i < m_Identities.Count; i++)
                    m_IdentityIds.Add(m_Identities.Entries.Span[i], i + 1);
                BuildPorts();
                BuildExecutionFrameOwners();
                BuildValueSlots();
                BuildHostInputs();
                BuildExtensions();
                BuildStateTransitionHooks();
                BuildInputs();
                BuildOutputAssignments();
                OrchestrationOperation[] operations = BuildOperations();
                var program = new OrchestrationProgramContent<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>>(
                    operations, BuildControlFlow(), m_Inputs.ToArray(), m_Constants.ToArray(), m_DefaultValues.ToArray(),
                    BuildGraphCalls(), m_LifecycleHooks.ToArray(), Array.Empty<OrchestrationHookInput>(), m_Blackboards.ToArray(),
                    m_BlackboardOutputs.ToArray(), m_OutputTemplates.ToArray(), new OrchestrationCorePorts(Bind("AnyState"), Bind("OnEnter"),
                        Bind("Root"), Bind("OnExit"), Bind("True"), Bind("False"), Bind("Then"), Bind("Reset")),
                    m_Entry.Value, checked(Math.Max(1024, m_Operations.Count * 128)));
                return new OrchestrationGraphContent<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>>(
                    m_Identity, m_Lowering.Profile, m_Identities, program, m_Extensions.ToArray(), BuildOperationContents(), BuildEntries(),
                    BuildVariables(), BuildGeneratedBlackboards(), m_HostInputs.ToArray(), BuildPortMetadata(), BuildSources());
            }

            IEnumerable<string> IdentityTexts()
            {
                yield return m_Identity.GraphId;
                yield return "AnyState";
                yield return "OnEnter";
                yield return "Root";
                yield return "OnExit";
                yield return "True";
                yield return "False";
                yield return "Then";
                yield return "Reset";
                foreach (ProgramScopeLayout scope in m_Scopes)
                    yield return scope.Identity;
                for (int i = 0; i < m_Operations.Count; i++)
                {
                    yield return m_OperationIdentities[i];
                    foreach (SemanticConstantOutputBinding constant in m_Operations[i].ConstantOutputs)
                        yield return ConstantOutputIdentity(i, constant.OutputPortId);
                    foreach (SemanticHostInputPort input in m_Operations[i].HostInputs)
                    {
                        yield return input.InputId;
                        yield return input.OutputPortId;
                        yield return input.ValueTypeIdentity;
                        if (input.ValueTypeIdentity.StartsWith("Enum:", StringComparison.Ordinal))
                            yield return input.ValueTypeIdentity.Substring(5);
                    }
                    yield return m_Operations[i].Text0;
                    string type = HostInputType(m_Operations[i]);
                    if (type != null)
                    {
                        yield return type;
                        if (type.StartsWith("Enum:", StringComparison.Ordinal))
                            yield return type.Substring(5);
                    }
                }
                for (int i = 0; i < m_ControlFlow.Count; i++)
                {
                    ProgramControlFlowEdge edge = m_ControlFlow[i];
                    yield return edge.Identity;
                    yield return edge.SourcePort;
                    yield return edge.TargetPort;
                }
                for (int i = 0; i < m_Literals.Count; i++)
                {
                    SemanticLiteral literal = m_Literals[i];
                    if (literal.Kind == SemanticLiteralKind.String)
                        yield return literal.String;
                    if (literal.Kind == SemanticLiteralKind.Enum)
                        yield return literal.Enum.TypeIdentity;
                    if (literal.Kind == SemanticLiteralKind.Identities)
                        for (int identity = 0; identity < literal.Identities.Length; identity++)
                            yield return literal.Identities.Span[identity];
                }
                for (int i = 0; i < m_References.Count; i++)
                {
                    yield return m_References[i].Identity;
                    yield return m_References[i].ExternalIdentity;
                }
                for (int i = 0; i < m_SourceMap.Count; i++)
                {
                    ProgramExecutionSource source = m_SourceMap[i].Source;
                    if (source.TargetKind == ProgramSourceTargetKind.GraphInvocation)
                        yield return m_GraphInvocationIdentities[i];
                    yield return source.GraphId;
                    yield return source.NodeId;
                    yield return source.PortId;
                    yield return source.DeclarationId;
                    yield return source.CompiledPortId;
                }
                foreach (ProgramCatalogEntry entry in m_Catalog)
                {
                    yield return entry.Dependency.IdentityDomainId;
                    yield return entry.Dependency.Identity;
                    foreach (ProgramCatalogField field in entry.Fields)
                        if (field.Dependency.HasValue)
                        {
                            yield return field.Dependency.Value.IdentityDomainId;
                            yield return field.Dependency.Value.Identity;
                        }
                }
                for (int i = 0; i < m_Operations.Count; i++)
                {
                    if (!m_Operations[i].Extension.HasValue)
                        continue;
                    OrchestrationExtensionReference extension = m_Operations[i].Extension.Value;
                    yield return extension.Identity.DomainId;
                    yield return extension.Identity.OperationId;
                    for (int portIndex = 0; portIndex < extension.Ports.Length; portIndex++)
                        yield return extension.Ports.Span[portIndex].Id;
                    for (int fieldIndex = 0; fieldIndex < extension.Fields.Length; fieldIndex++)
                    {
                        OrchestrationExtensionContentField field = extension.Fields.Span[fieldIndex];
                        yield return field.Id;
                        yield return field.TypeIdentity;
                        yield return field.IdentityDomainId;
                    }
                }
                if (HasActionBlackboards())
                {
                    OrchestrationExtensionDefinition facts = ActionInstanceBlackboard();
                    yield return facts.Identity.DomainId;
                    yield return facts.Identity.OperationId;
                    for (int portIndex = 0; portIndex < facts.Contract.Ports.Length; portIndex++)
                        yield return facts.Contract.Ports.Span[portIndex].Id;
                }
            }

            int Bind(string text)
            {
                if (string.IsNullOrEmpty(text))
                    return 0;
                if (m_IdentityIds.TryGetValue(text, out int identity))
                    return identity;
                throw new KeyNotFoundException($"身份文本 '{text}' 未包含在已发布内容表 '{m_Identities.Identity}' 中。");
            }
            void BuildExecutionIdentities()
            {
                m_OperationIdentities = new string[m_Operations.Count];
                m_GraphInvocationIdentities = new string[m_SourceMap.Count];
                for (int i = 0; i < m_SourceMap.Count; i++)
                {
                    ProgramExecutionSource source = m_SourceMap[i].Source;
                    if (source.TargetKind == ProgramSourceTargetKind.GraphInvocation)
                        m_GraphInvocationIdentities[i] = ExecutionIdentity(source, m_Identity.GraphId, null);
                    if (source.TargetKind == ProgramSourceTargetKind.Operation && m_OperationIdentities[source.TargetIndex] == null)
                        m_OperationIdentities[source.TargetIndex] = ExecutionIdentity(source, m_Identity.GraphId,
                            m_Operations[source.TargetIndex].TemplateIdentity);
                }
                for (int operationIndex = 0; operationIndex < m_OperationIdentities.Length; operationIndex++)
                    if (m_OperationIdentities[operationIndex] == null)
                        throw new InvalidOperationException($"操作 '{operationIndex}' 缺少正式发生点来源。");
            }

            string ConstantOutputIdentity(int operationIndex, string portId) =>
                StableHash.Compute("orchestration-constant-output", m_OperationIdentities[operationIndex], portId).ToString();

            void BuildPorts()
            {
                m_Ports = new Ports[m_Operations.Count];
                m_RuntimeOperationCount = m_Operations.Count;
                for (int i = 0; i < m_Operations.Count; i++)
                {
                    SemanticOperation operation = m_Operations[i];
                    OperationValuePortContract contract = OrchestrationValuePortContracts.Require(operation, m_GraphCalls);
                    var inputs = new List<Port>();
                    var outputs = new List<Port>();
                    AddPorts(inputs, contract.Inputs);
                    AddPorts(outputs, contract.Outputs);
                    foreach (Port port in outputs)
                        port.OperationIndex = i;
                    foreach (SemanticConstantOutputBinding constant in operation.ConstantOutputs)
                    {
                        Port port = outputs.Find(value => value.Identity == constant.OutputPortId);
                        port.OperationIndex = m_RuntimeOperationCount++;
                        port.Index = 0;
                        port.ConstantIndex = constant.LiteralIndex;
                        port.ValueTypeIdentity = constant.ValueTypeIdentity;
                    }
                    foreach (SemanticHostInputPort input in operation.HostInputs)
                        outputs.Find(value => value.Identity == input.OutputPortId).ValueTypeIdentity = input.ValueTypeIdentity;
                    m_Ports[i] = new Ports { Inputs = inputs.ToArray(), Outputs = outputs.ToArray() };
                }
                m_EvaluationOffsets = new int[m_RuntimeOperationCount + 1];
                for (int i = 0; i < m_Operations.Count; i++)
                    m_EvaluationOffsets[i + 1] = checked(m_EvaluationOffsets[i] + ExecutionOutputCount(i) + 1);
                for (int i = 0; i < m_Operations.Count; i++)
                    foreach (Port port in m_Ports[i].Outputs)
                        if (port.ConstantIndex >= 0)
                            m_EvaluationOffsets[port.OperationIndex + 1] = checked(m_EvaluationOffsets[port.OperationIndex] + 2);
            }

            static void AddPorts(List<Port> target, IReadOnlyList<OperationValuePortDefinition> definitions)
            {
                for (int i = 0; i < definitions.Count; i++)
                {
                    OperationValuePortDefinition definition = definitions[i];
                    target.Add(new Port { Identity = definition.Identity, Index = definition.Order,
                        Kind = definition.Constraint == OperationValuePortConstraint.Fixed ? Kind(definition.FixedKind) : 0,
                        AuthorDefinition = definition });
                }
            }

            void BuildExecutionFrameOwners()
            {
                var operationInvocationPaths = new string[m_Operations.Count];
                foreach (ProgramSourceMapEntry entry in m_SourceMap)
                {
                    ProgramExecutionSource source = entry.Source;
                    string path = source.SourceInvocationPath;
                    if (string.IsNullOrEmpty(path))
                        continue;
                    if (!m_GraphInvocationFrames.TryGetValue(path, out GraphInvocationFrame invocation))
                    {
                        invocation = new GraphInvocationFrame
                        {
                            ParentPath = source.ParentInvocationPath,
                            DisplayPath = entry.DisplayPath
                        };
                        m_GraphInvocationFrames.Add(path, invocation);
                    }
                    if (source.TargetKind == ProgramSourceTargetKind.GraphInvocation)
                        invocation.IsTimelineOccurrence |= source.InvocationCallerKind is ProgramInvocationCallerKind.TimelineClip
                            or ProgramInvocationCallerKind.PresentationMarker or ProgramInvocationCallerKind.PresentationTreeClip;
                    if (source.TargetKind == ProgramSourceTargetKind.Operation)
                        operationInvocationPaths[source.TargetIndex] = source.GraphInvocationPath;
                }
                for (int i = 0; i < m_Operations.Count; i++)
                {
                    string path = operationInvocationPaths[i];
                    if (string.IsNullOrEmpty(path) || !m_GraphInvocationFrames.TryGetValue(path, out GraphInvocationFrame invocation))
                        throw new InvalidOperationException($"操作 '{i}' 缺少正式图调用来源，无法发布执行帧 owner。");
                    if (!invocation.IsTimelineOccurrence || m_Operations[i].Code != OrchestrationOperationCode.Root)
                        continue;
                    if (invocation.CanonicalRootOperationIndex >= 0)
                        throw new InvalidOperationException($"Timeline 调用 '{path}' 有多个执行帧 Root：'{invocation.DisplayPath}'。");
                    invocation.CanonicalRootOperationIndex = i;
                }
                foreach (KeyValuePair<string, GraphInvocationFrame> invocation in m_GraphInvocationFrames)
                    ResolveExecutionFrameOwner(invocation.Key, invocation.Value);
                m_ExecutionFrameOwners = new int[m_Operations.Count];
                for (int i = 0; i < m_Operations.Count; i++)
                    m_ExecutionFrameOwners[i] = m_GraphInvocationFrames[operationInvocationPaths[i]].OwnerOperationIndex;
            }

            int ResolveExecutionFrameOwner(string path, GraphInvocationFrame invocation)
            {
                if (invocation.OwnerOperationIndex >= 0)
                    return invocation.OwnerOperationIndex;
                int owner;
                if (invocation.IsTimelineOccurrence)
                {
                    if (invocation.CanonicalRootOperationIndex < 0)
                        throw new InvalidOperationException($"Timeline 调用 '{path}' 缺少执行帧 Root：'{invocation.DisplayPath}'。");
                    owner = invocation.CanonicalRootOperationIndex;
                }
                else if (invocation.ParentPath.Length == 0)
                    owner = m_Entry.Value;
                else
                {
                    if (!m_GraphInvocationFrames.TryGetValue(invocation.ParentPath, out GraphInvocationFrame parent))
                        throw new InvalidOperationException($"图调用 '{path}' 的父调用 '{invocation.ParentPath}' 未发布：'{invocation.DisplayPath}'。");
                    owner = ResolveExecutionFrameOwner(invocation.ParentPath, parent);
                }
                invocation.OwnerOperationIndex = owner;
                return owner;
            }

            int ExecutionFrameOwner(ProgramScopeLayout scope)
            {
                if (!m_GraphInvocationFrames.TryGetValue(scope.OwnerIdentity, out GraphInvocationFrame invocation))
                    throw new InvalidOperationException($"黑板作用域 '{scope.Identity}' 的调用 owner '{scope.OwnerIdentity}' 未发布。");
                return invocation.OwnerOperationIndex;
            }

            void BuildValueSlots()
            {
                m_StateValueSlots = EmptyIndexes(m_StateSlots.Count);
                var inputParameters = new HashSet<int>();
                for (int i = 0; i < m_GraphCalls.Count; i++)
                {
                    for (int input = 0; input < m_GraphCalls[i].Inputs.Count; input++)
                    {
                        inputParameters.Add(m_GraphCalls[i].Inputs[input].StateSlot);
                        m_ParameterSlots.Add(m_GraphCalls[i].Inputs[input].StateSlot);
                    }
                    for (int output = 0; output < m_GraphCalls[i].Outputs.Count; output++)
                        m_ParameterSlots.Add(m_GraphCalls[i].Outputs[output].StateSlot);
                }
                for (int i = 0; i < m_StateSlots.Count; i++)
                {
                    ProgramStateSlot source = m_StateSlots[i];
                    if (source.Semantic != ProgramStateSemantic.BlackboardValue)
                        continue;
                    OrchestrationValueKind kind = Kind(source.ValueKind);
                    m_StateValueSlots[i] = m_DefaultValues.Count;
                    m_DefaultValues.Add(source.DefaultConstantIndex < 0
                        ? DefaultValue(kind)
                        : m_Constants[LowerConstant(source.DefaultConstantIndex)]);
                    ProgramScopeLayout scope = Scope(i);
                    OrchestrationBlackboardLifetime lifetime = m_ParameterSlots.Contains(i)
                        ? OrchestrationBlackboardLifetime.GraphInstance
                        : (OrchestrationBlackboardLifetime)CatalogInt(Declaration(i), "Lifetime");
                    m_Blackboards.Add(new OrchestrationBlackboardSlot((OrchestrationScopeKind)scope.Kind,
                        lifetime, scope.Kind == ProgramScopeKind.Character ? 0 : scope.CompiledOwnerIndex, scope.OwnerOperation.Value,
                        ExecutionFrameOwner(scope), kind, inputParameters.Contains(i)));
                }
                m_OutputOffsets = EmptyIndexes(m_Operations.Count);
                for (int i = 0; i < m_Operations.Count; i++)
                {
                    if (!m_Operations[i].Extension.HasValue ||
                        m_Operations[i].Extension.Value.Kind != OrchestrationExtensionKind.Action || ExecutionOutputCount(i) == 0 ||
                        !HasPersistentOutput(i))
                        continue;
                    m_OutputOffsets[i] = m_DefaultValues.Count;
                    foreach (Port port in ExecutionOutputs(i))
                    {
                        m_DefaultValues.Add(DefaultValue(port.Kind));
                        m_Blackboards.Add(default);
                    }
                }
            }

            void BuildHostInputs()
            {
                m_InputSlots = EmptyIndexes(m_Operations.Count);
                var inputTypes = new Dictionary<string, string>(StringComparer.Ordinal);
                for (int i = 0; i < m_Operations.Count; i++)
                {
                    SemanticOperation operation = m_Operations[i];
                    if (operation.HostInputs.Count > 0)
                    {
                        m_InputSlots[i] = m_HostInputs.Count;
                        foreach (SemanticHostInputPort input in operation.HostInputs)
                            Add(input.InputId, input.ValueTypeIdentity);
                    }
                    else
                    {
                        string type = HostInputType(operation);
                        if (type == null)
                            continue;
                        m_InputSlots[i] = m_HostInputs.Count;
                        Add(operation.Text0, type);
                    }
                }

                void Add(string identity, string type)
                {
                    if (inputTypes.TryGetValue(identity, out string existing) && existing != type)
                        throw new InvalidOperationException($"宿主输入 '{identity}' 同时声明了 '{existing}' 与 '{type}'。");
                    inputTypes[identity] = type;
                    m_HostInputs.Add(new OrchestrationGraphHostInput(Bind(identity), Bind(type), m_HostInputs.Count));
                }
            }

            string HostInputType(SemanticOperation operation)
            {
                switch (operation.Code)
                {
                    case OrchestrationOperationCode.InputBoolean: return GraphValueTypeIdentity.Of(OrchestrationValueKind.Boolean);
                    case OrchestrationOperationCode.InputScalar: return GraphValueTypeIdentity.Of(OrchestrationValueKind.Scalar);
                    case OrchestrationOperationCode.InputVector2:
                    case OrchestrationOperationCode.InputVector2Magnitude: return GraphValueTypeIdentity.Of(OrchestrationValueKind.Vector2);
                    case OrchestrationOperationCode.HostInput: return null;
                    default: return null;
                }
            }

            void BuildExtensions()
            {
                m_ExtensionIndexes = EmptyIndexes(m_Operations.Count);
                m_BlackboardFactsIndexes = EmptyIndexes(m_Operations.Count);
                var observers = new HashSet<int>();
                if (HasActionBlackboards())
                    foreach (ProgramSourceMapEntry entry in m_SourceMap)
                        if (entry.Source.TargetKind == ProgramSourceTargetKind.GraphInvocation)
                            observers.Add(entry.Source.TargetIndex);
                int factsBinding = -1;
                for (int i = 0; i < m_Operations.Count; i++)
                {
                    if (m_Operations[i].Extension.HasValue)
                        m_ExtensionIndexes[i] = AddExtension(m_Operations[i].Extension.Value);
                    if (!UsesActionBlackboard(i) && !observers.Contains(i))
                        continue;
                    if (factsBinding < 0)
                        factsBinding = AddExtension(ActionInstanceBlackboard()
                            .CreateReference(0, m_Identity.GraphId + "/action-blackboard-facts"));
                    m_BlackboardFactsIndexes[i] = factsBinding;
                }
            }

            OrchestrationExtensionDefinition ActionInstanceBlackboard() =>
                m_Domain.ActionInstanceBlackboard ?? throw new InvalidOperationException(
                    $"图 '{m_Identity.GraphId}' 声明了 ActionInstance 作用域黑板，编译领域必须提供对应扩展。");

            bool HasActionBlackboards()
            {
                foreach (ProgramScopeLayout scope in m_Scopes)
                    if (scope.Kind == ProgramScopeKind.ActionInstance)
                        foreach (int slot in scope.StateSlots)
                            if (m_StateSlots[slot].Semantic == ProgramStateSemantic.BlackboardValue)
                                return true;
                return false;
            }

            bool UsesActionBlackboard(int operationIndex)
            {
                SemanticOperation operation = m_Operations[operationIndex];
                if ((operation.Code == OrchestrationOperationCode.BlackboardGet ||
                     operation.Code == OrchestrationOperationCode.BlackboardSet) &&
                    Scope(StateReference(operationIndex)).Kind == ProgramScopeKind.ActionInstance)
                    return true;
                foreach (SemanticBlackboardPortBinding input in operation.BlackboardInputs)
                    if (Scope(BoundStateSlot(operationIndex, input.PortId)).Kind == ProgramScopeKind.ActionInstance)
                        return true;
                foreach (SemanticBlackboardPortBinding output in operation.BlackboardOutputs)
                    if (Scope(BoundStateSlot(operationIndex, output.PortId)).Kind == ProgramScopeKind.ActionInstance)
                        return true;
                return false;
            }

            int AddExtension(OrchestrationExtensionReference reference)
            {
                int index = m_Extensions.Count;
                m_Extensions.Add(reference);
                return index;
            }

            void BuildStateTransitionHooks()
            {
                m_LifecycleHookRanges = new OrchestrationRange[m_Operations.Count];
                for (int i = 0; i < m_StateTransitionHooks.Count; i++)
                {
                    OrchestrationExtensionReference reference = m_StateTransitionHooks[i];
                    m_LifecycleHookRanges[reference.ContentIndex] = new OrchestrationRange(m_LifecycleHooks.Count, 1);
                    m_LifecycleHooks.Add(new OrchestrationHookBinding(OrchestrationLifecycleHook.StateTransition,
                        AddExtension(reference), default));
                }
            }

            void BuildInputs()
            {
                var grouped = new OrchestrationValueInputBinding?[m_Operations.Count][];
                for (int i = 0; i < grouped.Length; i++)
                    grouped[i] = new OrchestrationValueInputBinding?[m_Ports[i].Inputs.Length];
                for (int i = 0; i < m_ControlFlow.Count; i++)
                {
                    ProgramControlFlowEdge edge = m_ControlFlow[i];
                    if (edge.Kind != ProgramControlFlowKind.Value)
                        continue;
                    Port source = m_Ports[edge.Source.Value].Output(edge.SourcePort);
                    Port target = m_Ports[edge.Target.Value].Input(edge.TargetPort);
                    OrchestrationValueKind kind = OutputKind(edge.Source.Value, source);
                    RequireInputKind(edge.Target.Value, target, kind);
                    AddInput(grouped, edge.Target.Value, target.Index, new OrchestrationValueInputBinding(
                        target.Index, kind, OrchestrationValueInputSource.Operation, source.OperationIndex, source.Index));
                }
                for (int i = 0; i < m_ConstantInputs.Count; i++)
                {
                    SemanticConstantInputBinding input = m_ConstantInputs[i];
                    Port target = m_Ports[input.TargetOperation.Value].Input(input.TargetPort);
                    OrchestrationValueKind kind = Kind(input.ResolvedValueKind);
                    RequireInputKind(input.TargetOperation.Value, target, kind);
                    AddInput(grouped, input.TargetOperation.Value, target.Index, new OrchestrationValueInputBinding(
                        target.Index, kind, OrchestrationValueInputSource.Constant, LowerConstant(input.ConstantIndex), -1));
                }
                m_InputRanges = new OrchestrationRange[m_Operations.Count];
                for (int i = 0; i < grouped.Length; i++)
                {
                    foreach (SemanticBlackboardPortBinding input in m_Operations[i].BlackboardInputs)
                    {
                        Port target = m_Ports[i].Input(input.PortId);
                        AddInput(grouped, i, target.Index, new OrchestrationValueInputBinding(target.Index, Kind(input.ValueKind),
                            OrchestrationValueInputSource.Blackboard, ValueSlot(BoundStateSlot(i, input.PortId)), -1));
                    }
                    foreach (SemanticDerivedInput input in m_Operations[i].DerivedInputs)
                    {
                        Port target = m_Ports[i].Input(input.PortId);
                        OrchestrationValueInputSource source = input.Source == SemanticDerivedInputSource.SelfOutput
                            ? OrchestrationValueInputSource.Operation : OrchestrationValueInputSource.StateCompletionTransition;
                        AddInput(grouped, i, target.Index, new OrchestrationValueInputBinding(target.Index, Kind(input.ValueKind),
                            source, input.Source == SemanticDerivedInputSource.SelfOutput ? i : -1, input.SourceOutputPortIndex));
                    }
                    int offset = m_Inputs.Count;
                    for (int port = 0; port < grouped[i].Length; port++)
                        if (grouped[i][port].HasValue)
                            m_Inputs.Add(grouped[i][port].Value);
                    m_InputRanges[i] = new OrchestrationRange(offset, m_Inputs.Count - offset);
                }
            }

            static void AddInput(OrchestrationValueInputBinding?[][] groups, int operationIndex, int portIndex,
                OrchestrationValueInputBinding binding)
            {
                if (groups[operationIndex][portIndex].HasValue)
                    throw new InvalidOperationException($"节点 '{operationIndex}' 输入端口 '{portIndex}' 连接了多个来源。");
                groups[operationIndex][portIndex] = binding;
            }

            OrchestrationValueKind OutputKind(int operationIndex, Port port)
            {
                if (port.Kind != 0)
                    return port.Kind;
                SemanticOperation operation = m_Operations[operationIndex];
                if (operation.Code == OrchestrationOperationCode.BlackboardGet)
                    return Kind(m_StateSlots[StateReference(operationIndex)].ValueKind);
                if (operation.Code == OrchestrationOperationCode.Constant)
                    return Kind(OrchestrationValuePortContracts.FromLiteral(m_Literals[operation.LiteralReferences[0]].Kind));
                throw new InvalidOperationException($"节点 '{operationIndex}' 输出 '{port.Identity}' 未声明实际值类型。");
            }

            void RequireInputKind(int operationIndex, Port port, OrchestrationValueKind kind)
            {
                if (port.Kind != 0)
                {
                    if (port.Kind != kind)
                        throw new InvalidOperationException($"节点 '{operationIndex}' 输入 '{port.Identity}' 需要 '{port.Kind}'，连接值为 '{kind}'。");
                    return;
                }
                if (m_Operations[operationIndex].Code == OrchestrationOperationCode.BlackboardSet)
                {
                    if (Kind(m_StateSlots[StateReference(operationIndex)].ValueKind) != kind)
                        throw new InvalidOperationException($"节点 '{operationIndex}' 黑板输入值 '{kind}' 与声明不匹配。");
                    return;
                }
                if (!port.AuthorDefinition.Accepts(OrchestrationValuePortContracts.FromOrchestrationKind(kind)))
                    throw new InvalidOperationException($"节点 '{operationIndex}' 输入 '{port.Identity}' 不接受 '{kind}'。");
            }

            void BuildOutputAssignments()
            {
                m_OutputAssignmentRanges = new OrchestrationRange[m_Operations.Count];
                for (int i = 0; i < m_Operations.Count; i++)
                {
                    int offset = m_BlackboardOutputs.Count;
                    foreach (SemanticBlackboardPortBinding output in m_Operations[i].BlackboardOutputs)
                        m_BlackboardOutputs.Add(new OrchestrationBlackboardOutputBinding(
                            m_Ports[i].Output(output.PortId).Index, ValueSlot(BoundStateSlot(i, output.PortId))));
                    m_OutputAssignmentRanges[i] = new OrchestrationRange(offset, m_BlackboardOutputs.Count - offset);
                }
            }

            OrchestrationOperation[] BuildOperations()
            {
                int[] parents = BuildParentStates();
                var result = new OrchestrationOperation[m_RuntimeOperationCount];
                for (int i = 0; i < m_Operations.Count; i++)
                {
                    SemanticOperation source = m_Operations[i];
                    int integer0 = source.Integer0;
                    if (source.Output.HasValue)
                    {
                        integer0 = m_OutputTemplates.Count;
                        m_OutputTemplates.Add(source.Output.Value);
                    }
                    bool isRunnable = false;
                    foreach (int state in source.StateSlots)
                        isRunnable |= m_StateSlots[state].Semantic == ProgramStateSemantic.RunnableLifecycle;
                    int constant = source.Code == OrchestrationOperationCode.Constant && source.LiteralReferences.Count > 0
                        ? LowerConstant(source.LiteralReferences[0]) : -1;
                    int valueSlot = source.Code == OrchestrationOperationCode.BlackboardGet || source.Code == OrchestrationOperationCode.BlackboardSet
                        ? ValueSlot(StateReference(i)) : -1;
                    int stateId = source.Code == OrchestrationOperationCode.State || source.Code == OrchestrationOperationCode.StateExit
                        ? Bind(m_OperationIdentities[i]) : 0;
                    result[i] = new OrchestrationOperation(source.Code,
                        isRunnable, Bind(m_OperationIdentities[i]), integer0, source.Integer1, constant,
                        m_InputSlots[i], source.Code == OrchestrationOperationCode.InputRequest ? Bind(source.Text0) : 0,
                        valueSlot, source.Code == OrchestrationOperationCode.Timeline ? Bind(source.Text0) : 0,
                        stateId, source.Code == OrchestrationOperationCode.State ? StateMachineOwner(i) : -1,
                        m_ExtensionIndexes[i], GraphCallIndex(i), m_InputRanges[i], ExecutionOutputCount(i),
                        m_OutputOffsets[i], m_LifecycleHookRanges[i], m_BlackboardFactsIndexes[i], parents[i], m_OutputAssignmentRanges[i],
                        m_ExecutionFrameOwners[i]);
                    foreach (Port port in m_Ports[i].Outputs)
                        if (port.ConstantIndex >= 0)
                            result[port.OperationIndex] = new OrchestrationOperation(OrchestrationOperationCode.Constant,
                                false, Bind(ConstantOutputIdentity(i, port.Identity)), 0, 0, LowerConstant(port.ConstantIndex),
                                -1, 0, -1, 0, 0, -1, -1, -1, default, 1, -1, default, -1, parents[i], default,
                                m_ExecutionFrameOwners[i]);
                }
                return result;
            }

            OrchestrationFlowEdge[] BuildControlFlow()
            {
                var result = new List<OrchestrationFlowEdge>();
                for (int i = 0; i < m_ControlFlow.Count; i++)
                {
                    ProgramControlFlowEdge edge = m_ControlFlow[i];
                    if (edge.Kind == ProgramControlFlowKind.Value)
                        continue;
                    result.Add(new OrchestrationFlowEdge(Bind(edge.Identity), edge.Source.Value, edge.Target.Value,
                        Bind(edge.SourcePort), Bind(edge.TargetPort), (OrchestrationFlowKind)edge.Kind,
                        edge.Order, edge.Priority, edge.AbortPolicy,
                        edge.HasCondition ? edge.Condition.Value : -1));
                }
                return result.ToArray();
            }

            OrchestrationGraphCall[] BuildGraphCalls()
            {
                var result = new OrchestrationGraphCall[m_GraphCalls.Count];
                for (int i = 0; i < result.Length; i++)
                {
                    ProgramGraphCallFrame frame = m_GraphCalls[i];
                    result[i] = new OrchestrationGraphCall(frame.OwnerOperation.Value, frame.EntryOperation.Value,
                        GraphParameters(frame.Inputs), GraphParameters(frame.Outputs));
                }
                return result;
            }

            OrchestrationGraphParameter[] GraphParameters(IReadOnlyList<ProgramGraphParameterBinding> bindings)
            {
                var result = new OrchestrationGraphParameter[bindings.Count];
                for (int i = 0; i < result.Length; i++)
                    result[i] = new OrchestrationGraphParameter(i, ValueSlot(bindings[i].StateSlot), Kind(bindings[i].ValueKind));
                return result;
            }

            int GraphCallIndex(int operationIndex)
            {
                for (int i = 0; i < m_GraphCalls.Count; i++)
                    if (m_GraphCalls[i].OwnerOperation.Value == operationIndex)
                        return i;
                return -1;
            }

            int ExecutionOutputCount(int operationIndex)
            {
                int count = 0;
                foreach (Port port in m_Ports[operationIndex].Outputs)
                    if (port.ConstantIndex < 0)
                        count++;
                return count;
            }

            IEnumerable<Port> ExecutionOutputs(int operationIndex)
            {
                foreach (Port port in m_Ports[operationIndex].Outputs)
                    if (port.ConstantIndex < 0)
                        yield return port;
            }

            bool HasPersistentOutput(int operationIndex)
            {
                foreach (SemanticDerivedInput input in m_Operations[operationIndex].DerivedInputs)
                    if (input.Source == SemanticDerivedInputSource.SelfOutput)
                        return true;
                foreach (ProgramControlFlowEdge edge in m_ControlFlow)
                    if (edge.Kind == ProgramControlFlowKind.Value && edge.Source.Value == operationIndex &&
                        m_Ports[operationIndex].Output(edge.SourcePort).ConstantIndex < 0)
                        return true;
                foreach (ProgramSourceMapEntry entry in m_SourceMap)
                    if (entry.Source.TargetKind == ProgramSourceTargetKind.OperationPort &&
                        entry.Source.TargetIndex == operationIndex &&
                        entry.Source.ValuePortDirection == ProgramValuePortDirection.Output &&
                        m_Ports[operationIndex].Output(entry.Source.CompiledPortId).ConstantIndex < 0)
                        return true;
                return false;
            }

            int BoundStateSlot(int operationIndex, string portId)
            {
                foreach (ProgramReference reference in m_References)
                    if (reference.Kind == ProgramReferenceKind.StateSlot && reference.HasSourceOperation &&
                        reference.SourceOperation.Value == operationIndex && reference.ExternalIdentity == portId)
                        return reference.TargetIndex;
                throw new InvalidOperationException($"节点 '{operationIndex}' 的黑板端口 '{portId}' 未绑定正式状态槽。");
            }

            int[] BuildParentStates()
            {
                var parents = EmptyIndexes(m_Operations.Count);
                for (int i = 0; i < m_Operations.Count; i++)
                {
                    if (m_Operations[i].Code != OrchestrationOperationCode.State)
                        continue;
                    var visited = new bool[m_Operations.Count];
                    VisitStateBody(i, i, parents, visited);
                }
                return parents;
            }

            void VisitStateBody(int state, int operationIndex, int[] parents, bool[] visited)
            {
                if (visited[operationIndex])
                    return;
                visited[operationIndex] = true;
                if (operationIndex != state)
                {
                    if (parents[operationIndex] >= 0 && parents[operationIndex] != state)
                        throw new InvalidOperationException($"节点 '{operationIndex}' 被多个 State 主体拥有，不能发布静态状态祖先。");
                    parents[operationIndex] = state;
                    if (m_Operations[operationIndex].Code == OrchestrationOperationCode.State)
                        return;
                }
                for (int i = 0; i < m_ControlFlow.Count; i++)
                {
                    ProgramControlFlowEdge edge = m_ControlFlow[i];
                    if (edge.Source.Value == operationIndex && edge.Kind != ProgramControlFlowKind.Value &&
                        edge.Kind != ProgramControlFlowKind.Transition)
                        VisitStateBody(state, edge.Target.Value, parents, visited);
                }
                for (int i = 0; i < m_References.Count; i++)
                {
                    ProgramReference reference = m_References[i];
                    if (reference.Kind == ProgramReferenceKind.Operation && reference.TargetIndex == operationIndex &&
                        reference.HasSourceOperation && m_Operations[reference.SourceOperation.Value].Code == OrchestrationOperationCode.State)
                        VisitStateBody(state, reference.SourceOperation.Value, parents, visited);
                }
            }

            int StateMachineOwner(int operationIndex)
            {
                for (int i = 0; i < m_References.Count; i++)
                {
                    ProgramReference reference = m_References[i];
                    if (reference.Kind == ProgramReferenceKind.Operation && reference.SourceOperation.Value == operationIndex &&
                        m_Operations[reference.TargetIndex].Code == OrchestrationOperationCode.StateMachine)
                        return reference.TargetIndex;
                }
                throw new InvalidOperationException($"State 节点 '{operationIndex}' 未声明所属 StateMachine。");
            }

            int StateReference(int operationIndex)
            {
                for (int i = 0; i < m_References.Count; i++)
                    if (m_References[i].Kind == ProgramReferenceKind.StateSlot &&
                        m_References[i].SourceOperation.Value == operationIndex)
                        return m_References[i].TargetIndex;
                throw new InvalidOperationException($"节点 '{operationIndex}' 未声明黑板槽引用。");
            }

            int ValueSlot(int stateSlot)
            {
                int result = m_StateValueSlots[stateSlot];
                if (result < 0)
                    throw new InvalidOperationException($"状态槽 '{stateSlot}' 不是可发布的通用黑板值。");
                return result;
            }

            ProgramScopeLayout Scope(int stateSlot)
            {
                for (int i = 0; i < m_Scopes.Count; i++)
                    foreach (int slot in m_Scopes[i].StateSlots)
                        if (slot == stateSlot)
                            return m_Scopes[i];
                throw new InvalidOperationException($"黑板槽 '{stateSlot}' 未声明正式作用域。");
            }

            ProgramCatalogEntry Declaration(int stateSlot)
            {
                for (int i = 0; i < m_References.Count; i++)
                {
                    ProgramReference reference = m_References[i];
                    if (reference.Kind != ProgramReferenceKind.StateSlot || reference.HasSourceOperation || reference.TargetIndex != stateSlot)
                        continue;
                    for (int entry = 0; entry < m_Catalog.Count; entry++)
                        if (m_Catalog[entry].Dependency.IdentityDomainId == "btsmtl.blackboard-declaration" &&
                            m_Catalog[entry].Dependency.Identity == reference.ExternalIdentity)
                            return m_Catalog[entry];
                }
                throw new InvalidOperationException($"黑板槽 '{stateSlot}' 未声明变量目录项。");
            }

            SemanticLiteral CatalogLiteral(ProgramCatalogEntry declaration, string name)
            {
                foreach (ProgramCatalogField field in declaration.Fields)
                    if (field.Name == name)
                        return m_Literals[field.ConstantIndex];
                throw new InvalidOperationException($"黑板声明 '{declaration.Dependency.Identity}' 缺少字段 '{name}'。");
            }

            int CatalogInt(ProgramCatalogEntry declaration, string name) => CatalogLiteral(declaration, name).Int32;

            string CatalogText(ProgramCatalogEntry declaration, string name)
            {
                foreach (ProgramCatalogField field in declaration.Fields)
                    if (field.Name == name)
                        return m_Literals[field.ConstantIndex].String;
                throw new InvalidOperationException($"黑板声明 '{declaration.Dependency.Identity}' 缺少字段 '{name}'。");
            }

            OrchestrationGraphBlackboardBinding[] BuildGeneratedBlackboards()
            {
                var result = new List<OrchestrationGraphBlackboardBinding>();
                for (int slot = 0; slot < m_StateSlots.Count; slot++)
                {
                    if (m_StateSlots[slot].Semantic != ProgramStateSemantic.BlackboardValue || m_ParameterSlots.Contains(slot))
                        continue;
                    ProgramCatalogEntry declaration = Declaration(slot);
                    foreach (ProgramCatalogField field in declaration.Fields)
                        if (field.Name == "GeneratedField")
                        {
                            result.Add(new OrchestrationGraphBlackboardBinding(ValueSlot(slot), Bind(Scope(slot).Identity),
                                Bind(CatalogText(declaration, "GeneratedContract")),
                                m_Literals[field.ConstantIndex].Int32));
                            break;
                        }
                }
                return result.ToArray();
            }

            OrchestrationGraphEntry[] BuildEntries()
            {
                var result = new List<OrchestrationGraphEntry>();
                foreach (ProgramReference reference in m_References)
                    if (!reference.HasSourceOperation && reference.Kind == ProgramReferenceKind.Operation)
                        result.Add(new OrchestrationGraphEntry(Bind(reference.Identity),
                            new OrchestrationExecutionEntry(reference.TargetIndex,
                                m_EntryContracts == null ? m_Contract : m_EntryContracts[reference.Identity])));
                if (result.Count == 0)
                    result.Add(new OrchestrationGraphEntry(Bind(m_Identity.GraphId), new OrchestrationExecutionEntry(m_Entry.Value, m_Contract)));
                return result.ToArray();
            }

            OrchestrationGraphVariable[] BuildVariables()
            {
                var result = new List<OrchestrationGraphVariable>();
                var seen = new HashSet<int>();
                foreach (ProgramSourceMapEntry entry in m_SourceMap)
                {
                    ProgramExecutionSource source = entry.Source;
                    if (source.TargetKind != ProgramSourceTargetKind.StateSlot || m_StateValueSlots[source.TargetIndex] < 0 ||
                        m_ParameterSlots.Contains(source.TargetIndex) ||
                        !seen.Add(source.TargetIndex))
                        continue;
                    ProgramCatalogEntry declaration = Declaration(source.TargetIndex);
                    result.Add(new OrchestrationGraphVariable(Bind(source.GraphId), Bind(source.DeclarationId),
                        Bind(CatalogLiteral(declaration, "Key").String), Bind(CatalogLiteral(declaration, "ValueType").String),
                        IsPublic(declaration), ValueSlot(source.TargetIndex)));
                }
                return result.ToArray();
            }

            OrchestrationGraphPort[] BuildPortMetadata()
            {
                var result = new List<OrchestrationGraphPort>();
                foreach (ProgramSourceMapEntry entry in m_SourceMap)
                {
                    ProgramExecutionSource source = entry.Source;
                    if (source.TargetKind != ProgramSourceTargetKind.OperationPort)
                        continue;
                    bool trigger = source.ValuePortDirection == ProgramValuePortDirection.None;
                    bool input = source.IsInputPort;
                    string compiledPort = source.CompiledPortId;
                    int valueIndex = -1;
                    if (!trigger)
                    {
                        Port port = input ? m_Ports[source.TargetIndex].Input(compiledPort) : m_Ports[source.TargetIndex].Output(compiledPort);
                        if (input)
                        {
                            OrchestrationRange range = m_InputRanges[source.TargetIndex];
                            for (int i = range.Offset; i < range.Offset + range.Count; i++)
                                if (m_Inputs[i].TargetPortIndex == port.Index)
                                    valueIndex = i;
                        }
                        else
                            valueIndex = m_EvaluationOffsets[port.OperationIndex] + port.Index + 1;
                    }
                    result.Add(new OrchestrationGraphPort(Bind(source.GraphId), Bind(source.NodeId), Bind(source.PortId),
                        !trigger && !input ? m_Ports[source.TargetIndex].Output(compiledPort).OperationIndex : source.TargetIndex,
                        input, trigger, Bind(compiledPort), valueIndex));
                }
                return result.ToArray();
            }

            OrchestrationGraphSource[] BuildSources()
            {
                var result = new List<OrchestrationGraphSource>();
                var names = new Dictionary<(ProgramValuePortDirection, ProgramInvocationCallerKind),
                    (string Direction, string Caller)>();
                for (int i = 0; i < m_SourceMap.Count; i++)
                {
                    ProgramSourceMapEntry entry = m_SourceMap[i];
                    ProgramExecutionSource source = entry.Source;
                    var nameKey = (source.ValuePortDirection, source.InvocationCallerKind);
                    if (!names.TryGetValue(nameKey, out var text))
                    {
                        text = (source.ValuePortDirection.ToString(), source.InvocationCallerKind.ToString());
                        names.Add(nameKey, text);
                    }
                    var path = new string[entry.Text.DisplayPathSegments.Count];
                    for (int segment = 0; segment < path.Length; segment++)
                        path[segment] = entry.Text.DisplayPathSegments[segment];
                    int targetIndex = source.TargetKind == ProgramSourceTargetKind.OperationPort &&
                        source.ValuePortDirection == ProgramValuePortDirection.Output ? m_Ports[source.TargetIndex].Output(source.CompiledPortId).OperationIndex
                        : source.TargetKind == ProgramSourceTargetKind.StateSlot && m_StateValueSlots[source.TargetIndex] >= 0
                            ? ValueSlot(source.TargetIndex) : source.TargetIndex;
                    OrchestrationGraphSourceTargetKind targetKind = source.TargetKind == ProgramSourceTargetKind.StateSlot
                        ? m_StateValueSlots[source.TargetIndex] < 0
                            ? OrchestrationGraphSourceTargetKind.AuthorStateSlot
                            : OrchestrationGraphSourceTargetKind.BlackboardValue
                        : SourceTargetKind(source.TargetKind);
                    if (source.TargetKind == ProgramSourceTargetKind.Constant)
                    {
                        if (m_ConstantIndexes.TryGetValue(source.TargetIndex, out int publishedConstant))
                            targetIndex = publishedConstant;
                        else
                            targetKind = OrchestrationGraphSourceTargetKind.AuthorConstant;
                    }
                    OrchestrationExecutionEntry invocationEntry = source.TargetKind == ProgramSourceTargetKind.GraphInvocation
                        ? new OrchestrationExecutionEntry(source.InvocationEntryOperationIndex, m_Contract)
                        : default;
                    int sourceIdentityId = source.TargetKind switch
                    {
                        ProgramSourceTargetKind.Operation => Bind(m_OperationIdentities[source.TargetIndex]),
                        ProgramSourceTargetKind.GraphInvocation => Bind(m_GraphInvocationIdentities[i]),
                        _ => OrchestrationGraphExecutionSource.NoExecutionIdentity
                    };
                    result.Add(new OrchestrationGraphSource(new OrchestrationGraphExecutionSource(
                        targetKind, targetIndex, source.GraphId, source.NodeId, source.PortId,
                        source.EdgeId, source.DeclarationId, source.TimelineId, source.TrackId, source.ClipId,
                        source.IsInputPort, source.GraphInvocationPath, source.CompiledPortId, text.Direction,
                        source.SourceInvocationPath, source.ParentInvocationPath, text.Caller,
                        source.InvocationCallerId, source.InvocationCallerClipId,
                        source.InvocationCallerOperationIndex, invocationEntry, sourceIdentityId), entry.SourceType, path));
                    if (source.TargetKind == ProgramSourceTargetKind.Operation)
                        foreach (Port port in m_Ports[source.TargetIndex].Outputs)
                            if (port.ConstantIndex >= 0)
                                result.Add(new OrchestrationGraphSource(new OrchestrationGraphExecutionSource(
                                    OrchestrationGraphSourceTargetKind.Operation, port.OperationIndex, source.GraphId, source.NodeId, source.PortId,
                                    source.EdgeId, source.DeclarationId, source.TimelineId, source.TrackId, source.ClipId,
                                    source.IsInputPort, source.GraphInvocationPath, source.CompiledPortId, text.Direction,
                                    source.SourceInvocationPath, source.ParentInvocationPath, text.Caller,
                                    source.InvocationCallerId, source.InvocationCallerClipId,
                                    source.InvocationCallerOperationIndex, invocationEntry,
                                    Bind(ConstantOutputIdentity(source.TargetIndex, port.Identity))), entry.SourceType, path));
                }
                return result.ToArray();
            }

            static OrchestrationGraphSourceTargetKind SourceTargetKind(ProgramSourceTargetKind kind) => kind switch
            {
                ProgramSourceTargetKind.Operation => OrchestrationGraphSourceTargetKind.Operation,
                ProgramSourceTargetKind.Constant => OrchestrationGraphSourceTargetKind.Constant,
                ProgramSourceTargetKind.Reference => OrchestrationGraphSourceTargetKind.Reference,
                ProgramSourceTargetKind.Producer => OrchestrationGraphSourceTargetKind.Producer,
                ProgramSourceTargetKind.CatalogEntry => OrchestrationGraphSourceTargetKind.CatalogEntry,
                ProgramSourceTargetKind.BodyMotion => OrchestrationGraphSourceTargetKind.BodyMotion,
                ProgramSourceTargetKind.ControlModule => OrchestrationGraphSourceTargetKind.ControlModule,
                ProgramSourceTargetKind.ControlTransition => OrchestrationGraphSourceTargetKind.ControlTransition,
                ProgramSourceTargetKind.OperationPort => OrchestrationGraphSourceTargetKind.OperationPort,
                ProgramSourceTargetKind.GraphInvocation => OrchestrationGraphSourceTargetKind.GraphInvocation,
                ProgramSourceTargetKind.OptimizedAway => OrchestrationGraphSourceTargetKind.OptimizedAway,
                _ => throw new InvalidOperationException($"来源种类 '{kind}' 没有编排发布目标。")
            };

            bool IsPublic(ProgramCatalogEntry declaration)
            {
                foreach (ProgramCatalogField field in declaration.Fields)
                    if (field.Name == "Public")
                        return m_Literals[field.ConstantIndex].Boolean;
                return false;
            }

            int LowerConstant(int semanticIndex)
            {
                if (m_ConstantIndexes.TryGetValue(semanticIndex, out int existing))
                    return existing;
                var value = LowerValue(m_Literals[semanticIndex]);
                int index = m_Constants.Count;
                m_Constants.Add(value);
                m_ConstantIndexes.Add(semanticIndex, index);
                return index;
            }

            OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> LowerValue(SemanticLiteral literal)
            {
                OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> value;
                if (literal.Kind == SemanticLiteralKind.Curve)
                    throw new InvalidOperationException($"曲线 '{literal.Identity}' 属于领域内容，不能进入通用值槽。");
                switch (literal.Kind)
                {
                    case SemanticLiteralKind.Boolean:
                        value = OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>.FromBoolean(literal.Boolean);
                        break;
                    case SemanticLiteralKind.Int32:
                        value = OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>.FromInt32(literal.Int32);
                        break;
                    case SemanticLiteralKind.UInt64:
                        value = OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>.FromUInt64(literal.UInt64);
                        break;
                    case SemanticLiteralKind.Number:
                        value = OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>.FromScalar(
                            m_Lowering.Number(literal.Number.Value, literal.Identity, literal.Number.Precision));
                        break;
                    case SemanticLiteralKind.Vector2:
                        value = OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>.FromVector2(m_Lowering.Vector2(
                            m_Lowering.Number(literal.Vector2.X, literal.Identity + ".x", literal.Vector2.Precision),
                            m_Lowering.Number(literal.Vector2.Y, literal.Identity + ".y", literal.Vector2.Precision)));
                        break;
                    case SemanticLiteralKind.Vector3:
                        value = OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>.FromVector3(m_Lowering.Vector3(
                            m_Lowering.Number(literal.Vector3.X, literal.Identity + ".x", literal.Vector3.Precision),
                            m_Lowering.Number(literal.Vector3.Y, literal.Identity + ".y", literal.Vector3.Precision),
                            m_Lowering.Number(literal.Vector3.Z, literal.Identity + ".z", literal.Vector3.Precision)));
                        break;
                    case SemanticLiteralKind.Yaw:
                        value = OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>.FromYaw(
                            m_Lowering.Yaw(m_Lowering.Number(literal.Yaw.Value, literal.Identity, literal.Yaw.Precision)));
                        break;
                    case SemanticLiteralKind.String:
                        value = OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>.FromIdentity(
                            literal.String is null ? OrchestrationIdentityValue.Null :
                                OrchestrationIdentityValue.Content(Bind(literal.String)));
                        break;
                    case SemanticLiteralKind.Quaternion:
                        value = OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>.FromQuaternion(m_Lowering.Quaternion(
                            m_Lowering.Number(literal.Quaternion.X, literal.Identity + ".x", SemanticNumericPrecision.TargetRounded),
                            m_Lowering.Number(literal.Quaternion.Y, literal.Identity + ".y", SemanticNumericPrecision.TargetRounded),
                            m_Lowering.Number(literal.Quaternion.Z, literal.Identity + ".z", SemanticNumericPrecision.TargetRounded),
                            m_Lowering.Number(literal.Quaternion.W, literal.Identity + ".w", SemanticNumericPrecision.TargetRounded)));
                        break;
                    case SemanticLiteralKind.Enum:
                        value = OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>.FromEnum(
                            new OrchestrationEnumValue(Bind(literal.Enum.TypeIdentity), literal.Enum.Value));
                        break;
                    default:
                        throw new InvalidOperationException($"语义常量 '{literal.Identity}' 类型 '{literal.Kind}' 不属于通用值。");
                }
                return value;
            }

            OrchestrationOperationContent[] BuildOperationContents()
            {
                var result = new List<OrchestrationOperationContent>();
                for (int operationIndex = 0; operationIndex < m_Operations.Count; operationIndex++)
                {
                    IReadOnlyList<SemanticPayloadField> source = m_Operations[operationIndex].PayloadFields;
                    if (!m_Operations[operationIndex].Extension.HasValue)
                        continue;
                    IBtsmtlOrchestrationContentEncoder<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>, TScalar> encoder =
                        m_Domain.ContentEncoder ?? throw new InvalidOperationException(
                            $"图 '{m_Identity.GraphId}' 含扩展操作，编译领域必须提供扩展内容编码。");
                    ReadOnlyMemory<OrchestrationExtensionContentField> declarations = m_Operations[operationIndex].Extension.Value.Fields;
                    IBtsmtlOrchestrationContentBuffer<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>, TScalar> writer =
                        encoder.Begin(declarations);
                    for (int i = 0; i < source.Count; i++)
                    {
                        SemanticPayloadField field = source[i];
                        OrchestrationExtensionContentField declaration = writer.GetField(field.Name);
                        SemanticLiteral literal = m_Literals[field.LiteralIndex];
                        switch (literal.Kind)
                        {
                            case SemanticLiteralKind.String:
                                if (declaration.Kind == OrchestrationExtensionFieldKind.Identity)
                                    writer.WriteIdentity(in declaration, literal.String);
                                else
                                    writer.WriteText(in declaration, literal.String);
                                break;
                            case SemanticLiteralKind.Enum:
                                writer.WriteEnum(in declaration, literal.Enum.TypeIdentity, literal.Enum.Value);
                                break;
                            case SemanticLiteralKind.Identities:
                                writer.WriteIdentities(in declaration, literal.Identities.Span);
                                break;
                            case SemanticLiteralKind.Curve:
                                writer.WriteCurve(in declaration, LowerCurve(literal));
                                break;
                            default:
                                var value = LowerValue(literal);
                                writer.WriteValue(in declaration, in value);
                                break;
                        }
                    }
                    result.Add(new OrchestrationOperationContent(operationIndex,
                        encoder.ContractIdentity, writer.Complete()));
                }
                return result.ToArray();
            }

            GameplayAbilityCurve<TScalar> LowerCurve(SemanticLiteral literal)
            {
                GameplayAbilityCurve<double> source = literal.Curve;
                var keys = new GameplayAbilityCurveKey<TScalar>[source.Keys.Count];
                for (int i = 0; i < keys.Length; i++)
                {
                    GameplayAbilityCurveKey<double> key = source.Keys[i];
                    string identity = literal.Identity + "/key:" + i.ToString(CultureInfo.InvariantCulture);
                    keys[i] = new GameplayAbilityCurveKey<TScalar>(
                        Number(key.Time, ".time"), Number(key.Value, ".value"),
                        Number(key.InTangent, ".in-tangent"), Number(key.OutTangent, ".out-tangent"),
                        Number(key.InWeight, ".in-weight"), Number(key.OutWeight, ".out-weight"),
                        key.WeightedMode, key.Stepped);

                    TScalar Number(double value, string member) =>
                        m_Lowering.Number(value, identity + member, SemanticNumericPrecision.TargetRounded);
                }
                return new GameplayAbilityCurve<TScalar>(source.PreWrapMode, source.PostWrapMode, keys);
            }

            OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> DefaultValue(OrchestrationValueKind kind) =>
                kind == OrchestrationValueKind.Quaternion
                    ? OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>.FromQuaternion(m_Lowering.QuaternionIdentity)
                    : OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>.Default(kind);

            static int[] EmptyIndexes(int count)
            {
                var result = new int[count];
                Array.Fill(result, -1);
                return result;
            }

            OrchestrationValueKind Kind(ProgramStateValueKind kind) => m_Domain.StateSchema.RuntimeKind(kind);

            static OrchestrationValueKind Kind(SemanticValueKind kind) => kind switch
            {
                SemanticValueKind.Boolean => OrchestrationValueKind.Boolean,
                SemanticValueKind.Int32 => OrchestrationValueKind.Int32,
                SemanticValueKind.UInt64 => OrchestrationValueKind.UInt64,
                SemanticValueKind.Number => OrchestrationValueKind.Scalar,
                SemanticValueKind.Vector2 => OrchestrationValueKind.Vector2,
                SemanticValueKind.Vector3 => OrchestrationValueKind.Vector3,
                SemanticValueKind.Yaw => OrchestrationValueKind.Yaw,
                SemanticValueKind.Identity => OrchestrationValueKind.Identity,
                SemanticValueKind.Quaternion => OrchestrationValueKind.Quaternion,
                SemanticValueKind.Enum => OrchestrationValueKind.Enum,
                _ => throw new InvalidOperationException($"语义类型 '{kind}' 不属于通用值。")
            };

        }
    }
}
