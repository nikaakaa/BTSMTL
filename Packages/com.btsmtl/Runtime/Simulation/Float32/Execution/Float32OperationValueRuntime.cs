using System;
using static ThirdPersonSimulation.Float32GraphValueRuntime;

namespace ThirdPersonSimulation
{
    internal abstract class Float32OperationValueRuntime : IFloat32ValueInputReader
    {
        protected readonly Float32GameplayAbilityExecutionData m_Ability;
        protected readonly GameplayAbilityExecutionLayout m_Layout;
        readonly Float32GraphValueRuntime m_Values;

        protected Float32OperationValueRuntime(Float32GameplayAbilityExecutionData data,
            GameplayAbilityExecutionLayout layout, Float32GraphValueWorkspace workspace)
        {
            m_Ability = data;
            m_Layout = layout;
            m_Values = new Float32GraphValueRuntime(data, workspace
#if UNITY_EDITOR
                , null
#endif
                );
        }

        protected abstract AbilityStateValue EvaluateDomainValue<TTarget>(OperationControlCursor<TTarget> cursor,
            SimulationOperation operation, string outputPort, Float32ValueInputLease inputs)
            where TTarget : struct, IOperationControlTarget<TTarget>;
        protected abstract void ResetGraphCallParameter(int slot);
        protected abstract void WriteGraphCallParameter(int slot, AbilityStateValue value);
        protected abstract AbilityStateValue ReadGraphCallParameter(int slot);
        protected abstract bool IsAvailabilityQuery { get; }
        protected abstract void TraceResult(SimulationOperation operation, string outputPort, AbilityStateValue value, bool predictive);
        protected abstract void TraceInput(SimulationOperation operation, CompiledValueInputBinding input, AbilityStateValue value, bool predictive);

        public void BeginEvaluation() => m_Values.BeginEvaluation();

        public void PrepareSubGraph<TTarget>(
            OperationControlCursor<TTarget> cursor,
            SimulationOperation operation)
            where TTarget : struct, IOperationControlTarget<TTarget>
        {
            ProgramGraphCallFrame frame = m_Layout.Topology.GraphCallFrames(operation.Handle)[0];
            OperationRunnableStatus status = cursor.ReadStatus(frame.EntryOperation);
            if (status == OperationRunnableStatus.Running || status == OperationRunnableStatus.Stopping)
                return;
            using Float32ValueInputLease inputs = ReadInputs(cursor, operation);
            if (inputs.Count != frame.Inputs.Count)
                throw new InvalidOperationException($"Graph call frame '{frame.Identity}' received '{inputs.Count}' inputs, expected '{frame.Inputs.Count}'.");
            for (int i = 0; i < frame.Outputs.Count; i++)
                ResetGraphCallParameter(frame.Outputs[i].StateSlot);
            for (int i = 0; i < frame.Inputs.Count; i++)
                WriteGraphCallParameter(frame.Inputs[i].StateSlot, inputs[i]);
        }

        public AbilityStateValue Evaluate<TTarget>(OperationControlCursor<TTarget> cursor,
            OperationHandle operation, string outputPort = "", int outputPortIndex = -1)
            where TTarget : struct, IOperationControlTarget<TTarget>
        {
            var context = new Context<TTarget>(this, cursor);
            return m_Values.Evaluate(ref context, operation, outputPort, outputPortIndex);
        }

        public Float32ValueInputLease ReadInputs<TTarget>(OperationControlCursor<TTarget> cursor,
            SimulationOperation operation) where TTarget : struct, IOperationControlTarget<TTarget>
        {
            var context = new Context<TTarget>(this, cursor);
            return m_Values.ReadInputs(ref context, operation);
        }

        public bool EvaluateCondition<TTarget>(OperationControlCursor<TTarget> cursor, ProgramControlFlowEdge edge)
            where TTarget : struct, IOperationControlTarget<TTarget> =>
            edge != null && (!edge.HasCondition || ToBoolean(Evaluate(cursor, edge.Condition)));

        AbilityStateValue ReadSubGraphOutput<TTarget>(
            OperationControlCursor<TTarget> cursor,
            SimulationOperation operation,
            string outputPort,
            int evaluationSlot)
            where TTarget : struct, IOperationControlTarget<TTarget>
        {
            ProgramGraphCallFrame frame = m_Layout.Topology.GraphCallFrames(operation.Handle)[0];
            int stateSlot = m_Layout.ValueLayout.GraphOutputStateSlot(evaluationSlot);
            if (stateSlot < 0)
                throw new InvalidOperationException($"Graph call frame '{frame.Identity}' has no output port '{outputPort}'.");
            if (cursor.ReadStatus(frame.EntryOperation) != OperationRunnableStatus.Success)
                return m_Ability.DefaultStateValue(stateSlot);
            return ReadGraphCallParameter(stateSlot);
        }

        readonly struct Context<TTarget> : IFloat32GraphValueContext
            where TTarget : struct, IOperationControlTarget<TTarget>
        {
            readonly Float32OperationValueRuntime m_Owner;
            readonly OperationControlCursor<TTarget> m_Cursor;

            public Context(Float32OperationValueRuntime owner, OperationControlCursor<TTarget> cursor)
            {
                m_Owner = owner;
                m_Cursor = cursor;
            }

            public bool IsPredictiveEvaluation => m_Cursor.IsPredictiveEvaluation || m_Owner.IsAvailabilityQuery;
            public void RequireExecution(OperationHandle operation)
            {
                if (!m_Owner.IsAvailabilityQuery)
                    m_Cursor.RequireExecution(operation);
            }
            public AbilityStateValue EvaluateDomainValue(SimulationOperation operation, string outputPort,
                int outputPortIndex, Float32ValueInputLease inputs) => operation.Code == SimulationOperationCode.SubGraph
                    ? m_Owner.ReadSubGraphOutput(m_Cursor, operation, outputPort,
                        m_Owner.m_Layout.ValueLayout.ValueEvaluationSlot(operation.Handle, outputPortIndex))
                    : m_Owner.EvaluateDomainValue(m_Cursor, operation, outputPort, inputs);
            public void TraceResult(SimulationOperation operation, string outputPort, int outputPortIndex, AbilityStateValue value) =>
                m_Owner.TraceResult(operation, outputPort, value, IsPredictiveEvaluation);
            public void TraceInput(SimulationOperation operation, CompiledValueInputBinding input, AbilityStateValue value) =>
                m_Owner.TraceInput(operation, input, value, IsPredictiveEvaluation);
        }
    }
}
