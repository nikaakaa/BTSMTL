using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

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

    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
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

        [CanonicalValidation]
        void RequireRecord()
        {
            SemanticNumeric.RequirePrecision(Precision);
            Value = SemanticNumeric.RequireFinite(Value, nameof(Value));
        }
    }

    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
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

        [CanonicalValidation]
        void RequireRecord()
        {
            SemanticNumeric.RequirePrecision(Precision);
            X = SemanticNumeric.RequireFinite(X, nameof(X));
            Y = SemanticNumeric.RequireFinite(Y, nameof(Y));
        }
    }

    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
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

        [CanonicalValidation]
        void RequireRecord()
        {
            SemanticNumeric.RequirePrecision(Precision);
            X = SemanticNumeric.RequireFinite(X, nameof(X));
            Y = SemanticNumeric.RequireFinite(Y, nameof(Y));
            Z = SemanticNumeric.RequireFinite(Z, nameof(Z));
        }
    }

    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
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

        [CanonicalValidation]
        void RequireRecord()
        {
            X = SemanticNumeric.RequireFinite(X, nameof(X));
            Y = SemanticNumeric.RequireFinite(Y, nameof(Y));
            Z = SemanticNumeric.RequireFinite(Z, nameof(Z));
            W = SemanticNumeric.RequireFinite(W, nameof(W));
        }
    }

    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
    public readonly partial struct SemanticNumericToken
    {
        public SemanticNumericToken(SemanticNumberValue value, string sourceIdentity)
        {
            Value = value;
            SourceIdentity = SimulationIdentity.Require(sourceIdentity, nameof(sourceIdentity));
        }

        public SemanticNumberValue Value { get; }
        public string SourceIdentity { get; }

        [CanonicalValidation]
        void RequireRecord() => SimulationIdentity.Require(SourceIdentity, nameof(SourceIdentity));
    }

    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
    public partial struct SemanticDataToken
    {
        [CanonicalUnionCase] public bool Boolean { get; }
        [CanonicalUnionCase] public int Int32 { get; }
        [CanonicalUnionCase] public uint UInt32 { get; }
        [CanonicalUnionCase] public ulong UInt64 { get; }
        [CanonicalUnionCase] public string String { get; private set; }
        [CanonicalUnionCase] public SemanticNumericToken Number { get; }
        [CanonicalUnionCase] public ReadOnlyMemory<byte> Bytes { get; }

        [CanonicalValidation]
        void RequireRecord() => String = String ?? string.Empty;
    }

    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
    public sealed partial class SemanticDataDocument
    {
        readonly ReadOnlyCollection<SemanticDataToken> m_Tokens;

        public SemanticDataDocument(IEnumerable<SemanticDataToken> tokens)
        {
            m_Tokens = new List<SemanticDataToken>(tokens ?? Array.Empty<SemanticDataToken>()).AsReadOnly();
        }

        public IReadOnlyList<SemanticDataToken> Tokens => m_Tokens;
        public static SemanticDataDocument Empty { get; } = new SemanticDataDocument(Array.Empty<SemanticDataToken>());
    }

    public sealed class SemanticDataWriter
    {
        readonly List<SemanticDataToken> m_Tokens = new List<SemanticDataToken>();

        public void WriteBoolean(bool value) => m_Tokens.Add(SemanticDataToken.FromBoolean(value));
        public void WriteInt32(int value) => m_Tokens.Add(SemanticDataToken.FromInt32(value));
        public void WriteUInt32(uint value) => m_Tokens.Add(SemanticDataToken.FromUInt32(value));
        public void WriteUInt64(ulong value) => m_Tokens.Add(SemanticDataToken.FromUInt64(value));
        public void WriteString(string value) => m_Tokens.Add(SemanticDataToken.FromString(value));
        public void WriteNumber(double value, string sourceIdentity, SemanticNumericPrecision precision = SemanticNumericPrecision.TargetRounded) => m_Tokens.Add(SemanticDataToken.FromNumber(new SemanticNumericToken(new SemanticNumberValue(value, precision), sourceIdentity)));
        public void WriteBytes(byte[] value) => m_Tokens.Add(SemanticDataToken.FromBytes(value));
        public SemanticDataDocument Build() => new SemanticDataDocument(m_Tokens);
    }

    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
    public sealed partial class SemanticLiteral
    {
        public int Index { get; }
        public string Identity { get; private set; }
        [CanonicalUnionCase] public bool Boolean { get; }
        [CanonicalUnionCase] public int Int32 { get; }
        [CanonicalUnionCase] public ulong UInt64 { get; }
        [CanonicalUnionCase] public SemanticNumberValue Number { get; }
        [CanonicalUnionCase] public SemanticVector2Value Vector2 { get; }
        [CanonicalUnionCase] public SemanticVector3Value Vector3 { get; }
        [CanonicalUnionCase] public SemanticNumberValue Yaw { get; }
        [CanonicalUnionCase] public string String { get; }
        [CanonicalUnionCase] public SemanticDataDocument Document { get; }
        [CanonicalUnionCase] public SemanticQuaternionValue Quaternion { get; }
        [CanonicalUnionCase] public ProgramEnumConstant Enum { get; }

        [CanonicalValidation]
        void RequireRecord()
        {
            if (Index < 0)
                throw new ArgumentOutOfRangeException(nameof(Index));
            Identity = SimulationIdentity.Require(Identity, nameof(Identity));
        }
    }

    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
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

        [CanonicalValidation]
        void RequireRecord()
        {
            if (double.IsNaN(Number0) || double.IsInfinity(Number0))
                throw new ArgumentOutOfRangeException(nameof(Number0));
        }
    }

    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
    public sealed partial class SemanticGraphParameters
    {
        public SemanticGraphParameters(IReadOnlyList<SemanticLiteral> literals,
            IReadOnlyList<SemanticOperationParameterValues> operations)
        {
            Literals = literals;
            Operations = operations;
        }

        public IReadOnlyList<SemanticLiteral> Literals { get; }
        [CanonicalPayload] public IReadOnlyList<SemanticOperationParameterValues> Operations { get; }
    }

    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
    public sealed partial class SemanticOperationStateLayout
    {
        readonly ReadOnlyCollection<int> m_StateSlots;

        internal SemanticOperationStateLayout(OperationHandle handle, SimulationOperationCode code,
            IEnumerable<int> stateSlots)
        {
            Handle = handle;
            Code = code;
            m_StateSlots = new List<int>(stateSlots ?? Array.Empty<int>()).AsReadOnly();
            RequireRecord();
        }

        public OperationHandle Handle { get; }
        public SimulationOperationCode Code { get; }
        public IReadOnlyList<int> StateSlots => m_StateSlots;

        [CanonicalValidation]
        void RequireRecord()
        {
            if (!Handle.IsValid)
                throw new ArgumentException("Semantic operation handle is invalid.");
        }
    }

    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
    public sealed partial class SemanticOperationLayout
    {
        readonly ReadOnlyCollection<int> m_Operands;
        readonly ReadOnlyCollection<int> m_LiteralReferences;

        public SemanticOperationLayout(OperationHandle handle, string templateIdentity,
            SimulationOperationCode code, IEnumerable<int> operands, IEnumerable<int> literalReferences,
            IEnumerable<int> stateSlots, string number0SourceIdentity, int parameterIndex)
        {
            State = new SemanticOperationStateLayout(handle, code, stateSlots);
            TemplateIdentity = templateIdentity.Trim();
            m_Operands = new List<int>(operands ?? Array.Empty<int>()).AsReadOnly();
            m_LiteralReferences = new List<int>(literalReferences ?? Array.Empty<int>()).AsReadOnly();
            Number0SourceIdentity = number0SourceIdentity ?? string.Empty;
            ParameterIndex = parameterIndex;
            RequireRecord();
        }

        public SemanticOperationStateLayout State { get; }
        public string TemplateIdentity { get; }
        public IReadOnlyList<int> Operands => m_Operands;
        public IReadOnlyList<int> LiteralReferences => m_LiteralReferences;
        public string Number0SourceIdentity { get; }
        public int ParameterIndex { get; }
        public OperationHandle Handle => State.Handle;
        public SimulationOperationCode Code => State.Code;
        public IReadOnlyList<int> StateSlots => State.StateSlots;

        [CanonicalValidation]
        void RequireRecord() => SimulationIdentity.Require(TemplateIdentity, nameof(TemplateIdentity));
    }

    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
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

    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
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

        public SemanticOperation(OperationHandle handle, string templateIdentity, SimulationOperationCode code,
            IEnumerable<int> operands, IEnumerable<int> literalReferences, IEnumerable<int> stateSlots,
            string number0SourceIdentity, IReadOnlyList<SemanticOperationParameterValues> parameters, int parameterIndex)
            : this(new SemanticOperationLayout(handle, templateIdentity, code, operands, literalReferences,
                stateSlots, number0SourceIdentity, parameterIndex), parameters)
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
        public SimulationOperationCode Code => Layout.Code;
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

    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
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

        [CanonicalValidation]
        void RequireRecord()
        {
            if (!TargetOperation.IsValid)
                throw new ArgumentException("Target operation is invalid.");
            if (ConstantIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(ConstantIndex));
            SimulationIdentity.Require(TargetPort, nameof(TargetPort));
        }
    }

    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
    public sealed partial class GameplayAbilitySemanticIrContract
    {
        public GameplayAbilitySemanticIrContract(ProgramId programId, string compilerVersion,
            OperationSetVersion operationSetVersion, int tickRate,
            GameplayAbilityCapabilityManifest capabilities, GameplayAbilityRootDescriptor root)
        {
            ProgramId = programId;
            CompilerVersion = SimulationIdentity.Require(compilerVersion, nameof(compilerVersion));
            OperationSetVersion = operationSetVersion;
            TickRate = tickRate;
            Capabilities = capabilities;
            Root = root;
            RequireRecord();
        }

        public ProgramId ProgramId { get; }
        public string CompilerVersion { get; }
        public OperationSetVersion OperationSetVersion { get; }
        public int TickRate { get; }
        public GameplayAbilityCapabilityManifest Capabilities { get; }
        public GameplayAbilityRootDescriptor Root { get; }

        [CanonicalValidation]
        void RequireRecord()
        {
            if (!ProgramId.IsValid || !OperationSetVersion.IsValid || TickRate <= 0)
                throw new ArgumentException("Semantic IR contract is incomplete.");
            SimulationIdentity.Require(CompilerVersion, nameof(CompilerVersion));
            if (Capabilities == null)
                throw new ArgumentNullException(nameof(Capabilities));
            if (!Root.IsValid || !Root.IsAbility)
                throw new ArgumentException("Semantic IR requires an Ability root descriptor.", nameof(Root));
        }
    }

    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
    public sealed partial class GameplayAbilitySemanticIrManifest
    {
        public GameplayAbilitySemanticIrManifest(ProgramId programId, string compilerVersion,
            OperationSetVersion operationSetVersion, int tickRate, ProgramRevision sourceRevision,
            GameplayAbilityCapabilityManifest capabilities, GameplayAbilityRootDescriptor root)
        {
            Contract = new GameplayAbilitySemanticIrContract(programId, compilerVersion,
                operationSetVersion, tickRate, capabilities, root);
            SourceRevision = sourceRevision;
            RequireRecord();
        }

        public GameplayAbilitySemanticIrContract Contract { get; }
        public ProgramRevision SourceRevision { get; }
        public ProgramId ProgramId => Contract.ProgramId;
        public string CompilerVersion => Contract.CompilerVersion;
        public OperationSetVersion OperationSetVersion => Contract.OperationSetVersion;
        public int TickRate => Contract.TickRate;
        public GameplayAbilityCapabilityManifest Capabilities => Contract.Capabilities;
        public GameplayAbilityRootDescriptor Root => Contract.Root;

        [CanonicalValidation]
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
            StateLayoutHash = GameplayAbilitySemanticIrCodec.ComputeStateLayoutHash(this);
        }

        internal GameplayAbilitySemanticIr(GameplayAbilitySemanticIrManifest manifest,
            SemanticGraphTables tables, ProgramGraphSources sources)
        {
            Manifest = manifest;
            Tables = tables;
            var operations = new SemanticOperation[Structure.OperationLayouts.Count];
            for (int i = 0; i < operations.Length; i++)
                operations[i] = new SemanticOperation(Structure.OperationLayouts[i], Parameters.Operations);
            m_Operations = Array.AsReadOnly(operations);
            m_StateLayout = new SemanticStateLayout(Structure.StateTables, m_Operations);
            GraphSources = sources;
            m_StateSlotByOperation = new int[m_Operations.Count];
            Array.Fill(m_StateSlotByOperation, -1);
            ProgramExecutionSourceCoverage.Require(ExecutionSources, m_Operations.Count, m_StateLayout.Slots.Count);
            GameplayAbilityRootValidation.RequireEntryReference(Manifest.Root, Structure.References, m_Operations);
            ValidateReferences();
            ValidateGraphCallFrames();
            StateLayoutHash = GameplayAbilitySemanticIrCodec.ComputeStateLayoutHash(this);
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
            GraphExecutionChunk values = ReferenceEquals(parameters, structure.Parameters)
                ? structure.Chunks.Parameters
                : GraphExecutionChunk.Encode(writer =>
                    GameplayAbilitySemanticIrCodec.WriteParameters(writer, this, structure.Chunks.Structure.Hash));
            m_Chunks = new GraphExecutionChunks(structure.Chunks.Structure, values, sources.DiagnosticChunk);
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
        public GraphExecutionChunks Chunks => m_Chunks ??= GameplayAbilitySemanticIrCodec.CreateChunks(this);
        public StableHash StateLayoutHash { get; }
        internal SemanticStateLayout StateLayout => m_StateLayout;
        internal SemanticGraphTables Tables { get; }

        void ValidateGraphCallFrames()
        {
            var operationCodes = new SimulationOperationCode[m_Operations.Count];
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
                .Require(source.Code, source.Handle, m_StateLayout.GraphCallFrames)
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
                CameraProgramOperationSchema.Validate(operation, Parameters.Literals);
                VfxProgramOperationSchema.Validate(operation, Parameters.Literals);
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
                    .Require(source.Code, source.Handle, m_StateLayout.GraphCallFrames)
                    .RequireSelection(edge.SourcePort);
                OperationValuePortDefinition targetPort = GameplayAbilityValuePortContracts
                    .Require(target.Code, target.Handle, m_StateLayout.GraphCallFrames)
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
                    .Require(target.Code, target.Handle, m_StateLayout.GraphCallFrames)
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
            if (operation.Code == SimulationOperationCode.BlackboardGet)
                return ResolveBlackboardKind(operation);
            if (operation.Code == SimulationOperationCode.Constant && operation.LiteralReferences.Count > 0)
                return GameplayAbilityValuePortContracts.FromLiteral(Parameters.Literals[operation.LiteralReferences[0]].Kind);
            if (operation.Code == SimulationOperationCode.ReadEquipmentParameter)
            {
                return (EquipmentParameterValueKind)operation.Integer0 switch
                {
                    EquipmentParameterValueKind.Boolean => SemanticValueKind.Boolean,
                    EquipmentParameterValueKind.Int32 => SemanticValueKind.Int32,
                    EquipmentParameterValueKind.Scalar => SemanticValueKind.Number,
                    EquipmentParameterValueKind.Vector2 => SemanticValueKind.Vector2,
                    EquipmentParameterValueKind.Vector3 => SemanticValueKind.Vector3,
                    EquipmentParameterValueKind.Yaw => SemanticValueKind.Yaw,
                    EquipmentParameterValueKind.GameplayTag => SemanticValueKind.Identity,
                    EquipmentParameterValueKind.GameplayEffect => SemanticValueKind.Identity,
                    EquipmentParameterValueKind.AnimationProducer => SemanticValueKind.Identity,
                    _ => throw new InvalidDataException($"Equipment Parameter operation '{operation.Handle}' has an invalid value kind.")
                };
            }
            throw new InvalidDataException($"Value output '{operation.Code}.{port.Identity}' cannot resolve a concrete kind.");
        }

        SemanticValueKind ResolveInputKind(
            SemanticOperation operation,
            OperationValuePortDefinition port,
            SemanticValueKind actualKind)
        {
            if (port.Constraint == OperationValuePortConstraint.Dynamic)
            {
                if (operation.Code == SimulationOperationCode.BlackboardSet)
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
    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
    internal sealed partial class SemanticGraphStructure
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

    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
    internal sealed partial class SemanticGraphTables
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
