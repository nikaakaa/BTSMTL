using System;
using System.Collections.Generic;
using System.Threading;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;

namespace BTSMTL.Compilation
{
    public static partial class BtsmtlOrchestrationGraphCompiler
    {
        internal sealed class ParameterBindings<TValue> where TValue : unmanaged
        {
            internal ParameterBindings(OrchestrationGraphContent<TValue> content,
                IReadOnlyList<SemanticOperation> operations, IReadOnlyList<SemanticLiteral> literals,
                IReadOnlyList<SemanticConstantInputBinding> constantInputs,
                IReadOnlyList<ProgramControlFlowEdge> controlFlow, IReadOnlyList<ProgramReference> references,
                IReadOnlyList<ProgramGraphCallFrame> graphCalls, IReadOnlyList<ProgramStateSlot> stateSlots,
                IReadOnlyList<ProgramScopeLayout> scopes, IReadOnlyList<ProgramCatalogEntry> catalog,
                IReadOnlyList<ProgramSourceMapEntry> sourceMap,
                IReadOnlyList<OrchestrationExtensionReference> stateTransitionHooks,
                IReadOnlyDictionary<string, OrchestrationExecutionContract> entryContracts,
                object domain, Type compilationType, OrchestrationExecutionContract contract, string encoderIdentity,
                string[] structureIdentityTexts, int[] valueLiteralIndexes, int[] curveLiteralIndexes,
                int[] constantLiteralIndexes, int[] defaultLiteralIndexes, IReadOnlyCollection<int> structureLiteralIndexes,
                Dictionary<string, int> identityIds, IdentityCache executionIdentities)
            {
                Content = content;
                Operations = operations;
                Literals = literals;
                ConstantInputs = constantInputs;
                ControlFlow = controlFlow;
                References = references;
                GraphCalls = graphCalls;
                StateSlots = stateSlots;
                Scopes = scopes;
                Catalog = catalog;
                SourceMap = sourceMap;
                StateTransitionHooks = stateTransitionHooks;
                EntryContracts = entryContracts;
                Domain = domain;
                CompilationType = compilationType;
                Contract = contract;
                EncoderIdentity = encoderIdentity;
                StructureIdentityTexts = structureIdentityTexts;
                ValueLiteralIndexes = valueLiteralIndexes;
                CurveLiteralIndexes = curveLiteralIndexes;
                ConstantLiteralIndexes = constantLiteralIndexes;
                DefaultLiteralIndexes = defaultLiteralIndexes;
                StructureLiteralIndexes = structureLiteralIndexes;
                IdentityIds = identityIds;
                ExecutionIdentities = executionIdentities;
            }

            internal readonly OrchestrationGraphContent<TValue> Content;
            internal readonly IReadOnlyList<SemanticOperation> Operations;
            internal readonly IReadOnlyList<SemanticLiteral> Literals;
            internal readonly IReadOnlyList<SemanticConstantInputBinding> ConstantInputs;
            internal readonly IReadOnlyList<ProgramControlFlowEdge> ControlFlow;
            internal readonly IReadOnlyList<ProgramReference> References;
            internal readonly IReadOnlyList<ProgramGraphCallFrame> GraphCalls;
            internal readonly IReadOnlyList<ProgramStateSlot> StateSlots;
            internal readonly IReadOnlyList<ProgramScopeLayout> Scopes;
            internal readonly IReadOnlyList<ProgramCatalogEntry> Catalog;
            internal readonly IReadOnlyList<ProgramSourceMapEntry> SourceMap;
            internal readonly IReadOnlyList<OrchestrationExtensionReference> StateTransitionHooks;
            internal readonly IReadOnlyDictionary<string, OrchestrationExecutionContract> EntryContracts;
            internal readonly object Domain;
            internal readonly Type CompilationType;
            internal readonly OrchestrationExecutionContract Contract;
            internal readonly string EncoderIdentity;
            internal readonly string[] StructureIdentityTexts;
            internal readonly int[] ValueLiteralIndexes;
            internal readonly int[] CurveLiteralIndexes;
            internal readonly int[] ConstantLiteralIndexes;
            internal readonly int[] DefaultLiteralIndexes;
            internal readonly IReadOnlyCollection<int> StructureLiteralIndexes;
            internal readonly Dictionary<string, int> IdentityIds;
            internal readonly IdentityCache ExecutionIdentities;
        }

        sealed partial class Compilation<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues, TLowering>
        {
            internal BtsmtlOrchestrationGraphLayout<TValue> RestoreLayout(OrchestrationGraphContent<TValue> content)
            {
                BuildExecutionIdentities();
                m_StructureIdentityTexts = new List<string>(StructureIdentityTexts()).ToArray();
                m_Identities = content.Identities;
                m_StructureIdentities = content.Structure.Identities;
                BuildIdentityBindings();
                BuildPorts();
                BuildExecutionFrameOwners();
                BuildValueSlots();
                BuildConstantBindings();
                CollectValueLiterals();
                foreach (var binding in VariableDeclarations())
                    ReadVariableMetadata(binding.Declaration);
                ThrowIfInvalid("编排绑定恢复");
                var identities = new IdentityCache(m_OperationIdentityTexts, m_InvocationIdentityTexts);
                return new BtsmtlOrchestrationGraphLayout<TValue>(CreateBindings(content, identities));
            }

            ParameterBindings<TValue> CreateBindings(OrchestrationGraphContent<TValue> content,
                IdentityCache identities, ParameterBindings<TValue> previous = null)
            {
                int[] constants;
                int[] defaults;
                IReadOnlyDictionary<string, OrchestrationExecutionContract> entryContracts;
                if (previous == null)
                {
                    constants = m_ConstantLiteralIndexes;
                    defaults = m_DefaultLiteralIndexes;
                    entryContracts = m_EntryContracts == null ? null :
                        new Dictionary<string, OrchestrationExecutionContract>(m_EntryContracts, StringComparer.Ordinal);
                }
                else
                {
                    constants = previous.ConstantLiteralIndexes;
                    defaults = previous.DefaultLiteralIndexes;
                    entryContracts = previous.EntryContracts;
                }
                return new ParameterBindings<TValue>(content, m_Operations, m_Literals, m_ConstantInputs,
                    m_ControlFlow, m_References, m_GraphCalls, m_StateSlots, m_Scopes, m_Catalog, m_SourceMap,
                    m_StateTransitionHooks, entryContracts, m_Domain, GetType(), m_Contract,
                    m_Domain.ContentEncoder?.ContractIdentity, m_StructureIdentityTexts,
                    m_ValueLiteralIndexes, m_CurveLiteralIndexes, constants, defaults,
                    previous == null ? m_StructureLiteralIndexes : previous.StructureLiteralIndexes, m_IdentityIds, identities);
            }

            BtsmtlOrchestrationCompilation<TValue> CompleteCompilation(OrchestrationGraphContent<TValue> content,
                ParameterBindings<TValue> bindings)
            {
                Volatile.Write(ref m_Domain.Identities, bindings.ExecutionIdentities);
                return new BtsmtlOrchestrationCompilation<TValue>(content,
                    new BtsmtlOrchestrationGraphLayout<TValue>(bindings));
            }

            bool CanReuseBindings(ParameterBindings<TValue> previous)
            {
                GraphCompilationIdentity identity = previous.Content.Identity.Source;
                if (previous.CompilationType != GetType() || !ReferenceEquals(previous.Domain, m_Domain) ||
                    previous.EncoderIdentity != m_Domain.ContentEncoder?.ContractIdentity ||
                    previous.Content.Structure.NumericProfile != m_Lowering.Profile ||
                    identity.GraphId != m_Identity.GraphId || identity.EncodingContract != m_Identity.EncodingContract ||
                    identity.NodeContract != m_Identity.NodeContract || identity.NumericTarget != m_Identity.NumericTarget ||
                    previous.Content.Structure.Program.RootOperationIndex != m_Entry.Value || previous.Contract != m_Contract ||
                    !ReferenceEquals(previous.ConstantInputs, m_ConstantInputs) ||
                    !ReferenceEquals(previous.ControlFlow, m_ControlFlow) || !ReferenceEquals(previous.References, m_References) ||
                    !ReferenceEquals(previous.GraphCalls, m_GraphCalls) || !ReferenceEquals(previous.StateSlots, m_StateSlots) ||
                    !ReferenceEquals(previous.Scopes, m_Scopes) || !ReferenceEquals(previous.Catalog, m_Catalog) ||
                    previous.Operations.Count != m_Operations.Count || previous.Literals.Count != m_Literals.Count ||
                    previous.SourceMap.Count != m_SourceMap.Count || previous.StateTransitionHooks.Count != m_StateTransitionHooks.Count)
                    return false;
                if ((previous.EntryContracts == null) != (m_EntryContracts == null))
                    return false;
                if (m_EntryContracts != null)
                {
                    if (previous.EntryContracts.Count != m_EntryContracts.Count)
                        return false;
                    foreach (KeyValuePair<string, OrchestrationExecutionContract> entry in m_EntryContracts)
                        if (!previous.EntryContracts.TryGetValue(entry.Key, out OrchestrationExecutionContract contract) ||
                            contract != entry.Value)
                            return false;
                }
                for (int i = 0; i < m_Operations.Count; i++)
                {
                    SemanticOperation current = m_Operations[i];
                    SemanticOperation original = previous.Operations[i];
                    if (!ReferenceEquals(current.Layout, original.Layout))
                        return false;
                    switch (current.Code)
                    {
                        case OrchestrationOperationCode.Loop:
                        case OrchestrationOperationCode.Parallel:
                        case OrchestrationOperationCode.Cycle:
                            if (current.Integer0 != original.Integer0 || current.Integer1 != original.Integer1)
                                return false;
                            break;
                        case OrchestrationOperationCode.Compare:
                        case OrchestrationOperationCode.Calculate:
                        case OrchestrationOperationCode.StateExitCause:
                            if (current.Integer0 != original.Integer0)
                                return false;
                            break;
                        case OrchestrationOperationCode.BlackboardSet:
                            if (current.Integer1 != original.Integer1)
                                return false;
                            break;
                        case OrchestrationOperationCode.InputBoolean:
                        case OrchestrationOperationCode.InputScalar:
                        case OrchestrationOperationCode.InputVector2:
                        case OrchestrationOperationCode.InputVector2Magnitude:
                            if (current.Text0 != original.Text0)
                                return false;
                            break;
                    }
                }
                for (int i = 0; i < m_SourceMap.Count; i++)
                    if (!ReferenceEquals(m_SourceMap[i].Source, previous.SourceMap[i].Source))
                        return false;
                for (int i = 0; i < m_Literals.Count; i++)
                    if (m_Literals[i].Kind != previous.Literals[i].Kind ||
                        m_Literals[i].Identity != previous.Literals[i].Identity ||
                        m_Literals[i].Kind == SemanticLiteralKind.Enum &&
                        m_Literals[i].Enum.TypeIdentity != previous.Literals[i].Enum.TypeIdentity)
                        return false;
                foreach (int index in previous.StructureLiteralIndexes)
                    if (!SameLiteral(m_Literals[index], previous.Literals[index]))
                        return false;
                for (int i = 0; i < m_StateTransitionHooks.Count; i++)
                {
                    OrchestrationExtensionReference current = m_StateTransitionHooks[i];
                    OrchestrationExtensionReference original = previous.StateTransitionHooks[i];
                    if (!current.Identity.Equals(original.Identity) || current.SemanticVersion != original.SemanticVersion ||
                        current.ContentIndex != original.ContentIndex || current.Kind != original.Kind || current.Access != original.Access ||
                        !current.Ports.Equals(original.Ports) || !current.Fields.Equals(original.Fields) ||
                        current.RequiredInvocations != original.RequiredInvocations || current.Completion != original.Completion)
                        return false;
                }
                return true;
            }

            static bool SameLiteral(SemanticLiteral current, SemanticLiteral previous)
            {
                if (ReferenceEquals(current, previous))
                    return true;
                if (current.Kind != previous.Kind)
                    return false;
                return current.Kind switch
                {
                    SemanticLiteralKind.Boolean => current.Boolean == previous.Boolean,
                    SemanticLiteralKind.Int32 => current.Int32 == previous.Int32,
                    SemanticLiteralKind.UInt64 => current.UInt64 == previous.UInt64,
                    SemanticLiteralKind.ObjectHandle => current.ObjectHandle == previous.ObjectHandle,
                    SemanticLiteralKind.Number => current.Number.Equals(previous.Number),
                    SemanticLiteralKind.Vector2 => current.Vector2.Equals(previous.Vector2),
                    SemanticLiteralKind.Vector3 => current.Vector3.Equals(previous.Vector3),
                    SemanticLiteralKind.Yaw => current.Yaw.Equals(previous.Yaw),
                    SemanticLiteralKind.String => current.String == previous.String,
                    SemanticLiteralKind.Quaternion => current.Quaternion.Equals(previous.Quaternion),
                    SemanticLiteralKind.Enum => current.Enum.Equals(previous.Enum),
                    SemanticLiteralKind.Identities => current.Identities.Span.SequenceEqual(previous.Identities.Span),
                    SemanticLiteralKind.Curve => SameCurve(current.Curve, previous.Curve),
                    _ => throw new ArgumentOutOfRangeException(nameof(current.Kind))
                };
            }

            static bool SameCurve(GameplayAbilityCurve<double> current, GameplayAbilityCurve<double> previous)
            {
                if (ReferenceEquals(current, previous))
                    return true;
                if (current.PreWrapMode != previous.PreWrapMode || current.PostWrapMode != previous.PostWrapMode ||
                    current.Keys.Count != previous.Keys.Count)
                    return false;
                for (int i = 0; i < current.Keys.Count; i++)
                    if (!current.Keys[i].Equals(previous.Keys[i]))
                        return false;
                return true;
            }

            BtsmtlOrchestrationCompilation<TValue> BindParameters(ParameterBindings<TValue> previous)
            {
                m_StructureIdentityTexts = previous.StructureIdentityTexts;
                m_ValueLiteralIndexes = previous.ValueLiteralIndexes;
                m_CurveLiteralIndexes = previous.CurveLiteralIndexes;
                m_StructureIdentities = previous.Content.Structure.Identities;
                if (ReferenceEquals(m_Operations, previous.Operations) && ReferenceEquals(m_Literals, previous.Literals))
                {
                    m_Identities = previous.Content.Identities;
                    m_IdentityIds = previous.IdentityIds;
                    OrchestrationGraphContent<TValue> unchanged = m_Domain.Encoding.Create(m_Identity,
                        previous.Content.Structure, previous.Content.Parameters, BindSources(previous), previous.Content);
                    return CompleteCompilation(unchanged, CreateBindings(unchanged, previous.ExecutionIdentities, previous));
                }
                m_Identities = OrchestrationIdentityPublication.Extend(m_StructureIdentities, IdentityTexts());
                if (m_Identities.Entries.Span.SequenceEqual(previous.Content.Identities.Entries.Span))
                    m_Identities = previous.Content.Identities;
                bool identitiesChanged = !ReferenceEquals(m_Identities, previous.Content.Identities);
                m_IdentityIds = previous.IdentityIds;
                if (identitiesChanged)
                    BuildIdentityBindings();
                PrepareValues();
                ThrowIfInvalid();

                OrchestrationGraphParameters<TValue> original = previous.Content.Parameters;
                ReadOnlyMemory<TValue> constants = BindLiteralValues(original.Constants,
                    previous.ConstantLiteralIndexes, previous, identitiesChanged);
                ReadOnlyMemory<TValue> defaults = BindLiteralValues(original.DefaultValues,
                    previous.DefaultLiteralIndexes, previous, identitiesChanged);
                OrchestrationGraphContent<TValue> source = previous.Content;
                var content = m_Domain.Encoding.Create(m_Identity, source.Structure,
                    new OrchestrationGraphParameters<TValue>(SourceParameterIdentity(), identitiesChanged ? ParameterIdentities() : original.Identities,
                        BindOperationPayloads(previous, identitiesChanged, Rebind), constants, defaults, BindOperationContents(previous)),
                    BindSources(previous), source);
                return CompleteCompilation(content, CreateBindings(content, previous.ExecutionIdentities, previous));

                int Rebind(int identity) => identity == 0 ? 0 : Bind(previous.Content.Identities.Resolve(identity));
            }

            ReadOnlyMemory<TValue> BindLiteralValues(ReadOnlyMemory<TValue> original, int[] literals,
                ParameterBindings<TValue> previous, bool identitiesChanged)
            {
                TValue[] changed = null;
                for (int i = 0; i < literals.Length; i++)
                {
                    int literal = literals[i];
                    if (literal < 0)
                        continue;
                    SemanticLiteral current = m_Literals[literal];
                    if (SameLiteral(current, previous.Literals[literal]) &&
                        !(identitiesChanged && (current.Kind is SemanticLiteralKind.String or SemanticLiteralKind.Enum)))
                        continue;
                    changed ??= original.ToArray();
                    changed[i] = m_LoweredValues[literal];
                }
                return changed == null ? original : changed;
            }

            ReadOnlyMemory<ReadOnlyMemory<byte>> BindOperationContents(ParameterBindings<TValue> previous)
            {
                ReadOnlyMemory<ReadOnlyMemory<byte>> original = previous.Content.Parameters.OperationContents;
                ReadOnlySpan<OrchestrationOperationContentBinding> bindings = previous.Content.Structure.ParameterLayout.OperationContents.Span;
                ReadOnlyMemory<byte>[] changed = null;
                for (int i = 0; i < original.Length; i++)
                {
                    int operation = bindings[i].OperationIndex;
                    foreach (SemanticPayloadField field in m_Operations[operation].PayloadFields)
                        if (!SameLiteral(m_Literals[field.LiteralIndex], previous.Literals[field.LiteralIndex]))
                        {
                            changed ??= original.ToArray();
                            changed[i] = BuildOperationContent(operation).Payload;
                            break;
                        }
                }
                return changed == null ? original : changed;
            }

            OrchestrationGraphSourceMap BindSources(ParameterBindings<TValue> previous)
            {
                OrchestrationGraphSourceMap original = previous.Content.Sources;
                if (ReferenceEquals(m_SourceMap, previous.SourceMap))
                    return original;
                OrchestrationGraphSourceText[] changed = null;
                int offset = 0;
                for (int i = 0; i < m_SourceMap.Count; i++)
                {
                    ProgramSourceMapEntry entry = m_SourceMap[i];
                    int count = entry.Source.TargetKind == ProgramSourceTargetKind.Operation
                        ? m_Operations[entry.Source.TargetIndex].ConstantOutputs.Count + 1 : 1;
                    for (int j = 0; j < count; j++, offset++)
                    {
                        OrchestrationGraphSourceText value = original.Entries.Span[offset];
                        if (entry.SourceType == value.SourceType &&
                            entry.Text.DisplayPathSegments.Span.SequenceEqual(value.DisplayPathSegments.Span))
                            continue;
                        changed ??= original.Entries.ToArray();
                        changed[offset] = new OrchestrationGraphSourceText(entry.SourceType, entry.Text.DisplayPathSegments);
                    }
                }
                return changed == null ? original : new OrchestrationGraphSourceMap(changed);
            }

            OrchestrationProgramStructure BuildProgramStructure(OperationDraft[] operations,
                OrchestrationFlowEdge[] edges, OrchestrationCorePorts ports)
            {
                int count = operations.Length;
                int kinds = OrchestrationProgramStructure.FlowKindCount;
                var edgeIndexes = new int[edges.Length];
                var offsets = new int[checked(count * kinds + 1)];
                for (int i = 0; i < edges.Length; i++)
                {
                    edgeIndexes[i] = i;
                    ref readonly OrchestrationFlowEdge edge = ref edges[i];
                    offsets[edge.SourceOperationIndex * kinds + (int)edge.Kind]++;
                    if (operations[edge.SourceOperationIndex].ExecutionFrameOwnerOperationIndex !=
                        operations[edge.TargetOperationIndex].ExecutionFrameOwnerOperationIndex)
                        Error("control_execution_frame_mismatch", ProgramSourceTargetKind.Operation, edge.SourceOperationIndex,
                            $"控制边 '{m_Identities.Resolve(edge.IdentityId)}' 跨越执行帧归属。");
                }
                Array.Sort(edgeIndexes, (leftIndex, rightIndex) =>
                {
                    ref readonly OrchestrationFlowEdge left = ref edges[leftIndex];
                    ref readonly OrchestrationFlowEdge right = ref edges[rightIndex];
                    int source = left.SourceOperationIndex.CompareTo(right.SourceOperationIndex);
                    if (source != 0)
                        return source;
                    return left.Kind == right.Kind ? OrchestrationFlowEdge.CompareOrder(in left, in right)
                        : ((byte)left.Kind).CompareTo((byte)right.Kind);
                });
                for (int i = 1; i < offsets.Length; i++)
                    offsets[i] += offsets[i - 1];

                var initial = EmptyIndexes(count);
                var any = EmptyIndexes(count);
                var onEnter = EmptyIndexes(count);
                var roots = EmptyIndexes(count);
                var onExit = EmptyIndexes(count);
                var evaluationOffsets = new int[count + 1];
                for (int i = 0; i < count; i++)
                {
                    ref readonly OperationDraft operation = ref operations[i];
                    evaluationOffsets[i + 1] = checked(evaluationOffsets[i] + operation.OutputCount + 1);
                    if (operation.Code == OrchestrationOperationCode.Parallel)
                    {
                        int children = OrchestrationProgramStructure.Outgoing(offsets, edgeIndexes, i, OrchestrationFlowKind.Child).Length;
                        if (children > 31)
                            Error("parallel_child_limit", ProgramSourceTargetKind.Operation, i,
                                $"Parallel 有 {children} 个子项，超过完成位图允许的 31 个。");
                    }
                    if (operation.Code == OrchestrationOperationCode.SubGraph)
                    {
                        int entries = OrchestrationProgramStructure.Outgoing(offsets, edgeIndexes, i, OrchestrationFlowKind.Enter).Length;
                        if (entries != 1)
                            Error("graph_call_entry_count", ProgramSourceTargetKind.Operation, i,
                                $"GraphCall 必须有一个 Enter 调用入口，实际为 {entries} 个。");
                    }
                    if (operation.Code == OrchestrationOperationCode.StateMachine)
                    {
                        int initialCount = 0;
                        int anyCount = 0;
                        foreach (int edgeIndex in OrchestrationProgramStructure.Outgoing(offsets, edgeIndexes, i, OrchestrationFlowKind.Enter))
                        {
                            ref readonly OrchestrationFlowEdge edge = ref edges[edgeIndex];
                            OrchestrationOperationCode target = operations[edge.TargetOperationIndex].Code;
                            string port = m_Identities.Resolve(edge.SourcePortId);
                            if (port == OrchestrationCorePortNames.StateMachine && target == OrchestrationOperationCode.StateEnter)
                            {
                                initialCount++;
                                initial[i] = edge.TargetOperationIndex;
                            }
                            else if (edge.SourcePortId == ports.AnyState && target == OrchestrationOperationCode.StateAny)
                            {
                                anyCount++;
                                any[i] = edge.TargetOperationIndex;
                            }
                            else
                                Error("state_machine_entry_invalid", ProgramSourceTargetKind.Operation, i,
                                    $"状态机入口 '{port}' 指向 {target}，必须明确声明 StateMachine／StateEnter 或 AnyState／StateAny。");
                        }
                        if (initialCount != 1)
                            Error("state_machine_initial_entry_count", ProgramSourceTargetKind.Operation, i,
                                $"状态机必须有一个明确的初始入口，实际为 {initialCount} 个。");
                        if (anyCount > 1)
                            Error("state_machine_any_entry_count", ProgramSourceTargetKind.Operation, i,
                                $"状态机最多有一个 AnyState 入口，实际为 {anyCount} 个。");
                    }
                    if (operation.Code != OrchestrationOperationCode.State)
                        continue;
                    if (operations[operation.StateMachineOwnerOperationIndex].ExecutionFrameOwnerOperationIndex !=
                        operation.ExecutionFrameOwnerOperationIndex)
                        Error("state_execution_frame_mismatch", ProgramSourceTargetKind.Operation, i,
                            "State 与所属 StateMachine 跨越执行帧归属。");
                    int first = offsets[i * kinds];
                    int end = offsets[(i + 1) * kinds];
                    for (int j = first; j < end; j++)
                    {
                        int index = edgeIndexes[j];
                        ref readonly OrchestrationFlowEdge edge = ref edges[index];
                        if (edge.Kind == OrchestrationFlowKind.Enter && edge.SourcePortId == ports.StateOnEnter)
                            StateEdge(onEnter, i, index, OrchestrationCorePortNames.StateOnEnter);
                        else if (edge.Kind == OrchestrationFlowKind.Enter && edge.SourcePortId == ports.StateRoot)
                            StateEdge(roots, i, index, OrchestrationCorePortNames.StateRoot);
                        else if (edge.Kind == OrchestrationFlowKind.Exit && edge.SourcePortId == ports.StateOnExit)
                            StateEdge(onExit, i, index, OrchestrationCorePortNames.StateOnExit);
                    }
                }
                for (int i = 0; i < count; i++)
                {
                    ref readonly OperationDraft operation = ref operations[i];
                    var ancestors = new HashSet<int>();
                    for (int parent = operation.ParentStateOperationIndex; parent >= 0;
                         parent = operations[parent].ParentStateOperationIndex)
                        if (!ancestors.Add(parent))
                        {
                            Error("state_parent_cycle", ProgramSourceTargetKind.Operation, i,
                                "状态祖先路径成环。");
                            break;
                        }
                    if (operation.Code == OrchestrationOperationCode.EmitOutput)
                    {
                        if (operation.Inputs.Count != m_OutputTemplates[operation.Integer0].Ports.Length)
                            Error("output_input_count", ProgramSourceTargetKind.Operation, i,
                                "输出节点必须为模板的每个端口提供输入。");
                    }
                }
                if ((operations[m_Entry.Value].Code != OrchestrationOperationCode.Root &&
                     (operations[m_Entry.Value].Code != OrchestrationOperationCode.ConditionResult || m_Contract != OrchestrationExecutionContract.Complete)) ||
                    operations[m_Entry.Value].ExecutionFrameOwnerOperationIndex != m_Entry.Value)
                    Error("program_root_invalid", ProgramSourceTargetKind.Operation, m_Entry.Value,
                        "Graph 入口必须持有自身执行帧，并声明 Root 或同步条件结果。");

                foreach (ProgramGraphCallFrame frame in m_GraphCalls)
                {
                    int owner = frame.OwnerOperation.Value;
                    int frameOwner = operations[owner].ExecutionFrameOwnerOperationIndex;
                    if (operations[frame.EntryOperation.Value].ExecutionFrameOwnerOperationIndex != frameOwner)
                        Error("graph_call_execution_frame_mismatch", ProgramSourceTargetKind.Operation, owner,
                            "GraphCall 与入口跨越执行帧归属。");
                    foreach (ProgramGraphParameterBinding parameter in frame.Inputs)
                        ValidateParameterFrame(owner, parameter, frameOwner);
                    foreach (ProgramGraphParameterBinding parameter in frame.Outputs)
                        ValidateParameterFrame(owner, parameter, frameOwner);
                }
                return new OrchestrationProgramStructure(edgeIndexes, offsets, initial, any, onEnter, roots, onExit,
                    evaluationOffsets, BuildStateOperationIndexes(operations));
            }

            static int[] BuildStateOperationIndexes(OperationDraft[] operations)
            {
                int maximumIdentity = 0;
                for (int i = 0; i < operations.Length; i++)
                    if (operations[i].Code is OrchestrationOperationCode.State or OrchestrationOperationCode.StateExit)
                        maximumIdentity = Math.Max(maximumIdentity, operations[i].StateIdentityId);
                var states = EmptyIndexes(maximumIdentity + 1);
                for (int i = 0; i < operations.Length; i++)
                    if (operations[i].Code is OrchestrationOperationCode.State or OrchestrationOperationCode.StateExit)
                        states[operations[i].StateIdentityId] = i;
                return states;
            }

            void StateEdge(int[] entries, int operation, int edge, string port)
            {
                if (entries[operation] >= 0)
                    Error("state_control_entry_multiple", ProgramSourceTargetKind.Operation, operation,
                        $"State 的 '{port}' 入口重复声明。");
                entries[operation] = edge;
            }

            void ValidateParameterFrame(int operation, ProgramGraphParameterBinding parameter, int frameOwner)
            {
                if (m_Blackboards[ValueSlot(parameter.StateSlot)].ExecutionFrameOwnerOperationIndex != frameOwner)
                    Error("graph_parameter_execution_frame_mismatch", ProgramSourceTargetKind.Operation, operation,
                        $"图参数 '{parameter.ParameterName}' 跨越执行帧归属。");
            }

            void ValidateCompleteEntries(OperationDraft[] operations, OrchestrationFlowEdge[] edges,
                OrchestrationProgramStructure structure, OrchestrationGraphEntry[] entries)
            {
                foreach (OrchestrationGraphEntry entry in entries)
                {
                    if (entry.Execution.Contract != OrchestrationExecutionContract.Complete)
                        continue;
                    string identity = m_Identity.GraphId;
                    foreach (ProgramReference reference in m_References)
                        if (!reference.HasSourceOperation && reference.Kind == ProgramReferenceKind.Operation &&
                            reference.TargetIndex == entry.Execution.OperationIndex)
                        {
                            identity = reference.Identity;
                            break;
                        }
                    Validate(entry.Execution.OperationIndex, entry.Execution.InputPortId, identity);
                }
                for (int operation = 0; operation < operations.Length; operation++)
                {
                    int exit = structure.StateOnExitEdges.Span[operation];
                    if (exit >= 0)
                        Validate(edges[exit].TargetOperationIndex, 0,
                            $"State.OnExit（{SourceLocation(ProgramSourceTargetKind.Operation, operation)}）");
                }

                void Validate(int entry, int inputPort, string identity)
                {
                    var path = new List<int>();
                    var active = new HashSet<(int Operation, int Port)>();
                    var results = new Dictionary<(int Operation, int Port), CompleteResult>();
                    bool reported = false;

                    void Wait(int operation, string reason)
                    {
                        if (reported)
                            return;
                        reported = true;
                        var calls = new List<string>();
                        foreach (int step in path)
                            calls.Add(SourceLocation(ProgramSourceTargetKind.Operation, step));
                        Error("complete_entry_may_wait", ProgramSourceTargetKind.Operation, entry,
                            $"Complete 入口 '{identity}' 不能证明当帧结束：{reason}；等待节点：" +
                            $"{SourceLocation(ProgramSourceTargetKind.Operation, operation)}；调用链：{string.Join(" → ", calls)}。");
                    }

                    CompleteResult Visit(int operation, int port = 0)
                    {
                        var key = (operation, port);
                        if (results.TryGetValue(key, out CompleteResult cached))
                            return cached;
                        path.Add(operation);
                        try
                        {
                            if (!active.Add(key))
                            {
                                Wait(operation, "同步调用成环");
                                return CompleteResult.Running;
                            }
                            try
                            {
                                CompleteResult result = Execute(operation, port);
                                if ((result & CompleteResult.Running) == 0)
                                    results.Add(key, result);
                                return result;
                            }
                            finally
                            {
                                active.Remove(key);
                            }
                        }
                        finally
                        {
                            path.RemoveAt(path.Count - 1);
                        }
                    }

                    CompleteResult Sequence(int operation, int port = -1, bool conditions = false)
                    {
                        CompleteResult result = CompleteResult.Success;
                        foreach (int edge in structure.Outgoing(operation, OrchestrationFlowKind.Child))
                        {
                            if (port >= 0 && edges[edge].SourcePortId != port)
                                continue;
                            if ((result & CompleteResult.Success) == 0)
                                break;
                            CompleteResult child = Visit(edges[edge].TargetOperationIndex, edges[edge].TargetPortId);
                            result = (result & ~CompleteResult.Success) | child |
                                (conditions && edges[edge].HasCondition ? CompleteResult.Failure : 0);
                        }
                        return result;
                    }

                    CompleteResult Single(int operation, OrchestrationFlowKind kind)
                    {
                        foreach (int edge in structure.Outgoing(operation, kind))
                            return Visit(edges[edge].TargetOperationIndex, edges[edge].TargetPortId) |
                                (edges[edge].HasCondition ? CompleteResult.Failure : 0);
                        return CompleteResult.Success;
                    }

                    CompleteResult Execute(int operation, int port)
                    {
                        OperationDraft op = operations[operation];
                        switch (op.Code)
                        {
                            case OrchestrationOperationCode.Root:
                                return Sequence(operation);
                            case OrchestrationOperationCode.Sequence:
                                return Sequence(operation, conditions: true);
                            case OrchestrationOperationCode.StateOnEnter:
                            case OrchestrationOperationCode.StateOnExit:
                            case OrchestrationOperationCode.TimelineEnter:
                                return Single(operation, OrchestrationFlowKind.Child);
                            case OrchestrationOperationCode.SubGraph:
                                return Single(operation, OrchestrationFlowKind.Enter);
                            case OrchestrationOperationCode.Succeed:
                                return CompleteResult.Success;
                            case OrchestrationOperationCode.Loop:
                            {
                                ReadOnlySpan<int> children = structure.Outgoing(operation, OrchestrationFlowKind.Child);
                                if (children.Length != 1)
                                    return CompleteResult.Failure;
                                CompleteResult child = Visit(edges[children[0]].TargetOperationIndex,
                                    edges[children[0]].TargetPortId);
                                CompleteResult terminal = op.Integer0 == 1 ? CompleteResult.Success :
                                    op.Integer0 == 2 ? CompleteResult.Failure : 0;
                                CompleteResult result = child & terminal;
                                if ((child & ~(terminal | CompleteResult.Running)) != 0)
                                {
                                    Wait(operation, "Loop 在主体结束后进入下一轮，返回 Running");
                                    result |= CompleteResult.Running;
                                }
                                return result | (child & CompleteResult.Running) |
                                    (edges[children[0]].HasCondition ? CompleteResult.Failure : 0);
                            }
                            case OrchestrationOperationCode.Selector:
                            {
                                CompleteResult result = CompleteResult.Failure;
                                foreach (int edge in structure.Outgoing(operation, OrchestrationFlowKind.Child))
                                {
                                    if ((result & CompleteResult.Failure) == 0)
                                        break;
                                    result = (result & ~CompleteResult.Failure) |
                                        Visit(edges[edge].TargetOperationIndex, edges[edge].TargetPortId) |
                                        (edges[edge].HasCondition ? CompleteResult.Failure : 0);
                                }
                                return result;
                            }
                            case OrchestrationOperationCode.Parallel:
                            {
                                CompleteResult result = CompleteResult.Success;
                                bool first = true;
                                foreach (int edge in structure.Outgoing(operation, OrchestrationFlowKind.Child))
                                {
                                    CompleteResult child = Visit(edges[edge].TargetOperationIndex, edges[edge].TargetPortId);
                                    result |= child & CompleteResult.Running;
                                    if (first && op.Integer0 == 2)
                                    {
                                        result |= child & CompleteResult.Failure;
                                        if (!edges[edge].HasCondition && (child & CompleteResult.Running) == 0)
                                            return child;
                                    }
                                    first = false;
                                }
                                return result;
                            }
                            case OrchestrationOperationCode.Branch:
                            {
                                CompleteResult result = Sequence(operation, Bind(OrchestrationCorePortNames.BranchTrue)) |
                                    Sequence(operation, Bind(OrchestrationCorePortNames.BranchFalse));
                                return (result & CompleteResult.Success) == 0 ? result :
                                    (result & ~CompleteResult.Success) | Sequence(operation, Bind(OrchestrationCorePortNames.BranchThen));
                            }
                            case OrchestrationOperationCode.Cycle:
                            {
                                if (port == Bind(OrchestrationCorePortNames.CycleReset))
                                    return CompleteResult.Success;
                                CompleteResult result = CompleteResult.Success;
                                foreach (int edge in structure.Outgoing(operation, OrchestrationFlowKind.Child))
                                    result |= Visit(edges[edge].TargetOperationIndex, edges[edge].TargetPortId);
                                return result;
                            }
                            case OrchestrationOperationCode.State:
                            case OrchestrationOperationCode.StateMachine:
                            case OrchestrationOperationCode.Timeline:
                                Wait(operation, $"{op.Code} 的正式执行可能等待状态转移、主体或时间推进");
                                return CompleteResult.Success | CompleteResult.Failure | CompleteResult.Running;
                            case OrchestrationOperationCode.Extension:
                                if (m_Extensions[op.ExtensionBindingIndex].Completion != OrchestrationExtensionCompletion.Immediate)
                                {
                                    Wait(operation, $"扩展 '{m_Extensions[op.ExtensionBindingIndex].Identity}' 声明为 Resumable");
                                    return CompleteResult.Success | CompleteResult.Failure | CompleteResult.Running;
                                }
                                break;
                        }
                        CompleteResult leaf = op.Code is OrchestrationOperationCode.BlackboardSet or OrchestrationOperationCode.EmitOutput
                            ? CompleteResult.Success : CompleteResult.Success | CompleteResult.Failure;
                        return (leaf & ~CompleteResult.Success) | Sequence(operation);
                    }

                    Visit(entry, inputPort);
                }
            }

            [Flags]
            enum CompleteResult : byte { Success = 1, Failure = 2, Running = 4 }
        }
    }
}
