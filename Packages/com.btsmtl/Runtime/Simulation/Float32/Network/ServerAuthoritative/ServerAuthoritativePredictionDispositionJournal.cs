using System;
using System.Buffers;
using System.Collections.Generic;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.ServerAuthoritative
{
    internal sealed class ServerAuthoritativePredictionDispositionJournal
    {
        readonly int m_Capacity;
        readonly EventId[] m_Keys;
        readonly ServerAuthoritativeJournalEntry[] m_Entries;
        int m_Count;

        public ServerAuthoritativePredictionDispositionJournal(int historyCapacity)
        {
            if (historyCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(historyCapacity));
            m_Capacity = checked(historyCapacity * 64);
            m_Keys = new EventId[m_Capacity];
            m_Entries = new ServerAuthoritativeJournalEntry[m_Capacity];
        }

        public ulong Cursor { get; private set; }
        public int Count => m_Count;
        public int LastRejectedCount { get; private set; }

        public bool WasCommitted(EventId eventId)
        {
            int index = Find(eventId);
            return index >= 0 &&
                m_Entries[index].Disposition != ServerAuthoritativeEventDisposition.PredictedRejected;
        }

        public void Record(ServerAuthoritativeJournalEntry entry, ulong firstRetainedHistoryTick)
        {
            ulong cursor = Cursor;
            Record(m_Keys, m_Entries, ref m_Count, ref cursor, entry, firstRetainedHistoryTick, m_Capacity);
            Cursor = cursor;
        }

        public void Prune(ulong firstRetainedHistoryTick) =>
            Prune(m_Keys, m_Entries, ref m_Count, firstRetainedHistoryTick);

        public void ApplyConfirmation(
            SimulationTick authorityTick,
            ServerAuthoritativeEventHorizon horizon,
            ulong firstRetainedHistoryTick)
        {
            ulong cursor = Cursor;
            int rejectedCount = 0;
            for (int i = 0; i < m_Count; i++)
            {
                ServerAuthoritativeJournalEntry entry = m_Entries[i];
                if (entry.Tick.Value > authorityTick.Value ||
                    entry.Disposition == ServerAuthoritativeEventDisposition.AuthorityConfirmed ||
                    entry.Disposition == ServerAuthoritativeEventDisposition.PredictedRejected)
                {
                    continue;
                }
                bool confirmed = !horizon.IsEmpty &&
                    (entry.Sequence < horizon.Sequence ||
                     entry.Sequence == horizon.Sequence && entry.EventId.Equals(horizon.EventId));
                if (!confirmed)
                    rejectedCount++;
                Record(
                    m_Keys,
                    m_Entries,
                    ref m_Count,
                    ref cursor,
                    new ServerAuthoritativeJournalEntry(
                        entry.EventId,
                        entry.Tick,
                        entry.Sequence,
                        confirmed
                            ? ServerAuthoritativeEventDisposition.AuthorityConfirmed
                            : ServerAuthoritativeEventDisposition.PredictedRejected),
                    firstRetainedHistoryTick,
                    m_Capacity);
            }
            if (!horizon.IsEmpty && Find(horizon.EventId) < 0)
            {
                Record(
                    m_Keys,
                    m_Entries,
                    ref m_Count,
                    ref cursor,
                    new ServerAuthoritativeJournalEntry(
                        horizon.EventId,
                        authorityTick,
                        horizon.Sequence,
                        ServerAuthoritativeEventDisposition.AuthorityConfirmed),
                    firstRetainedHistoryTick,
                    m_Capacity);
            }
            Cursor = cursor;
            LastRejectedCount = rejectedCount;
        }

        public void ApplyPrune(ulong firstRetainedHistoryTick) =>
            Prune(m_Keys, m_Entries, ref m_Count, firstRetainedHistoryTick);

        public ServerAuthoritativePredictionJournalCheckpoint PrepareConfirmation(
            SimulationTick authorityTick,
            ServerAuthoritativeEventHorizon horizon,
            ulong firstRetainedHistoryTick)
        {
            EventId[] keys = ArrayPool<EventId>.Shared.Rent(m_Count + 1);
            ServerAuthoritativeJournalEntry[] entries =
                ArrayPool<ServerAuthoritativeJournalEntry>.Shared.Rent(m_Count + 1);
            try
            {
                int count = m_Count;
                Array.Copy(m_Keys, keys, count);
                Array.Copy(m_Entries, entries, count);
                ulong cursor = Cursor;
                int rejectedCount = 0;
                for (int i = 0; i < count; i++)
                {
                    ServerAuthoritativeJournalEntry entry = entries[i];
                    if (entry.Tick.Value > authorityTick.Value ||
                        entry.Disposition == ServerAuthoritativeEventDisposition.AuthorityConfirmed ||
                        entry.Disposition == ServerAuthoritativeEventDisposition.PredictedRejected)
                    {
                        continue;
                    }
                    bool confirmed = !horizon.IsEmpty &&
                        (entry.Sequence < horizon.Sequence ||
                         entry.Sequence == horizon.Sequence && entry.EventId.Equals(horizon.EventId));
                    if (!confirmed)
                        rejectedCount++;
                    Record(
                        keys,
                        entries,
                        ref count,
                        ref cursor,
                        new ServerAuthoritativeJournalEntry(
                            entry.EventId,
                            entry.Tick,
                            entry.Sequence,
                            confirmed
                                ? ServerAuthoritativeEventDisposition.AuthorityConfirmed
                                : ServerAuthoritativeEventDisposition.PredictedRejected),
                        firstRetainedHistoryTick,
                        m_Capacity);
                }
                if (!horizon.IsEmpty && Find(keys, count, horizon.EventId) < 0)
                {
                    Record(
                        keys,
                        entries,
                        ref count,
                        ref cursor,
                        new ServerAuthoritativeJournalEntry(
                            horizon.EventId,
                            authorityTick,
                            horizon.Sequence,
                            ServerAuthoritativeEventDisposition.AuthorityConfirmed),
                        firstRetainedHistoryTick,
                        m_Capacity);
                }
                return CreateCheckpoint(keys, entries, count, cursor, rejectedCount);
            }
            finally
            {
                ArrayPool<EventId>.Shared.Return(keys);
                ArrayPool<ServerAuthoritativeJournalEntry>.Shared.Return(entries);
            }
        }

        public ServerAuthoritativePredictionJournalCheckpoint PreparePrune(
            ServerAuthoritativePredictionJournalCheckpoint checkpoint,
            ulong firstRetainedHistoryTick)
        {
            if (checkpoint == null)
                throw new ArgumentNullException(nameof(checkpoint));
            EventId[] keys = ArrayPool<EventId>.Shared.Rent(checkpoint.Entries.Count);
            ServerAuthoritativeJournalEntry[] entries =
                ArrayPool<ServerAuthoritativeJournalEntry>.Shared.Rent(checkpoint.Entries.Count);
            try
            {
                int count = checkpoint.Entries.Count;
                for (int i = 0; i < count; i++)
                {
                    keys[i] = checkpoint.Entries[i].Key;
                    entries[i] = checkpoint.Entries[i].Value;
                }
                Prune(keys, entries, ref count, firstRetainedHistoryTick);
                return CreateCheckpoint(keys, entries, count, checkpoint.Cursor, checkpoint.LastRejectedCount);
            }
            finally
            {
                ArrayPool<EventId>.Shared.Return(keys);
                ArrayPool<ServerAuthoritativeJournalEntry>.Shared.Return(entries);
            }
        }

        public void WriteState(CanonicalWriter writer)
        {
            ServerAuthoritativePredictionStateCodec.WriteJournalHeader(writer, Cursor, m_Count);
            for (int index = 0; index < m_Count; index++)
                ServerAuthoritativePredictionStateCodec.WriteJournalEntry(writer, m_Keys[index], m_Entries[index]);
        }

        public ServerAuthoritativePredictionJournalCheckpoint Capture()
        {
            var entries = new KeyValuePair<EventId, ServerAuthoritativeJournalEntry>[m_Count];
            for (int i = 0; i < entries.Length; i++)
                entries[i] = new KeyValuePair<EventId, ServerAuthoritativeJournalEntry>(m_Keys[i], m_Entries[i]);
            return new ServerAuthoritativePredictionJournalCheckpoint(entries, Cursor, LastRejectedCount);
        }

        public void Restore(ServerAuthoritativePredictionJournalCheckpoint checkpoint)
        {
            if (checkpoint == null)
                throw new ArgumentNullException(nameof(checkpoint));
            if (checkpoint.Entries.Count > m_Capacity)
                throw new InvalidOperationException("Prediction disposition journal checkpoint exceeds its configured capacity.");
            Array.Clear(m_Keys, 0, m_Count);
            Array.Clear(m_Entries, 0, m_Count);
            for (int i = 0; i < checkpoint.Entries.Count; i++)
            {
                KeyValuePair<EventId, ServerAuthoritativeJournalEntry> pair = checkpoint.Entries[i];
                if (i > 0 && checkpoint.Entries[i - 1].Key.CompareTo(pair.Key) >= 0)
                    throw new InvalidOperationException("Prediction disposition journal checkpoint EventId order is invalid.");
                m_Keys[i] = pair.Key;
                m_Entries[i] = pair.Value;
            }
            m_Count = checkpoint.Entries.Count;
            Cursor = checkpoint.Cursor;
            LastRejectedCount = checkpoint.LastRejectedCount;
        }

        ServerAuthoritativePredictionJournalCheckpoint CreateCheckpoint(
            EventId[] keys,
            ServerAuthoritativeJournalEntry[] entries,
            int count,
            ulong cursor,
            int lastRejectedCount)
        {
            var values = new KeyValuePair<EventId, ServerAuthoritativeJournalEntry>[count];
            for (int i = 0; i < values.Length; i++)
                values[i] = new KeyValuePair<EventId, ServerAuthoritativeJournalEntry>(keys[i], entries[i]);
            return new ServerAuthoritativePredictionJournalCheckpoint(values, cursor, lastRejectedCount);
        }

        static void Record(
            EventId[] keys,
            ServerAuthoritativeJournalEntry[] entries,
            ref int count,
            ref ulong cursor,
            ServerAuthoritativeJournalEntry entry,
            ulong firstRetainedHistoryTick,
            int capacity)
        {
            int entryIndex = Find(keys, count, entry.EventId);
            if (entryIndex < 0)
            {
                Prune(keys, entries, ref count, firstRetainedHistoryTick);
                if (count >= capacity)
                    throw new InvalidOperationException("Prediction disposition journal capacity is exhausted by live predicted events.");
                entryIndex = ~entryIndex;
                Array.Copy(keys, entryIndex, keys, entryIndex + 1, count - entryIndex);
                Array.Copy(entries, entryIndex, entries, entryIndex + 1, count - entryIndex);
                count++;
            }
            else if (entries[entryIndex].Disposition == ServerAuthoritativeEventDisposition.AuthorityConfirmed ||
                entries[entryIndex].Tick == entry.Tick &&
                entries[entryIndex].Sequence == entry.Sequence &&
                entries[entryIndex].Disposition == entry.Disposition)
            {
                return;
            }

            keys[entryIndex] = entry.EventId;
            entries[entryIndex] = entry;
            cursor = checked(cursor + 1);
        }

        static void Prune(
            EventId[] keys,
            ServerAuthoritativeJournalEntry[] entries,
            ref int count,
            ulong firstRetainedHistoryTick)
        {
            int outputIndex = 0;
            for (int i = 0; i < count; i++)
            {
                if (entries[i].Tick.Value >= firstRetainedHistoryTick ||
                    entries[i].Disposition == ServerAuthoritativeEventDisposition.PredictedCommitted ||
                    entries[i].Disposition == ServerAuthoritativeEventDisposition.SuppressedDuplicate)
                {
                    if (outputIndex != i)
                    {
                        keys[outputIndex] = keys[i];
                        entries[outputIndex] = entries[i];
                    }
                    outputIndex++;
                }
            }
            count = outputIndex;
        }

        int Find(EventId eventId) => Find(m_Keys, m_Count, eventId);

        static int Find(EventId[] keys, int count, EventId eventId)
        {
            int left = 0;
            int right = count - 1;
            while (left <= right)
            {
                int middle = left + (right - left) / 2;
                int comparison = keys[middle].CompareTo(eventId);
                if (comparison == 0)
                    return middle;
                if (comparison < 0)
                    left = middle + 1;
                else
                    right = middle - 1;
            }
            return ~left;
        }
    }

    internal sealed class ServerAuthoritativePredictionJournalCheckpoint
    {
        readonly KeyValuePair<EventId, ServerAuthoritativeJournalEntry>[] m_Entries;

        public ServerAuthoritativePredictionJournalCheckpoint(
            KeyValuePair<EventId, ServerAuthoritativeJournalEntry>[] entries,
            ulong cursor,
            int lastRejectedCount)
        {
            KeyValuePair<EventId, ServerAuthoritativeJournalEntry>[] values = entries ??
                throw new ArgumentNullException(nameof(entries));
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0 && values[i - 1].Key.CompareTo(values[i].Key) >= 0)
                    throw new ArgumentException("Prediction journal checkpoint EventId order is invalid.", nameof(entries));
            }
            m_Entries = values;
            Cursor = cursor;
            LastRejectedCount = lastRejectedCount;
        }

        public IReadOnlyList<KeyValuePair<EventId, ServerAuthoritativeJournalEntry>> Entries => m_Entries;
        public ulong Cursor { get; }
        public int LastRejectedCount { get; }
    }
}
