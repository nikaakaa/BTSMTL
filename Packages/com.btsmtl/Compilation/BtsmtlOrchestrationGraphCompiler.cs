using System;
using System.Collections.Generic;
using System.Globalization;
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
        internal BtsmtlOrchestrationGraphCompiler.IdentityCache Identities;
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

    public sealed class OrchestrationNumericLoweringException : Exception
    {
        public OrchestrationNumericLoweringException(string sourceIdentity, string message) : base(message) =>
            SourceIdentity = sourceIdentity;

        public string SourceIdentity { get; }
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
                throw new OrchestrationNumericLoweringException(sourceIdentity,
                    $"语义数值 '{sourceIdentity}' 无法以 Float32 精确表示。");
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

    public static partial class BtsmtlOrchestrationGraphCompiler
    {
        internal sealed class IdentityCache
        {
            internal IdentityCache(
                IReadOnlyDictionary<(string GraphId, string InvocationPath, string Template), string> operations,
                IReadOnlyDictionary<(string GraphId, string SourceInvocationPath, string InvocationPath,
                    string SourceGraphId, string TimelineId, string TrackId, string ClipId,
                    ProgramInvocationCallerKind CallerKind, string CallerId, string CallerClipId), string> invocations,
                OrchestrationIdentityTable table)
            {
                Operations = operations;
                Invocations = invocations;
                Table = table;
            }

            internal readonly IReadOnlyDictionary<(string GraphId, string InvocationPath, string Template), string> Operations;
            internal readonly IReadOnlyDictionary<(string GraphId, string SourceInvocationPath, string InvocationPath,
                string SourceGraphId, string TimelineId, string TrackId, string ClipId,
                ProgramInvocationCallerKind CallerKind, string CallerId, string CallerClipId), string> Invocations;
            internal readonly OrchestrationIdentityTable Table;
        }

        public static OrchestrationGraphContent<TValue>
            Compile<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues, TLowering>(
                IReadOnlyList<SemanticOperation> operations, IReadOnlyList<SemanticLiteral> literals,
                IReadOnlyList<SemanticConstantInputBinding> constantInputs,
                IReadOnlyList<ProgramControlFlowEdge> controlFlow, IReadOnlyList<ProgramReference> references,
                IReadOnlyList<ProgramGraphCallFrame> graphCalls, IReadOnlyList<ProgramStateSlot> stateSlots,
                IReadOnlyList<ProgramScopeLayout> scopes, IReadOnlyList<ProgramCatalogEntry> catalog,
                IReadOnlyList<ProgramSourceMapEntry> sourceMap, OperationHandle entry,
                GraphExecutionIdentity identity, OrchestrationExecutionContract contract,
                IReadOnlyList<OrchestrationExtensionReference> stateTransitionHooks,
                TLowering lowering,
                BtsmtlOrchestrationCompilationDomain<TValue, TScalar> domain,
                SimulationCompileReport report,
                IReadOnlyDictionary<string, OrchestrationExecutionContract> entryContracts = null)
            where TValue : unmanaged
            where TScalar : unmanaged, IComparable<TScalar>, IEquatable<TScalar>
            where TVector2 : unmanaged
            where TVector3 : unmanaged
            where TYaw : unmanaged
            where TQuaternion : unmanaged
            where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
            where TLowering : struct, IBtsmtlOrchestrationNumericLowering<TScalar, TVector2, TVector3, TYaw, TQuaternion> =>
            new Compilation<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues, TLowering>(
                operations, literals, constantInputs, controlFlow, references, graphCalls, stateSlots,
                scopes, catalog, sourceMap, entry, identity, contract, stateTransitionHooks, lowering,
                domain, report, entryContracts).Build();

        sealed partial class Compilation<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues, TLowering>
            where TValue : unmanaged
            where TScalar : unmanaged, IComparable<TScalar>, IEquatable<TScalar>
            where TVector2 : unmanaged
            where TVector3 : unmanaged
            where TYaw : unmanaged
            where TQuaternion : unmanaged
            where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
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
                internal Compilation<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues, TLowering> Owner;
                internal int OperationIndex;
                internal Port Input(string identity) => Find(Inputs, identity);
                internal Port Output(string identity) => Find(Outputs, identity);

                Port Find(Port[] ports, string identity)
                {
                    for (int i = 0; i < ports.Length; i++)
                        if (ports[i].Identity == identity)
                            return ports[i];
                    throw Owner.Invariant(ProgramSourceTargetKind.Operation, OperationIndex,
                        $"图值端口 '{identity}' 未在正式合同中声明。");
                }
            }

            sealed class ValueLoweringException : Exception
            {
                internal ValueLoweringException(string message) : base(message) { }
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
            readonly SimulationCompileReport m_Report;
            readonly OperationHandle m_Entry;
            readonly GraphExecutionIdentity m_Identity;
            readonly OrchestrationExecutionContract m_Contract;
            readonly IReadOnlyDictionary<string, OrchestrationExecutionContract> m_EntryContracts;
            readonly IReadOnlyList<OrchestrationExtensionReference> m_StateTransitionHooks;
            readonly TLowering m_Lowering;
            readonly BtsmtlOrchestrationCompilationDomain<TValue, TScalar> m_Domain;
            readonly IdentityCache m_PreviousIdentities;
            readonly Dictionary<(string GraphId, string InvocationPath, string Template), string> m_OperationIdentityTexts = new();
            readonly Dictionary<(string GraphId, string SourceInvocationPath, string InvocationPath,
                string SourceGraphId, string TimelineId, string TrackId, string ClipId,
                ProgramInvocationCallerKind CallerKind, string CallerId, string CallerClipId), string> m_InvocationIdentityTexts = new();
            readonly Dictionary<(int Operation, string Port), string> m_ConstantOutputIdentities = new();
            readonly List<TValue> m_Constants = new();
            readonly List<TValue> m_DefaultValues = new();
            readonly List<OrchestrationBlackboardSlot> m_Blackboards = new();
            readonly List<OrchestrationValueInputBinding> m_Inputs = new();
            readonly List<OrchestrationExtensionReference> m_Extensions = new();
            readonly List<OrchestrationHookBinding> m_LifecycleHooks = new();
            readonly List<OrchestrationBlackboardOutputBinding> m_BlackboardOutputs = new();
            readonly List<OrchestrationGraphHostInput> m_HostInputs = new();
            readonly List<OrchestrationOutputTemplate> m_OutputTemplates = new();
            readonly Dictionary<int, int> m_ConstantIndexes = new();
            readonly Dictionary<int, TValue> m_LoweredValues = new();
            readonly Dictionary<int, GameplayAbilityCurve<TScalar>> m_LoweredCurves = new();
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
                BtsmtlOrchestrationCompilationDomain<TValue, TScalar> domain,
                SimulationCompileReport report,
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
                m_Report = report;
                m_Entry = entry;
                m_Identity = identity;
                m_Contract = contract;
                m_EntryContracts = entryContracts;
                m_StateTransitionHooks = stateTransitionHooks;
                m_Lowering = lowering;
                m_Domain = domain;
                m_PreviousIdentities = Volatile.Read(ref domain.Identities);
            }

            internal OrchestrationGraphContent<TValue> Build()
            {
                BuildExecutionIdentities();
                ValidateDomain();
                m_Identities = OrchestrationIdentityPublication.Create(IdentityTexts(), m_PreviousIdentities?.Table);
                m_IdentityIds = new Dictionary<string, int>(m_Identities.Count, StringComparer.Ordinal);
                for (int i = 0; i < m_Identities.Count; i++)
                    m_IdentityIds.Add(m_Identities.Entries.Span[i], i + 1);
                BuildPorts();
                BuildExecutionFrameOwners();
                BuildHostInputs();
                ValidateInputs();
                int[] parents = BuildParentStates();
                PrepareValues();
                if (!m_Report.IsValid)
                    throw new InvalidOperationException($"图 '{m_Identity.GraphId}' 编排编译失败：{string.Join("；", m_Report.Messages)}");
                BuildValueSlots();
                BuildExtensions();
                BuildStateTransitionHooks();
                BuildInputs();
                BuildOutputAssignments();
                OperationDraft[] operations = BuildOperations(parents);
                OrchestrationFlowEdge[] controlFlow = BuildControlFlow();
                var corePorts = new OrchestrationCorePorts(Bind("AnyState"), Bind("OnEnter"),
                    Bind("Root"), Bind("OnExit"), Bind("True"), Bind("False"), Bind("Then"), Bind("Reset"));
                OrchestrationProgramStructure structure = BuildProgramStructure(operations, controlFlow, corePorts);
                ValidateValueDependencies(operations, controlFlow, structure);
                BuildTypedValues(operations, controlFlow, structure);
                OrchestrationAssignmentOpcode[] assignments = BuildTypedAssignments(operations);
                if (!m_Report.IsValid)
                    throw new InvalidOperationException($"图 '{m_Identity.GraphId}' 编排编译失败：{string.Join("；", m_Report.Messages)}");
                OrchestrationValueSlotType[] valueSlotTypes = BuildValueSlotTypes(out int[] valueKindSlotCounts);
                OrchestrationValueSchedule valueSchedule = BuildValueSchedule(operations, controlFlow, structure);
                OrchestrationGraphEntry[] entries = BuildEntries();
                OrchestrationWorkspaceCapacity workspaceCapacity = BuildWorkspaceCapacity(operations, controlFlow,
                    structure, valueSchedule, entries);
                BuildOperationTables(operations, assignments, out OrchestrationOperation[] hotOperations,
                    out byte[] operationPayloads, out OrchestrationOperationColdData[] coldOperations);
                if (!m_Report.IsValid)
                    throw new InvalidOperationException($"图 '{m_Identity.GraphId}' 编排编译失败：{string.Join("；", m_Report.Messages)}");
                var program = new OrchestrationProgramContent<TValue>(
                    hotOperations, operationPayloads, coldOperations, controlFlow, structure, valueSchedule,
                    workspaceCapacity, m_Inputs.ToArray(), m_Constants.ToArray(), m_DefaultValues.ToArray(),
                    valueSlotTypes, valueKindSlotCounts,
                    BuildGraphCalls(), m_LifecycleHooks.ToArray(), Array.Empty<OrchestrationHookInput>(), m_Blackboards.ToArray(),
                    m_BlackboardOutputs.ToArray(), m_OutputTemplates.ToArray(), corePorts,
                    m_Entry.Value, checked(Math.Max(1024, m_Operations.Count * 128)));
                var content = new OrchestrationGraphContent<TValue>(
                    m_Identity, m_Lowering.Profile, m_Identities, program, m_Extensions.ToArray(), BuildOperationContents(), entries,
                    BuildVariables(), BuildGeneratedBlackboards(), m_HostInputs.ToArray(), BuildPortMetadata(), BuildSources());
                Volatile.Write(ref m_Domain.Identities,
                    new IdentityCache(m_OperationIdentityTexts, m_InvocationIdentityTexts, m_Identities));
                return content;
            }

            string SourceLocation(ProgramSourceTargetKind kind, int index, string portId = null,
                ProgramValuePortDirection direction = ProgramValuePortDirection.None)
            {
                if (kind == ProgramSourceTargetKind.Operation && portId != null)
                    foreach (ProgramSourceMapEntry entry in m_SourceMap)
                        if (entry.Source.TargetKind == ProgramSourceTargetKind.OperationPort &&
                            entry.Source.TargetIndex == index && entry.Source.CompiledPortId == portId &&
                            entry.Source.ValuePortDirection == direction)
                            return Describe(entry, portId);
                foreach (ProgramSourceMapEntry entry in m_SourceMap)
                    if (entry.Source.TargetKind == kind && entry.Source.TargetIndex == index)
                        return Describe(entry, portId);
                return kind == ProgramSourceTargetKind.Operation
                    ? $"graph:{m_Identity.GraphId}/operation:{index}/template:{m_Operations[index].TemplateIdentity}（SourceMap 缺失）"
                    : $"graph:{m_Identity.GraphId}/{kind}:{index}（SourceMap 缺失）";
            }

            static string Describe(ProgramSourceMapEntry entry, string portId)
            {
                ProgramExecutionSource source = entry.Source;
                var location = new SimulationSourceLocation(entry.SourceType, source.GraphId, source.NodeId,
                    source.EdgeId, source.TimelineId, source.ClipId, entry.DisplayPath, source.TrackId,
                    source.DeclarationId, source.PortId);
                return $"{location.ImmutableDataIdentity} [路径:{entry.DisplayPath}; 调用:{source.GraphInvocationPath}; 编译端口:{portId}]";
            }

            void Error(string code, ProgramSourceTargetKind kind, int index, string message,
                string portId = null, ProgramValuePortDirection direction = ProgramValuePortDirection.None) =>
                m_Report.ArtifactError(code, SourceLocation(kind, index, portId, direction), message);

            InvalidOperationException Invariant(ProgramSourceTargetKind kind, int index, string message) =>
                new InvalidOperationException($"{SourceLocation(kind, index)}: {message}");

            void ValidateDomain()
            {
                if (!m_Domain.ActionInstanceBlackboard.HasValue)
                    foreach (ProgramScopeLayout scope in m_Scopes)
                        if (scope.Kind == ProgramScopeKind.ActionInstance)
                            foreach (int slot in scope.StateSlots)
                                if (m_StateSlots[slot].Semantic == ProgramStateSemantic.BlackboardValue)
                                    Error("action_blackboard_extension_missing", ProgramSourceTargetKind.StateSlot, slot,
                                        $"ActionInstance 黑板作用域 '{scope.Identity}' 缺少编译领域的实例黑板扩展。");
                if (m_Domain.ContentEncoder == null)
                    for (int i = 0; i < m_Operations.Count; i++)
                        if (m_Operations[i].Extension.HasValue)
                            Error("extension_content_encoder_missing", ProgramSourceTargetKind.Operation, i,
                                "扩展操作缺少编译领域的作者内容编码器。");
            }

            void ValidateInputs()
            {
                var assigned = new HashSet<(int Operation, int Port)>();
                for (int i = 0; i < m_ControlFlow.Count; i++)
                {
                    ProgramControlFlowEdge edge = m_ControlFlow[i];
                    if (edge.Kind != ProgramControlFlowKind.Value)
                        continue;
                    Port source = m_Ports[edge.Source.Value].Output(edge.SourcePort);
                    Port target = m_Ports[edge.Target.Value].Input(edge.TargetPort);
                    ValidateInputOwner(edge.Target.Value, target);
                    if (TryOutputKind(edge.Source.Value, source, out OrchestrationValueKind kind))
                        ValidateInputKind(edge.Target.Value, target, kind);
                }
                foreach (SemanticConstantInputBinding input in m_ConstantInputs)
                {
                    Port target = m_Ports[input.TargetOperation.Value].Input(input.TargetPort);
                    ValidateInputOwner(input.TargetOperation.Value, target);
                    ValidateInputKind(input.TargetOperation.Value, target, Kind(input.ResolvedValueKind));
                }
                for (int i = 0; i < m_Operations.Count; i++)
                {
                    foreach (SemanticBlackboardPortBinding input in m_Operations[i].BlackboardInputs)
                    {
                        Port target = m_Ports[i].Input(input.PortId);
                        ValidateInputOwner(i, target);
                        ValidateInputKind(i, target, Kind(input.ValueKind));
                    }
                    foreach (SemanticDerivedInput input in m_Operations[i].DerivedInputs)
                    {
                        Port target = m_Ports[i].Input(input.PortId);
                        ValidateInputOwner(i, target);
                        ValidateInputKind(i, target, Kind(input.ValueKind));
                        if (input.Source == SemanticDerivedInputSource.SelfOutput &&
                            (input.SourceOutputPortIndex < 0 || input.SourceOutputPortIndex >= ExecutionOutputCount(i)))
                            Error("derived_output_port_invalid", ProgramSourceTargetKind.Operation, i,
                                $"自输出输入 '{input.PortId}' 指向未声明的输出端口 '{input.SourceOutputPortIndex}'。",
                                input.PortId, ProgramValuePortDirection.Input);
                    }
                }

                void ValidateInputOwner(int operationIndex, Port port)
                {
                    if (!assigned.Add((operationIndex, port.Index)))
                        Error("value_input_multiple_sources", ProgramSourceTargetKind.Operation, operationIndex,
                            $"输入端口 '{port.Identity}' 连接了多个来源。", port.Identity, ProgramValuePortDirection.Input);
                }
            }

            void PrepareValues()
            {
                var values = new SortedSet<int>();
                var curves = new SortedSet<int>();
                foreach (ProgramStateSlot slot in m_StateSlots)
                    if (slot.Semantic == ProgramStateSemantic.BlackboardValue && slot.DefaultConstantIndex >= 0)
                        values.Add(slot.DefaultConstantIndex);
                foreach (SemanticConstantInputBinding input in m_ConstantInputs)
                    values.Add(input.ConstantIndex);
                foreach (SemanticOperation operation in m_Operations)
                {
                    if (operation.Code == OrchestrationOperationCode.Constant && operation.LiteralReferences.Count > 0)
                        values.Add(operation.LiteralReferences[0]);
                    foreach (SemanticConstantOutputBinding output in operation.ConstantOutputs)
                        values.Add(output.LiteralIndex);
                    if (!operation.Extension.HasValue)
                        continue;
                    foreach (SemanticPayloadField field in operation.PayloadFields)
                        switch (m_Literals[field.LiteralIndex].Kind)
                        {
                            case SemanticLiteralKind.String:
                            case SemanticLiteralKind.Enum:
                            case SemanticLiteralKind.Identities:
                                break;
                            case SemanticLiteralKind.Curve:
                                curves.Add(field.LiteralIndex);
                                break;
                            default:
                                values.Add(field.LiteralIndex);
                                break;
                        }
                }
                foreach (int index in values)
                    try
                    {
                        m_LoweredValues.Add(index, LowerValue(m_Literals[index]));
                    }
                    catch (ValueLoweringException exception)
                    {
                        Error("value_literal_kind_invalid", ProgramSourceTargetKind.Constant, index, exception.Message);
                    }
                    catch (OrchestrationNumericLoweringException exception)
                    {
                        Error("numeric_lowering_invalid", ProgramSourceTargetKind.Constant, index,
                            $"数值 '{exception.SourceIdentity}' 无法降低：{exception.Message}");
                    }
                foreach (int index in curves)
                    try
                    {
                        m_LoweredCurves.Add(index, LowerCurve(m_Literals[index]));
                    }
                    catch (OrchestrationNumericLoweringException exception)
                    {
                        Error("numeric_lowering_invalid", ProgramSourceTargetKind.Constant, index,
                            $"曲线数值 '{exception.SourceIdentity}' 无法降低：{exception.Message}");
                    }
            }

            TScalar LowerNumber(double value, string sourceIdentity, SemanticNumericPrecision precision)
            {
                try
                {
                    return m_Lowering.Number(value, sourceIdentity, precision);
                }
                catch (SimulationNumericConversionException exception)
                {
                    throw new OrchestrationNumericLoweringException(sourceIdentity, exception.Message);
                }
                catch (ArgumentOutOfRangeException exception)
                {
                    throw new OrchestrationNumericLoweringException(sourceIdentity, exception.Message);
                }
                catch (OverflowException exception)
                {
                    throw new OrchestrationNumericLoweringException(sourceIdentity, exception.Message);
                }
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
                if (HasActionBlackboards() && m_Domain.ActionInstanceBlackboard.HasValue)
                {
                    OrchestrationExtensionDefinition facts = m_Domain.ActionInstanceBlackboard.Value;
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
                throw new KeyNotFoundException($"图 '{m_Identity.GraphId}' 的身份文本 '{text}' 未包含在已发布内容表 '{m_Identities.Identity}' 中。");
            }
            void BuildExecutionIdentities()
            {
                m_OperationIdentities = new string[m_Operations.Count];
                m_GraphInvocationIdentities = new string[m_SourceMap.Count];
                for (int i = 0; i < m_SourceMap.Count; i++)
                {
                    ProgramExecutionSource source = m_SourceMap[i].Source;
                    if (source.TargetKind == ProgramSourceTargetKind.GraphInvocation)
                        m_GraphInvocationIdentities[i] = ExecutionIdentity(source, null);
                    if (source.TargetKind == ProgramSourceTargetKind.Operation && m_OperationIdentities[source.TargetIndex] == null)
                        m_OperationIdentities[source.TargetIndex] = ExecutionIdentity(source,
                            m_Operations[source.TargetIndex].TemplateIdentity);
                }
                for (int operationIndex = 0; operationIndex < m_OperationIdentities.Length; operationIndex++)
                    if (m_OperationIdentities[operationIndex] == null)
                        throw Invariant(ProgramSourceTargetKind.Operation, operationIndex, "操作缺少正式发生点来源。");
            }

            string ExecutionIdentity(ProgramExecutionSource source, string templateIdentity)
            {
                if (source.TargetKind == ProgramSourceTargetKind.Operation)
                {
                    var operationKey = (m_Identity.GraphId, source.GraphInvocationPath, templateIdentity);
                    if (m_OperationIdentityTexts.TryGetValue(operationKey, out string operationIdentity))
                        return operationIdentity;
                    if (m_PreviousIdentities == null || !m_PreviousIdentities.Operations.TryGetValue(operationKey, out operationIdentity))
                        operationIdentity = StableHash.Compute("orchestration-operation", m_Identity.GraphId,
                            source.GraphInvocationPath, templateIdentity).ToString();
                    m_OperationIdentityTexts.Add(operationKey, operationIdentity);
                    return operationIdentity;
                }
                var invocationKey = (m_Identity.GraphId, source.SourceInvocationPath, source.GraphInvocationPath,
                    source.GraphId, source.TimelineId, source.TrackId, source.ClipId,
                    source.InvocationCallerKind, source.InvocationCallerId, source.InvocationCallerClipId);
                if (m_InvocationIdentityTexts.TryGetValue(invocationKey, out string invocationIdentity))
                    return invocationIdentity;
                if (m_PreviousIdentities == null || !m_PreviousIdentities.Invocations.TryGetValue(invocationKey, out invocationIdentity))
                    invocationIdentity = StableHash.Compute("orchestration-invocation", m_Identity.GraphId,
                        source.SourceInvocationPath, source.GraphInvocationPath, source.GraphId,
                        source.TimelineId, source.TrackId, source.ClipId,
                        source.InvocationCallerKind.ToString(), source.InvocationCallerId, source.InvocationCallerClipId).ToString();
                m_InvocationIdentityTexts.Add(invocationKey, invocationIdentity);
                return invocationIdentity;
            }

            string ConstantOutputIdentity(int operationIndex, string portId)
            {
                var key = (operationIndex, portId);
                if (!m_ConstantOutputIdentities.TryGetValue(key, out string identity))
                {
                    identity = StableHash.Compute("orchestration-constant-output", m_OperationIdentities[operationIndex], portId).ToString();
                    m_ConstantOutputIdentities.Add(key, identity);
                }
                return identity;
            }

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
                    m_Ports[i] = new Ports { Inputs = inputs.ToArray(), Outputs = outputs.ToArray(),
                        Owner = this, OperationIndex = i };
                }
            }

            void AddPorts(List<Port> target, IReadOnlyList<OperationValuePortDefinition> definitions)
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
                        throw Invariant(ProgramSourceTargetKind.Operation, i, "操作缺少正式图调用来源，无法发布执行帧 owner。");
                    if (!invocation.IsTimelineOccurrence || m_Operations[i].Code != OrchestrationOperationCode.Root)
                        continue;
                    if (invocation.CanonicalRootOperationIndex >= 0)
                        throw Invariant(ProgramSourceTargetKind.Operation, i,
                            $"Timeline 调用 '{path}' 有多个执行帧 Root：'{invocation.DisplayPath}'。");
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
                        throw new InvalidOperationException($"图 '{m_Identity.GraphId}' 的 Timeline 调用 '{path}' 缺少执行帧 Root：'{invocation.DisplayPath}'。");
                    owner = invocation.CanonicalRootOperationIndex;
                }
                else if (invocation.ParentPath.Length == 0)
                    owner = m_Entry.Value;
                else
                {
                    if (!m_GraphInvocationFrames.TryGetValue(invocation.ParentPath, out GraphInvocationFrame parent))
                        throw new InvalidOperationException($"图 '{m_Identity.GraphId}' 的调用 '{path}' 的父调用 '{invocation.ParentPath}' 未发布：'{invocation.DisplayPath}'。");
                    owner = ResolveExecutionFrameOwner(invocation.ParentPath, parent);
                }
                invocation.OwnerOperationIndex = owner;
                return owner;
            }

            int ExecutionFrameOwner(ProgramScopeLayout scope)
            {
                if (!m_GraphInvocationFrames.TryGetValue(scope.OwnerIdentity, out GraphInvocationFrame invocation))
                    throw Invariant(ProgramSourceTargetKind.StateSlot, scope.StateSlots[0],
                        $"黑板作用域 '{scope.Identity}' 的调用 owner '{scope.OwnerIdentity}' 未发布。");
                return invocation.OwnerOperationIndex;
            }

            void BuildValueSlots()
            {
                bool[] persistentOutputs = CollectPersistentOutputs();
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
                        !persistentOutputs[i])
                        continue;
                    m_OutputOffsets[i] = m_DefaultValues.Count;
                    foreach (Port port in ExecutionOutputs(i))
                    {
                        m_DefaultValues.Add(DefaultValue(port.Kind));
                        m_Blackboards.Add(default);
                    }
                }
            }

            OrchestrationValueSlotType[] BuildValueSlotTypes(out int[] counts)
            {
                var slots = new OrchestrationValueSlotType[m_DefaultValues.Count];
                counts = new int[Enum.GetValues(typeof(OrchestrationValueKind)).Length];
                TValues values = default;
                for (int valueSlot = 0; valueSlot < slots.Length; valueSlot++)
                {
                    OrchestrationValueKind kind = values.Kind(m_DefaultValues[valueSlot]);
                    slots[valueSlot] = new OrchestrationValueSlotType(kind, counts[(int)kind]++);
                }
                return slots;
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
                            Add(i, input.InputId, input.ValueTypeIdentity);
                    }
                    else
                    {
                        string type = HostInputType(operation);
                        if (type == null)
                            continue;
                        m_InputSlots[i] = m_HostInputs.Count;
                        Add(i, operation.Text0, type);
                    }
                }

                void Add(int operationIndex, string identity, string type)
                {
                    if (inputTypes.TryGetValue(identity, out string existing) && existing != type)
                    {
                        Error("host_input_type_conflict", ProgramSourceTargetKind.Operation, operationIndex,
                            $"宿主输入 '{identity}' 同时声明了 '{existing}' 与 '{type}'。");
                        return;
                    }
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
                        factsBinding = AddExtension(m_Domain.ActionInstanceBlackboard.Value
                            .CreateReference(0, m_Identity.GraphId + "/action-blackboard-facts"));
                    m_BlackboardFactsIndexes[i] = factsBinding;
                }
            }

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
                    OrchestrationValueKind kind = source.Kind;
                    grouped[edge.Target.Value][target.Index] = new OrchestrationValueInputBinding(
                        target.Index, kind, OrchestrationValueInputSource.Operation, source.OperationIndex, source.Index);
                }
                for (int i = 0; i < m_ConstantInputs.Count; i++)
                {
                    SemanticConstantInputBinding input = m_ConstantInputs[i];
                    Port target = m_Ports[input.TargetOperation.Value].Input(input.TargetPort);
                    OrchestrationValueKind kind = Kind(input.ResolvedValueKind);
                    grouped[input.TargetOperation.Value][target.Index] = new OrchestrationValueInputBinding(
                        target.Index, kind, OrchestrationValueInputSource.Constant, LowerConstant(input.ConstantIndex), -1);
                }
                m_InputRanges = new OrchestrationRange[m_Operations.Count];
                for (int i = 0; i < grouped.Length; i++)
                {
                    foreach (SemanticBlackboardPortBinding input in m_Operations[i].BlackboardInputs)
                    {
                        Port target = m_Ports[i].Input(input.PortId);
                        grouped[i][target.Index] = new OrchestrationValueInputBinding(target.Index, Kind(input.ValueKind),
                            OrchestrationValueInputSource.Blackboard, ValueSlot(BoundStateSlot(i, input.PortId)), -1);
                    }
                    foreach (SemanticDerivedInput input in m_Operations[i].DerivedInputs)
                    {
                        Port target = m_Ports[i].Input(input.PortId);
                        OrchestrationValueInputSource source = input.Source == SemanticDerivedInputSource.SelfOutput
                            ? OrchestrationValueInputSource.Operation : OrchestrationValueInputSource.StateCompletionTransition;
                        grouped[i][target.Index] = new OrchestrationValueInputBinding(target.Index, Kind(input.ValueKind),
                            source, input.Source == SemanticDerivedInputSource.SelfOutput ? i : -1, input.SourceOutputPortIndex);
                    }
                    int offset = m_Inputs.Count;
                    for (int port = 0; port < grouped[i].Length; port++)
                        if (grouped[i][port].HasValue)
                            m_Inputs.Add(grouped[i][port].Value);
                    m_InputRanges[i] = new OrchestrationRange(offset, m_Inputs.Count - offset);
                }
            }

            bool TryOutputKind(int operationIndex, Port port, out OrchestrationValueKind kind)
            {
                if (port.Kind != 0)
                {
                    kind = port.Kind;
                    return true;
                }
                SemanticOperation operation = m_Operations[operationIndex];
                if (operation.Code == OrchestrationOperationCode.BlackboardGet)
                    kind = Kind(m_StateSlots[StateReference(operationIndex)].ValueKind);
                else if (operation.Code == OrchestrationOperationCode.Constant)
                {
                    SemanticLiteralKind literalKind = m_Literals[operation.LiteralReferences[0]].Kind;
                    if (literalKind == SemanticLiteralKind.Curve || literalKind == SemanticLiteralKind.Identities)
                    {
                        kind = default;
                        return false;
                    }
                    kind = Kind(OrchestrationValuePortContracts.FromLiteral(literalKind));
                }
                else
                {
                    Error("value_output_kind_missing", ProgramSourceTargetKind.Operation, operationIndex,
                        $"输出端口 '{port.Identity}' 未声明实际值类型。", port.Identity, ProgramValuePortDirection.Output);
                    kind = default;
                    return false;
                }
                port.Kind = kind;
                return true;
            }

            void ValidateInputKind(int operationIndex, Port port, OrchestrationValueKind kind)
            {
                if (port.Kind != 0)
                {
                    if (port.Kind != kind)
                        Error("value_input_kind_mismatch", ProgramSourceTargetKind.Operation, operationIndex,
                            $"输入 '{port.Identity}' 需要 '{port.Kind}'，连接值为 '{kind}'。", port.Identity, ProgramValuePortDirection.Input);
                    return;
                }
                if (m_Operations[operationIndex].Code == OrchestrationOperationCode.BlackboardSet)
                {
                    if (Kind(m_StateSlots[StateReference(operationIndex)].ValueKind) != kind)
                        Error("blackboard_input_kind_mismatch", ProgramSourceTargetKind.Operation, operationIndex,
                            $"黑板输入值 '{kind}' 与声明不匹配。", port.Identity, ProgramValuePortDirection.Input);
                    return;
                }
                if (!port.AuthorDefinition.Accepts(OrchestrationValuePortContracts.FromOrchestrationKind(kind)))
                    Error("value_input_kind_rejected", ProgramSourceTargetKind.Operation, operationIndex,
                        $"输入 '{port.Identity}' 不接受 '{kind}'。", port.Identity, ProgramValuePortDirection.Input);
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

            OperationDraft[] BuildOperations(int[] parents)
            {
                var result = new OperationDraft[m_RuntimeOperationCount];
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
                    result[i] = new OperationDraft(source.Code,
                        isRunnable, Bind(m_OperationIdentities[i]), integer0, source.Integer1, constant,
                        m_InputSlots[i], source.Code == OrchestrationOperationCode.InputRequest ? Bind(source.Text0) : 0,
                        valueSlot, source.Code is OrchestrationOperationCode.Timeline or OrchestrationOperationCode.TimelineSection
                            ? Bind(source.Text0) : 0,
                        stateId, source.Code == OrchestrationOperationCode.State ? StateMachineOwner(i) : -1,
                        m_ExtensionIndexes[i], GraphCallIndex(i), m_InputRanges[i], ExecutionOutputCount(i),
                        m_OutputOffsets[i], m_LifecycleHookRanges[i], m_BlackboardFactsIndexes[i], parents[i], m_OutputAssignmentRanges[i],
                        m_ExecutionFrameOwners[i]);
                    foreach (Port port in m_Ports[i].Outputs)
                        if (port.ConstantIndex >= 0)
                            result[port.OperationIndex] = new OperationDraft(OrchestrationOperationCode.Constant,
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

            bool[] CollectPersistentOutputs()
            {
                var result = new bool[m_Operations.Count];
                for (int i = 0; i < m_Operations.Count; i++)
                    foreach (SemanticDerivedInput input in m_Operations[i].DerivedInputs)
                        if (input.Source == SemanticDerivedInputSource.SelfOutput)
                        {
                            result[i] = true;
                            break;
                        }
                foreach (ProgramControlFlowEdge edge in m_ControlFlow)
                    if (edge.Kind == ProgramControlFlowKind.Value &&
                        m_Ports[edge.Source.Value].Output(edge.SourcePort).ConstantIndex < 0)
                        result[edge.Source.Value] = true;
                foreach (ProgramSourceMapEntry entry in m_SourceMap)
                {
                    ProgramExecutionSource source = entry.Source;
                    if (source.TargetKind == ProgramSourceTargetKind.OperationPort &&
                        source.ValuePortDirection == ProgramValuePortDirection.Output &&
                        m_Ports[source.TargetIndex].Output(source.CompiledPortId).ConstantIndex < 0)
                        result[source.TargetIndex] = true;
                }
                return result;
            }

            int BoundStateSlot(int operationIndex, string portId)
            {
                foreach (ProgramReference reference in m_References)
                    if (reference.Kind == ProgramReferenceKind.StateSlot && reference.HasSourceOperation &&
                        reference.SourceOperation.Value == operationIndex && reference.ExternalIdentity == portId)
                        return reference.TargetIndex;
                throw Invariant(ProgramSourceTargetKind.Operation, operationIndex,
                    $"黑板端口 '{portId}' 未绑定正式状态槽。");
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
                    {
                        Error("state_body_multiple_owners", ProgramSourceTargetKind.Operation, operationIndex,
                            $"节点同时属于 State '{SourceLocation(ProgramSourceTargetKind.Operation, parents[operationIndex])}' 和 '{SourceLocation(ProgramSourceTargetKind.Operation, state)}'，不能发布静态状态祖先。");
                        return;
                    }
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
                throw Invariant(ProgramSourceTargetKind.Operation, operationIndex,
                    "State 节点未声明所属 StateMachine。");
            }

            int StateReference(int operationIndex)
            {
                for (int i = 0; i < m_References.Count; i++)
                    if (m_References[i].Kind == ProgramReferenceKind.StateSlot &&
                        m_References[i].SourceOperation.Value == operationIndex)
                        return m_References[i].TargetIndex;
                throw Invariant(ProgramSourceTargetKind.Operation, operationIndex, "节点未声明黑板槽引用。");
            }

            int ValueSlot(int stateSlot)
            {
                int result = m_StateValueSlots[stateSlot];
                if (result < 0)
                    throw Invariant(ProgramSourceTargetKind.StateSlot, stateSlot, "状态槽不是可发布的通用黑板值。");
                return result;
            }

            ProgramScopeLayout Scope(int stateSlot)
            {
                for (int i = 0; i < m_Scopes.Count; i++)
                    foreach (int slot in m_Scopes[i].StateSlots)
                        if (slot == stateSlot)
                            return m_Scopes[i];
                throw Invariant(ProgramSourceTargetKind.StateSlot, stateSlot, "黑板槽未声明正式作用域。");
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
                throw Invariant(ProgramSourceTargetKind.StateSlot, stateSlot, "黑板槽未声明变量目录项。");
            }

            SemanticLiteral CatalogLiteral(ProgramCatalogEntry declaration, string name)
            {
                foreach (ProgramCatalogField field in declaration.Fields)
                    if (field.Name == name)
                        return m_Literals[field.ConstantIndex];
                throw Invariant(ProgramSourceTargetKind.CatalogEntry, declaration.Index,
                    $"黑板声明 '{declaration.Dependency.Identity}' 缺少字段 '{name}'。");
            }

            int CatalogInt(ProgramCatalogEntry declaration, string name) => CatalogLiteral(declaration, name).Int32;

            string CatalogText(ProgramCatalogEntry declaration, string name) => CatalogLiteral(declaration, name).String;

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
                    ReadOnlyMemory<string> path = entry.Text.DisplayPathSegments;
                    int targetIndex = source.TargetKind == ProgramSourceTargetKind.OperationPort &&
                        source.ValuePortDirection == ProgramValuePortDirection.Output ? m_Ports[source.TargetIndex].Output(source.CompiledPortId).OperationIndex
                        : source.TargetKind == ProgramSourceTargetKind.StateSlot && m_StateValueSlots[source.TargetIndex] >= 0
                            ? ValueSlot(source.TargetIndex) : source.TargetIndex;
                    OrchestrationGraphSourceTargetKind targetKind = source.TargetKind == ProgramSourceTargetKind.StateSlot
                        ? m_StateValueSlots[source.TargetIndex] < 0
                            ? OrchestrationGraphSourceTargetKind.AuthorStateSlot
                            : OrchestrationGraphSourceTargetKind.BlackboardValue
                        : SourceTargetKind(source);
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

            OrchestrationGraphSourceTargetKind SourceTargetKind(ProgramExecutionSource source) => source.TargetKind switch
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
                _ => throw Invariant(source.TargetKind, source.TargetIndex,
                    $"来源种类 '{source.TargetKind}' 没有编排发布目标。")
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
                var value = m_LoweredValues[semanticIndex];
                int index = m_Constants.Count;
                m_Constants.Add(value);
                m_ConstantIndexes.Add(semanticIndex, index);
                return index;
            }

            TValue LowerValue(SemanticLiteral literal)
            {
                TValues values = default;
                TValue value;
                if (literal.Kind == SemanticLiteralKind.Curve)
                    throw new ValueLoweringException($"曲线 '{literal.Identity}' 属于领域内容，不能进入通用值槽。");
                switch (literal.Kind)
                {
                    case SemanticLiteralKind.Boolean:
                        value = values.FromBoolean(literal.Boolean);
                        break;
                    case SemanticLiteralKind.Int32:
                        value = values.FromInt32(literal.Int32);
                        break;
                    case SemanticLiteralKind.UInt64:
                        value = values.FromUInt64(literal.UInt64);
                        break;
                    case SemanticLiteralKind.Number:
                        value = values.FromScalar(
                            LowerNumber(literal.Number.Value, literal.Identity, literal.Number.Precision));
                        break;
                    case SemanticLiteralKind.Vector2:
                        value = values.FromVector2(m_Lowering.Vector2(
                            LowerNumber(literal.Vector2.X, literal.Identity + ".x", literal.Vector2.Precision),
                            LowerNumber(literal.Vector2.Y, literal.Identity + ".y", literal.Vector2.Precision)));
                        break;
                    case SemanticLiteralKind.Vector3:
                        value = values.FromVector3(m_Lowering.Vector3(
                            LowerNumber(literal.Vector3.X, literal.Identity + ".x", literal.Vector3.Precision),
                            LowerNumber(literal.Vector3.Y, literal.Identity + ".y", literal.Vector3.Precision),
                            LowerNumber(literal.Vector3.Z, literal.Identity + ".z", literal.Vector3.Precision)));
                        break;
                    case SemanticLiteralKind.Yaw:
                        value = values.FromYaw(
                            m_Lowering.Yaw(LowerNumber(literal.Yaw.Value, literal.Identity, literal.Yaw.Precision)));
                        break;
                    case SemanticLiteralKind.String:
                        value = values.FromIdentity(
                            literal.String is null ? OrchestrationIdentityValue.Null :
                                OrchestrationIdentityValue.Content(Bind(literal.String)));
                        break;
                    case SemanticLiteralKind.Quaternion:
                        value = values.FromQuaternion(m_Lowering.Quaternion(
                            LowerNumber(literal.Quaternion.X, literal.Identity + ".x", SemanticNumericPrecision.TargetRounded),
                            LowerNumber(literal.Quaternion.Y, literal.Identity + ".y", SemanticNumericPrecision.TargetRounded),
                            LowerNumber(literal.Quaternion.Z, literal.Identity + ".z", SemanticNumericPrecision.TargetRounded),
                            LowerNumber(literal.Quaternion.W, literal.Identity + ".w", SemanticNumericPrecision.TargetRounded)));
                        break;
                    case SemanticLiteralKind.Enum:
                        value = values.FromEnum(
                            new OrchestrationEnumValue(Bind(literal.Enum.TypeIdentity), literal.Enum.Value));
                        break;
                    default:
                        throw new ValueLoweringException($"语义常量 '{literal.Identity}' 类型 '{literal.Kind}' 不属于通用值。");
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
                    IBtsmtlOrchestrationContentEncoder<TValue, TScalar> encoder =
                        m_Domain.ContentEncoder;
                    ReadOnlyMemory<OrchestrationExtensionContentField> declarations = m_Operations[operationIndex].Extension.Value.Fields;
                    IBtsmtlOrchestrationContentBuffer<TValue, TScalar> writer =
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
                                writer.WriteCurve(in declaration, m_LoweredCurves[field.LiteralIndex]);
                                break;
                            default:
                                var value = m_LoweredValues[field.LiteralIndex];
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
                        LowerNumber(value, identity + member, SemanticNumericPrecision.TargetRounded);
                }
                return new GameplayAbilityCurve<TScalar>(source.PreWrapMode, source.PostWrapMode, keys);
            }

            TValue DefaultValue(OrchestrationValueKind kind) =>
                kind == OrchestrationValueKind.Quaternion
                    ? default(TValues).FromQuaternion(m_Lowering.QuaternionIdentity)
                    : default(TValues).Default(kind);

            static int[] EmptyIndexes(int count)
            {
                var result = new int[count];
                Array.Fill(result, -1);
                return result;
            }

            OrchestrationValueKind Kind(ProgramStateValueKind kind) => m_Domain.StateSchema.RuntimeKind(kind);

            OrchestrationValueKind Kind(SemanticValueKind kind) => kind switch
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
                _ => throw new InvalidOperationException($"图 '{m_Identity.GraphId}' 的语义类型 '{kind}' 不属于通用值。")
            };

        }
    }
}
