using System;
using System.Collections.Generic;
using System.Globalization;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;

namespace BTSMTL.Compilation
{
    public class BtsmtlGraphSemanticBuilder
    {
        sealed class GraphInvocationScope : IDisposable
        {
            Stack<(string Path, string ParentPath)> m_Paths;
            internal GraphInvocationScope(Stack<(string Path, string ParentPath)> paths,
                string path, string parentPath)
            {
                m_Paths = paths;
                paths.Push((path, parentPath));
            }
            public void Dispose()
            {
                m_Paths?.Pop();
                m_Paths = null;
            }
        }

        readonly Stack<(string Path, string ParentPath)> m_GraphInvocationPaths = new();
        readonly List<string> m_OperationInvocationPaths = new();

        public string CurrentGraphInvocationPath => m_GraphInvocationPaths.Count == 0
            ? string.Empty : m_GraphInvocationPaths.Peek().Path;

        public IDisposable PushGraphInvocation(string path, string parentInvocationPath)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("图编译必须指定调用路径。", nameof(path));
            return new GraphInvocationScope(m_GraphInvocationPaths, path, parentInvocationPath);
        }

        protected readonly SimulationCompileReport m_Report;
        readonly ProgramStateSchema m_StateSchema;
        protected readonly List<SemanticOperation> m_Operations = new List<SemanticOperation>();
        readonly List<SemanticOperationParameterValues> m_OperationParameters = new();
        protected readonly List<SemanticLiteral> m_Literals = new List<SemanticLiteral>();
        protected readonly List<SemanticConstantInputBinding> m_ConstantInputBindings = new List<SemanticConstantInputBinding>();
        protected readonly List<ProgramControlFlowEdge> m_ControlFlow = new List<ProgramControlFlowEdge>();
        protected readonly List<ProgramReference> m_References = new List<ProgramReference>();
        protected readonly List<ProgramGraphCallFrame> m_GraphCallFrames = new List<ProgramGraphCallFrame>();
        protected readonly List<ProgramStateSlot> m_StateSlots = new List<ProgramStateSlot>();
        protected readonly List<ProgramScopeLayout> m_Scopes = new List<ProgramScopeLayout>();
        protected readonly List<ProgramCatalogEntry> m_CatalogEntries = new List<ProgramCatalogEntry>();
        protected readonly List<ProgramSourceMapEntry> m_SourceMap = new List<ProgramSourceMapEntry>();
        readonly Dictionary<(string SourceType, string DisplayPath), ProgramSourceMapText> m_SourceTexts = new();
        protected readonly Dictionary<string, ProgramGraphSourceRevision> m_GraphSourceRevisions = new(StringComparer.Ordinal);
        readonly Dictionary<string, int> m_ConstantByIdentity = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly Dictionary<ProgramIdentityDependency, int> m_CatalogByIdentity = new();
        readonly HashSet<string> m_ReferenceIdentities = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> m_ScopeIdentities = new HashSet<string>(StringComparer.Ordinal);
        readonly Dictionary<ProgramStateValueKind, int> m_DefaultConstants = new Dictionary<ProgramStateValueKind, int>();
        readonly IReadOnlyDictionary<(string InvocationPath, string Template, string Field, int Ordinal), string> m_PreviousDependencyIdentityTexts;
        readonly Dictionary<(string InvocationPath, string Template, string Field, int Ordinal), string> m_DependencyIdentityTexts = new();

        public BtsmtlGraphSemanticBuilder(SimulationCompileReport report, ProgramStateSchema stateSchema,
            IReadOnlyDictionary<(string InvocationPath, string Template, string Field, int Ordinal), string> previousDependencyIdentityTexts = null)
        {
            m_Report = report;
            m_StateSchema = stateSchema;
            m_PreviousDependencyIdentityTexts = previousDependencyIdentityTexts;
        }

        public SimulationCompileReport Report => m_Report;
        public IReadOnlyDictionary<(string InvocationPath, string Template, string Field, int Ordinal), string> DependencyIdentityTexts => m_DependencyIdentityTexts;

        public OrchestrationGraphContent<TValue>
            BuildOrchestration<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues, TLowering>(
                OperationHandle entry, GraphExecutionIdentity identity, TLowering lowering,
                IBtsmtlOrchestrationContentEncoder<TValue, TScalar> contentEncoder,
                IReadOnlyDictionary<string, OrchestrationExecutionContract> entryContracts)
            where TValue : unmanaged
            where TScalar : unmanaged, IComparable<TScalar>, IEquatable<TScalar>
            where TVector2 : unmanaged
            where TVector3 : unmanaged
            where TYaw : unmanaged
            where TQuaternion : unmanaged
            where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
            where TLowering : struct, IBtsmtlOrchestrationNumericLowering<TScalar, TVector2, TVector3, TYaw, TQuaternion>
        {
            ValidateSingleChildControlFlow();
            if (!m_Report.IsValid)
                throw new InvalidOperationException("图编译失败，见编译报告。");
            return BtsmtlOrchestrationGraphCompiler.Compile<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues, TLowering>(
                m_Operations, m_Literals, m_ConstantInputBindings, m_ControlFlow, m_References,
                m_GraphCallFrames, m_StateSlots, m_Scopes, m_CatalogEntries, m_SourceMap, entry, identity,
                OrchestrationExecutionContract.Complete,
                Array.Empty<OrchestrationExtensionReference>(),
                lowering,
                new BtsmtlOrchestrationCompilationDomain<TValue, TScalar>(
                    m_StateSchema, contentEncoder, null),
                m_Report,
                entryContracts);
        }

        public bool TryGetCatalogEntry(ProgramIdentityDependency dependency, out int index)
        {
            return m_CatalogByIdentity.TryGetValue(dependency, out index);
        }

        public OperationHandle DeclareOperation(
            SimulationSourceLocation source,
            OrchestrationOperationCode code,
            IReadOnlyList<int> constantReferences,
            int integer0 = 0,
            int integer1 = 0,
            ulong unsigned0 = 0,
            double number0 = 0d,
            string text0 = null,
            uint flags = 0,
            string phase = "",
            OrchestrationExtensionDefinition? extension = null,
            IReadOnlyList<SemanticPayloadField> payloadFields = null,
            OrchestrationOutputTemplate? output = null,
            IReadOnlyList<SemanticHostInputPort> hostInputs = null,
            IReadOnlyList<SemanticBlackboardPortBinding> blackboardInputs = null,
            IReadOnlyList<SemanticBlackboardPortBinding> blackboardOutputs = null,
            IReadOnlyList<SemanticConstantOutputBinding> constantOutputs = null,
            IReadOnlyList<SemanticDerivedInput> derivedInputs = null)
        {
            var handle = new OperationHandle(m_Operations.Count);
            List<int> stateSlots = DeclareOperationStateSlots(handle, code, source, extension);
            int parameterIndex = m_OperationParameters.Count;
            m_OperationParameters.Add(new SemanticOperationParameterValues(
                integer0, integer1, unsigned0, number0, text0, flags));
            m_Operations.Add(new SemanticOperation(
                handle,
                phase.Length == 0 ? source.TemplateIdentity : source.TemplateIdentity + "/phase:" + phase,
                code,
                Array.Empty<int>(),
                constantReferences ?? Array.Empty<int>(),
                stateSlots,
                $"{source.Identity}/number0",
                m_OperationParameters,
                parameterIndex,
                extension?.CreateReference(handle.Value, source.DisplayPath),
                payloadFields ?? Array.Empty<SemanticPayloadField>(),
                output,
                hostInputs ?? Array.Empty<SemanticHostInputPort>(),
                blackboardInputs ?? Array.Empty<SemanticBlackboardPortBinding>(),
                blackboardOutputs ?? Array.Empty<SemanticBlackboardPortBinding>(),
                constantOutputs ?? Array.Empty<SemanticConstantOutputBinding>(),
                derivedInputs ?? Array.Empty<SemanticDerivedInput>()));
            m_OperationInvocationPaths.Add(CurrentGraphInvocationPath);
            AddSourceMap(ProgramSourceTargetKind.Operation, handle.Value, source);
            if (extension.HasValue)
                DeclareExtensionDependencies(handle, extension.Value, payloadFields, source);
            return handle;
        }

        void DeclareExtensionDependencies(OperationHandle operation,
            in OrchestrationExtensionDefinition extension, IReadOnlyList<SemanticPayloadField> payload,
            SimulationSourceLocation source)
        {
            foreach (OrchestrationExtensionContentField declaration in extension.Contract.Fields.Span)
            {
                if (declaration.Kind != OrchestrationExtensionFieldKind.Identity)
                    continue;
                foreach (SemanticPayloadField field in payload)
                {
                    if (field.Name != declaration.Id)
                        continue;
                    SemanticLiteral literal = m_Literals[field.LiteralIndex];
                    if (declaration.Cardinality == OrchestrationExtensionFieldCardinality.Single)
                        Declare(declaration, literal.String, 0);
                    else
                        for (int index = 0; index < literal.Identities.Length; index++)
                            Declare(declaration, literal.Identities.Span[index], index);
                }
            }

            void Declare(OrchestrationExtensionContentField field, string identity, int ordinal)
            {
                if (identity is null || identity.Length == 0)
                    return;
                var dependency = new ProgramIdentityDependency(field.IdentityDomainId, identity);
                int catalog = DeclareCatalogEntry(dependency, 1, Array.Empty<ProgramCatalogField>(), source);
                var key = (CurrentGraphInvocationPath, m_Operations[operation.Value].TemplateIdentity, field.Id, ordinal);
                if (m_PreviousDependencyIdentityTexts == null || !m_PreviousDependencyIdentityTexts.TryGetValue(key, out string reference))
                    reference = StableHash.Compute("orchestration-dependency", key.Item1,
                        key.Item2, key.Item3, ordinal.ToString(CultureInfo.InvariantCulture)).ToString();
                m_DependencyIdentityTexts.TryAdd(key, reference);
                DeclareReference(reference, operation, ProgramReferenceKind.CatalogEntry,
                    catalog, dependency.Identity, source);
            }
        }

        public void DeclareOperationPortSource(OperationHandle operation, SimulationSourceLocation source,
            string compiledPortId, ProgramValuePortDirection direction, bool isInputPort = false)
        {
            if (!operation.IsValid || operation.Value >= m_Operations.Count)
                throw new ArgumentOutOfRangeException(nameof(operation));
            if (string.IsNullOrEmpty(source.PortId))
                throw new ArgumentException("Operation port Source Map requires a port identity.", nameof(source));
            AddSourceMap(ProgramSourceTargetKind.OperationPort, operation.Value, source,
                compiledPortId, direction,
                isInputPort: isInputPort);
        }

        public void DeclareOptimizedSource(SimulationSourceLocation source)
        {
            if (string.IsNullOrEmpty(source.GraphId) || string.IsNullOrEmpty(source.NodeId) ||
                string.IsNullOrEmpty(source.ContentHash))
                throw new ArgumentException("Optimized source mapping requires graph, node and content identities.", nameof(source));
            AddSourceMap(ProgramSourceTargetKind.OptimizedAway, 0, source);
        }

        public void DeclareGraphInvocation(OperationHandle owner, OperationHandle entry, SimulationSourceLocation source,
            ProgramInvocationCallerKind callerKind, string callerId, string callerClipId, int callerOperationIndex)
        {
            if (!owner.IsValid || owner.Value >= m_Operations.Count)
                throw new InvalidOperationException("图调用必须指定已有所属操作。");
            AddSourceMap(ProgramSourceTargetKind.GraphInvocation, owner.Value, source,
                callerKind: callerKind, callerId: callerId,
                callerClipId: callerClipId, callerOperationIndex: callerOperationIndex,
                invocationEntryOperationIndex: entry.Value);
        }

        public int DeclareConstant(SimulationSourceLocation source, string fieldName, object value)
        {
            string identity = $"{source.ImmutableDataIdentity}/constant/{fieldName}";
            if (m_ConstantByIdentity.TryGetValue(identity, out int existing))
                return existing;
            int index = m_Literals.Count;
            SemanticLiteral literal;
            try
            {
                literal = CreateLiteral(index, identity, value);
            }
            catch (Exception exception)
            {
                m_Report.Error("numeric_or_constant_invalid", source.Identity, $"Field '{fieldName}' cannot be compiled: {exception.Message}");
                return -1;
            }
            m_Literals.Add(literal);
            m_ConstantByIdentity.Add(identity, index);
            AddSourceMap(ProgramSourceTargetKind.Constant, index, source);
            return index;
        }

        public void DeclareConstantInputBinding(
            OperationHandle targetOperation,
            string targetPort,
            int constantIndex,
            SemanticValueKind resolvedValueKind,
            SimulationSourceLocation source)
        {
            try
            {
                m_ConstantInputBindings.Add(new SemanticConstantInputBinding(
                    targetOperation,
                    targetPort,
                    constantIndex,
                    resolvedValueKind));
            }
            catch (Exception exception)
            {
                m_Report.Error("constant_input_binding_invalid", source.Identity, exception.Message);
            }
        }

        public int DeclareCatalogEntry(ProgramIdentityDependency dependency, int revision, IEnumerable<ProgramCatalogField> fields, SimulationSourceLocation source)
        {
            if (m_CatalogByIdentity.TryGetValue(dependency, out int existing))
                return existing;
            int index = m_CatalogEntries.Count;
            try
            {
                var validFields = fields == null
                    ? Array.Empty<ProgramCatalogField>()
                    : new List<ProgramCatalogField>(fields).FindAll(value => value != null).ToArray();
                m_CatalogEntries.Add(new ProgramCatalogEntry(index, dependency, revision, validFields));
                m_CatalogByIdentity.Add(dependency, index);
                AddSourceMap(ProgramSourceTargetKind.CatalogEntry, index, source);
                return index;
            }
            catch (Exception exception)
            {
                m_Report.Error("catalog_entry_invalid", source.Identity, exception.Message);
                return -1;
            }
        }

        public int DeclareStateSlot(
            string identity,
            ProgramStateValueKind valueKind,
            ProgramStateOwnerKind ownerKind,
            ProgramStateSemantic semantic,
            string ownerIdentity,
            ProgramSourceTargetKind sourceTargetKind,
            SimulationSourceLocation source)
        {
            int index = m_StateSlots.Count;
            m_StateSlots.Add(CreateStateSlot(
                index,
                identity,
                valueKind,
                ownerKind,
                semantic,
                ownerIdentity,
                GetDefaultConstant(source, valueKind)));
            AddSourceMap(sourceTargetKind, index, source);
            return index;
        }

        ProgramStateSlot CreateStateSlot(int index, string identity, ProgramStateValueKind valueKind,
            ProgramStateOwnerKind ownerKind, ProgramStateSemantic semantic, string ownerIdentity, int defaultConstant)
        {
            string codec = m_StateSchema.CodecIdentity(valueKind);
            var slot = new ProgramStateSlot(index, identity, valueKind, ownerKind, semantic, ownerIdentity, codec, defaultConstant);
            m_StateSchema.RequireSlot(valueKind, ownerKind, semantic);
            return slot;
        }

        public void DeclareSourceMap(
            ProgramSourceTargetKind targetKind,
            int targetIndex,
            SimulationSourceLocation source)
        {
            AddSourceMap(targetKind, targetIndex, source);
        }

        public ProgramCatalogField StateDefaultField(int slot) =>
            new("Default", ProgramCatalogFieldKind.Constant, m_StateSlots[slot].DefaultConstantIndex, null);

        public ProgramCatalogField ConstantField(SimulationSourceLocation source, string name, object value)
        {
            int constant = DeclareConstant(source, name, value);
            return constant >= 0 ? new ProgramCatalogField(name, ProgramCatalogFieldKind.Constant, constant, null) : null;
        }

        public ProgramCatalogField IdentityField(string name, ProgramIdentityDependency dependency)
        {
            return new ProgramCatalogField(name, ProgramCatalogFieldKind.Identity, -1, dependency);
        }

        public void DeclareControlFlow(
            string identity,
            OperationHandle source,
            OperationHandle target,
            string sourcePort,
            string targetPort,
            ProgramControlFlowKind kind,
            int order,
            int priority,
            OrchestrationAbortPolicy abortPolicy,
            bool hasCondition,
            OperationHandle condition,
            SimulationSourceLocation sourceLocation)
        {
            try
            {
                m_ControlFlow.Add(new ProgramControlFlowEdge(identity, source, target, sourcePort, targetPort, kind, order, priority, abortPolicy, hasCondition, condition));
                AddSourceMap(ProgramSourceTargetKind.Reference, m_ControlFlow.Count - 1, sourceLocation);
            }
            catch (Exception exception)
            {
                m_Report.Error("control_flow_invalid", sourceLocation.Identity, exception.Message);
            }
        }

        public void DeclareReference(string identity, OperationHandle sourceOperation, ProgramReferenceKind kind, int targetIndex, string externalIdentity, SimulationSourceLocation source)
        {
            if (!m_ReferenceIdentities.Add(identity))
            {
                m_Report.Error("reference_duplicate", source.Identity, $"Reference identity '{identity}' is duplicated.");
                return;
            }
            try
            {
                m_References.Add(new ProgramReference(identity, sourceOperation, kind, targetIndex, externalIdentity));
            }
            catch (Exception exception)
            {
                m_Report.Error("reference_invalid", source.Identity, exception.Message);
            }
        }

        public void DeclareGraphCallFrame(
            string identity,
            OperationHandle ownerOperation,
            OperationHandle entryOperation,
            string childGraphIdentity,
            IEnumerable<ProgramGraphParameterBinding> inputs,
            IEnumerable<ProgramGraphParameterBinding> outputs,
            SimulationSourceLocation source)
        {
            try
            {
                m_GraphCallFrames.Add(new ProgramGraphCallFrame(
                    m_GraphCallFrames.Count,
                    identity,
                    ownerOperation,
                    entryOperation,
                    childGraphIdentity,
                    inputs,
                    outputs));
            }
            catch (Exception exception)
            {
                m_Report.Error("graph_call_frame_invalid", source.Identity, exception.Message);
            }
        }

        public void DeclareScope(
            string identity,
            ProgramScopeKind kind,
            string ownerIdentity,
            OperationHandle ownerOperation,
            IReadOnlyList<int> slots,
            SimulationSourceLocation source)
        {
            if (!m_ScopeIdentities.Add(identity))
            {
                m_Report.Error("scope_duplicate", source.Identity, $"Scope identity '{identity}' is duplicated.");
                return;
            }
            m_Scopes.Add(new ProgramScopeLayout(m_Scopes.Count, identity, kind, ownerIdentity, ownerOperation, slots));
        }

        public int DeclareStandaloneStateSlot(SimulationSourceLocation source, ProgramStateValueKind valueKind, ProgramStateOwnerKind ownerKind, ProgramStateSemantic semantic, string ownerIdentity)
        {
            return DeclareStandaloneStateSlot(source, valueKind, ownerKind, semantic, ownerIdentity, null, false);
        }

        public int DeclareStandaloneStateSlot(
            SimulationSourceLocation source,
            ProgramStateValueKind valueKind,
            ProgramStateOwnerKind ownerKind,
            ProgramStateSemantic semantic,
            string ownerIdentity,
            object defaultValue)
        {
            return DeclareStandaloneStateSlot(source, valueKind, ownerKind, semantic, ownerIdentity, defaultValue, true);
        }

        int DeclareStandaloneStateSlot(
            SimulationSourceLocation source,
            ProgramStateValueKind valueKind,
            ProgramStateOwnerKind ownerKind,
            ProgramStateSemantic semantic,
            string ownerIdentity,
            object defaultValue,
            bool hasDefaultValue)
        {
            int index = m_StateSlots.Count;
            string identity = $"{source.Identity}/state/{m_StateSchema.SemanticIdentity(semantic)}/{ownerIdentity}";
            int defaultConstant = hasDefaultValue
                ? DeclareStateDefaultConstant(source, valueKind, semantic, ownerIdentity, defaultValue)
                : GetDefaultConstant(source, valueKind);
            m_StateSlots.Add(CreateStateSlot(index, identity, valueKind, ownerKind, semantic, ownerIdentity, defaultConstant));
            AddSourceMap(ProgramSourceTargetKind.StateSlot, index, source);
            return index;
        }

        int DeclareStateDefaultConstant(
            SimulationSourceLocation source,
            ProgramStateValueKind valueKind,
            ProgramStateSemantic semantic,
            string ownerIdentity,
            object defaultValue)
        {
            if (valueKind != ProgramStateValueKind.Yaw)
                return DeclareConstant(source, $"default:{m_StateSchema.SemanticIdentity(semantic)}:{ownerIdentity}", defaultValue);
            float yaw = Convert.ToSingle(defaultValue, CultureInfo.InvariantCulture);
            string identity = $"{source.ImmutableDataIdentity}/constant/default:{m_StateSchema.SemanticIdentity(semantic)}:{ownerIdentity}";
            if (m_ConstantByIdentity.TryGetValue(identity, out int existing))
                return existing;
            int index = m_Literals.Count;
            m_Literals.Add(SemanticLiteral.FromYaw(identity, index, new SemanticNumberValue(yaw)));
            m_ConstantByIdentity.Add(identity, index);
            AddSourceMap(ProgramSourceTargetKind.Constant, index, source);
            return index;
        }

        protected void ValidateSingleChildControlFlow()
        {
            var childCounts = new Dictionary<int, int>();
            for (int i = 0; i < m_ControlFlow.Count; i++)
            {
                ProgramControlFlowEdge edge = m_ControlFlow[i];
                if (edge.Kind != ProgramControlFlowKind.Child)
                    continue;
                childCounts.TryGetValue(edge.Source.Value, out int count);
                childCounts[edge.Source.Value] = count + 1;
            }

            for (int i = 0; i < m_Operations.Count; i++)
            {
                SemanticOperation operation = m_Operations[i];
                if (!RequiresSingleChild(operation.Code) ||
                    !childCounts.TryGetValue(operation.Handle.Value, out int count) ||
                    count <= 1)
                    continue;
                m_Report.Error(
                    "single_child_control_flow_invalid",
                    operation.TemplateIdentity,
                    $"Operation '{operation.Handle}' ({operation.Code}) has '{count}' child edges; at most one is allowed.");
            }
        }

        static bool RequiresSingleChild(OrchestrationOperationCode code)
        {
            return code == OrchestrationOperationCode.Root ||
                   code == OrchestrationOperationCode.SubGraph ||
                   code == OrchestrationOperationCode.StateOnEnter ||
                   code == OrchestrationOperationCode.StateOnExit ||
                   code == OrchestrationOperationCode.TimelineEnter;
        }

        protected virtual List<int> DeclareOperationStateSlots(OperationHandle handle,
            OrchestrationOperationCode code, SimulationSourceLocation source, OrchestrationExtensionDefinition? extension)
        {
            var slots = new List<int>();
            if (code == OrchestrationOperationCode.Cycle)
                slots.Add(DeclareStateSlot(source, handle, ProgramStateValueKind.Int32,
                    ProgramStateOwnerKind.Runnable, ProgramStateSemantic.RunnableChildCursor));
            return slots;
        }

        protected int DeclareStateSlot(SimulationSourceLocation source, OperationHandle handle, ProgramStateValueKind valueKind, ProgramStateOwnerKind ownerKind, ProgramStateSemantic semantic)
        {
            int index = m_StateSlots.Count;
            string identity = $"{source.Identity}/state/{m_StateSchema.SemanticIdentity(semantic)}";
            int defaultConstant = GetDefaultConstant(source, valueKind);
            m_StateSlots.Add(CreateStateSlot(index, identity, valueKind, ownerKind, semantic, $"operation:{handle.Value}", defaultConstant));
            AddSourceMap(ProgramSourceTargetKind.StateSlot, index, source);
            return index;
        }

        int GetDefaultConstant(SimulationSourceLocation source, ProgramStateValueKind kind)
        {
            if (!m_StateSchema.HasDefaultValue(kind))
                return -1;
            if (m_DefaultConstants.TryGetValue(kind, out int index))
                return index;
            if (kind == ProgramStateValueKind.Yaw)
            {
                string identity = $"{source.ImmutableDataIdentity}/constant/default:{kind}";
                index = m_Literals.Count;
                m_Literals.Add(SemanticLiteral.FromYaw(identity, index, new SemanticNumberValue(0d)));
                m_ConstantByIdentity.Add(identity, index);
                AddSourceMap(ProgramSourceTargetKind.Constant, index, source);
                m_DefaultConstants.Add(kind, index);
                return index;
            }
            object value = kind switch
            {
                ProgramStateValueKind.Boolean => false,
                ProgramStateValueKind.Int32 => 0,
                ProgramStateValueKind.UInt64 => 0UL,
                ProgramStateValueKind.Scalar => 0f,
                ProgramStateValueKind.Vector2 => new SemanticVector2Value(0d, 0d),
                ProgramStateValueKind.Vector3 => new SemanticVector3Value(0d, 0d, 0d),
                ProgramStateValueKind.Yaw => throw new InvalidOperationException(),
                ProgramStateValueKind.Identity => string.Empty,
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
            index = DeclareConstant(source, "default:" + kind, value);
            m_DefaultConstants.Add(kind, index);
            return index;
        }

        // 宿主作者值（如引擎向量、曲线、资产引用）由派生 builder 先转换为通用语义值，再交给 CreateCoreLiteral。
        protected virtual SemanticLiteral CreateLiteral(int index, string identity, object value) =>
            CreateCoreLiteral(index, identity, value);

        public static SemanticLiteral CreateCoreLiteral(int index, string identity, object value)
        {
            switch (value)
            {
                case bool boolean: return SemanticLiteral.FromBoolean(identity, index, boolean);
                case int int32: return SemanticLiteral.FromInt32(identity, index, int32);
                case uint uint32: return SemanticLiteral.FromUInt64(identity, index, uint32);
                case ulong uint64: return SemanticLiteral.FromUInt64(identity, index, uint64);
                case float single: return SemanticLiteral.FromNumber(identity, index, new SemanticNumberValue(single));
                case double number: return SemanticLiteral.FromNumber(identity, index, new SemanticNumberValue(number));
                case string text: return SemanticLiteral.FromString(identity, index, text);
                case ReadOnlyMemory<string> identities: return SemanticLiteral.FromIdentities(identity, index, identities);
                case SemanticEnumValue semanticEnum:
                    return SemanticLiteral.FromEnum(identity, index, new ProgramEnumConstant(semanticEnum.TypeIdentity, semanticEnum.Value));
                case SemanticQuaternionValue quaternion: return SemanticLiteral.FromQuaternion(identity, index, quaternion);
                case SemanticVector2Value vector2: return SemanticLiteral.FromVector2(identity, index, vector2);
                case SemanticVector3Value vector3: return SemanticLiteral.FromVector3(identity, index, vector3);
                case GameplayAbilityCurve<double> curve: return SemanticLiteral.FromCurve(identity, index, curve);
                case Enum enumValue: return SemanticLiteral.FromInt32(identity, index, Convert.ToInt32(enumValue, CultureInfo.InvariantCulture));
                case null: return SemanticLiteral.FromString(identity, index, null);
                default: throw new InvalidOperationException($"Unsupported constant type '{value.GetType().FullName}'.");
            }
        }

        void AddSourceMap(ProgramSourceTargetKind targetKind, int targetIndex, SimulationSourceLocation source,
            string compiledPortId = "", ProgramValuePortDirection direction = ProgramValuePortDirection.None,
            ProgramInvocationCallerKind callerKind = ProgramInvocationCallerKind.None,
            string callerId = "", string callerClipId = "", int callerOperationIndex = -1,
            int invocationEntryOperationIndex = -1, bool isInputPort = false)
        {
            if (m_GraphSourceRevisions.TryGetValue(source.GraphId, out ProgramGraphSourceRevision revision))
            {
                if (revision.Revision != source.ContentHash)
                    throw new InvalidOperationException($"图 '{source.GraphId}' 的来源版本冲突。");
            }
            else
                m_GraphSourceRevisions.Add(source.GraphId, new ProgramGraphSourceRevision(source.GraphId, source.ContentHash));
            try
            {
                string sourceInvocationPath = CurrentGraphInvocationPath;
                string sourceParentInvocationPath = m_GraphInvocationPaths.Count == 0
                    ? string.Empty : m_GraphInvocationPaths.Peek().ParentPath;
                string graphInvocationPath = sourceInvocationPath;
                if (targetKind == ProgramSourceTargetKind.GraphInvocation && !string.IsNullOrEmpty(callerId))
                    graphInvocationPath = $"{graphInvocationPath}/{callerId}";
                var execution = new ProgramExecutionSource(
                    targetKind,
                    targetIndex,
                    source.GraphId,
                    source.NodeId,
                    source.PortId,
                    source.EdgeId,
                    source.DeclarationId,
                    source.TimelineId,
                    source.TrackId,
                    source.ClipId,
                    direction == ProgramValuePortDirection.Input || isInputPort,
                    targetKind is ProgramSourceTargetKind.Operation or ProgramSourceTargetKind.OperationPort
                        ? m_OperationInvocationPaths[targetIndex]
                        : graphInvocationPath,
                    compiledPortId,
                    direction,
                    sourceInvocationPath,
                    sourceParentInvocationPath, callerKind, callerId, callerClipId, callerOperationIndex,
                    invocationEntryOperationIndex);
                var textKey = (source.SourceType, source.DisplayPath);
                if (!m_SourceTexts.TryGetValue(textKey, out ProgramSourceMapText text))
                {
                    text = new ProgramSourceMapText(source.SourceType, source.DisplayPath.Split('/'));
                    m_SourceTexts.Add(textKey, text);
                }
                m_SourceMap.Add(new ProgramSourceMapEntry(execution, text, source.DisplayPath));
            }
            catch (Exception exception)
            {
                m_Report.Error("source_map_invalid", source.Identity, exception.Message);
            }
        }

    }
}
