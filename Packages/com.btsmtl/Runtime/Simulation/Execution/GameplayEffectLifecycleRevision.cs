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

    internal readonly struct GameplayEffectLifecycleRevisionKey : IComparable<GameplayEffectLifecycleRevisionKey>
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

        internal void Retain()
        {
            SourceActor.Retain();
            Scope.Retain();
        }

        internal void Release()
        {
            SourceActor.Release();
            Scope.Release();
        }

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

    internal readonly struct GameplayEffectLifecycleRevisionEntry
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

        internal void Retain() => Result.Retain();
        internal void Release() => Result.Release();

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
            writer.WriteByte((byte)key.Kind);
            if (key.Kind != GameplayEffectLifecycleRevisionKeyKind.Reconciliation)
                writer.WriteUInt64(key.InstanceId);
            else
            {
                writer.WriteText(key.SourceActor.Value);
                WriteScope(writer, key.Scope);
            }
            writer.WriteUInt64(entry.Revision);
            writer.WriteUInt64(entry.AcceptedTick);
            writer.WriteUInt64(entry.AcceptedChangeCursor);
            writer.WriteByte((byte)entry.Operation);
            if (key.Kind == GameplayEffectLifecycleRevisionKeyKind.ActiveSubject)
                return;
            writer.WriteBoolean(entry.TouchesActive);
            WriteResult(writer, entry.Result);
        }

        internal static void Read(CanonicalReader reader, GameplayEffectPublishedIdentityReader identities,
            out GameplayEffectLifecycleRevisionKey key,
            out GameplayEffectLifecycleRevisionEntry entry)
        {
            GameplayEffectLifecycleRevisionKeyKind kind = (GameplayEffectLifecycleRevisionKeyKind)reader.ReadByte();
            if (kind == GameplayEffectLifecycleRevisionKeyKind.Instance ||
                kind == GameplayEffectLifecycleRevisionKeyKind.ActiveSubject)
            {
                ulong instanceId = reader.ReadUInt64();
                if (instanceId == 0)
                    throw new System.IO.InvalidDataException("Gameplay Effect lifecycle instance identity is invalid.");
                key = kind == GameplayEffectLifecycleRevisionKeyKind.Instance
                    ? GameplayEffectLifecycleRevisionKey.ForInstance(instanceId)
                    : GameplayEffectLifecycleRevisionKey.ForActiveSubject(instanceId);
            }
            else if (kind == GameplayEffectLifecycleRevisionKeyKind.Reconciliation)
            {
                ActorId source = new ActorId(reader.ReadText());
                GameplayEffectReconciliationScope scope = ReadScope(reader, identities);
                bool selected = scope.PredictionKey != 0 && (
                    scope.Kind == GameplayEffectReconciliationScopeKind.PredictionKey &&
                        scope.EffectId.IsEmpty && scope.PredictedInstanceId == 0 ||
                    scope.Kind == GameplayEffectReconciliationScopeKind.Effect &&
                        !scope.EffectId.IsEmpty && scope.PredictedInstanceId == 0 ||
                    scope.Kind == GameplayEffectReconciliationScopeKind.Instance &&
                        !scope.EffectId.IsEmpty && scope.PredictedInstanceId != 0);
                if (!selected)
                    throw new System.IO.InvalidDataException("Gameplay Effect lifecycle reconciliation identity is invalid.");
                key = GameplayEffectLifecycleRevisionKey.ForReconciliation(source, scope);
            }
            else
                throw new System.IO.InvalidDataException("Gameplay Effect lifecycle revision key kind is invalid.");
            ulong revision = reader.ReadUInt64();
            ulong tick = reader.ReadUInt64();
            ulong cursor = reader.ReadUInt64();
            GameplayEffectLifecycleKind operation = (GameplayEffectLifecycleKind)reader.ReadByte();
            if (revision == 0 || tick == 0 || operation < GameplayEffectLifecycleKind.Applied ||
                operation > GameplayEffectLifecycleKind.Overflow)
                throw new System.IO.InvalidDataException("Gameplay Effect lifecycle revision result is invalid.");
            if (kind == GameplayEffectLifecycleRevisionKeyKind.ActiveSubject)
            {
                entry = GameplayEffectLifecycleRevisionEntry.ForActiveSubject(revision, tick, cursor, operation);
                return;
            }
            bool touchesActive = reader.ReadBoolean();
            GameplayEffectReconciliationResult result = ReadResult(reader, identities);
            bool reconciliation = operation == GameplayEffectLifecycleKind.Confirmed ||
                operation == GameplayEffectLifecycleKind.Rejected || operation == GameplayEffectLifecycleKind.Corrected;
            if (result.Kind < GameplayEffectReconciliationResultKind.Confirmed ||
                result.Kind > GameplayEffectReconciliationResultKind.Applied || result.AuthoritativeRevision != revision ||
                reconciliation != (key.Kind == GameplayEffectLifecycleRevisionKeyKind.Reconciliation) ||
                reconciliation && GameplayEffectLifecycleRevisionKey.ForReconciliation(key.SourceActor, result.Scope).CompareTo(key) != 0)
                throw new System.IO.InvalidDataException("Gameplay Effect lifecycle revision result is invalid.");
            entry = new GameplayEffectLifecycleRevisionEntry(revision, tick, cursor, operation, touchesActive, result);
        }

        static void WriteScope(CanonicalWriter writer, in GameplayEffectReconciliationScope scope)
        {
            writer.WriteByte((byte)scope.Kind);
            writer.WriteUInt64(scope.PredictionKey);
            writer.WriteText(scope.EffectId);
            writer.WriteUInt64(scope.PredictedInstanceId);
        }

        static GameplayEffectReconciliationScope ReadScope(CanonicalReader reader, GameplayEffectPublishedIdentityReader identities) =>
            new GameplayEffectReconciliationScope((GameplayEffectReconciliationScopeKind)reader.ReadByte(),
                reader.ReadUInt64(), identities.Read(reader), reader.ReadUInt64());

        static void WriteResult(CanonicalWriter writer, in GameplayEffectReconciliationResult result)
        {
            writer.WriteByte((byte)result.Kind);
            WriteScope(writer, result.Scope);
            writer.WriteUInt64(result.AuthoritativeInstanceId);
            writer.WriteUInt64(result.AuthoritativeRevision);
            writer.WriteInt32(result.RecordCount);
            writer.WriteUInt64(result.PredictedInstanceId);
            writer.WriteUInt64(result.PredictedRevision);
            writer.WriteUInt64(result.CurrentRevision);
            writer.WriteText(result.AttributeId);
            writer.WriteText(result.Reason);
            WriteFailure(writer, result.Failure);
        }

        static GameplayEffectReconciliationResult ReadResult(CanonicalReader reader, GameplayEffectPublishedIdentityReader identities) =>
            new GameplayEffectReconciliationResult((GameplayEffectReconciliationResultKind)reader.ReadByte(),
                ReadScope(reader, identities), reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadInt32(),
                reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadUInt64(), identities.Read(reader),
                reader.ReadText(), ReadFailure(reader, identities));

        static void WriteFailure(CanonicalWriter writer, in GameplayEffectExecutionFailure failure)
        {
            writer.WriteBoolean(failure.IsValid);
            if (!failure.IsValid)
                return;
            writer.WriteText(failure.OwnerEffectId);
            writer.WriteUInt64(failure.OwnerInstanceId);
            writer.WriteByte((byte)failure.Trigger);
            writer.WriteText(failure.RequestedEffectId);
            WriteContext(writer, failure.Context);
            writer.WriteUInt64(failure.Tick);
            writer.WriteByte((byte)failure.Code);
            writer.WriteText(failure.Reason);
            writer.WriteText(failure.ParameterId);
            writer.WriteText(failure.CapacityDimension);
            writer.WriteInt32(failure.Required);
            writer.WriteInt32(failure.Available);
        }

        static GameplayEffectExecutionFailure ReadFailure(CanonicalReader reader, GameplayEffectPublishedIdentityReader identities) =>
            reader.ReadBoolean() ? new GameplayEffectExecutionFailure(identities.Read(reader), reader.ReadUInt64(),
                (GameplayEffectLifecycleKind)reader.ReadByte(), identities.Read(reader), ReadContext(reader),
                reader.ReadUInt64(), (GameplayEffectApplyResultKind)reader.ReadByte(), reader.ReadText(),
                identities.Read(reader), reader.ReadText(), reader.ReadInt32(), reader.ReadInt32()) : default;

        static void WriteContext(CanonicalWriter writer, in GameplayEffectContextIdentity context)
        {
            writer.WriteBoolean(context.IsValid);
            if (!context.IsValid)
                return;
            writer.WriteText(context.SourceActor.Value);
            writer.WriteText(context.TargetActor.Value);
            writer.WriteUInt64(context.SourceActionInstanceId);
            writer.WriteUInt64(context.PredictionKey);
            writer.WriteUInt64(context.GameplayResultId);
            writer.WriteUInt64(context.SourceTick);
            writer.WriteBoolean(context.Predicted);
            GameplayEffectScope.CanonicalWrite(writer, context.Scope);
            context.Holder.Write(writer);
            context.Success.Write(writer);
        }

        static GameplayEffectContextIdentity ReadContext(CanonicalReader reader) =>
            reader.ReadBoolean() ? new GameplayEffectContextIdentity(new ActorId(reader.ReadText()),
                new ActorId(reader.ReadText()), reader.ReadUInt64(), reader.ReadUInt64(),
                reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadBoolean(),
                GameplayEffectScope.CanonicalRead(reader), GameplayEffectHolderIdentity.Read(reader), GameplayEffectSuccessContext.Read(reader)) : default;
    }
}
