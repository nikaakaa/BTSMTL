using System;
using System.Collections.Generic;
using System.Linq;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.ServerAuthoritative
{
    internal sealed class ServerAuthoritativePredictionHistory
    {
        readonly int m_Capacity;
        readonly ulong[] m_Ticks;
        readonly ServerAuthoritativePredictionHistoryRecord[] m_Records;
        readonly List<ServerAuthoritativePredictionHistoryRecord> m_ReplayRecords;
        readonly RecordPage[] m_RecordPages;
        readonly int[] m_FreeRecords;
        int m_FreeRecordCount;
        int m_Count;

        readonly ServerAuthoritativeRemoteBodyTimeline m_RemoteBodies;
        readonly ServerAuthoritativePredictionHistoryCheckpoint[] m_Checkpoints;

        public ServerAuthoritativePredictionHistory(
            int capacity,
            int tickRate,
            int maximumExtrapolationTicks,
            IEnumerable<ActorId> lockedRemoteActors)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            m_Capacity = capacity;
            m_Ticks = new ulong[capacity];
            m_Records = new ServerAuthoritativePredictionHistoryRecord[capacity];
            m_ReplayRecords = new List<ServerAuthoritativePredictionHistoryRecord>(capacity);
            int recordCapacity = checked(capacity * 2 + 1);
            m_RecordPages = new RecordPage[recordCapacity];
            m_FreeRecords = new int[recordCapacity];
            for (int index = 0; index < recordCapacity; index++)
            {
                m_RecordPages[index] = new RecordPage(this, index);
                m_FreeRecords[index] = index;
            }
            m_FreeRecordCount = recordCapacity;
            m_RemoteBodies = new ServerAuthoritativeRemoteBodyTimeline(
                checked(capacity * 4),
                tickRate,
                maximumExtrapolationTicks,
                lockedRemoteActors);
            m_Checkpoints = new ServerAuthoritativePredictionHistoryCheckpoint[2];
            for (int index = 0; index < m_Checkpoints.Length; index++)
                m_Checkpoints[index] = new ServerAuthoritativePredictionHistoryCheckpoint(capacity,
                    m_RemoteBodies.PrepareCheckpointStorage());
        }

        public int Count => m_Count;

        internal void PrepareRecordStorage(int values, int requests, int observedConstraints)
        {
            for (int index = 0; index < m_RecordPages.Length; index++)
                m_RecordPages[index].PrepareStorage(values, requests, observedConstraints);
        }
        public int MaximumTextValues => checked(m_Capacity * m_RecordPages[0].MaximumTextValues +
            m_RemoteBodies.MaximumTextValues);
        internal int MaximumOwnerInputTextValues => checked(OwnerCanonicalInputHeader.CanonicalMaximumTextReferences +
            m_RecordPages[0].InputStorage.MaximumTextValues);
        public ulong FirstRetainedTick => m_Count == 0 ? ulong.MaxValue : m_Ticks[0];
        public bool IsRemoteObservationPrimed => m_RemoteBodies.IsPrimed;
        internal IReadOnlyList<ActorId> RemoteBodyActors => m_RemoteBodies.LockedActors;
        public int RemoteBodySampleCount => m_RemoteBodies.SampleCount;
        public int RemoteBodyCapacityPerActor => m_RemoteBodies.CapacityPerActor;
        public ulong RemoteBodyFirstSampleTick => m_RemoteBodies.FirstSampleTick;
        public ulong RemoteBodyLastSampleTick => m_RemoteBodies.LastSampleTick;
        public ulong RemoteBodyEvictionCount => m_RemoteBodies.EvictionCount;

        public void Observe(RemotePresentationBatch batch) => m_RemoteBodies.Observe(batch);

        public ServerAuthoritativeRemoteBodySelectionFrame SelectRemoteBodyFrame(SimulationTick tick) =>
            m_RemoteBodies.Select(tick);

        public ulong GetLastPredictedInputSequence(ulong confirmedInputSequence)
        {
            ulong sequence = confirmedInputSequence;
            for (int i = 0; i < m_Count; i++)
                sequence = Math.Max(sequence, m_Records[i].Input.InputSequence);
            return sequence;
        }

        public bool TryGet(SimulationTick tick, out ServerAuthoritativePredictionHistoryRecord record)
        {
            int index = Find(tick.Value);
            if (index < 0)
            {
                record = null;
                return false;
            }
            record = m_Records[index];
            return true;
        }

        public ServerAuthoritativePredictionHistoryRecord FirstRecord() =>
            m_Count == 0
                ? throw new InvalidOperationException("Prediction history is empty.")
                : m_Records[0];

        public ServerAuthoritativePredictionHistoryRecord LastRecord()
        {
            if (m_Count == 0)
                throw new InvalidOperationException("Hard recovery has no local Pipeline frame to reconstruct model-owned state.");
            return m_Records[m_Count - 1];
        }

        public IReadOnlyList<ServerAuthoritativePredictionHistoryRecord> GetReplayAfter(ulong confirmedInputSequence)
        {
            m_ReplayRecords.Clear();
            for (int i = 0; i < m_Count; i++)
            {
                if (m_Records[i].Input.InputSequence > confirmedInputSequence)
                    m_ReplayRecords.Add(m_Records[i]);
            }
            return m_ReplayRecords;
        }

        public int CountReplayAfter(ulong confirmedInputSequence)
        {
            int count = 0;
            for (int i = 0; i < m_Count; i++)
            {
                if (m_Records[i].Input.InputSequence > confirmedInputSequence)
                    count = checked(count + 1);
            }
            return count;
        }

        public void Add(
            OwnerCanonicalInputBatch input,
            Float32CompletedSimulationStep completed,
            ulong journalCursor,
            ulong confirmedInputSequence,
            ulong lastAuthorityAckTick,
            ulong lastBaselineTick)
        {
            if (!input.IsValid || !completed.IsValid || !completed.StepSnapshot.IsValid)
                throw new ArgumentException("Prediction history capture requires input and a completed Step snapshot.");
            if (completed.Step.Tick != completed.StepSnapshot.Tick || completed.State.Actors.Count != 1 ||
                completed.State.Actors[0].ActorId != input.ActorId)
            {
                throw new InvalidOperationException("Prediction history completed step does not match its owner input.");
            }
            int insertionIndex = Find(completed.Step.Tick.Value);
            if (insertionIndex >= 0)
                throw new InvalidOperationException($"Prediction history already contains Tick '{completed.Step.Tick}'.");
            insertionIndex = ~insertionIndex;
            using RecordPage page = AcquireRecord(
                input,
                completed.StepSnapshot.CompositionIdentity,
                completed.StepSnapshot.World,
                completed.StepSnapshot.PipelineProjection,
                completed.Step.ObservedWorldConstraints,
                journalCursor);
            ServerAuthoritativePredictionHistoryRecord record = page.Record;

            if (m_Count >= m_Capacity)
            {
                if (m_Records[0].Input.InputSequence > confirmedInputSequence)
                {
                    throw new InvalidOperationException(
                        $"Prediction history capacity cannot discard unconfirmed input: firstTick={m_Ticks[0]};firstSequence={m_Records[0].Input.InputSequence};confirmedSequence={confirmedInputSequence};lastAckTick={lastAuthorityAckTick};lastBaselineTick={lastBaselineTick}.");
                }
                m_Records[0].ReleaseStorage();
                m_Count--;
                Array.Copy(m_Ticks, 1, m_Ticks, 0, m_Count);
                Array.Copy(m_Records, 1, m_Records, 0, m_Count);
                if (insertionIndex > 0)
                    insertionIndex--;
            }

            Array.Copy(m_Ticks, insertionIndex, m_Ticks, insertionIndex + 1, m_Count - insertionIndex);
            Array.Copy(m_Records, insertionIndex, m_Records, insertionIndex + 1, m_Count - insertionIndex);
            record.RetainStorage();
            m_Ticks[insertionIndex] = completed.Step.Tick.Value;
            m_Records[insertionIndex] = record;
            m_Count++;
        }

        public void SealJournalCursor(SimulationTick tick, ulong journalCursor)
        {
            int index = Find(tick.Value);
            if (index < 0)
                throw new InvalidOperationException($"Prediction history has no Current Tick '{tick}' to seal its journal cursor.");
            ServerAuthoritativePredictionHistoryRecord previous = m_Records[index];
            using RecordPage page = AcquireRecord(previous.Input, previous.CompositionIdentity, previous.World,
                previous.PipelineProjection, previous.ObservedWorldConstraints, journalCursor);
            ServerAuthoritativePredictionHistoryRecord replacement = page.Record;
            replacement.RetainStorage();
            m_Records[index] = replacement;
            previous.ReleaseStorage();
        }

        public void PruneConfirmedThrough(ulong inputSequence)
        {
            int outputIndex = 0;
            for (int i = 0; i < m_Count; i++)
            {
                if (m_Records[i].Input.InputSequence <= inputSequence)
                {
                    m_Records[i].ReleaseStorage();
                    continue;
                }
                if (outputIndex != i)
                {
                    m_Ticks[outputIndex] = m_Ticks[i];
                    m_Records[outputIndex] = m_Records[i];
                }
                outputIndex++;
            }
            Array.Clear(m_Ticks, outputIndex, m_Count - outputIndex);
            Array.Clear(m_Records, outputIndex, m_Count - outputIndex);
            m_Count = outputIndex;
        }

        public ServerAuthoritativePredictionHistoryCheckpoint PrepareClear() =>
            Capture(0);

        public void WriteState(CanonicalWriter writer)
        {
            ServerAuthoritativePredictionStateCodec.WriteHistoryHeader(writer, m_Count);
            for (int index = 0; index < m_Count; index++)
                ServerAuthoritativePredictionStateCodec.WriteHistoryRecord(writer, m_Records[index]);
            m_RemoteBodies.WriteState(writer);
        }

        public ServerAuthoritativePredictionHistoryCheckpoint Capture() => Capture(m_Count);

        ServerAuthoritativePredictionHistoryCheckpoint Capture(int recordCount)
        {
            for (int index = 0; index < m_Checkpoints.Length; index++)
            {
                ServerAuthoritativePredictionHistoryCheckpoint checkpoint = m_Checkpoints[index];
                if (checkpoint.IsHeld)
                    continue;
                checkpoint.Begin();
                try
                {
                    for (int record = 0; record < recordCount; record++)
                        checkpoint.CaptureRecord(m_Ticks[record], m_Records[record]);
                    m_RemoteBodies.CaptureInto(checkpoint.RemoteBodies);
                    return checkpoint;
                }
                catch
                {
                    checkpoint.Dispose();
                    throw;
                }
            }
            throw new InvalidOperationException("Prediction history checkpoints exceeded their prepared transaction capacity.");
        }

        public void Restore(ServerAuthoritativePredictionHistoryCheckpoint checkpoint)
        {
            ClearRecords();
            for (int i = 0; i < checkpoint.Records.Count; i++)
            {
                KeyValuePair<ulong, ServerAuthoritativePredictionHistoryRecord> pair = checkpoint.Records[i];
                pair.Value.RetainStorage();
                m_Ticks[m_Count] = pair.Key;
                m_Records[m_Count++] = pair.Value;
            }
            m_RemoteBodies.Restore(checkpoint.RemoteBodies);
        }

        internal void ReleaseStateStorage()
        {
            m_ReplayRecords.Clear();
            ClearRecords();
            m_RemoteBodies.ReleaseStorage();
        }

        void ClearRecords()
        {
            for (int index = 0; index < m_Count; index++)
                m_Records[index].ReleaseStorage();
            Array.Clear(m_Ticks, 0, m_Count);
            Array.Clear(m_Records, 0, m_Count);
            m_Count = 0;
        }

        internal sealed class RecordPage : IDisposable
        {
            readonly ServerAuthoritativePredictionHistory m_Owner;
            readonly int m_Index;
            int m_References;
            internal Float32SimulationInputFrameStorage InputStorage { get; private set; }
            internal CanonicalBuffer<ObservedWorldConstraint> ObservedConstraints { get; private set; }
            internal ObservedWorldConstraintFrame ObservedFrame { get; private set; }
            internal int MaximumTextValues => checked(OwnerCanonicalInputHeader.CanonicalMaximumTextReferences +
                InputStorage.MaximumTextValues + ServerAuthoritativePredictionStateCodec.MaximumHistorySnapshotTextValues +
                ObservedConstraints.Capacity * ObservedWorldConstraint.CanonicalMaximumTextReferences);

            internal RecordPage(ServerAuthoritativePredictionHistory owner, int index)
            {
                m_Owner = owner;
                m_Index = index;
                Record = new ServerAuthoritativePredictionHistoryRecord(this);
            }

            internal ServerAuthoritativePredictionHistoryRecord Record { get; }
            internal void PrepareStorage(int values, int requests, int observedConstraints)
            {
                InputStorage = new Float32SimulationInputFrameStorage(values, requests);
                ObservedConstraints = new CanonicalBuffer<ObservedWorldConstraint>(observedConstraints);
                ObservedFrame = new ObservedWorldConstraintFrame(ObservedConstraints);
            }
            internal void Begin() => m_References = 1;
            internal void Retain() => m_References++;
            public void Dispose() => Release();

            internal void Release()
            {
                if (--m_References != 0)
                    return;
                Record.Clear();
                InputStorage.ClearBorrowedCopy();
                ObservedConstraints.Clear();
                ObservedFrame.SetPreparedMetadata(default, default);
                m_Owner.m_FreeRecords[m_Owner.m_FreeRecordCount++] = m_Index;
            }
        }

        RecordPage AcquireRecord(OwnerCanonicalInputBatch input, SimulationSessionCompositionIdentity compositionIdentity,
            SimulationWorldSnapshot world, SimulationPipelineStateSnapshot pipelineProjection,
            ObservedWorldConstraintFrame observedWorldConstraints, ulong journalCursor)
        {
            if (m_FreeRecordCount == 0)
                throw new InvalidOperationException("Prediction history record storage exceeded its transaction retention capacity.");
            RecordPage page = m_RecordPages[m_FreeRecords[--m_FreeRecordCount]];
            page.Begin();
            try
            {
                SimulationInput capturedInput = page.InputStorage.CopyOrderedBorrowedText(input.Input, input.Input.Requests);
                var captured = new OwnerCanonicalInputBatch(in input, capturedInput);
                page.ObservedConstraints.CopyFrom(observedWorldConstraints.Constraints);
                page.ObservedFrame.SetPreparedMetadata(observedWorldConstraints.Tick, observedWorldConstraints.FrameHash);
                page.Record.Initialize(captured, compositionIdentity, world, pipelineProjection, page.ObservedFrame, journalCursor);
                return page;
            }
            catch
            {
                page.Release();
                throw;
            }
        }

        int Find(ulong tick)
        {
            int left = 0;
            int right = m_Count - 1;
            while (left <= right)
            {
                int middle = left + (right - left) / 2;
                if (m_Ticks[middle] == tick)
                    return middle;
                if (m_Ticks[middle] < tick)
                    left = middle + 1;
                else
                    right = middle - 1;
            }
            return ~left;
        }
    }

    internal sealed class ServerAuthoritativePredictionHistoryCheckpoint : IDisposable
    {
        readonly IReadOnlyList<KeyValuePair<ulong, ServerAuthoritativePredictionHistoryRecord>> m_Records;
        readonly List<KeyValuePair<ulong, ServerAuthoritativePredictionHistoryRecord>> m_PreparedRecords;

        internal ServerAuthoritativePredictionHistoryCheckpoint(int capacity,
            ServerAuthoritativeRemoteBodyTimelineCheckpoint remoteBodies)
        {
            m_PreparedRecords = new List<KeyValuePair<ulong, ServerAuthoritativePredictionHistoryRecord>>(capacity);
            m_Records = m_PreparedRecords;
            RemoteBodies = remoteBodies;
        }

        public ServerAuthoritativePredictionHistoryCheckpoint(
            KeyValuePair<ulong, ServerAuthoritativePredictionHistoryRecord>[] records,
            ServerAuthoritativeRemoteBodyTimelineCheckpoint remoteBodies)
        {
            RemoteBodies = remoteBodies;
            m_Records = records;
            for (int index = 0; index < records.Length; index++)
                records[index].Value.RetainStorage();
            RemoteBodies.RetainStorage();
        }

        internal bool IsHeld { get; private set; }

        internal void Begin()
        {
            IsHeld = true;
            RemoteBodies.RetainStorage();
        }

        internal void CaptureRecord(ulong tick, ServerAuthoritativePredictionHistoryRecord record)
        {
            record.RetainStorage();
            m_PreparedRecords.Add(new KeyValuePair<ulong, ServerAuthoritativePredictionHistoryRecord>(tick, record));
        }

        public IReadOnlyList<KeyValuePair<ulong, ServerAuthoritativePredictionHistoryRecord>> Records => m_Records;
        public ServerAuthoritativeRemoteBodyTimelineCheckpoint RemoteBodies { get; }
        public ulong FirstRetainedTick => Records.Count == 0 ? ulong.MaxValue : Records[0].Key;

        public void Dispose()
        {
            RemoteBodies.ReleaseStorage();
            for (int index = 0; index < m_Records.Count; index++)
                m_Records[index].Value.ReleaseStorage();
            if (m_PreparedRecords != null)
            {
                m_PreparedRecords.Clear();
                RemoteBodies.ClearPrepared();
                IsHeld = false;
            }
        }
    }

    internal readonly struct ServerAuthoritativeRemoteBodySelection
    {
        public ServerAuthoritativeRemoteBodySelection(
            ActorId actorId,
            SimulationTick targetTick,
            WorldBodyState beforeBody,
            WorldBodyState finalBody,
            SimulationTick sourcePreviousTick,
            SimulationTick sourceCurrentTick,
            ObservedWorldConstraintSamplingKind samplingKind)
        {
            if (!actorId.IsValid || !targetTick.IsValid ||
                beforeBody.ActorId != actorId || finalBody.ActorId != actorId ||
                !sourcePreviousTick.IsValid || !sourceCurrentTick.IsValid ||
                sourceCurrentTick.CompareTo(sourcePreviousTick) < 0 ||
                samplingKind is not (ObservedWorldConstraintSamplingKind.Exact or
                    ObservedWorldConstraintSamplingKind.Interpolation or ObservedWorldConstraintSamplingKind.ConstantVelocityExtrapolation))
            {
                throw new ArgumentException("Remote body selection identity is incomplete or inconsistent.");
            }
            ActorId = actorId;
            TargetTick = targetTick;
            BeforeBody = beforeBody;
            FinalBody = finalBody;
            SourcePreviousTick = sourcePreviousTick;
            SourceCurrentTick = sourceCurrentTick;
            SamplingKind = samplingKind;
        }

        public ActorId ActorId { get; }
        public SimulationTick TargetTick { get; }
        public WorldBodyState BeforeBody { get; }
        public WorldBodyState FinalBody { get; }
        public SimulationTick SourcePreviousTick { get; }
        public SimulationTick SourceCurrentTick { get; }
        public ObservedWorldConstraintSamplingKind SamplingKind { get; }

        public ObservedWorldConstraint ToObservedConstraint(StableHash contactShapeConfigurationHash) =>
            new ObservedWorldConstraint(
                ActorId,
                TargetTick,
                BeforeBody,
                FinalBody,
                SourcePreviousTick,
                SourceCurrentTick,
                SamplingKind,
                contactShapeConfigurationHash);

        public CharacterBodySample ToBodySample() =>
            new CharacterBodySample(
                ActorId,
                TargetTick,
                BeforeBody,
                FinalBody,
                FinalBody.Position - BeforeBody.Position,
                Float32Angle.Delta(BeforeBody.Yaw, FinalBody.Yaw));
    }

    internal readonly struct ServerAuthoritativeRemoteBodySelectionFrame
    {
        readonly ServerAuthoritativeRemoteBodySelection[] m_Selections;

        public ServerAuthoritativeRemoteBodySelectionFrame(ServerAuthoritativeRemoteBodySelection[] selections) =>
            m_Selections = selections;

        public IReadOnlyList<ServerAuthoritativeRemoteBodySelection> Selections => m_Selections;

        public void CopyObservedWorldConstraints(CanonicalBuffer<ObservedWorldConstraint> destination,
            StableHash contactShapeConfigurationHash)
        {
            for (int i = 0; i < m_Selections.Length; i++)
                destination.Add(m_Selections[i].ToObservedConstraint(contactShapeConfigurationHash));
        }

        public void CopyBodySamples(Span<CharacterBodySample> destination)
        {
            if (destination.Length != m_Selections.Length)
                throw new ArgumentException("Remote body selection output length does not match its selections.", nameof(destination));
            for (int i = 0; i < m_Selections.Length; i++)
                destination[i] = m_Selections[i].ToBodySample();
        }
    }

    internal sealed class ServerAuthoritativeRemoteBodyTimeline
    {
        readonly int m_CapacityPerActor;
        readonly int m_TickRate;
        readonly int m_MaximumExtrapolationTicks;
        readonly ActorId[] m_LockedActors;
        readonly ulong[][] m_TickTracks;
        readonly CharacterBodySample[][] m_SampleTracks;
        readonly int[] m_Counts;
        readonly ServerAuthoritativeRemoteBodySelection[] m_SelectionScratch;
        readonly CharacterBodySample[] m_ObservationScratch;
        ulong m_EvictionCount;
        internal IReadOnlyList<ActorId> LockedActors => m_LockedActors;
        public int MaximumTextValues => checked(m_LockedActors.Length * (ActorId.CanonicalMaximumTextReferences +
            CharacterBodySample.CanonicalMaximumTextReferences * m_CapacityPerActor));

        public ServerAuthoritativeRemoteBodyTimeline(
            int capacityPerActor,
            int tickRate,
            int maximumExtrapolationTicks,
            IEnumerable<ActorId> lockedActors)
        {
            if (capacityPerActor <= 0 || tickRate <= 0 || maximumExtrapolationTicks <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacityPerActor));
            ActorId[] actors = lockedActors == null ? Array.Empty<ActorId>() : lockedActors.ToArray();
            Array.Sort(actors);
            if (actors.Length == 0)
                throw new ArgumentException("Remote body timeline requires a locked remote Actor roster.", nameof(lockedActors));
            for (int i = 0; i < actors.Length; i++)
            {
                if (!actors[i].IsValid || i > 0 && actors[i - 1] == actors[i])
                    throw new ArgumentException("Remote body timeline Actor roster is invalid.", nameof(lockedActors));
            }
            m_CapacityPerActor = capacityPerActor;
            m_TickRate = tickRate;
            m_MaximumExtrapolationTicks = maximumExtrapolationTicks;
            m_LockedActors = actors;
            m_TickTracks = new ulong[actors.Length][];
            m_SampleTracks = new CharacterBodySample[actors.Length][];
            m_Counts = new int[actors.Length];
            for (int i = 0; i < actors.Length; i++)
            {
                m_TickTracks[i] = new ulong[capacityPerActor];
                m_SampleTracks[i] = new CharacterBodySample[capacityPerActor];
            }
            m_SelectionScratch = new ServerAuthoritativeRemoteBodySelection[actors.Length];
            m_ObservationScratch = new CharacterBodySample[capacityPerActor];
            for (int index = 0; index < actors.Length; index++)
                actors[index].Retain();
        }

        public bool IsPrimed
        {
            get
            {
                for (int i = 0; i < m_LockedActors.Length; i++)
                {
                    if (m_Counts[i] == 0)
                        return false;
                }
                return true;
            }
        }

        public int SampleCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < m_LockedActors.Length; i++)
                    count = checked(count + m_Counts[i]);
                return count;
            }
        }

        public int CapacityPerActor => m_CapacityPerActor;

        public ulong FirstSampleTick
        {
            get
            {
                ulong tick = ulong.MaxValue;
                for (int i = 0; i < m_LockedActors.Length; i++)
                {
                    if (m_Counts[i] > 0)
                        tick = Math.Min(tick, m_TickTracks[i][0]);
                }
                return tick == ulong.MaxValue ? 0 : tick;
            }
        }

        public ulong LastSampleTick
        {
            get
            {
                ulong tick = 0;
                for (int i = 0; i < m_LockedActors.Length; i++)
                {
                    if (m_Counts[i] > 0)
                        tick = Math.Max(tick, m_TickTracks[i][m_Counts[i] - 1]);
                }
                return tick;
            }
        }

        public ulong EvictionCount => m_EvictionCount;

        public void Observe(RemotePresentationBatch batch)
        {
            if (!batch.IsValid)
                throw new ArgumentException("Remote presentation batch is invalid.", nameof(batch));
            int actorIndex = FindActor(batch.ActorId);
            if (actorIndex < 0)
                throw new InvalidOperationException($"Remote body sample targets unlocked Actor '{batch.ActorId}'.");

            IReadOnlyList<CharacterBodySample> input = batch.BodySamples;
            int inputCount = input.Count;
            if (inputCount == 0)
                return;

            ulong[] ticks = m_TickTracks[actorIndex];
            CharacterBodySample[] samples = m_SampleTracks[actorIndex];
            int count = m_Counts[actorIndex];
            int mergedCount = CountMergedSamples(ticks, samples, count, input);
            int retainedCount = Math.Min(mergedCount, m_CapacityPerActor);
            int skippedCount = mergedCount - retainedCount;
            CharacterBodySample[] mergedSamples = m_ObservationScratch;
            try
            {
                int existingIndex = 0;
                int inputIndex = 0;
                CharacterBodySample incoming = input[0];
                CharacterBodySample previous = default;
                for (int i = 0; i < mergedCount; i++)
                {
                    CharacterBodySample sample;
                    bool consumesInput;
                    if (inputIndex == inputCount ||
                        existingIndex < count && ticks[existingIndex] <= incoming.Tick.Value)
                    {
                        sample = samples[existingIndex++];
                        consumesInput = inputIndex < inputCount && sample.Tick == incoming.Tick;
                    }
                    else
                    {
                        sample = incoming;
                        consumesInput = true;
                    }
                    if (consumesInput && ++inputIndex < inputCount)
                        incoming = input[inputIndex];
                    if (i > 0 && previous.Tick.Value + 1UL == sample.Tick.Value &&
                        !WorldSolveBatchRequest.BodyEquals(previous.FinalBody, sample.BeforeBody))
                    {
                        throw new InvalidOperationException("Remote body timeline contains a discontinuous consecutive BeforeBody.");
                    }
                    if (i >= skippedCount)
                        mergedSamples[i - skippedCount] = sample;
                    previous = sample;
                }

                for (int index = 0; index < retainedCount; index++)
                    RetainSample(mergedSamples[index]);
                ReleaseSamples(actorIndex);
                for (int index = 0; index < retainedCount; index++)
                    ticks[index] = mergedSamples[index].Tick.Value;
                Array.Copy(mergedSamples, 0, samples, 0, retainedCount);
                m_Counts[actorIndex] = retainedCount;
                m_EvictionCount = checked(m_EvictionCount + (ulong)skippedCount);
            }
            finally
            {
                Array.Clear(mergedSamples, 0, retainedCount);
            }
        }

        public ServerAuthoritativeRemoteBodySelectionFrame Select(SimulationTick targetTick)
        {
            if (!IsPrimed)
                throw new InvalidOperationException("Remote body timeline has not completed observation priming.");
            for (int i = 0; i < m_LockedActors.Length; i++)
            {
                ActorId actorId = m_LockedActors[i];
                m_SelectionScratch[i] = SelectActor(
                    actorId,
                    m_TickTracks[i],
                    m_SampleTracks[i],
                    m_Counts[i],
                    targetTick);
            }
            return new ServerAuthoritativeRemoteBodySelectionFrame(m_SelectionScratch);
        }

        public void WriteState(CanonicalWriter writer)
        {
            writer.WriteInt32(m_LockedActors.Length);
            for (int actor = 0; actor < m_LockedActors.Length; actor++)
            {
                writer.WriteText(m_LockedActors[actor].Value);
                writer.WriteInt32(m_Counts[actor]);
                for (int sample = 0; sample < m_Counts[actor]; sample++)
                    CharacterBodySample.CanonicalWrite(writer, m_SampleTracks[actor][sample]);
            }
        }

        internal ServerAuthoritativeRemoteBodyTimelineCheckpoint PrepareCheckpointStorage()
        {
            var actors = new ServerAuthoritativeRemoteBodyActorCheckpoint[m_LockedActors.Length];
            for (int i = 0; i < m_LockedActors.Length; i++)
                actors[i] = new ServerAuthoritativeRemoteBodyActorCheckpoint(m_LockedActors[i], m_CapacityPerActor);
            return new ServerAuthoritativeRemoteBodyTimelineCheckpoint(actors);
        }

        internal void CaptureInto(ServerAuthoritativeRemoteBodyTimelineCheckpoint checkpoint)
        {
            for (int actor = 0; actor < m_LockedActors.Length; actor++)
                checkpoint.Actors[actor].CaptureSamples(m_SampleTracks[actor].AsSpan(0, m_Counts[actor]));
        }

        public void Restore(ServerAuthoritativeRemoteBodyTimelineCheckpoint checkpoint)
        {
            if (checkpoint.Actors.Count != m_LockedActors.Length)
                throw new InvalidOperationException("Remote body timeline checkpoint roster does not match the locked roster.");
            for (int i = 0; i < m_LockedActors.Length; i++)
            {
                ServerAuthoritativeRemoteBodyActorCheckpoint actor = checkpoint.Actors[i];
                if (actor.ActorId != m_LockedActors[i])
                    throw new InvalidOperationException("Remote body timeline checkpoint Actor order does not match the locked roster.");
                for (int sampleIndex = 0; sampleIndex < actor.Samples.Count; sampleIndex++)
                    RetainSample(actor.Samples[sampleIndex]);
                ReleaseSamples(i);
                for (int sampleIndex = 0; sampleIndex < actor.Samples.Count; sampleIndex++)
                {
                    m_TickTracks[i][sampleIndex] = actor.Samples[sampleIndex].Tick.Value;
                    m_SampleTracks[i][sampleIndex] = actor.Samples[sampleIndex];
                }
                m_Counts[i] = actor.Samples.Count;
            }
        }

        internal void ReleaseStorage()
        {
            for (int index = 0; index < m_LockedActors.Length; index++)
            {
                ReleaseSamples(index);
                m_LockedActors[index].Release();
            }
        }

        void ReleaseSamples(int actorIndex)
        {
            int count = m_Counts[actorIndex];
            for (int index = 0; index < count; index++)
                ReleaseSample(m_SampleTracks[actorIndex][index]);
            Array.Clear(m_TickTracks[actorIndex], 0, count);
            Array.Clear(m_SampleTracks[actorIndex], 0, count);
            m_Counts[actorIndex] = 0;
            m_SelectionScratch[actorIndex] = default;
        }

        internal static void RetainSample(in CharacterBodySample sample)
        {
            sample.ActorId.Retain();
            sample.BeforeBody.ActorId.Retain();
            sample.FinalBody.ActorId.Retain();
        }

        internal static void ReleaseSample(in CharacterBodySample sample)
        {
            sample.FinalBody.ActorId.Release();
            sample.BeforeBody.ActorId.Release();
            sample.ActorId.Release();
        }

        static int CountMergedSamples(
            ulong[] ticks,
            CharacterBodySample[] samples,
            int count,
            IReadOnlyList<CharacterBodySample> input)
        {
            int existingIndex = Find(ticks, count, input[0].Tick.Value);
            if (existingIndex < 0)
                existingIndex = ~existingIndex;
            int mergedCount = count;
            for (int index = 0; index < input.Count; index++)
            {
                CharacterBodySample sample = input[index];
                while (existingIndex < count && ticks[existingIndex] < sample.Tick.Value)
                    existingIndex++;
                if (existingIndex < count && ticks[existingIndex] == sample.Tick.Value)
                {
                    if (!SampleEquals(samples[existingIndex], sample))
                        throw new InvalidOperationException($"Remote body timeline Tick '{sample.Tick}' changed canonical value.");
                }
                else
                {
                    mergedCount++;
                }
            }
            return mergedCount;
        }

        int FindActor(ActorId actorId) => Array.BinarySearch(m_LockedActors, actorId);

        static int Find(ulong[] ticks, int count, ulong tick)
        {
            int left = 0;
            int right = count - 1;
            while (left <= right)
            {
                int middle = left + (right - left) / 2;
                if (ticks[middle] == tick)
                    return middle;
                if (ticks[middle] < tick)
                    left = middle + 1;
                else
                    right = middle - 1;
            }
            return ~left;
        }

        ServerAuthoritativeRemoteBodySelection SelectActor(
            ActorId actorId,
            ulong[] ticks,
            CharacterBodySample[] samples,
            int count,
            SimulationTick targetTick)
        {
            int exactIndex = Find(ticks, count, targetTick.Value);
            if (exactIndex >= 0)
            {
                ref readonly CharacterBodySample exact = ref samples[exactIndex];
                return new ServerAuthoritativeRemoteBodySelection(
                    actorId,
                    targetTick,
                    exact.BeforeBody,
                    exact.FinalBody,
                    exact.Tick,
                    exact.Tick,
                    ObservedWorldConstraintSamplingKind.Exact);
            }
            ulong beforeTick = targetTick.Value > 1 ? targetTick.Value - 1 : targetTick.Value;
            BodySelection before = ResolveBodyAt(actorId, ticks, samples, count, beforeTick, true);
            BodySelection final = ResolveBodyAt(actorId, ticks, samples, count, targetTick.Value, false);
            ObservedWorldConstraintSamplingKind kind = before.Kind.CompareTo(final.Kind) >= 0
                ? before.Kind
                : final.Kind;
            return new ServerAuthoritativeRemoteBodySelection(
                actorId,
                targetTick,
                before.Body,
                final.Body,
                before.SourcePreviousTick.CompareTo(final.SourcePreviousTick) <= 0
                    ? before.SourcePreviousTick
                    : final.SourcePreviousTick,
                before.SourceCurrentTick.CompareTo(final.SourceCurrentTick) >= 0
                    ? before.SourceCurrentTick
                    : final.SourceCurrentTick,
                kind);
        }

        BodySelection ResolveBodyAt(
            ActorId actorId,
            ulong[] ticks,
            CharacterBodySample[] samples,
            int count,
            ulong targetTick,
            bool allowFirstBefore)
        {
            int exactIndex = Find(ticks, count, targetTick);
            if (exactIndex >= 0)
                return new BodySelection(samples[exactIndex].FinalBody, samples[exactIndex].Tick, samples[exactIndex].Tick, ObservedWorldConstraintSamplingKind.Exact);
            int insertionIndex = ~exactIndex;
            if (targetTick < ulong.MaxValue && insertionIndex < count && ticks[insertionIndex] == targetTick + 1)
            {
                ref readonly CharacterBodySample nextExact = ref samples[insertionIndex];
                return new BodySelection(nextExact.BeforeBody, nextExact.Tick, nextExact.Tick, ObservedWorldConstraintSamplingKind.Exact);
            }

            bool hasLower = insertionIndex > 0;
            bool hasUpper = insertionIndex < count;
            if (hasLower && hasUpper)
            {
                ref readonly CharacterBodySample lower = ref samples[insertionIndex - 1];
                ref readonly CharacterBodySample upper = ref samples[insertionIndex];
                Float32Scalar amount = Float32Scalar.FromDouble(
                    (targetTick - lower.Tick.Value) /
                    (double)(upper.Tick.Value - lower.Tick.Value));
                return new BodySelection(
                    InterpolateBody(actorId, lower.FinalBody, upper.FinalBody, amount),
                    lower.Tick,
                    upper.Tick,
                    ObservedWorldConstraintSamplingKind.Interpolation);
            }
            if (!hasLower && allowFirstBefore && count > 0 && ticks[0] == targetTick + 1)
            {
                return new BodySelection(samples[0].BeforeBody, samples[0].Tick, samples[0].Tick, ObservedWorldConstraintSamplingKind.Exact);
            }
            if (count == 0)
                throw new InvalidOperationException("Remote body timeline Actor track is empty.");
            ref readonly CharacterBodySample latest = ref samples[count - 1];
            if (targetTick < latest.Tick.Value)
                throw new InvalidOperationException($"Remote Actor '{actorId}' has no observation interval for Tick '{targetTick}'.");
            ulong extrapolationTicks = targetTick - latest.Tick.Value;
            if (extrapolationTicks > (ulong)m_MaximumExtrapolationTicks)
            {
                throw new InvalidOperationException(
                    $"Remote Actor '{actorId}' observation extrapolation '{extrapolationTicks}' exceeds configured maximum '{m_MaximumExtrapolationTicks}'.");
            }
            Float32Scalar seconds = Float32Scalar.FromDouble(extrapolationTicks / (double)m_TickRate);
            WorldBodyState body = latest.FinalBody;
            return new BodySelection(
                new WorldBodyState(
                    actorId,
                    body.Position + body.Velocity * seconds,
                    body.Yaw,
                    body.Velocity,
                    body.VerticalVelocity,
                    body.Grounded,
                    body.Collision),
                latest.Tick,
                latest.Tick,
                ObservedWorldConstraintSamplingKind.ConstantVelocityExtrapolation);
        }

        static WorldBodyState InterpolateBody(
            ActorId actorId,
            WorldBodyState from,
            WorldBodyState to,
            Float32Scalar amount)
        {
            Float32Scalar yawDelta = Float32Angle.Delta(from.Yaw, to.Yaw);
            return new WorldBodyState(
                actorId,
                from.Position + (to.Position - from.Position) * amount,
                new Float32Yaw(from.Yaw.Degrees + yawDelta * amount),
                from.Velocity + (to.Velocity - from.Velocity) * amount,
                from.VerticalVelocity + (to.VerticalVelocity - from.VerticalVelocity) * amount,
                amount < Float32Scalar.FromDouble(0.5d) ? from.Grounded : to.Grounded,
                amount < Float32Scalar.FromDouble(0.5d) ? from.Collision : to.Collision);
        }

        static bool SampleEquals(in CharacterBodySample left, in CharacterBodySample right) =>
            left.ActorId == right.ActorId && left.Tick == right.Tick &&
            WorldSolveBatchRequest.BodyEquals(left.BeforeBody, right.BeforeBody) &&
            WorldSolveBatchRequest.BodyEquals(left.FinalBody, right.FinalBody) &&
            left.AppliedDisplacement == right.AppliedDisplacement &&
            left.AppliedYawDegrees == right.AppliedYawDegrees;

        readonly struct BodySelection
        {
            public BodySelection(
                WorldBodyState body,
                SimulationTick sourcePreviousTick,
                SimulationTick sourceCurrentTick,
                ObservedWorldConstraintSamplingKind kind)
            {
                Body = body;
                SourcePreviousTick = sourcePreviousTick;
                SourceCurrentTick = sourceCurrentTick;
                Kind = kind;
            }

            public WorldBodyState Body { get; }
            public SimulationTick SourcePreviousTick { get; }
            public SimulationTick SourceCurrentTick { get; }
            public ObservedWorldConstraintSamplingKind Kind { get; }
        }
    }

    internal sealed class ServerAuthoritativeRemoteBodyTimelineCheckpoint
    {
        readonly ServerAuthoritativeRemoteBodyActorCheckpoint[] m_Actors;
        static readonly Comparison<ServerAuthoritativeRemoteBodyActorCheckpoint> s_CompareActorsByActor =
            (left, right) => left.ActorId.CompareTo(right.ActorId);

        public ServerAuthoritativeRemoteBodyTimelineCheckpoint(
            ServerAuthoritativeRemoteBodyActorCheckpoint[] actors)
        {
            ServerAuthoritativeRemoteBodyActorCheckpoint[] values = actors ??
                throw new ArgumentNullException(nameof(actors));
            Array.Sort(values, s_CompareActorsByActor);
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == null || i > 0 && values[i - 1].ActorId == values[i].ActorId)
                    throw new ArgumentException("Remote body checkpoint roster is invalid.", nameof(actors));
            }
            m_Actors = values;
        }

        public IReadOnlyList<ServerAuthoritativeRemoteBodyActorCheckpoint> Actors => m_Actors;

        internal void ClearPrepared()
        {
            for (int actor = 0; actor < m_Actors.Length; actor++)
                m_Actors[actor].ClearPrepared();
        }

        internal void RetainStorage()
        {
            for (int actorIndex = 0; actorIndex < m_Actors.Length; actorIndex++)
            {
                ServerAuthoritativeRemoteBodyActorCheckpoint actor = m_Actors[actorIndex];
                actor.ActorId.Retain();
                for (int sampleIndex = 0; sampleIndex < actor.Samples.Count; sampleIndex++)
                    ServerAuthoritativeRemoteBodyTimeline.RetainSample(actor.Samples[sampleIndex]);
            }
        }

        internal void ReleaseStorage()
        {
            for (int actorIndex = 0; actorIndex < m_Actors.Length; actorIndex++)
            {
                ServerAuthoritativeRemoteBodyActorCheckpoint actor = m_Actors[actorIndex];
                for (int sampleIndex = 0; sampleIndex < actor.Samples.Count; sampleIndex++)
                    ServerAuthoritativeRemoteBodyTimeline.ReleaseSample(actor.Samples[sampleIndex]);
                actor.ActorId.Release();
            }
        }
    }

    internal sealed class ServerAuthoritativeRemoteBodyActorCheckpoint
    {
        readonly IReadOnlyList<CharacterBodySample> m_Samples;
        readonly List<CharacterBodySample> m_PreparedSamples;
        static readonly Comparison<CharacterBodySample> s_CompareSamplesByTick =
            (left, right) => left.Tick.CompareTo(right.Tick);

        internal ServerAuthoritativeRemoteBodyActorCheckpoint(ActorId actorId, int sampleCapacity)
        {
            ActorId = actorId;
            m_PreparedSamples = new List<CharacterBodySample>(sampleCapacity);
            m_Samples = m_PreparedSamples;
        }

        internal void CaptureSamples(ReadOnlySpan<CharacterBodySample> samples)
        {
            for (int index = 0; index < samples.Length; index++)
            {
                ServerAuthoritativeRemoteBodyTimeline.RetainSample(samples[index]);
                m_PreparedSamples.Add(samples[index]);
            }
        }

        internal void ClearPrepared() => m_PreparedSamples.Clear();

        public ServerAuthoritativeRemoteBodyActorCheckpoint(
            ActorId actorId,
            CharacterBodySample[] samples)
        {
            if (!actorId.IsValid)
                throw new ArgumentException("Remote body checkpoint ActorId is invalid.", nameof(actorId));
            ActorId = actorId;
            CharacterBodySample[] values = samples ?? Array.Empty<CharacterBodySample>();
            Array.Sort(values, s_CompareSamplesByTick);
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i].ActorId != actorId || i > 0 && values[i - 1].Tick == values[i].Tick)
                    throw new ArgumentException("Remote body checkpoint sample order is invalid.", nameof(samples));
                if (i > 0 && values[i - 1].Tick.Value + 1 == values[i].Tick.Value &&
                    !WorldSolveBatchRequest.BodyEquals(values[i - 1].FinalBody, values[i].BeforeBody))
                {
                    throw new InvalidOperationException("Remote body timeline contains a discontinuous consecutive BeforeBody.");
                }
            }
            m_Samples = values;
        }

        public ActorId ActorId { get; }
        public IReadOnlyList<CharacterBodySample> Samples => m_Samples;
    }
}
