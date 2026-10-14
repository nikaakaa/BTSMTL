using Float32ValueInputBuffer = ThirdPersonSimulation.ProgramValueInputBuffer<ThirdPersonSimulation.AbilityStateValue>;
using ProgramValues = ThirdPersonSimulation.ProgramValueCalculation<ThirdPersonSimulation.AbilityStateValue, ThirdPersonSimulation.Float32Scalar, ThirdPersonSimulation.Float32SimulationMath, ThirdPersonSimulation.Float32ProgramNumericValueAccess>;
using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    public readonly struct Float32ValueInputLease : IDisposable
    {
        readonly Float32GraphValueRuntime m_Owner;
        readonly Float32ValueInputBuffer m_Buffer;
        readonly int m_Depth;

        internal Float32ValueInputLease(
            Float32GraphValueRuntime owner,
            Float32ValueInputBuffer buffer,
            int depth)
        {
            m_Owner = owner;
            m_Buffer = buffer;
            m_Depth = depth;
        }

        public int Count => m_Buffer.Count;
        internal ReadOnlySpan<AbilityStateValue> Values => m_Buffer.Values;
        public AbilityStateValue this[int index] => m_Buffer[index];

        public AbilityStateValue FindByKind(ProgramStateValueKind kind)
        {
            for (int i = 0; i < m_Buffer.Count; i++)
            {
                if (m_Buffer.Values[i].Kind == kind)
                    return m_Buffer.Values[i];
            }
            return default;
        }

        public void Dispose()
        {
            m_Owner?.ReleaseInputBuffer(m_Buffer, m_Depth);
        }
    }

    public sealed class Float32GraphValueWorkspace
    {
        internal readonly bool[] ActiveValueSlots;
        internal readonly BorrowedValueObservationScratch<AbilityStateValue> ObservationValues;
        internal readonly List<Float32ValueInputBuffer> InputBuffers = new();

        internal Float32GraphValueWorkspace(Float32GameplayAbilityExecutionData data)
            : this((Float32GraphExecutionData)data) =>
            ObservationValues = new BorrowedValueObservationScratch<AbilityStateValue>(data.ValueLayout.ValueEvaluationSlotCount);

        public Float32GraphValueWorkspace(Float32GraphExecutionData data)
        {
            int depthCapacity = checked(data.Operations.Count + 1);
            ActiveValueSlots = new bool[data.ValueLayout.ValueEvaluationSlotCount];
            int inputCapacity = 0;
            for (int index = 0; index < data.Operations.Count; index++)
                inputCapacity = Math.Max(inputCapacity, data.ValueLayout.ValueInputs(data.Operations[index].Handle).Length);
            while (InputBuffers.Count < depthCapacity)
                InputBuffers.Add(new Float32ValueInputBuffer(inputCapacity));
        }
    }

    public interface IFloat32GraphValueContext
    {
        bool IsReadOnlyObservation { get; }
        void RequireExecution(OperationHandle operation);
        AbilityStateValue EvaluateDomainValue(SimulationOperation operation, string outputPort, int outputPortIndex, Float32ValueInputLease inputs);
        void TraceResult(SimulationOperation operation, string outputPort, int outputPortIndex, AbilityStateValue value);
        void TraceInput(SimulationOperation operation, CompiledValueInputBinding input, AbilityStateValue value);
    }

    public sealed class Float32GraphValueRuntime
    {
        readonly Float32GraphExecutionData m_Ability;
        readonly Float32GraphValueLayout m_Layout;
        readonly bool[] m_ActiveValueSlots;
        readonly BorrowedValueObservationScratch<AbilityStateValue> m_ObservationValues;
        readonly List<Float32ValueInputBuffer> m_InputBuffers;
        int m_InputBufferDepth;
#if UNITY_EDITOR
        readonly IProgramValuePortInputs<AbilityStateValue> m_Ports;
#endif

        public Float32GraphValueRuntime(Float32GraphExecutionData data, Float32GraphValueWorkspace workspace
#if UNITY_EDITOR
            , IProgramValuePortInputs<AbilityStateValue> ports
#endif
            )
        {
            m_Ability = data ?? throw new ArgumentNullException(nameof(data));
            m_Layout = data.ValueLayout;
            m_ActiveValueSlots = workspace.ActiveValueSlots;
            m_ObservationValues = workspace.ObservationValues;
            m_InputBuffers = workspace.InputBuffers;
#if UNITY_EDITOR
            m_Ports = ports;
#endif
        }

        internal void ClearObservedValues() => m_ObservationValues.Clear();

        internal void RecordObservedValue(OperationHandle operation, int outputPortIndex, in AbilityStateValue value) =>
            m_ObservationValues.Write(m_Layout.ValueEvaluationSlot(operation, outputPortIndex), in value);

        public void BeginEvaluation()
        {
            if (m_InputBufferDepth != 0)
                throw new InvalidOperationException("Float32 value runtime retained recursion state across evaluations.");
        }


		public AbilityStateValue Evaluate<TContext>(
			ref TContext context,
			OperationHandle handle,
			string outputPort = "",
			int outputPortIndex = -1)
			where TContext : struct, IFloat32GraphValueContext
		{
            context.RequireExecution(handle);
			int valueSlot = m_Layout.ValueEvaluationSlot(handle, outputPortIndex);
            bool readOnlyObservation = context.IsReadOnlyObservation;
#if UNITY_EDITOR
            if (m_Ports != null && m_Ports.TryRead(handle, outputPortIndex, false, out AbilityStateValue overridden))
            {
                context.TraceResult(m_Ability.Operations[handle.Value], outputPort, outputPortIndex, overridden);
                return overridden;
            }
#endif
			if (m_ActiveValueSlots[valueSlot])
				throw new InvalidOperationException($"Value operation cycle reached '{handle}/{outputPort}'.");

            if (readOnlyObservation && m_ObservationValues.IsWritten(valueSlot))
                return m_ObservationValues.Read(valueSlot);
			m_ActiveValueSlots[valueSlot] = true;
			try
			{
				SimulationOperation operation = m_Ability.Operations[handle.Value];
				using Float32ValueInputLease inputs = ReadInputs(ref context, operation);
				AbilityStateValue result;
				switch (operation.Code)
                {
                    case SimulationOperationCode.ConditionResult:
						result = AbilityStateValue.FromBoolean(ProgramValues.ConditionResult(inputs.Values));
						break;
                    case SimulationOperationCode.Calculate:
                        result = Float32GraphCalculations.Evaluate((GraphCalculationKind)operation.Integer0, inputs);
                        break;
                    case SimulationOperationCode.IdentityValue:
                        result = inputs[0];
                        break;
                    case SimulationOperationCode.SelectValue:
                        result = inputs[1];
                        break;
                    case SimulationOperationCode.Compare:
						result = AbilityStateValue.FromBoolean(Compare(operation.Integer0, inputs));
						break;
                    case SimulationOperationCode.And:
						result = AbilityStateValue.FromBoolean(ProgramValues.And(inputs.Values));
						break;
                    case SimulationOperationCode.Or:
						result = AbilityStateValue.FromBoolean(ProgramValues.Or(inputs.Values));
						break;
                    case SimulationOperationCode.Not:
						result = AbilityStateValue.FromBoolean(ProgramValues.Not(inputs.Values));
						break;
                    case SimulationOperationCode.Constant:
						result = operation.ConstantReferences.Count > 0
							? ValueFromConstant(m_Ability.Constants[operation.ConstantReferences[0]])
							: AbilityStateValue.FromBoolean(false);
						break;
                    default:
                        result = context.EvaluateDomainValue(operation, outputPort, outputPortIndex, inputs);
                        break;
                }
                context.TraceResult(operation, outputPort, outputPortIndex, result);
                if (readOnlyObservation) m_ObservationValues.Write(valueSlot, in result);
				return result;
			}
			finally
			{
				m_ActiveValueSlots[valueSlot] = false;
			}
		}

        public Float32ValueInputLease ReadInputs<TContext>(
            ref TContext context,
            SimulationOperation operation)
            where TContext : struct, IFloat32GraphValueContext
        {
            ReadOnlySpan<CompiledValueInputBinding> inputs = m_Layout.ValueInputs(operation.Handle);
            int depth = m_InputBufferDepth;
            Float32ValueInputBuffer buffer = RequireInputBuffer(depth);
            m_InputBufferDepth++;
            try
            {
                for (int i = 0; i < inputs.Length; i++)
                {
                    if (operation.Code == SimulationOperationCode.SelectValue && i > 0)
                        i = ProgramValues.ToBoolean(buffer.Values[0]) ? 1 : 2;
                    ref readonly CompiledValueInputBinding input = ref inputs[i];
                    AbilityStateValue value = ReadInputValue(ref context, operation, in input);
                    buffer.Add(in value);
                    context.TraceInput(operation, input, value);
                    if (operation.Code == SimulationOperationCode.SelectValue && i > 0)
                        break;
                    if (i == 0 &&
                        ProgramValues.StopsAfterFirstInput(operation.Code, in value))
                        break;
                }
                return new Float32ValueInputLease(this, buffer, depth);
            }
            catch
            {
                ReleaseInputBuffer(buffer, depth);
                throw;
            }
        }

        Float32ValueInputBuffer RequireInputBuffer(int depth)
        {
            if (depth >= m_InputBuffers.Count)
                throw new InvalidOperationException("Graph value recursion exceeds its prepared operation bound.");
            return m_InputBuffers[depth];
        }

        internal AbilityStateValue ReadInputValue<TContext>(ref TContext context,
            SimulationOperation operation, in CompiledValueInputBinding input)
            where TContext : struct, IFloat32GraphValueContext
        {
#if UNITY_EDITOR
            if (m_Ports != null && m_Ports.TryRead(operation.Handle, input.TargetPortIndex, true, out AbilityStateValue value))
                return value;
#endif
            return input.SourceKind == CompiledValueInputSourceKind.Operation
                ? Evaluate(ref context, input.SourceOperation, input.SourceOutputPortIdentity, input.SourceOutputPortIndex)
                : ValueFromConstant(m_Ability.Constants[input.ConstantIndex]);
        }

        internal void ReleaseInputBuffer(Float32ValueInputBuffer buffer, int depth)
        {
            if (depth != m_InputBufferDepth - 1 || !ReferenceEquals(buffer, m_InputBuffers[depth]))
                throw new InvalidOperationException("Float32 value input buffers must be released in recursion order.");
            buffer.Clear();
            m_InputBufferDepth--;
        }

        static bool Compare(int comparison, Float32ValueInputLease inputs)
        {
            if (inputs.Count < 2)
                return false;
            if (inputs[0].Kind == ProgramStateValueKind.Int32 && inputs[1].Kind == ProgramStateValueKind.Int32)
            {
                int a = inputs[0].Int32, b = inputs[1].Int32;
                return comparison switch { 0 => a == b, 1 => a != b, 2 => a < b, 3 => a <= b, 4 => a >= b, 5 => a > b,
                    _ => throw new InvalidOperationException($"比较操作 '{comparison}' 不受支持。") };
            }
            return ProgramValues.CompareScalars(comparison, inputs.Values);
        }

        public static AbilityStateValue ConvertValue(AbilityStateValue value, ProgramStateValueKind expected)
        {
            if (value.Kind == expected)
                return value;
            if (expected == ProgramStateValueKind.Scalar)
                return AbilityStateValue.FromScalar(ProgramValues.ToScalar(value));
            if (expected == ProgramStateValueKind.Int32 && value.Kind == ProgramStateValueKind.Scalar)
                return AbilityStateValue.FromInt32(checked((int)value.Scalar.ToSingle()));
            throw new InvalidOperationException($"Cannot assign '{value.Kind}' to '{expected}'.");
        }

        public static AbilityStateValue ValueFromConstant(ProgramConstant constant)
        {
            return constant.Kind switch
            {
                ProgramConstantKind.Boolean => AbilityStateValue.FromBoolean(constant.Boolean),
                ProgramConstantKind.Int32 => AbilityStateValue.FromInt32(constant.Int32),
                ProgramConstantKind.UInt64 => AbilityStateValue.FromUInt64(constant.UInt64),
                ProgramConstantKind.Scalar => AbilityStateValue.FromScalar(constant.Scalar),
                ProgramConstantKind.Vector2 => AbilityStateValue.FromVector2(constant.Vector2),
                ProgramConstantKind.Vector3 => AbilityStateValue.FromVector3(constant.Vector3),
                ProgramConstantKind.Yaw => AbilityStateValue.FromYaw(constant.Yaw),
                ProgramConstantKind.String => AbilityStateValue.FromIdentity(constant.Text),
                ProgramConstantKind.Quaternion => AbilityStateValue.FromQuaternion(constant.Quaternion),
                ProgramConstantKind.Enum => AbilityStateValue.FromEnum(constant.Enum.TypeIdentity, constant.Enum.Value),
                ProgramConstantKind.Curve => throw new InvalidOperationException(
                    $"Curve constant '{constant.Identity}' cannot enter typed Character state evaluation."),
                _ => throw new ArgumentOutOfRangeException(nameof(constant.Kind))
            };
        }
    }
}
