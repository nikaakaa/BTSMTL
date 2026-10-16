using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using BTSMTL.Orchestration;

namespace ThirdPersonSimulation
{
    public enum SemanticNumericPrecision : byte
    {
        Exact = 1,
        TargetRounded = 2
    }

    static class SemanticNumeric
    {
        internal static void RequirePrecision(SemanticNumericPrecision precision)
        {
            if (precision != SemanticNumericPrecision.Exact && precision != SemanticNumericPrecision.TargetRounded)
                throw new ArgumentOutOfRangeException(nameof(precision));
        }

        internal static double RequireFinite(double value, string parameter)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(parameter);
            return value == 0d ? 0d : value;
        }
    }

    public partial struct SemanticNumberValue
    {
        public SemanticNumberValue(double value, SemanticNumericPrecision precision = SemanticNumericPrecision.TargetRounded)
        {
            Value = value;
            Precision = precision;
            RequireRecord();
        }

        public double Value { get; private set; }
        public SemanticNumericPrecision Precision { get; }

        [ValueValidation]
        void RequireRecord()
        {
            SemanticNumeric.RequirePrecision(Precision);
            Value = SemanticNumeric.RequireFinite(Value, nameof(Value));
        }
    }

    public partial struct SemanticVector2Value
    {
        public SemanticVector2Value(double x, double y, SemanticNumericPrecision precision = SemanticNumericPrecision.TargetRounded)
        {
            X = x;
            Y = y;
            Precision = precision;
            RequireRecord();
        }

        public double X { get; private set; }
        public double Y { get; private set; }
        public SemanticNumericPrecision Precision { get; }

        [ValueValidation]
        void RequireRecord()
        {
            SemanticNumeric.RequirePrecision(Precision);
            X = SemanticNumeric.RequireFinite(X, nameof(X));
            Y = SemanticNumeric.RequireFinite(Y, nameof(Y));
        }
    }

    public partial struct SemanticVector3Value
    {
        public SemanticVector3Value(double x, double y, double z, SemanticNumericPrecision precision = SemanticNumericPrecision.TargetRounded)
        {
            X = x;
            Y = y;
            Z = z;
            Precision = precision;
            RequireRecord();
        }

        public double X { get; private set; }
        public double Y { get; private set; }
        public double Z { get; private set; }
        public SemanticNumericPrecision Precision { get; }

        [ValueValidation]
        void RequireRecord()
        {
            SemanticNumeric.RequirePrecision(Precision);
            X = SemanticNumeric.RequireFinite(X, nameof(X));
            Y = SemanticNumeric.RequireFinite(Y, nameof(Y));
            Z = SemanticNumeric.RequireFinite(Z, nameof(Z));
        }
    }

    public partial struct SemanticQuaternionValue
    {
        public SemanticQuaternionValue(double x, double y, double z, double w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
            RequireRecord();
        }

        public double X { get; private set; }
        public double Y { get; private set; }
        public double Z { get; private set; }
        public double W { get; private set; }

        [ValueValidation]
        void RequireRecord()
        {
            X = SemanticNumeric.RequireFinite(X, nameof(X));
            Y = SemanticNumeric.RequireFinite(Y, nameof(Y));
            Z = SemanticNumeric.RequireFinite(Z, nameof(Z));
            W = SemanticNumeric.RequireFinite(W, nameof(W));
        }
    }

    public sealed partial class SemanticLiteral
    {
        public int Index { get; }
        public string Identity { get; private set; }
        [ValueUnionCase] public bool Boolean { get; }
        [ValueUnionCase] public int Int32 { get; }
        [ValueUnionCase] public ulong UInt64 { get; }
        [ValueUnionCase] public SemanticNumberValue Number { get; }
        [ValueUnionCase] public SemanticVector2Value Vector2 { get; }
        [ValueUnionCase] public SemanticVector3Value Vector3 { get; }
        [ValueUnionCase] public SemanticNumberValue Yaw { get; }
        [ValueUnionCase] public string String { get; }
        [ValueUnionCase] public GameplayAbilityCurve<double> Curve { get; }
        [ValueUnionCase] public SemanticQuaternionValue Quaternion { get; }
        [ValueUnionCase] public ProgramEnumConstant Enum { get; }
        [ValueUnionCase] public ReadOnlyMemory<string> Identities { get; }

        [ValueValidation]
        void RequireRecord()
        {
            if (Index < 0)
                throw new ArgumentOutOfRangeException(nameof(Index));
            Identity = SimulationIdentity.Require(Identity, nameof(Identity));
        }
    }

    public readonly partial struct SemanticOperationParameterValues
    {
        public SemanticOperationParameterValues(int integer0, int integer1, ulong unsigned0,
            double number0, string text0, uint flags)
        {
            Integer0 = integer0;
            Integer1 = integer1;
            Unsigned0 = unsigned0;
            Number0 = number0 == 0d ? 0d : number0;
            Text0 = text0 ?? string.Empty;
            Flags = flags;
            RequireRecord();
        }

        public int Integer0 { get; }
        public int Integer1 { get; }
        public ulong Unsigned0 { get; }
        public double Number0 { get; }
        public string Text0 { get; }
        public uint Flags { get; }

        [ValueValidation]
        void RequireRecord()
        {
            if (double.IsNaN(Number0) || double.IsInfinity(Number0))
                throw new ArgumentOutOfRangeException(nameof(Number0));
        }
    }

    public sealed partial class SemanticGraphParameters
    {
        public SemanticGraphParameters(IReadOnlyList<SemanticLiteral> literals,
            IReadOnlyList<SemanticOperationParameterValues> operations)
        {
            Literals = literals;
            Operations = operations;
        }

        public IReadOnlyList<SemanticLiteral> Literals { get; }
        [ValuePayload] public IReadOnlyList<SemanticOperationParameterValues> Operations { get; }
    }

    [ValueStorage]
    public sealed partial class SemanticOperationStateLayout
    {
        readonly ReadOnlyCollection<int> m_StateSlots;

        internal SemanticOperationStateLayout(OperationHandle handle, OrchestrationOperationCode code,
            IEnumerable<int> stateSlots)
        {
            Handle = handle;
            Code = code;
            m_StateSlots = new List<int>(stateSlots ?? Array.Empty<int>()).AsReadOnly();
            RequireRecord();
        }

        public OperationHandle Handle { get; }
        public OrchestrationOperationCode Code { get; }
        public IReadOnlyList<int> StateSlots => m_StateSlots;

        [ValueValidation]
        void RequireRecord()
        {
            if (!Handle.IsValid)
                throw new ArgumentException("Semantic operation handle is invalid.");
        }
    }

    public readonly struct SemanticPayloadField
    {
        public SemanticPayloadField(string name, int literalIndex)
        {
            Name = name;
            LiteralIndex = literalIndex;
        }

        public string Name { get; }
        public int LiteralIndex { get; }
    }

    public readonly struct SemanticHostInputPort
    {
        public SemanticHostInputPort(string outputPortId, string inputId, SemanticValueKind valueKind,
            string valueTypeIdentity)
        {
            OutputPortId = outputPortId;
            InputId = inputId;
            ValueKind = valueKind;
            ValueTypeIdentity = valueTypeIdentity;
        }

        public string OutputPortId { get; }
        public string InputId { get; }
        public SemanticValueKind ValueKind { get; }
        public string ValueTypeIdentity { get; }
    }

    public readonly struct SemanticBlackboardPortBinding
    {
        public SemanticBlackboardPortBinding(string portId, string ownerId, string declarationId,
            SemanticValueKind valueKind, string valueTypeIdentity)
        {
            PortId = portId;
            OwnerId = ownerId;
            DeclarationId = declarationId;
            ValueKind = valueKind;
            ValueTypeIdentity = valueTypeIdentity;
        }

        public string PortId { get; }
        public string OwnerId { get; }
        public string DeclarationId { get; }
        public SemanticValueKind ValueKind { get; }
        public string ValueTypeIdentity { get; }
    }

    public readonly struct SemanticConstantPortDeclaration
    {
        public SemanticConstantPortDeclaration(string portId, string payloadFieldName,
            SemanticValueKind valueKind, string valueTypeIdentity)
        {
            PortId = portId;
            PayloadFieldName = payloadFieldName;
            ValueKind = valueKind;
            ValueTypeIdentity = valueTypeIdentity;
        }

        public string PortId { get; }
        public string PayloadFieldName { get; }
        public SemanticValueKind ValueKind { get; }
        public string ValueTypeIdentity { get; }
    }

    public readonly struct SemanticConstantOutputBinding
    {
        public SemanticConstantOutputBinding(string outputPortId, int literalIndex,
            SemanticValueKind valueKind, string valueTypeIdentity)
        {
            OutputPortId = outputPortId;
            LiteralIndex = literalIndex;
            ValueKind = valueKind;
            ValueTypeIdentity = valueTypeIdentity;
        }

        public string OutputPortId { get; }
        public int LiteralIndex { get; }
        public SemanticValueKind ValueKind { get; }
        public string ValueTypeIdentity { get; }
    }

    public enum SemanticDerivedInputSource : byte
    {
        SelfOutput = 1,
        StateCompletionTransition = 2
    }

    public readonly struct SemanticDerivedInput
    {
        public SemanticDerivedInput(string portId, SemanticDerivedInputSource source,
            int sourceOutputPortIndex, SemanticValueKind valueKind, string valueTypeIdentity)
        {
            PortId = portId;
            Source = source;
            SourceOutputPortIndex = sourceOutputPortIndex;
            ValueKind = valueKind;
            ValueTypeIdentity = valueTypeIdentity;
        }

        public string PortId { get; }
        public SemanticDerivedInputSource Source { get; }
        public int SourceOutputPortIndex { get; }
        public SemanticValueKind ValueKind { get; }
        public string ValueTypeIdentity { get; }
    }

    [ValueStorage]
    public sealed partial class SemanticOperationLayout
    {
        readonly ReadOnlyCollection<int> m_Operands;
        readonly ReadOnlyCollection<int> m_LiteralReferences;
        readonly ReadOnlyCollection<SemanticPayloadField> m_PayloadFields;
        readonly ReadOnlyCollection<SemanticHostInputPort> m_HostInputs;
        readonly ReadOnlyCollection<SemanticBlackboardPortBinding> m_BlackboardInputs;
        readonly ReadOnlyCollection<SemanticBlackboardPortBinding> m_BlackboardOutputs;
        readonly ReadOnlyCollection<SemanticConstantOutputBinding> m_ConstantOutputs;
        readonly ReadOnlyCollection<SemanticDerivedInput> m_DerivedInputs;

        public SemanticOperationLayout(OperationHandle handle, string templateIdentity,
            OrchestrationOperationCode code, IEnumerable<int> operands, IEnumerable<int> literalReferences,
            IEnumerable<int> stateSlots, string number0SourceIdentity, int parameterIndex,
            OrchestrationExtensionReference? extension, IEnumerable<SemanticPayloadField> payloadFields,
            OrchestrationOutputTemplate? output, IEnumerable<SemanticHostInputPort> hostInputs,
            IEnumerable<SemanticBlackboardPortBinding> blackboardInputs,
            IEnumerable<SemanticBlackboardPortBinding> blackboardOutputs,
            IEnumerable<SemanticConstantOutputBinding> constantOutputs, IEnumerable<SemanticDerivedInput> derivedInputs)
        {
            State = new SemanticOperationStateLayout(handle, code, stateSlots);
            TemplateIdentity = templateIdentity.Trim();
            m_Operands = new List<int>(operands ?? Array.Empty<int>()).AsReadOnly();
            m_LiteralReferences = new List<int>(literalReferences ?? Array.Empty<int>()).AsReadOnly();
            Number0SourceIdentity = number0SourceIdentity ?? string.Empty;
            ParameterIndex = parameterIndex;
            Extension = extension;
            m_PayloadFields = new List<SemanticPayloadField>(payloadFields).AsReadOnly();
            Output = output;
            m_HostInputs = new List<SemanticHostInputPort>(hostInputs).AsReadOnly();
            m_BlackboardInputs = new List<SemanticBlackboardPortBinding>(blackboardInputs).AsReadOnly();
            m_BlackboardOutputs = new List<SemanticBlackboardPortBinding>(blackboardOutputs).AsReadOnly();
            m_ConstantOutputs = new List<SemanticConstantOutputBinding>(constantOutputs).AsReadOnly();
            m_DerivedInputs = new List<SemanticDerivedInput>(derivedInputs).AsReadOnly();
            RequireRecord();
        }

        public SemanticOperationStateLayout State { get; }
        public string TemplateIdentity { get; }
        public IReadOnlyList<int> Operands => m_Operands;
        public IReadOnlyList<int> LiteralReferences => m_LiteralReferences;
        public string Number0SourceIdentity { get; }
        public int ParameterIndex { get; }
        public OrchestrationExtensionReference? Extension { get; }
        public IReadOnlyList<SemanticPayloadField> PayloadFields => m_PayloadFields;
        public OrchestrationOutputTemplate? Output { get; }
        public IReadOnlyList<SemanticHostInputPort> HostInputs => m_HostInputs;
        public IReadOnlyList<SemanticBlackboardPortBinding> BlackboardInputs => m_BlackboardInputs;
        public IReadOnlyList<SemanticBlackboardPortBinding> BlackboardOutputs => m_BlackboardOutputs;
        public IReadOnlyList<SemanticConstantOutputBinding> ConstantOutputs => m_ConstantOutputs;
        public IReadOnlyList<SemanticDerivedInput> DerivedInputs => m_DerivedInputs;
        public OperationHandle Handle => State.Handle;
        public OrchestrationOperationCode Code => State.Code;
        public IReadOnlyList<int> StateSlots => State.StateSlots;

        [ValueValidation]
        void RequireRecord() => SimulationIdentity.Require(TemplateIdentity, nameof(TemplateIdentity));
    }

    public sealed partial class SemanticStateTables
    {
        public SemanticStateTables(ReadOnlyCollection<ProgramStateSlot> slots,
            ReadOnlyCollection<ProgramScopeLayout> scopes, ReadOnlyCollection<ProgramGraphCallFrame> graphCallFrames)
        {
            Slots = slots;
            Scopes = scopes;
            GraphCallFrames = graphCallFrames;
        }

        public ReadOnlyCollection<ProgramStateSlot> Slots { get; }
        public ReadOnlyCollection<ProgramScopeLayout> Scopes { get; }
        public ReadOnlyCollection<ProgramGraphCallFrame> GraphCallFrames { get; }
    }

    [ValueStorage]
    public sealed partial class SemanticStateLayout
    {
        public SemanticStateLayout(SemanticStateTables tables, IReadOnlyList<SemanticOperation> operations)
        {
            Tables = tables;
            var values = new SemanticOperationStateLayout[operations.Count];
            for (int i = 0; i < values.Length; i++)
                values[i] = operations[i].Layout.State;
            Operations = Array.AsReadOnly(values);
        }

        public SemanticStateTables Tables { get; }
        public ReadOnlyCollection<SemanticOperationStateLayout> Operations { get; }
        public ReadOnlyCollection<ProgramStateSlot> Slots => Tables.Slots;
        public ReadOnlyCollection<ProgramScopeLayout> Scopes => Tables.Scopes;
        public ReadOnlyCollection<ProgramGraphCallFrame> GraphCallFrames => Tables.GraphCallFrames;
    }

    public sealed class SemanticOperation
    {
        readonly IReadOnlyList<SemanticOperationParameterValues> m_Parameters;

        public SemanticOperation(OperationHandle handle, string templateIdentity, OrchestrationOperationCode code,
            IEnumerable<int> operands, IEnumerable<int> literalReferences, IEnumerable<int> stateSlots,
            string number0SourceIdentity, IReadOnlyList<SemanticOperationParameterValues> parameters, int parameterIndex,
            OrchestrationExtensionReference? extension, IEnumerable<SemanticPayloadField> payloadFields,
            OrchestrationOutputTemplate? output, IEnumerable<SemanticHostInputPort> hostInputs,
            IEnumerable<SemanticBlackboardPortBinding> blackboardInputs,
            IEnumerable<SemanticBlackboardPortBinding> blackboardOutputs,
            IEnumerable<SemanticConstantOutputBinding> constantOutputs, IEnumerable<SemanticDerivedInput> derivedInputs)
            : this(new SemanticOperationLayout(handle, templateIdentity, code, operands, literalReferences,
                stateSlots, number0SourceIdentity, parameterIndex, extension, payloadFields, output, hostInputs,
                blackboardInputs, blackboardOutputs, constantOutputs, derivedInputs), parameters)
        {
        }

        internal SemanticOperation(SemanticOperationLayout layout, IReadOnlyList<SemanticOperationParameterValues> parameters)
        {
            Layout = layout;
            m_Parameters = parameters;
        }

        internal SemanticOperation(SemanticOperation source, IReadOnlyList<SemanticOperationParameterValues> parameters)
            : this(source.Layout, parameters)
        {
        }

        public SemanticOperationLayout Layout { get; }
        public OperationHandle Handle => Layout.Handle;
        public string TemplateIdentity => Layout.TemplateIdentity;
        public OrchestrationOperationCode Code => Layout.Code;
        public OrchestrationExtensionReference? Extension => Layout.Extension;
        public IReadOnlyList<SemanticPayloadField> PayloadFields => Layout.PayloadFields;
        public OrchestrationOutputTemplate? Output => Layout.Output;
        public IReadOnlyList<SemanticHostInputPort> HostInputs => Layout.HostInputs;
        public IReadOnlyList<SemanticBlackboardPortBinding> BlackboardInputs => Layout.BlackboardInputs;
        public IReadOnlyList<SemanticBlackboardPortBinding> BlackboardOutputs => Layout.BlackboardOutputs;
        public IReadOnlyList<SemanticConstantOutputBinding> ConstantOutputs => Layout.ConstantOutputs;
        public IReadOnlyList<SemanticDerivedInput> DerivedInputs => Layout.DerivedInputs;
        public IReadOnlyList<int> Operands => Layout.Operands;
        public IReadOnlyList<int> LiteralReferences => Layout.LiteralReferences;
        public IReadOnlyList<int> StateSlots => Layout.StateSlots;
        public int ParameterIndex => Layout.ParameterIndex;
        internal IReadOnlyList<SemanticOperationParameterValues> ParameterValues => m_Parameters;
        public int Integer0 => m_Parameters[ParameterIndex].Integer0;
        public int Integer1 => m_Parameters[ParameterIndex].Integer1;
        public ulong Unsigned0 => m_Parameters[ParameterIndex].Unsigned0;
        public double Number0 => m_Parameters[ParameterIndex].Number0;
        public string Number0SourceIdentity => Layout.Number0SourceIdentity;
        public string Text0 => m_Parameters[ParameterIndex].Text0;
        public uint Flags => m_Parameters[ParameterIndex].Flags;
    }

    public sealed partial class SemanticConstantInputBinding
    {
        public SemanticConstantInputBinding(
            OperationHandle targetOperation,
            string targetPort,
            int constantIndex,
            SemanticValueKind resolvedValueKind)
        {
            if (!Enum.IsDefined(typeof(SemanticValueKind), resolvedValueKind))
                throw new ArgumentOutOfRangeException(nameof(resolvedValueKind));
            TargetOperation = targetOperation;
            TargetPort = targetPort.Trim();
            ConstantIndex = constantIndex;
            ResolvedValueKind = resolvedValueKind;
            RequireRecord();
        }

        public OperationHandle TargetOperation { get; }
        public string TargetPort { get; }
        public int ConstantIndex { get; }
        public SemanticValueKind ResolvedValueKind { get; }

        [ValueValidation]
        void RequireRecord()
        {
            if (!TargetOperation.IsValid)
                throw new ArgumentException("Target operation is invalid.");
            if (ConstantIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(ConstantIndex));
            SimulationIdentity.Require(TargetPort, nameof(TargetPort));
        }
    }

    public sealed partial class GameplayAbilitySemanticIrContract
    {
        public GameplayAbilitySemanticIrContract(ProgramId programId, string compilerVersion,
            OperationSetVersion operationSetVersion, int tickRate,
            GameplayAbilityRootDescriptor root)
        {
            ProgramId = programId;
            CompilerVersion = SimulationIdentity.Require(compilerVersion, nameof(compilerVersion));
            OperationSetVersion = operationSetVersion;
            TickRate = tickRate;
            Root = root;
            RequireRecord();
        }

        public ProgramId ProgramId { get; }
        public string CompilerVersion { get; }
        public OperationSetVersion OperationSetVersion { get; }
        public int TickRate { get; }
        public GameplayAbilityRootDescriptor Root { get; }

        [ValueValidation]
        void RequireRecord()
        {
            if (!ProgramId.IsValid || !OperationSetVersion.IsValid || TickRate <= 0)
                throw new ArgumentException("Semantic IR contract is incomplete.");
            SimulationIdentity.Require(CompilerVersion, nameof(CompilerVersion));
            if (!Root.IsValid || !Root.IsAbility)
                throw new ArgumentException("Semantic IR requires an Ability root descriptor.", nameof(Root));
        }
    }

    public sealed partial class GameplayAbilitySemanticIrManifest
    {
        public GameplayAbilitySemanticIrManifest(ProgramId programId, string compilerVersion,
            OperationSetVersion operationSetVersion, int tickRate, ProgramRevision sourceRevision,
            GameplayAbilityRootDescriptor root)
        {
            Contract = new GameplayAbilitySemanticIrContract(programId, compilerVersion,
                operationSetVersion, tickRate, root);
            SourceRevision = sourceRevision;
            RequireRecord();
        }

        public GameplayAbilitySemanticIrContract Contract { get; }
        public ProgramRevision SourceRevision { get; }
        public ProgramId ProgramId => Contract.ProgramId;
        public string CompilerVersion => Contract.CompilerVersion;
        public OperationSetVersion OperationSetVersion => Contract.OperationSetVersion;
        public int TickRate => Contract.TickRate;
        public GameplayAbilityRootDescriptor Root => Contract.Root;

        [ValueValidation]
        void RequireRecord()
        {
            if (string.IsNullOrEmpty(SourceRevision.Value))
                throw new ArgumentException("Semantic IR source revision is required.");
        }
    }

    public sealed class GameplayAbilitySemanticIr
    {
        internal GraphExecutionChunks m_Chunks;
        readonly ReadOnlyCollection<SemanticOperation> m_Operations;
        SemanticGraphStructure Structure => Tables.Structure;
        readonly SemanticStateLayout m_StateLayout;
        readonly int[] m_StateSlotByOperation;

        public GameplayAbilitySemanticIr(
            GameplayAbilitySemanticIrManifest manifest,
            IEnumerable<SemanticOperation> operations,
            IEnumerable<SemanticLiteral> literals,
            IEnumerable<SemanticConstantInputBinding> constantInputBindings,
            IEnumerable<ProgramControlFlowEdge> controlFlow,
            IEnumerable<ProgramReference> references,
            IEnumerable<ProgramStateSlot> stateDeclarations,
            IEnumerable<ProgramScopeLayout> scopes,
            IEnumerable<ProgramOutputChannelLayout> outputChannels,
            IEnumerable<ProgramCatalogEntry> catalogEntries,
            ProgramGraphSources graphSources,
            IEnumerable<ProgramProducer> producers,
            IEnumerable<ProgramGraphCallFrame> graphCallFrames = null)
        {
            Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            m_Operations = Indexed(operations, value => value.Handle.Value, "semantic operation");
            var parameters = new SemanticGraphParameters(Indexed(literals, value => value.Index, "semantic literal"),
                m_Operations[0].ParameterValues);
            var callFrames = Indexed(graphCallFrames, value => value.Index, "graph call frame");
            var structure = new SemanticGraphStructure(m_Operations,
                SortConstantInputs(constantInputBindings, m_Operations, callFrames),
                ByIdentity(controlFlow, value => value.Identity, "control-flow edge"),
                ByIdentity(references, value => value.Identity, "reference"),
                new SemanticStateTables(Indexed(stateDeclarations, value => value.Index, "state declaration"),
                    ByIdentity(scopes, value => value.Identity, "scope"), callFrames),
                Indexed(outputChannels, value => value.Index, "output channel"),
                Indexed(catalogEntries, value => value.Index, "catalog entry"),
                graphSources.Layout.Chunk, Indexed(producers, value => value.Index, "producer"));
            Tables = new SemanticGraphTables(structure, parameters);
            m_StateLayout = new SemanticStateLayout(Structure.StateTables, m_Operations);
            GraphSources = graphSources;
            m_StateSlotByOperation = new int[m_Operations.Count];
            Array.Fill(m_StateSlotByOperation, -1);
            ProgramExecutionSourceCoverage.Require(
                ExecutionSources,
                m_Operations.Count,
                m_StateLayout.Slots.Count);
            GameplayAbilityRootValidation.RequireEntryReference(
                Manifest.Root,
                Structure.References,
                m_Operations);
            ValidateReferences();
            ValidateGraphCallFrames();
            StateLayoutHash = GraphSources.Layout.Publisher.ComputeStateLayoutHash(m_StateLayout);
        }

        GameplayAbilitySemanticIr(GameplayAbilitySemanticIr structure, GameplayAbilitySemanticIrManifest manifest,
            SemanticGraphParameters parameters, ProgramGraphSources sources)
        {
            Manifest = manifest;
            Tables = new SemanticGraphTables(structure.Tables.Structure, parameters);
            if (ReferenceEquals(parameters, structure.Parameters))
                m_Operations = structure.m_Operations;
            else
            {
                var operations = new SemanticOperation[structure.Operations.Count];
                for (int i = 0; i < operations.Length; i++)
                    operations[i] = new SemanticOperation(structure.Operations[i], parameters.Operations);
                m_Operations = Array.AsReadOnly(operations);
            }
            m_StateLayout = structure.m_StateLayout;
            m_StateSlotByOperation = structure.m_StateSlotByOperation;
            GraphSources = sources;
            StateLayoutHash = structure.StateLayoutHash;
            m_Chunks = GraphSources.Layout.Publisher.Publish(this, structure.Chunks,
                ReferenceEquals(parameters, structure.Parameters) ? GraphCompilationWork.Sources : GraphCompilationWork.Parameters);
        }

        public GameplayAbilitySemanticIr WithParameters(GameplayAbilitySemanticIrManifest manifest,
            SemanticGraphParameters parameters, ProgramGraphSources sources) =>
            new GameplayAbilitySemanticIr(this, manifest, parameters, sources);

        public GameplayAbilitySemanticIr WithSources(GameplayAbilitySemanticIrManifest manifest,
            ProgramGraphSources sources) => new GameplayAbilitySemanticIr(this, manifest, Parameters, sources);

        public GameplayAbilitySemanticIrManifest Manifest { get; }
        public IReadOnlyList<SemanticOperation> Operations => m_Operations;
        public SemanticGraphParameters Parameters => Tables.Parameters;
        public IReadOnlyList<SemanticLiteral> Literals => Parameters.Literals;
        public IReadOnlyList<SemanticConstantInputBinding> ConstantInputBindings => Structure.ConstantInputBindings;
        public IReadOnlyList<ProgramControlFlowEdge> ControlFlow => Structure.ControlFlow;
        public IReadOnlyList<ProgramReference> References => Structure.References;
        public IReadOnlyList<ProgramGraphCallFrame> GraphCallFrames => m_StateLayout.GraphCallFrames;
        public IReadOnlyList<ProgramStateSlot> StateDeclarations => m_StateLayout.Slots;
        public IReadOnlyList<ProgramScopeLayout> Scopes => m_StateLayout.Scopes;
        public IReadOnlyList<ProgramOutputChannelLayout> OutputChannels => Structure.OutputChannels;
        public IReadOnlyList<ProgramCatalogEntry> CatalogEntries => Structure.CatalogEntries;
        public ProgramGraphSources GraphSources { get; }
        public IReadOnlyList<ProgramSourceMapEntry> SourceMap => GraphSources.Diagnostics;
        public ProgramExecutionSourceLayout ExecutionSourceLayout => GraphSources.Layout;
        public IReadOnlyList<ProgramExecutionSource> ExecutionSources => GraphSources.Layout.Entries;
        public ProgramGraphSourceRevisionTable GraphSourceRevisions => GraphSources.Revisions;
        public IReadOnlyList<ProgramProducer> Producers => Structure.Producers;
        public SemanticHash SemanticHash => new SemanticHash(Chunks.ExecutionHash);
        public GraphExecutionChunks Chunks => m_Chunks ??= GraphSources.Layout.Publisher.Publish(this, null, GraphCompilationWork.Structure);
        public StableHash StateLayoutHash { get; }
        public SemanticStateLayout StateLayout => m_StateLayout;
        public SemanticGraphTables Tables { get; }

        void ValidateGraphCallFrames()
        {
            var operationCodes = new OrchestrationOperationCode[m_Operations.Count];
            for (int i = 0; i < m_StateLayout.GraphCallFrames.Count; i++)
            {
                ProgramGraphCallFrame frame = m_StateLayout.GraphCallFrames[i];
                if (frame.OwnerOperation.Value >= m_Operations.Count || frame.EntryOperation.Value >= m_Operations.Count)
                    throw new InvalidDataException($"Graph call frame '{frame.Identity}' references an operation outside the table.");
                ValidateGraphParameterBindings(frame, frame.Inputs);
                ValidateGraphParameterBindings(frame, frame.Outputs);
            }
            for (int i = 0; i < m_Operations.Count; i++)
                operationCodes[i] = m_Operations[i].Code;
            ProgramGraphCallFrameContract.ValidateCoverage(operationCodes, m_StateLayout.GraphCallFrames);
        }

        void ValidateGraphParameterBindings(
            ProgramGraphCallFrame frame,
            IReadOnlyList<ProgramGraphParameterBinding> bindings)
        {
            for (int i = 0; i < bindings.Count; i++)
            {
                ProgramGraphParameterBinding binding = bindings[i];
                if (binding.StateSlot >= m_StateLayout.Slots.Count)
                    throw new InvalidDataException($"Graph call frame '{frame.Identity}' parameter '{binding.ParameterName}' references an unknown state slot.");
                SemanticValueKind actualKind = GameplayAbilityValuePortContracts.FromState(
                    m_StateLayout.Slots[binding.StateSlot].ValueKind);
                if (actualKind != binding.ValueKind)
                    throw new InvalidDataException($"Graph call frame '{frame.Identity}' parameter '{binding.ParameterName}' state kind '{actualKind}' does not match '{binding.ValueKind}'.");
            }
        }

        public SemanticValueKind ResolveLinkedValueKind(ProgramControlFlowEdge edge)
        {
            if (edge == null || edge.Kind != ProgramControlFlowKind.Value)
                throw new ArgumentException("A Value control-flow edge is required.", nameof(edge));
            if (edge.Source.Value < 0 || edge.Source.Value >= m_Operations.Count ||
                edge.Target.Value < 0 || edge.Target.Value >= m_Operations.Count)
                throw new InvalidDataException($"Value edge '{edge.Identity}' references an operation outside the table.");
            SemanticOperation source = m_Operations[edge.Source.Value];
            OperationValuePortDefinition sourcePort = GameplayAbilityValuePortContracts
                .Require(source, m_StateLayout.GraphCallFrames)
                .RequireSelection(edge.SourcePort);
            return ResolveOutputKind(source, sourcePort);
        }

        void ValidateReferences()
        {
            GameplayAbilityOperationSet.RequireVersion(Manifest.OperationSetVersion);
            for (int i = 0; i < m_Operations.Count; i++)
            {
                SemanticOperation operation = m_Operations[i];
                GameplayAbilityOperationSet.RequireOperation(operation.Code);
                ValidateIndexes(operation.Operands, m_Operations.Count, $"Operation '{operation.Handle}' operand");
                ValidateIndexes(operation.LiteralReferences, Parameters.Literals.Count, $"Operation '{operation.Handle}' literal");
                ValidateIndexes(operation.StateSlots, m_StateLayout.Slots.Count, $"Operation '{operation.Handle}' state");
            }
            for (int i = 0; i < m_StateLayout.Slots.Count; i++)
            {
                int index = m_StateLayout.Slots[i].DefaultConstantIndex;
                if (index < -1 || index >= Parameters.Literals.Count)
                    throw new InvalidDataException($"State declaration '{m_StateLayout.Slots[i].Identity}' has invalid default literal '{index}'.");
            }
            for (int i = 0; i < Structure.References.Count; i++)
            {
                ProgramReference reference = Structure.References[i];
                if (reference.HasSourceOperation &&
                    (reference.SourceOperation.Value < 0 || reference.SourceOperation.Value >= m_Operations.Count))
                {
                    throw new InvalidDataException($"Reference '{reference.Identity}' has an unknown source operation.");
                }
                if (reference.Kind == ProgramReferenceKind.StateSlot && reference.HasSourceOperation &&
                    m_StateSlotByOperation[reference.SourceOperation.Value] < 0)
                    m_StateSlotByOperation[reference.SourceOperation.Value] = reference.TargetIndex;
            }
            var valueSources = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < Structure.ControlFlow.Count; i++)
            {
                ProgramControlFlowEdge edge = Structure.ControlFlow[i];
                if (edge.Source.Value < 0 || edge.Source.Value >= m_Operations.Count ||
                    edge.Target.Value < 0 || edge.Target.Value >= m_Operations.Count)
                    throw new InvalidDataException($"Control-flow edge '{edge.Identity}' references an operation outside the table.");
                SemanticOperation source = m_Operations[edge.Source.Value];
                if (edge.Kind != ProgramControlFlowKind.Value)
                    continue;
                SemanticOperation target = m_Operations[edge.Target.Value];
                OperationValuePortDefinition sourcePort = GameplayAbilityValuePortContracts
                    .Require(source, m_StateLayout.GraphCallFrames)
                    .RequireSelection(edge.SourcePort);
                OperationValuePortDefinition targetPort = GameplayAbilityValuePortContracts
                    .Require(target, m_StateLayout.GraphCallFrames)
                    .RequireInput(edge.TargetPort);
                SemanticValueKind sourceKind = ResolveOutputKind(source, sourcePort);
                SemanticValueKind targetKind = ResolveInputKind(target, targetPort, sourceKind);
                if (!targetPort.Accepts(sourceKind) || !KindsCanFlow(sourceKind, targetKind, targetPort.Constraint))
                    throw new InvalidDataException($"Value edge '{edge.Identity}' cannot assign '{sourceKind}' to '{target.Code}.{targetPort.Identity}' ({targetKind}).");
                string key = InputKey(edge.Target.Value, edge.TargetPort);
                if (!valueSources.Add(key))
                    throw new InvalidDataException($"Operation '{edge.Target}' input port '{edge.TargetPort}' has multiple linked Value sources.");
            }
            for (int i = 0; i < Structure.ConstantInputBindings.Count; i++)
            {
                SemanticConstantInputBinding binding = Structure.ConstantInputBindings[i];
                if (binding.TargetOperation.Value < 0 || binding.TargetOperation.Value >= m_Operations.Count)
                    throw new InvalidDataException($"Constant input binding targets unknown operation '{binding.TargetOperation}'.");
                if (binding.ConstantIndex < 0 || binding.ConstantIndex >= Parameters.Literals.Count)
                    throw new InvalidDataException($"Constant input binding targets unknown literal '{binding.ConstantIndex}'.");
                SemanticOperation target = m_Operations[binding.TargetOperation.Value];
                OperationValuePortDefinition port = GameplayAbilityValuePortContracts
                    .Require(target, m_StateLayout.GraphCallFrames)
                    .RequireInput(binding.TargetPort);
                SemanticValueKind literalKind = GameplayAbilityValuePortContracts.FromLiteral(Parameters.Literals[binding.ConstantIndex].Kind);
                SemanticValueKind expectedKind = ResolveInputKind(target, port, binding.ResolvedValueKind);
                if (binding.ResolvedValueKind != literalKind ||
                    !port.Accepts(binding.ResolvedValueKind) ||
                    !KindsCanFlow(binding.ResolvedValueKind, expectedKind, port.Constraint))
                    throw new InvalidDataException($"Constant input '{target.Code}.{binding.TargetPort}' has incompatible kind '{binding.ResolvedValueKind}'.");
                string key = InputKey(binding.TargetOperation.Value, binding.TargetPort);
                if (!valueSources.Add(key))
                    throw new InvalidDataException($"Operation '{binding.TargetOperation}' input port '{binding.TargetPort}' has both linked and constant Value sources.");
            }
        }

        SemanticValueKind ResolveOutputKind(SemanticOperation operation, OperationValuePortDefinition port)
        {
            if (port.Constraint == OperationValuePortConstraint.Fixed)
                return port.FixedKind;
            if (operation.Code == OrchestrationOperationCode.BlackboardGet)
                return ResolveBlackboardKind(operation);
            if (operation.Code == OrchestrationOperationCode.Constant && operation.LiteralReferences.Count > 0)
                return GameplayAbilityValuePortContracts.FromLiteral(Parameters.Literals[operation.LiteralReferences[0]].Kind);
            throw new InvalidDataException($"Value output '{operation.Code}.{port.Identity}' cannot resolve a concrete kind.");
        }

        SemanticValueKind ResolveInputKind(
            SemanticOperation operation,
            OperationValuePortDefinition port,
            SemanticValueKind actualKind)
        {
            if (port.Constraint == OperationValuePortConstraint.Dynamic)
            {
                if (operation.Code == OrchestrationOperationCode.BlackboardSet)
                    return ResolveBlackboardKind(operation);
                throw new InvalidDataException($"Value input '{operation.Code}.{port.Identity}' cannot resolve a concrete kind.");
            }
            return port.Resolve(actualKind);
        }

        SemanticValueKind ResolveBlackboardKind(SemanticOperation operation)
        {
            int stateSlot = m_StateSlotByOperation[operation.Handle.Value];
            if (stateSlot < 0)
                throw new InvalidDataException($"Blackboard operation '{operation.Handle}' has no compiled state address.");
            return GameplayAbilityValuePortContracts.FromState(m_StateLayout.Slots[stateSlot].ValueKind);
        }

        static bool KindsCanFlow(
            SemanticValueKind source,
            SemanticValueKind target,
            OperationValuePortConstraint constraint)
        {
            if (source == target)
                return true;
            return constraint switch
            {
                OperationValuePortConstraint.BooleanLike => OperationValuePortDefinition.IsBooleanLike(source),
                OperationValuePortConstraint.NumericLike => OperationValuePortDefinition.IsNumericLike(source) && OperationValuePortDefinition.IsNumericLike(target),
                _ => false
            };
        }

        static string InputKey(int operation, string port) => operation.ToString() + ":" + (port ?? string.Empty);

        static void ValidateIndexes(IReadOnlyList<int> values, int count, string label)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] < 0 || values[i] >= count)
                    throw new InvalidDataException($"{label} reference '{values[i]}' is outside the table.");
            }
        }

        static ReadOnlyCollection<T> Indexed<T>(IEnumerable<T> source, Func<T, int> index, string label)
        {
            var values = new List<T>(source ?? Array.Empty<T>());
            values.Sort((left, right) => index(left).CompareTo(index(right)));
            for (int i = 0; i < values.Count; i++)
            {
                if (index(values[i]) != i)
                    throw new InvalidDataException($"{label} table must contain contiguous canonical indexes.");
            }
            return values.AsReadOnly();
        }

        static ReadOnlyCollection<T> ByIdentity<T>(IEnumerable<T> source, Func<T, string> identity, string label)
        {
            var values = new List<T>(source ?? Array.Empty<T>());
            values.Sort((left, right) => string.CompareOrdinal(identity(left), identity(right)));
            for (int i = 1; i < values.Count; i++)
            {
                if (string.Equals(identity(values[i - 1]), identity(values[i]), StringComparison.Ordinal))
                    throw new InvalidDataException($"{label} identity '{identity(values[i])}' is duplicated.");
            }
            return values.AsReadOnly();
        }

        static ReadOnlyCollection<SemanticConstantInputBinding> SortConstantInputs(
            IEnumerable<SemanticConstantInputBinding> source,
            IReadOnlyList<SemanticOperation> operations,
            IReadOnlyList<ProgramGraphCallFrame> graphCallFrames)
        {
            var values = new List<SemanticConstantInputBinding>(source ?? Array.Empty<SemanticConstantInputBinding>());
            values.Sort((left, right) =>
            {
                int byOperation = left.TargetOperation.Value.CompareTo(right.TargetOperation.Value);
                if (byOperation != 0)
                    return byOperation;
                if (left.TargetOperation.Value < 0 || left.TargetOperation.Value >= operations.Count ||
                    right.TargetOperation.Value < 0 || right.TargetOperation.Value >= operations.Count)
                    return string.CompareOrdinal(left.TargetPort, right.TargetPort);
                OperationValuePortDefinition leftPort = GameplayAbilityValuePortContracts
                    .Require(operations[left.TargetOperation.Value].Code, operations[left.TargetOperation.Value].Handle, graphCallFrames)
                    .RequireInput(left.TargetPort);
                OperationValuePortDefinition rightPort = GameplayAbilityValuePortContracts
                    .Require(operations[right.TargetOperation.Value].Code, operations[right.TargetOperation.Value].Handle, graphCallFrames)
                    .RequireInput(right.TargetPort);
                int byOrder = leftPort.Order.CompareTo(rightPort.Order);
                return byOrder != 0 ? byOrder : string.CompareOrdinal(left.TargetPort, right.TargetPort);
            });
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] == null)
                    throw new InvalidDataException("Constant input binding table contains null.");
                if (i > 0 && values[i - 1].TargetOperation.Equals(values[i].TargetOperation) &&
                    string.Equals(values[i - 1].TargetPort, values[i].TargetPort, StringComparison.Ordinal))
                    throw new InvalidDataException($"Constant input binding '{values[i].TargetOperation}/{values[i].TargetPort}' is duplicated.");
            }
            return values.AsReadOnly();
        }

    }
    [ValueStorage]
    public sealed partial class SemanticGraphStructure
    {
        internal SemanticGraphStructure(IReadOnlyList<SemanticOperation> operations,
            ReadOnlyCollection<SemanticConstantInputBinding> constantInputBindings,
            ReadOnlyCollection<ProgramControlFlowEdge> controlFlow, ReadOnlyCollection<ProgramReference> references,
            SemanticStateTables stateTables, ReadOnlyCollection<ProgramOutputChannelLayout> outputChannels,
            ReadOnlyCollection<ProgramCatalogEntry> catalogEntries, GraphExecutionChunk executionSources,
            ReadOnlyCollection<ProgramProducer> producers)
        {
            var layouts = new SemanticOperationLayout[operations.Count];
            for (int i = 0; i < layouts.Length; i++)
                layouts[i] = operations[i].Layout;
            OperationLayouts = Array.AsReadOnly(layouts);
            ConstantInputBindings = constantInputBindings;
            ControlFlow = controlFlow;
            References = references;
            StateTables = stateTables;
            OutputChannels = outputChannels;
            CatalogEntries = catalogEntries;
            ExecutionSources = executionSources;
            Producers = producers;
        }

        public IReadOnlyList<SemanticOperationLayout> OperationLayouts { get; }
        public ReadOnlyCollection<SemanticConstantInputBinding> ConstantInputBindings { get; }
        public ReadOnlyCollection<ProgramControlFlowEdge> ControlFlow { get; }
        public ReadOnlyCollection<ProgramReference> References { get; }
        public SemanticStateTables StateTables { get; }
        public ReadOnlyCollection<ProgramOutputChannelLayout> OutputChannels { get; }
        public ReadOnlyCollection<ProgramCatalogEntry> CatalogEntries { get; }
        public GraphExecutionChunk ExecutionSources { get; }
        public ReadOnlyCollection<ProgramProducer> Producers { get; }
    }

    [ValueStorage]
    public sealed partial class SemanticGraphTables
    {
        internal SemanticGraphTables(SemanticGraphStructure structure, SemanticGraphParameters parameters)
        {
            Structure = structure;
            Parameters = parameters;
        }

        public SemanticGraphStructure Structure { get; }
        public SemanticGraphParameters Parameters { get; }
    }

}
