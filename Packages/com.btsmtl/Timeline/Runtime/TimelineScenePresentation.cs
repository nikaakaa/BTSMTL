using System;
using System.Collections.Generic;
using BTSMTL.Orchestration;
using BTSMTL.Timeline;
using ThirdPersonSimulation;

namespace BTSMTL.Timeline.Runtime
{
    public sealed class TimelineScenePresentation
    {
        readonly struct Target
        {
            internal Target(ITimelineScenePresentationSink sink, string parameterId,
                TimelineBindingValueKind kind, in State initial)
            {
                Sink = sink;
                ParameterId = parameterId;
                Kind = kind;
                Initial = initial;
            }

            internal readonly ITimelineScenePresentationSink Sink;
            internal readonly string ParameterId;
            internal readonly TimelineBindingValueKind Kind;
            internal readonly State Initial;
        }

        struct State
        {
            internal OperationHandle? Operation;
            internal OrchestrationTimelineStart<OrchestrationTime> Start;
            internal float Value;
            internal float NormalizedTime;
            internal int Cycle;
        }

        readonly List<Target> m_Targets = new();
        readonly Dictionary<(string Target, string Parameter), int> m_Slots = new();
        State[] m_Accepted;
        State[] m_Candidate;
        bool[] m_Changed;

        public int Prepare(OperationHandle? operation, string parameterId,
            TimelineBindingValueKind kind, ITimelineScenePresentationSink sink, float initialValue)
        {
            var key = (sink.TargetIdentity, parameterId);
            if (m_Slots.TryGetValue(key, out int slot))
                return slot;
            var initial = new State { Operation = operation, Value = initialValue };
            slot = m_Targets.Count;
            m_Targets.Add(new Target(sink, parameterId, kind, in initial));
            m_Slots.Add(key, slot);
            return slot;
        }

        public void CompletePreparation()
        {
            m_Accepted = new State[m_Targets.Count];
            m_Candidate = new State[m_Targets.Count];
            m_Changed = new bool[m_Targets.Count];
            for (int i = 0; i < m_Targets.Count; i++)
                m_Accepted[i] = m_Targets[i].Initial;
        }

        public void BeginFrame()
        {
            Array.Copy(m_Accepted, m_Candidate, m_Accepted.Length);
            Array.Clear(m_Changed, 0, m_Changed.Length);
        }

        public void Sample(int slot, OperationHandle? operation,
            in TimelineRuntimePresentationFrame frame,
            in TimelineRuntimeSample<Float32Scalar> sample, float value)
        {
            m_Candidate[slot] = new State
            {
                Operation = operation,
                Start = frame.Start,
                Value = value,
                NormalizedTime = sample.NormalizedTime.ToSingle(),
                Cycle = sample.Cycle
            };
            m_Changed[slot] = true;
        }

        public void CommitFrame()
        {
            for (int i = 0; i < m_Targets.Count; i++)
                if (m_Changed[i])
                    Apply(i, in m_Candidate[i]);
            (m_Accepted, m_Candidate) = (m_Candidate, m_Accepted);
        }

        void Apply(int slot, in State state)
        {
            Target target = m_Targets[slot];
            var sample = new TimelineScenePresentationSample(state.Operation,
                target.Sink.TargetIdentity, target.ParameterId, state.Value,
                state.NormalizedTime, in state.Start, state.Cycle);
            if (target.Kind == TimelineBindingValueKind.Boolean)
                target.Sink.WriteBoolean(sample);
            else
                target.Sink.WriteScalar(sample);
        }

        public void WriteRecordedState<TWriter>(ref TWriter writer)
            where TWriter : struct, IRecordedRuntimeStateWriter
        {
            writer.WriteSpan<State>(m_Accepted);
        }

        public void WriteRecordedExecutionState<TWriter>(ref TWriter writer)
            where TWriter : struct, IRecordedRuntimeStateWriter
        {
            writer.WriteSpan<State>(m_Accepted);
            writer.WriteSpan<State>(m_Candidate);
            writer.WriteSpan<bool>(m_Changed);
        }

        public void RestoreRecordedExecutionState(ref RecordedRuntimeStateReader reader)
        {
            reader.ReadSpan<State>(m_Accepted.Length).CopyTo(m_Accepted);
            reader.ReadSpan<State>(m_Candidate.Length).CopyTo(m_Candidate);
            reader.ReadSpan<bool>(m_Changed.Length).CopyTo(m_Changed);
            for (int i = 0; i < m_Accepted.Length; i++)
                Apply(i, in m_Accepted[i]);
        }

        public void ApplyRecordedDisplayState(ref RecordedRuntimeStateReader reader)
        {
            ReadOnlySpan<State> states = reader.ReadSpan<State>(m_Accepted.Length);
            for (int i = 0; i < states.Length; i++)
                Apply(i, in states[i]);
        }
    }
}
