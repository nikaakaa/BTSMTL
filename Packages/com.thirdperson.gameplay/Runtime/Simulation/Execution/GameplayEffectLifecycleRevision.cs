using System;

namespace ThirdPersonSimulation
{
    internal enum GameplayEffectLifecycleRevisionKeyKind : byte
    {
        Instance = 1,
        Reconciliation = 2,
        ActiveSubject = 3
    }

    [ValueOperations]
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

    [ValueOperations]
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

}
