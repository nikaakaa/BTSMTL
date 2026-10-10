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

    public enum SemanticLiteralKind : byte
    {
        Boolean = 1,
        Int32 = 2,
        UInt64 = 3,
        Number = 4,
        Vector2 = 5,
        Vector3 = 6,
        Yaw = 7,
        String = 8,
        Document = 9,
        Quaternion = 10,
        Enum = 11
    }

    public enum SemanticDataTokenKind : byte
    {
        Boolean = 1,
        Int32 = 2,
        UInt32 = 3,
        UInt64 = 4,
        String = 5,
        Number = 6,
        Bytes = 7
    }

    public readonly struct SemanticDataToken
    {
        readonly byte[] m_Bytes;

        SemanticDataToken(
            SemanticDataTokenKind kind,
            bool boolean,
            int int32,
            uint uint32,
            ulong uint64,
            string text,
            double number,
            byte[] bytes,
            string sourceIdentity,
            SemanticNumericPrecision precision)
        {
            Kind = kind;
            Boolean = boolean;
            Int32 = int32;
            UInt32 = uint32;
            UInt64 = uint64;
            Text = text ?? string.Empty;
            Number = RequireFinite(number, nameof(number));
            m_Bytes = bytes == null ? Array.Empty<byte>() : (byte[])bytes.Clone();
            SourceIdentity = sourceIdentity ?? string.Empty;
            Precision = precision;
            if (kind == SemanticDataTokenKind.Number && SourceIdentity.Length == 0)
                throw new ArgumentException("Semantic numeric token requires a source identity.", nameof(sourceIdentity));
        }

        public SemanticDataTokenKind Kind { get; }
        public bool Boolean { get; }
        public int Int32 { get; }
        public uint UInt32 { get; }
        public ulong UInt64 { get; }
        public string Text { get; }
        public double Number { get; }
        public ReadOnlyMemory<byte> Bytes => m_Bytes ?? Array.Empty<byte>();
        public string SourceIdentity { get; }
        public SemanticNumericPrecision Precision { get; }

        public static SemanticDataToken FromBoolean(bool value) => new SemanticDataToken(SemanticDataTokenKind.Boolean, value, 0, 0, 0, null, 0, null, null, SemanticNumericPrecision.Exact);
        public static SemanticDataToken FromInt32(int value) => new SemanticDataToken(SemanticDataTokenKind.Int32, false, value, 0, 0, null, 0, null, null, SemanticNumericPrecision.Exact);
        public static SemanticDataToken FromUInt32(uint value) => new SemanticDataToken(SemanticDataTokenKind.UInt32, false, 0, value, 0, null, 0, null, null, SemanticNumericPrecision.Exact);
        public static SemanticDataToken FromUInt64(ulong value) => new SemanticDataToken(SemanticDataTokenKind.UInt64, false, 0, 0, value, null, 0, null, null, SemanticNumericPrecision.Exact);
        public static SemanticDataToken FromString(string value) => new SemanticDataToken(SemanticDataTokenKind.String, false, 0, 0, 0, value, 0, null, null, SemanticNumericPrecision.Exact);
        public static SemanticDataToken FromNumber(double value, string sourceIdentity, SemanticNumericPrecision precision = SemanticNumericPrecision.TargetRounded) => new SemanticDataToken(SemanticDataTokenKind.Number, false, 0, 0, 0, null, value, null, SimulationIdentity.Require(sourceIdentity, nameof(sourceIdentity)), precision);
        public static SemanticDataToken FromBytes(byte[] value) => new SemanticDataToken(SemanticDataTokenKind.Bytes, false, 0, 0, 0, null, 0, value, null, SemanticNumericPrecision.Exact);

        static double RequireFinite(double value, string parameter)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(parameter);
            return value == 0d ? 0d : value;
        }
    }

    public sealed class SemanticDataDocument
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
        public void WriteNumber(double value, string sourceIdentity, SemanticNumericPrecision precision = SemanticNumericPrecision.TargetRounded) => m_Tokens.Add(SemanticDataToken.FromNumber(value, sourceIdentity, precision));
        public void WriteBytes(byte[] value) => m_Tokens.Add(SemanticDataToken.FromBytes(value));
        public SemanticDataDocument Build() => new SemanticDataDocument(m_Tokens);
    }

    public sealed class SemanticLiteral
    {
        SemanticLiteral(
            int index,
            string identity,
            SemanticLiteralKind kind,
            bool boolean,
            int int32,
            ulong uint64,
            double x,
            double y,
            double z,
            string text,
            SemanticDataDocument document,
            SemanticNumericPrecision precision,
            double w = 0)
        {
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(index));
            Index = index;
            Identity = SimulationIdentity.Require(identity, nameof(identity));
            Kind = kind;
            Boolean = boolean;
            Int32 = int32;
            UInt64 = uint64;
            X = RequireFinite(x, nameof(x));
            Y = RequireFinite(y, nameof(y));
            Z = RequireFinite(z, nameof(z));
            W = RequireFinite(w, nameof(w));
            Text = kind == SemanticLiteralKind.String ? text : text ?? string.Empty;
            Document = document;
            Precision = precision;
            if (kind == SemanticLiteralKind.Document && document == null)
                throw new ArgumentNullException(nameof(document));
        }

        public int Index { get; }
        public string Identity { get; }
        public SemanticLiteralKind Kind { get; }
        public bool Boolean { get; }
        public int Int32 { get; }
        public ulong UInt64 { get; }
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public double W { get; }
        public string Text { get; }
        public SemanticDataDocument Document { get; }
        public SemanticNumericPrecision Precision { get; }

        public static SemanticLiteral FromBoolean(int index, string identity, bool value) => new SemanticLiteral(index, identity, SemanticLiteralKind.Boolean, value, 0, 0, 0, 0, 0, null, null, SemanticNumericPrecision.Exact);
        public static SemanticLiteral FromInt32(int index, string identity, int value) => new SemanticLiteral(index, identity, SemanticLiteralKind.Int32, false, value, 0, 0, 0, 0, null, null, SemanticNumericPrecision.Exact);
        public static SemanticLiteral FromUInt64(int index, string identity, ulong value) => new SemanticLiteral(index, identity, SemanticLiteralKind.UInt64, false, 0, value, 0, 0, 0, null, null, SemanticNumericPrecision.Exact);
        public static SemanticLiteral FromNumber(int index, string identity, double value, SemanticNumericPrecision precision = SemanticNumericPrecision.TargetRounded) => new SemanticLiteral(index, identity, SemanticLiteralKind.Number, false, 0, 0, value, 0, 0, null, null, precision);
        public static SemanticLiteral FromVector2(int index, string identity, double x, double y, SemanticNumericPrecision precision = SemanticNumericPrecision.TargetRounded) => new SemanticLiteral(index, identity, SemanticLiteralKind.Vector2, false, 0, 0, x, y, 0, null, null, precision);
        public static SemanticLiteral FromVector3(int index, string identity, double x, double y, double z, SemanticNumericPrecision precision = SemanticNumericPrecision.TargetRounded) => new SemanticLiteral(index, identity, SemanticLiteralKind.Vector3, false, 0, 0, x, y, z, null, null, precision);
        public static SemanticLiteral FromYaw(int index, string identity, double value, SemanticNumericPrecision precision = SemanticNumericPrecision.TargetRounded) => new SemanticLiteral(index, identity, SemanticLiteralKind.Yaw, false, 0, 0, value, 0, 0, null, null, precision);
        public static SemanticLiteral FromString(int index, string identity, string value) => new SemanticLiteral(index, identity, SemanticLiteralKind.String, false, 0, 0, 0, 0, 0, value, null, SemanticNumericPrecision.Exact);
        public static SemanticLiteral FromDocument(int index, string identity, SemanticDataDocument value) => new SemanticLiteral(index, identity, SemanticLiteralKind.Document, false, 0, 0, 0, 0, 0, null, value, SemanticNumericPrecision.Exact);
        public static SemanticLiteral FromQuaternion(int index, string identity, double x, double y, double z, double w) =>
            new SemanticLiteral(index, identity, SemanticLiteralKind.Quaternion, false, 0, 0, x, y, z, null, null, SemanticNumericPrecision.TargetRounded, w);
        public static SemanticLiteral FromEnum(int index, string identity, string typeIdentity, int value) =>
            new SemanticLiteral(index, identity, SemanticLiteralKind.Enum, false, value, 0, 0, 0, 0, typeIdentity, null, SemanticNumericPrecision.Exact);

        static double RequireFinite(double value, string parameter)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(parameter);
            return value == 0d ? 0d : value;
        }
    }

    public sealed class SemanticOperation
    {
        readonly ReadOnlyCollection<int> m_Operands;
        readonly ReadOnlyCollection<int> m_LiteralReferences;
        readonly ReadOnlyCollection<int> m_StateSlots;

        public SemanticOperation(
            OperationHandle handle,
            string templateIdentity,
            SimulationOperationCode code,
            IEnumerable<int> operands,
            IEnumerable<int> literalReferences,
            IEnumerable<int> stateSlots,
            int integer0,
            int integer1,
            ulong unsigned0,
            double number0,
            string number0SourceIdentity,
            string text0,
            uint flags)
        {
            if (!handle.IsValid)
                throw new ArgumentException("Semantic operation handle is invalid.", nameof(handle));
            if (double.IsNaN(number0) || double.IsInfinity(number0))
                throw new ArgumentOutOfRangeException(nameof(number0));
            Handle = handle;
            TemplateIdentity = SimulationIdentity.Require(templateIdentity, nameof(templateIdentity));
            Code = code;
            m_Operands = ReadOnly(operands);
            m_LiteralReferences = ReadOnly(literalReferences);
            m_StateSlots = ReadOnly(stateSlots);
            Integer0 = integer0;
            Integer1 = integer1;
            Unsigned0 = unsigned0;
            Number0 = number0 == 0d ? 0d : number0;
            Number0SourceIdentity = number0SourceIdentity ?? string.Empty;
            Text0 = text0 ?? string.Empty;
            Flags = flags;
        }

        public OperationHandle Handle { get; }
        public string TemplateIdentity { get; }
        public SimulationOperationCode Code { get; }
        public IReadOnlyList<int> Operands => m_Operands;
        public IReadOnlyList<int> LiteralReferences => m_LiteralReferences;
        public IReadOnlyList<int> StateSlots => m_StateSlots;
        public int Integer0 { get; }
        public int Integer1 { get; }
        public ulong Unsigned0 { get; }
        public double Number0 { get; }
        public string Number0SourceIdentity { get; }
        public string Text0 { get; }
        public uint Flags { get; }

        static ReadOnlyCollection<int> ReadOnly(IEnumerable<int> source) => new List<int>(source ?? Array.Empty<int>()).AsReadOnly();
    }

    public sealed class SemanticConstantInputBinding
    {
        public SemanticConstantInputBinding(
            OperationHandle targetOperation,
            string targetPort,
            int constantIndex,
            SemanticValueKind resolvedValueKind)
        {
            if (!targetOperation.IsValid)
                throw new ArgumentException("Target operation is invalid.", nameof(targetOperation));
            if (constantIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(constantIndex));
            if (!Enum.IsDefined(typeof(SemanticValueKind), resolvedValueKind))
                throw new ArgumentOutOfRangeException(nameof(resolvedValueKind));
            TargetOperation = targetOperation;
            TargetPort = SimulationIdentity.Require(targetPort, nameof(targetPort));
            ConstantIndex = constantIndex;
            ResolvedValueKind = resolvedValueKind;
        }

        public OperationHandle TargetOperation { get; }
        public string TargetPort { get; }
        public int ConstantIndex { get; }
        public SemanticValueKind ResolvedValueKind { get; }
    }

    public sealed class GameplayAbilitySemanticIrManifest
    {
        public GameplayAbilitySemanticIrManifest(
            ProgramId programId,
            string compilerVersion,
            OperationSetVersion operationSetVersion,
            int tickRate,
            ProgramRevision sourceRevision,
            GameplayAbilityCapabilityManifest capabilities,
            GameplayAbilityRootDescriptor root)
        {
            if (!programId.IsValid || !operationSetVersion.IsValid || tickRate <= 0 || string.IsNullOrEmpty(sourceRevision.Value))
                throw new ArgumentException("Semantic IR manifest is incomplete.");
            ProgramId = programId;
            CompilerVersion = SimulationIdentity.Require(compilerVersion, nameof(compilerVersion));
            OperationSetVersion = operationSetVersion;
            TickRate = tickRate;
            SourceRevision = sourceRevision;
            Capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
            if (!root.IsValid || !root.IsAbility)
                throw new ArgumentException("Semantic IR requires an Ability root descriptor.", nameof(root));
            Root = root;
        }

        public ProgramId ProgramId { get; }
        public string CompilerVersion { get; }
        public OperationSetVersion OperationSetVersion { get; }
        public int TickRate { get; }
        public ProgramRevision SourceRevision { get; }
        public GameplayAbilityCapabilityManifest Capabilities { get; }
        public GameplayAbilityRootDescriptor Root { get; }
    }

    public sealed class GameplayAbilitySemanticIr
    {
        readonly ReadOnlyCollection<SemanticOperation> m_Operations;
        readonly ReadOnlyCollection<SemanticLiteral> m_Literals;
        readonly ReadOnlyCollection<SemanticConstantInputBinding> m_ConstantInputBindings;
        readonly ReadOnlyCollection<ProgramControlFlowEdge> m_ControlFlow;
        readonly ReadOnlyCollection<ProgramReference> m_References;
        readonly ReadOnlyCollection<ProgramGraphCallFrame> m_GraphCallFrames;
        readonly ReadOnlyCollection<ProgramStateSlot> m_StateDeclarations;
        readonly ReadOnlyCollection<ProgramScopeLayout> m_Scopes;
        readonly ReadOnlyCollection<ProgramOutputChannelLayout> m_OutputChannels;
        readonly ReadOnlyCollection<ProgramCatalogEntry> m_CatalogEntries;
        readonly ReadOnlyCollection<ProgramSourceMapEntry> m_SourceMap;
        readonly ReadOnlyCollection<ProgramProducer> m_Producers;
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
            IEnumerable<ProgramSourceMapEntry> sourceMap,
            IEnumerable<ProgramProducer> producers,
            IEnumerable<ProgramGraphCallFrame> graphCallFrames = null)
        {
            Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            m_Operations = Indexed(operations, value => value.Handle.Value, "semantic operation");
            m_Literals = Indexed(literals, value => value.Index, "semantic literal");
            m_GraphCallFrames = Indexed(graphCallFrames, value => value.Index, "graph call frame");
            m_ConstantInputBindings = SortConstantInputs(constantInputBindings, m_Operations, m_GraphCallFrames);
            m_ControlFlow = ByIdentity(controlFlow, value => value.Identity, "control-flow edge");
            m_References = ByIdentity(references, value => value.Identity, "reference");
            m_StateDeclarations = Indexed(stateDeclarations, value => value.Index, "state declaration");
            m_Scopes = ByIdentity(scopes, value => value.Identity, "scope");
            m_OutputChannels = Indexed(outputChannels, value => value.Index, "output channel");
            m_CatalogEntries = Indexed(catalogEntries, value => value.Index, "catalog entry");
            m_SourceMap = SortSourceMap(sourceMap);
            m_Producers = Indexed(producers, value => value.Index, "producer");
            m_StateSlotByOperation = new int[m_Operations.Count];
            Array.Fill(m_StateSlotByOperation, -1);
            ProgramSourceMapCoverage.Require(
                m_SourceMap,
                m_Operations.Count,
                m_StateDeclarations.Count);
            GameplayAbilityRootValidation.RequireEntryReference(
                Manifest.Root,
                m_References,
                m_Operations);
            ValidateReferences();
            ValidateGraphCallFrames();
            SemanticHash = GameplayAbilitySemanticIrCodec.ComputeHash(this);
        }

        public GameplayAbilitySemanticIrManifest Manifest { get; }
        public IReadOnlyList<SemanticOperation> Operations => m_Operations;
        public IReadOnlyList<SemanticLiteral> Literals => m_Literals;
        public IReadOnlyList<SemanticConstantInputBinding> ConstantInputBindings => m_ConstantInputBindings;
        public IReadOnlyList<ProgramControlFlowEdge> ControlFlow => m_ControlFlow;
        public IReadOnlyList<ProgramReference> References => m_References;
        public IReadOnlyList<ProgramGraphCallFrame> GraphCallFrames => m_GraphCallFrames;
        public IReadOnlyList<ProgramStateSlot> StateDeclarations => m_StateDeclarations;
        public IReadOnlyList<ProgramScopeLayout> Scopes => m_Scopes;
        public IReadOnlyList<ProgramOutputChannelLayout> OutputChannels => m_OutputChannels;
        public IReadOnlyList<ProgramCatalogEntry> CatalogEntries => m_CatalogEntries;
        public IReadOnlyList<ProgramSourceMapEntry> SourceMap => m_SourceMap;
        public IReadOnlyList<ProgramProducer> Producers => m_Producers;
        public SemanticHash SemanticHash { get; }

        void ValidateGraphCallFrames()
        {
            var operationCodes = new SimulationOperationCode[m_Operations.Count];
            for (int i = 0; i < m_GraphCallFrames.Count; i++)
            {
                ProgramGraphCallFrame frame = m_GraphCallFrames[i];
                if (frame.OwnerOperation.Value >= m_Operations.Count || frame.EntryOperation.Value >= m_Operations.Count)
                    throw new InvalidDataException($"Graph call frame '{frame.Identity}' references an operation outside the table.");
                ValidateGraphParameterBindings(frame, frame.Inputs);
                ValidateGraphParameterBindings(frame, frame.Outputs);
            }
            for (int i = 0; i < m_Operations.Count; i++)
                operationCodes[i] = m_Operations[i].Code;
            ProgramGraphCallFrameContract.ValidateCoverage(operationCodes, m_GraphCallFrames);
        }

        void ValidateGraphParameterBindings(
            ProgramGraphCallFrame frame,
            IReadOnlyList<ProgramGraphParameterBinding> bindings)
        {
            for (int i = 0; i < bindings.Count; i++)
            {
                ProgramGraphParameterBinding binding = bindings[i];
                if (binding.StateSlot >= m_StateDeclarations.Count)
                    throw new InvalidDataException($"Graph call frame '{frame.Identity}' parameter '{binding.ParameterName}' references an unknown state slot.");
                SemanticValueKind actualKind = GameplayAbilityValuePortContracts.FromState(
                    m_StateDeclarations[binding.StateSlot].ValueKind);
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
                .Require(source.Code, source.Handle, m_GraphCallFrames)
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
                ValidateIndexes(operation.LiteralReferences, m_Literals.Count, $"Operation '{operation.Handle}' literal");
                ValidateIndexes(operation.StateSlots, m_StateDeclarations.Count, $"Operation '{operation.Handle}' state");
                CameraProgramOperationSchema.Validate(operation, m_Literals);
                VfxProgramOperationSchema.Validate(operation, m_Literals);
            }
            for (int i = 0; i < m_StateDeclarations.Count; i++)
            {
                int index = m_StateDeclarations[i].DefaultConstantIndex;
                if (index < -1 || index >= m_Literals.Count)
                    throw new InvalidDataException($"State declaration '{m_StateDeclarations[i].Identity}' has invalid default literal '{index}'.");
            }
            for (int i = 0; i < m_References.Count; i++)
            {
                ProgramReference reference = m_References[i];
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
            for (int i = 0; i < m_ControlFlow.Count; i++)
            {
                ProgramControlFlowEdge edge = m_ControlFlow[i];
                if (edge.Source.Value < 0 || edge.Source.Value >= m_Operations.Count ||
                    edge.Target.Value < 0 || edge.Target.Value >= m_Operations.Count)
                    throw new InvalidDataException($"Control-flow edge '{edge.Identity}' references an operation outside the table.");
                SemanticOperation source = m_Operations[edge.Source.Value];
                if (edge.Kind != ProgramControlFlowKind.Value)
                    continue;
                SemanticOperation target = m_Operations[edge.Target.Value];
                OperationValuePortDefinition sourcePort = GameplayAbilityValuePortContracts
                    .Require(source.Code, source.Handle, m_GraphCallFrames)
                    .RequireSelection(edge.SourcePort);
                OperationValuePortDefinition targetPort = GameplayAbilityValuePortContracts
                    .Require(target.Code, target.Handle, m_GraphCallFrames)
                    .RequireInput(edge.TargetPort);
                SemanticValueKind sourceKind = ResolveOutputKind(source, sourcePort);
                SemanticValueKind targetKind = ResolveInputKind(target, targetPort, sourceKind);
                if (!targetPort.Accepts(sourceKind) || !KindsCanFlow(sourceKind, targetKind, targetPort.Constraint))
                    throw new InvalidDataException($"Value edge '{edge.Identity}' cannot assign '{sourceKind}' to '{target.Code}.{targetPort.Identity}' ({targetKind}).");
                string key = InputKey(edge.Target.Value, edge.TargetPort);
                if (!valueSources.Add(key))
                    throw new InvalidDataException($"Operation '{edge.Target}' input port '{edge.TargetPort}' has multiple linked Value sources.");
            }
            for (int i = 0; i < m_ConstantInputBindings.Count; i++)
            {
                SemanticConstantInputBinding binding = m_ConstantInputBindings[i];
                if (binding.TargetOperation.Value < 0 || binding.TargetOperation.Value >= m_Operations.Count)
                    throw new InvalidDataException($"Constant input binding targets unknown operation '{binding.TargetOperation}'.");
                if (binding.ConstantIndex < 0 || binding.ConstantIndex >= m_Literals.Count)
                    throw new InvalidDataException($"Constant input binding targets unknown literal '{binding.ConstantIndex}'.");
                SemanticOperation target = m_Operations[binding.TargetOperation.Value];
                OperationValuePortDefinition port = GameplayAbilityValuePortContracts
                    .Require(target.Code, target.Handle, m_GraphCallFrames)
                    .RequireInput(binding.TargetPort);
                SemanticValueKind literalKind = GameplayAbilityValuePortContracts.FromLiteral(m_Literals[binding.ConstantIndex].Kind);
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
                return GameplayAbilityValuePortContracts.FromLiteral(m_Literals[operation.LiteralReferences[0]].Kind);
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
            return GameplayAbilityValuePortContracts.FromState(m_StateDeclarations[stateSlot].ValueKind);
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

        static ReadOnlyCollection<ProgramSourceMapEntry> SortSourceMap(IEnumerable<ProgramSourceMapEntry> source)
        {
            var values = new List<ProgramSourceMapEntry>(source ?? Array.Empty<ProgramSourceMapEntry>());
            values.Sort((left, right) =>
            {
                int result = left.TargetKind.CompareTo(right.TargetKind);
                if (result != 0) return result;
                result = left.TargetIndex.CompareTo(right.TargetIndex);
                if (result != 0) return result;
                return string.CompareOrdinal(left.DisplayPath, right.DisplayPath);
            });
            return values.AsReadOnly();
        }
    }
}
