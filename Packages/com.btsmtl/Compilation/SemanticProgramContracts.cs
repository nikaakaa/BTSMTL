using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;

namespace BTSMTL.Compilation
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

        public SemanticOperation(SemanticOperation source, IReadOnlyList<SemanticOperationParameterValues> parameters)
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
        public IReadOnlyList<SemanticOperationParameterValues> ParameterValues => m_Parameters;
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
}
