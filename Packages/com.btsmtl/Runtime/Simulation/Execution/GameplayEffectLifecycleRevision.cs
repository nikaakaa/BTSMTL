using System;
using System.Collections.Generic;
using System.Linq;

namespace ThirdPersonSimulation
{
    internal enum GameplayEffectLifecycleRevisionKeyKind : byte
    {
        Instance = 1,
        Reconciliation = 2,
        ActiveSubject = 3
    }

    [CanonicalContract]
    internal readonly partial struct GameplayEffectLifecycleRevisionKey : IComparable<GameplayEffectLifecycleRevisionKey>
    {
        GameplayEffectLifecycleRevisionKey(GameplayEffectLifecycleRevisionKeyKind kind, ulong instanceId,
            ActorId sourceActor, GameplayEffectReconciliationScope scope)
        {
            Kind = kind;
            InstanceId = instanceId;
            SourceActor = sourceActor;
            Scope = scope;
        }

        internal GameplayEffectLifecycleRevisionKeyKind Kind { get; }
        internal ulong InstanceId { get; }
        internal ActorId SourceActor { get; }
        internal GameplayEffectReconciliationScope Scope { get; }

        internal static GameplayEffectLifecycleRevisionKey ForInstance(ulong instanceId) =>
            new GameplayEffectLifecycleRevisionKey(GameplayEffectLifecycleRevisionKeyKind.Instance,
                instanceId, default, default);

        internal static GameplayEffectLifecycleRevisionKey ForActiveSubject(ulong instanceId) =>
            new GameplayEffectLifecycleRevisionKey(GameplayEffectLifecycleRevisionKeyKind.ActiveSubject,
                instanceId, default, default);

        internal static GameplayEffectLifecycleRevisionKey ForReconciliation(ActorId sourceActor,
            GameplayEffectReconciliationScope scope) =>
            new GameplayEffectLifecycleRevisionKey(GameplayEffectLifecycleRevisionKeyKind.Reconciliation,
                0, sourceActor, new GameplayEffectReconciliationScope(scope.Kind, scope.PredictionKey,
                    scope.Kind == GameplayEffectReconciliationScopeKind.PredictionKey ? string.Empty : scope.EffectId,
                    scope.Kind == GameplayEffectReconciliationScopeKind.Instance ? scope.PredictedInstanceId : 0));

        internal void Retain() => CanonicalRetain(in this);

        internal void Release() => CanonicalRelease(in this);

        public int CompareTo(GameplayEffectLifecycleRevisionKey other)
        {
            int result = ((byte)Kind).CompareTo((byte)other.Kind);
            if (result != 0)
                return result;
            if (Kind != GameplayEffectLifecycleRevisionKeyKind.Reconciliation)
                return InstanceId.CompareTo(other.InstanceId);
            result = SourceActor.CompareTo(other.SourceActor);
            if (result != 0)
                return result;
            result = Scope.PredictionKey.CompareTo(other.Scope.PredictionKey);
            if (result != 0)
                return result;
            result = ((byte)Scope.Kind).CompareTo((byte)other.Scope.Kind);
            if (result != 0)
                return result;
            result = Scope.EffectId.CompareTo(other.Scope.EffectId);
            return result != 0 ? result : Scope.PredictedInstanceId.CompareTo(other.Scope.PredictedInstanceId);
        }
    }

    [CanonicalContract]
    internal readonly partial struct GameplayEffectLifecycleRevisionEntry
    {
        internal GameplayEffectLifecycleRevisionEntry(ulong revision, ulong acceptedTick, ulong acceptedChangeCursor,
            GameplayEffectLifecycleKind operation, bool touchesActive, in GameplayEffectReconciliationResult result)
        {
            Revision = revision;
            AcceptedTick = acceptedTick;
            AcceptedChangeCursor = acceptedChangeCursor;
            Operation = operation;
            TouchesActive = touchesActive;
            Result = result;
        }

        internal ulong Revision { get; }
        internal ulong AcceptedTick { get; }
        internal ulong AcceptedChangeCursor { get; }
        internal GameplayEffectLifecycleKind Operation { get; }
        internal bool TouchesActive { get; }
        internal GameplayEffectReconciliationResult Result { get; }

        internal static GameplayEffectLifecycleRevisionEntry ForActiveSubject(ulong revision, ulong acceptedTick,
            ulong acceptedChangeCursor, GameplayEffectLifecycleKind operation) =>
            new GameplayEffectLifecycleRevisionEntry(revision, acceptedTick, acceptedChangeCursor, operation, true, default);

        internal void Retain() => CanonicalRetain(in this);
        internal void Release() => CanonicalRelease(in this);

        internal bool Matches(in GameplayEffectLifecycleRevisionEntry other)
        {
            GameplayEffectReconciliationResult left = Result;
            GameplayEffectReconciliationResult right = other.Result;
            GameplayEffectExecutionFailure leftFailure = left.Failure;
            GameplayEffectExecutionFailure rightFailure = right.Failure;
            return Revision == other.Revision && AcceptedTick == other.AcceptedTick &&
                AcceptedChangeCursor == other.AcceptedChangeCursor && TouchesActive == other.TouchesActive && Operation == other.Operation &&
                left.Kind == right.Kind && left.Scope.Kind == right.Scope.Kind &&
                left.Scope.PredictionKey == right.Scope.PredictionKey &&
                EqualText(left.Scope.EffectId, right.Scope.EffectId) &&
                left.Scope.PredictedInstanceId == right.Scope.PredictedInstanceId &&
                left.AuthoritativeInstanceId == right.AuthoritativeInstanceId &&
                left.AuthoritativeRevision == right.AuthoritativeRevision && left.RecordCount == right.RecordCount &&
                left.PredictedInstanceId == right.PredictedInstanceId && left.PredictedRevision == right.PredictedRevision &&
                left.CurrentRevision == right.CurrentRevision && EqualText(left.AttributeId, right.AttributeId) &&
                EqualText(left.Reason, right.Reason) && leftFailure.IsValid == rightFailure.IsValid &&
                (!leftFailure.IsValid || leftFailure.OwnerInstanceId == rightFailure.OwnerInstanceId &&
                    leftFailure.Trigger == rightFailure.Trigger && leftFailure.Tick == rightFailure.Tick &&
                    leftFailure.Code == rightFailure.Code && EqualText(leftFailure.OwnerEffectId, rightFailure.OwnerEffectId) &&
                    EqualText(leftFailure.RequestedEffectId, rightFailure.RequestedEffectId) &&
                    EqualText(leftFailure.Reason, rightFailure.Reason) && EqualText(leftFailure.ParameterId, rightFailure.ParameterId) &&
                    EqualText(leftFailure.CapacityDimension, rightFailure.CapacityDimension) &&
                    leftFailure.Required == rightFailure.Required && leftFailure.Available == rightFailure.Available &&
                    EqualContext(leftFailure.Context, rightFailure.Context));
        }

        static bool EqualText(CanonicalTextValue left, CanonicalTextValue right) => left.Equals(right);

        static bool EqualContext(in GameplayEffectContextIdentity left, in GameplayEffectContextIdentity right) =>
            left.SourceActor == right.SourceActor && left.TargetActor == right.TargetActor &&
            left.SourceActionInstanceId == right.SourceActionInstanceId && left.PredictionKey == right.PredictionKey &&
            left.GameplayResultId == right.GameplayResultId && left.SourceTick == right.SourceTick && left.Predicted == right.Predicted;
    }

    internal sealed class GameplayEffectPublishedIdentityReader
    {
        readonly Entry[] m_Entries;

        internal GameplayEffectPublishedIdentityReader(IEnumerable<string> identities)
        {
            m_Entries = identities.Distinct(StringComparer.Ordinal).Select(value => new Entry(value)).ToArray();
            Array.Sort(m_Entries, CompareEncodedEntries);
        }

        internal CanonicalTextValue Read(CanonicalReader reader)
        {
            ArraySegment<byte> bytes = reader.ReadTextSegment();
            if (bytes.Array == null)
                return default;
            if (bytes.Count == 0)
                return CanonicalTextValue.Empty;
            int index = Find(bytes.AsSpan());
            return index >= 0 ? CanonicalTextValue.FromString(m_Entries[index].Value) : reader.ResolveText(bytes);
        }

        internal string ReadPublished(CanonicalReader reader)
        {
            ArraySegment<byte> bytes = reader.ReadTextSegment();
            if (bytes.Array == null)
                return null;
            if (bytes.Count == 0)
                return string.Empty;
            int index = Find(bytes.AsSpan());
            if (index < 0)
                throw new System.IO.InvalidDataException("Gameplay Effect identity is not declared by its published content.");
            return m_Entries[index].Value;
        }

        internal string ResolvePublished(in CanonicalTextValue value)
        {
            if (value.IsNull)
                return null;
            if (value.IsEmpty)
                return string.Empty;
            int low = 0;
            int high = m_Entries.Length - 1;
            while (low <= high)
            {
                int middle = low + ((high - low) >> 1);
                int comparison = m_Entries[middle].Value.AsSpan().SequenceCompareTo(value.Span);
                if (comparison < 0)
                    low = middle + 1;
                else if (comparison > 0)
                    high = middle - 1;
                else
                    return m_Entries[middle].Value;
            }
            throw new System.IO.InvalidDataException("Identity is not declared by its published content.");
        }

        int Find(ReadOnlySpan<byte> encoded)
        {
            int low = 0;
            int high = m_Entries.Length - 1;
            while (low <= high)
            {
                int middle = low + ((high - low) >> 1);
                int comparison = CanonicalTextEncoding.CompareEncoded(m_Entries[middle].Encoded.AsSpan(), encoded);
                if (comparison < 0)
                    low = middle + 1;
                else if (comparison > 0)
                    high = middle - 1;
                else
                    return middle;
            }
            return -1;
        }

        static int CompareEncodedEntries(Entry left, Entry right) =>
            CanonicalTextEncoding.CompareEncoded(left.Encoded.AsSpan(), right.Encoded.AsSpan());

        readonly struct Entry
        {
            internal Entry(string value)
            {
                Value = value;
                Encoded = new byte[CanonicalTextEncoding.ByteCount(value.AsSpan())];
                CanonicalTextEncoding.Encode(value.AsSpan(), Encoded.AsSpan());
            }

            internal readonly string Value;
            internal readonly byte[] Encoded;
        }
    }

    internal static class GameplayEffectLifecycleRevisionCodec
    {
        internal static void Write(CanonicalWriter writer, in GameplayEffectLifecycleRevisionKey key,
            in GameplayEffectLifecycleRevisionEntry entry)
        {
            GameplayEffectLifecycleRevisionKey.CanonicalWrite(writer, in key);
            GameplayEffectLifecycleRevisionEntry.CanonicalWrite(writer, in entry);
        }

        internal static void Read(CanonicalReader reader,
            out GameplayEffectLifecycleRevisionKey key, out GameplayEffectLifecycleRevisionEntry entry)
        {
            key = GameplayEffectLifecycleRevisionKey.CanonicalRead(reader);
            if (key.Kind == GameplayEffectLifecycleRevisionKeyKind.Reconciliation)
            {
                GameplayEffectReconciliationScope scope = key.Scope;
                bool selected = scope.PredictionKey != 0 && (
                    scope.Kind == GameplayEffectReconciliationScopeKind.PredictionKey &&
                        scope.EffectId.IsEmpty && scope.PredictedInstanceId == 0 ||
                    scope.Kind == GameplayEffectReconciliationScopeKind.Effect &&
                        !scope.EffectId.IsEmpty && scope.PredictedInstanceId == 0 ||
                    scope.Kind == GameplayEffectReconciliationScopeKind.Instance &&
                        !scope.EffectId.IsEmpty && scope.PredictedInstanceId != 0);
                if (!selected)
                    throw new System.IO.InvalidDataException("Gameplay Effect lifecycle reconciliation identity is invalid.");
            }
            else if (key.InstanceId == 0)
                throw new System.IO.InvalidDataException("Gameplay Effect lifecycle instance identity is invalid.");
            entry = GameplayEffectLifecycleRevisionEntry.CanonicalRead(reader);
            if (entry.Revision == 0 || entry.AcceptedTick == 0)
                throw new System.IO.InvalidDataException("Gameplay Effect lifecycle revision result is invalid.");
            if (key.Kind == GameplayEffectLifecycleRevisionKeyKind.ActiveSubject)
                return;
            GameplayEffectReconciliationResult result = entry.Result;
            bool reconciliation = entry.Operation == GameplayEffectLifecycleKind.Confirmed ||
                entry.Operation == GameplayEffectLifecycleKind.Rejected || entry.Operation == GameplayEffectLifecycleKind.Corrected;
            if (result.Kind < GameplayEffectReconciliationResultKind.Confirmed ||
                result.Kind > GameplayEffectReconciliationResultKind.Applied || result.AuthoritativeRevision != entry.Revision ||
                reconciliation != (key.Kind == GameplayEffectLifecycleRevisionKeyKind.Reconciliation) ||
                reconciliation && GameplayEffectLifecycleRevisionKey.ForReconciliation(key.SourceActor, result.Scope).CompareTo(key) != 0)
                throw new System.IO.InvalidDataException("Gameplay Effect lifecycle revision result is invalid.");
        }

    }
}
