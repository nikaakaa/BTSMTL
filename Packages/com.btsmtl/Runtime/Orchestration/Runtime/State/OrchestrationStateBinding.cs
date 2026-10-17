using System;
using System.Runtime.InteropServices;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public static unsafe class OrchestrationStateBinding
    {
        public static OrchestrationCandidateState<TValue> BindCandidate<TValue>(
            OrchestrationExecutionStateLayout<TValue> prepared,
            Span<byte> memory) where TValue : unmanaged
        {
            OrchestrationStateLayout layout = prepared.Layout;
            OrchestrationStateView state = layout.BindCandidate(memory);
            return new OrchestrationCandidateState<TValue>(BindFrames(prepared, state),
                default, default, default);
        }

        public static OrchestrationCandidateState<TValue> BindCandidate<TValue, TPlayback>(
            OrchestrationStateLayout<TValue, TPlayback> prepared,
            Span<byte> memory)
            where TValue : unmanaged where TPlayback : unmanaged
        {
            OrchestrationStateLayout layout = prepared.Layout;
            OrchestrationStateView state = layout.BindCandidate(memory);
            OrchestrationStateView execution = prepared.GetExecution(state);
            OrchestrationStateView blackboard = prepared.GetBlackboard(state);
            OrchestrationStateView timeline = prepared.GetTimeline(state);
            return new OrchestrationCandidateState<TValue>(BindFrames(prepared.Execution, execution),
                prepared.Blackboard.GetValues(blackboard), prepared.Blackboard.GetMetadata(blackboard),
                timeline);
        }

        public static OrchestrationCandidateState<TValue> BindCandidate<TValue, TPlayback>(
            OrchestrationStateLayout<TValue, TPlayback> prepared,
            Span<byte> memory, out Span<TPlayback> playbacks)
            where TValue : unmanaged where TPlayback : unmanaged
        {
            var state = BindCandidate(prepared, memory);
            playbacks = prepared.Timeline.GetPlaybacks(state.Timeline);
            return state;
        }

        public static OrchestrationCommittedState<TValue> BindCommitted<TValue>(
            OrchestrationExecutionStateLayout<TValue> prepared,
            ReadOnlySpan<byte> memory) where TValue : unmanaged
        {
            OrchestrationStateLayout layout = prepared.Layout;
            ReadOnlyOrchestrationStateView state = layout.BindCommitted(memory);
            return new OrchestrationCommittedState<TValue>(BindFrames(prepared, state),
                default, default, default);
        }

        public static OrchestrationCommittedState<TValue> BindCommitted<TValue, TPlayback>(
            OrchestrationStateLayout<TValue, TPlayback> prepared,
            ReadOnlySpan<byte> memory,
            out ReadOnlySpan<TPlayback> playbacks)
            where TValue : unmanaged where TPlayback : unmanaged
        {
            OrchestrationStateLayout layout = prepared.Layout;
            ReadOnlyOrchestrationStateView state = layout.BindCommitted(memory);
            ReadOnlyOrchestrationStateView execution = prepared.GetExecution(state);
            ReadOnlyOrchestrationStateView blackboard = prepared.GetBlackboard(state);
            ReadOnlyOrchestrationStateView timeline = prepared.GetTimeline(state);
            playbacks = prepared.Timeline.GetPlaybacks(timeline);
            return new OrchestrationCommittedState<TValue>(BindFrames(prepared.Execution, execution),
                prepared.Blackboard.GetValues(blackboard), prepared.Blackboard.GetMetadata(blackboard),
                timeline);
        }

        public static void CopyMemory<TValue, TPlayback>(
            OrchestrationStateLayout<TValue, TPlayback> prepared,
            ReadOnlySpan<int> skillContentIds, ReadOnlySpan<OrchestrationProgram<TValue>> programs,
            ReadOnlySpan<byte> source, Span<byte> destination)
            where TValue : unmanaged where TPlayback : unmanaged
        {
            ReadOnlyOrchestrationStateView sourceView = prepared.Layout.BindCommitted(source);
            OrchestrationStateView destinationView = prepared.Layout.BindCandidate(destination);
            ReadOnlyOrchestrationStateView sourceExecution = prepared.GetExecution(sourceView);
            var frames = BindFrames(prepared.Execution, prepared.GetExecution(destinationView));
            frames.CopyPayloadsFrom(BindFrames(prepared.Execution, sourceExecution),
                skillContentIds, programs);
            Span<OrchestrationStateRegion<byte>> payloads = stackalloc OrchestrationStateRegion<byte>[3];
            PreparePayloadRegions(prepared.Execution, source, sourceExecution, payloads);
            int copied = 0;
            foreach (OrchestrationStateRegion<byte> payload in payloads)
            {
                source.Slice(copied, payload.OffsetBytes - copied).CopyTo(destination.Slice(copied));
                copied = payload.OffsetBytes + payload.SizeBytes;
            }
            source.Slice(copied).CopyTo(destination.Slice(copied));
        }

        public static int WriteBlitState<TValue, TPlayback>(
            in OrchestrationStateLayout<TValue, TPlayback> prepared,
            ReadOnlySpan<int> skillContentIds, ReadOnlySpan<OrchestrationProgram<TValue>> programs,
            ReadOnlySpan<byte> source, Span<byte> output)
            where TValue : unmanaged where TPlayback : unmanaged
        {
            ReadOnlyOrchestrationStateView state = prepared.Layout.BindCommitted(source);
            ReadOnlyOrchestrationStateView execution = prepared.GetExecution(state);
            Span<OrchestrationStateRegion<byte>> payloads = stackalloc OrchestrationStateRegion<byte>[3];
            PreparePayloadRegions(prepared.Execution, source, execution, payloads);
            int written = 0;
            int copied = 0;
            foreach (OrchestrationStateRegion<byte> payload in payloads)
            {
                WriteBytes(source.Slice(copied, payload.OffsetBytes - copied), output, ref written);
                copied = payload.OffsetBytes + payload.SizeBytes;
            }
            WriteBytes(source.Slice(copied), output, ref written);
            var frames = BindFrames(prepared.Execution, execution);
            for (int slot = 0; slot < frames.Capacity; slot++)
            {
                int skill = frames.Header(slot).SkillContentId;
                if (skill == 0)
                    continue;
                ref readonly OrchestrationProgram<TValue> program = ref programs[skillContentIds.IndexOf(skill)];
                WriteBytes(MemoryMarshal.AsBytes(frames.Operations(slot).Slice(0, program.OperationCount)),
                    output, ref written);
                WriteBytes(MemoryMarshal.AsBytes(frames.Values(slot).Slice(0, program.ValueSlotCount)),
                    output, ref written);
                WriteBytes(MemoryMarshal.AsBytes(frames.BlackboardMetadata(slot).Slice(0, program.ValueSlotCount)),
                    output, ref written);
            }
            return written;
        }

        public static int ReadBlitState<TValue, TPlayback>(
            in OrchestrationStateLayout<TValue, TPlayback> prepared,
            ReadOnlySpan<int> skillContentIds, ReadOnlySpan<OrchestrationProgram<TValue>> programs,
            ReadOnlySpan<byte> input, Span<byte> clearedMemory)
            where TValue : unmanaged where TPlayback : unmanaged
        {
            ReadOnlyOrchestrationStateView state = prepared.Layout.BindCommitted(clearedMemory);
            Span<OrchestrationStateRegion<byte>> payloads = stackalloc OrchestrationStateRegion<byte>[3];
            PreparePayloadRegions(prepared.Execution, clearedMemory, prepared.GetExecution(state), payloads);
            int read = 0;
            int restored = 0;
            foreach (OrchestrationStateRegion<byte> payload in payloads)
            {
                ReadBytes(input, clearedMemory.Slice(restored, payload.OffsetBytes - restored), ref read);
                restored = payload.OffsetBytes + payload.SizeBytes;
            }
            ReadBytes(input, clearedMemory.Slice(restored), ref read);
            var frames = BindFrames(prepared.Execution,
                prepared.GetExecution(prepared.Layout.BindCandidate(clearedMemory)));
            for (int slot = 0; slot < frames.Capacity; slot++)
            {
                int skill = frames.Header(slot).SkillContentId;
                if (skill == 0)
                    continue;
                ref readonly OrchestrationProgram<TValue> program = ref programs[skillContentIds.IndexOf(skill)];
                ReadBytes(input, MemoryMarshal.AsBytes(frames.Operations(slot).Slice(0, program.OperationCount)),
                    ref read);
                ReadBytes(input, MemoryMarshal.AsBytes(frames.Values(slot).Slice(0, program.ValueSlotCount)),
                    ref read);
                ReadBytes(input, MemoryMarshal.AsBytes(frames.BlackboardMetadata(slot).Slice(0, program.ValueSlotCount)),
                    ref read);
            }
            return read;
        }

        public static void AdoptMemory(in OrchestrationStateLayout layout,
            in OrchestrationStateLayout actual, ReadOnlySpan<byte> memory)
        {
            layout.RequireCompatible(in actual);
            if (memory.Length != layout.SizeBytes)
                throw new ArgumentException("BTSMTL 编排区段内存尺寸与已准备布局不匹配。", nameof(memory));
            fixed (byte* address = memory)
                if ((ulong)address % (uint)layout.Alignment != 0)
                    throw new ArgumentException("BTSMTL 编排区段内存地址不满足已准备布局对齐。", nameof(memory));
        }

        static void PreparePayloadRegions<TValue>(
            in OrchestrationExecutionStateLayout<TValue> prepared, ReadOnlySpan<byte> memory,
            ReadOnlyOrchestrationStateView execution, Span<OrchestrationStateRegion<byte>> payloads)
            where TValue : unmanaged
        {
            int executionOffset;
            fixed (byte* memoryAddress = memory)
            fixed (byte* executionAddress = execution.Get(
                new OrchestrationStateRegion<byte>(0, prepared.Layout.SizeBytes)))
                executionOffset = checked((int)(executionAddress - memoryAddress));
            payloads[0] = new OrchestrationStateRegion<byte>(
                executionOffset + prepared.BlackboardMetadata.OffsetBytes, prepared.BlackboardMetadata.SizeBytes);
            payloads[1] = new OrchestrationStateRegion<byte>(
                executionOffset + prepared.Operations.OffsetBytes, prepared.Operations.SizeBytes);
            payloads[2] = new OrchestrationStateRegion<byte>(
                executionOffset + prepared.Values.OffsetBytes, prepared.Values.SizeBytes);
            for (int i = 1; i < payloads.Length; i++)
                for (int j = i; j > 0 && payloads[j].OffsetBytes < payloads[j - 1].OffsetBytes; j--)
                {
                    OrchestrationStateRegion<byte> previous = payloads[j - 1];
                    payloads[j - 1] = payloads[j];
                    payloads[j] = previous;
                }
        }

        static void WriteBytes(ReadOnlySpan<byte> source, Span<byte> output, ref int written)
        {
            source.CopyTo(output.Slice(written));
            written += source.Length;
        }

        static void ReadBytes(ReadOnlySpan<byte> input, Span<byte> destination, ref int read)
        {
            input.Slice(read, destination.Length).CopyTo(destination);
            read += destination.Length;
        }

        static OrchestrationExecutionFrames<TValue> BindFrames<TValue>(
            OrchestrationExecutionStateLayout<TValue> layout, OrchestrationStateView state)
            where TValue : unmanaged =>
            new OrchestrationExecutionFrames<TValue>(layout.GetFrames(state),
                ref layout.GetRoster(state)[0], layout.GetOperations(state), layout.GetValues(state),
                layout.GetBlackboardMetadata(state));

        static ReadOnlyOrchestrationExecutionFrames<TValue> BindFrames<TValue>(
            OrchestrationExecutionStateLayout<TValue> layout, ReadOnlyOrchestrationStateView state)
            where TValue : unmanaged =>
            new ReadOnlyOrchestrationExecutionFrames<TValue>(layout.GetFrames(state),
                layout.GetRoster(state), layout.GetOperations(state), layout.GetValues(state),
                layout.GetBlackboardMetadata(state));
    }

    public readonly ref struct OrchestrationCandidateState<TValue> where TValue : unmanaged
    {
        internal OrchestrationCandidateState(OrchestrationExecutionFrames<TValue> frames,
            Span<TValue> characterValues, Span<OrchestrationBlackboardMetadata> characterBlackboards,
            OrchestrationStateView timeline)
        {
            Frames = frames;
            CharacterValues = characterValues;
            CharacterBlackboards = characterBlackboards;
            Timeline = timeline;
        }

        public OrchestrationExecutionFrames<TValue> Frames { get; }
        public Span<TValue> CharacterValues { get; }
        public Span<OrchestrationBlackboardMetadata> CharacterBlackboards { get; }
        public OrchestrationStateView Timeline { get; }

        public OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3,
            TQuaternion, TMath, TValues, TContext> CreateCursor<TScalar, TVector2, TVector3,
            TQuaternion, TMath, TValues, TContext>(OrchestrationProgram<TValue> program,
            OrchestrationExecutionWorkspace<TValue> workspace,
            IOrchestrationExtensionBindings<TValue, TContext> bindings, int frameSlot,
            ReadOnlySpan<int> characterSlots,
            IOrchestrationTimelineDispatcher<TValue, TScalar, TVector2, TVector3,
                TQuaternion, TMath, TValues, TContext> timelineDispatcher,
            IOrchestrationTimelineClipExitDispatcher<TContext> clipExitDispatcher,
            in OrchestrationFrameInput<TValue> input, in OrchestrationIdentityOrdinalView identities,
            in TContext context)
            where TScalar : unmanaged where TVector2 : unmanaged where TVector3 : unmanaged
            where TQuaternion : unmanaged
            where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
            where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TQuaternion>
            where TContext : struct =>
            new OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3,
                TQuaternion, TMath, TValues, TContext>(program, workspace, bindings, Frames, frameSlot,
                CharacterValues, CharacterBlackboards, characterSlots, timelineDispatcher,
                clipExitDispatcher, Timeline, in input, in identities, in context, null, default);

        public OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3,
            TQuaternion, TMath, TValues, TContext> CreateCursor<TScalar, TVector2, TVector3,
            TQuaternion, TMath, TValues, TContext>(OrchestrationExecutionSourceBinding<TValue> preparedSource,
            OrchestrationExecutionWorkspace<TValue> workspace,
            IOrchestrationExtensionBindings<TValue, TContext> bindings, int frameSlot,
            OrchestrationExecutionSource<TValue> source, ReadOnlySpan<int> characterSlots,
            IOrchestrationTimelineDispatcher<TValue, TScalar, TVector2, TVector3,
                TQuaternion, TMath, TValues, TContext> timelineDispatcher,
            IOrchestrationTimelineClipExitDispatcher<TContext> clipExitDispatcher,
            in OrchestrationFrameInput<TValue> input, in OrchestrationIdentityOrdinalView identities,
            in TContext context)
            where TScalar : unmanaged where TVector2 : unmanaged where TVector3 : unmanaged
            where TQuaternion : unmanaged
            where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
            where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TQuaternion>
            where TContext : struct =>
            new OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3,
                TQuaternion, TMath, TValues, TContext>(preparedSource.Program, workspace, bindings, Frames, frameSlot,
                CharacterValues, CharacterBlackboards, characterSlots, timelineDispatcher,
                clipExitDispatcher, Timeline, in input, in identities, in context, preparedSource, source);
    }

    public readonly ref struct OrchestrationCommittedState<TValue> where TValue : unmanaged
    {
        internal OrchestrationCommittedState(ReadOnlyOrchestrationExecutionFrames<TValue> frames,
            ReadOnlySpan<TValue> characterValues,
            ReadOnlySpan<OrchestrationBlackboardMetadata> characterBlackboards,
            ReadOnlyOrchestrationStateView timeline)
        {
            Frames = frames;
            CharacterValues = characterValues;
            CharacterBlackboards = characterBlackboards;
            Timeline = timeline;
        }

        public ReadOnlyOrchestrationExecutionFrames<TValue> Frames { get; }
        public ReadOnlySpan<TValue> CharacterValues { get; }
        public ReadOnlySpan<OrchestrationBlackboardMetadata> CharacterBlackboards { get; }
        public ReadOnlyOrchestrationStateView Timeline { get; }

        public OrchestrationExecutionSource<TValue> BorrowExecutionSource(int frameSlot, ulong tick,
            in OrchestrationStopContext stopContext) =>
            new OrchestrationExecutionSource<TValue>(Frames, CharacterValues, CharacterBlackboards,
                frameSlot, tick, in stopContext);
    }
}
