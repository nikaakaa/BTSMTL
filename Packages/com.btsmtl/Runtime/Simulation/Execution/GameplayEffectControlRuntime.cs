using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    internal sealed class GameplayEffectControlRuntime<
        TApplication,
        TSpec,
        TActive,
        TPrediction,
        TTagQuery,
        TSavepoint>
        where TActive : class, IGameplayEffectActiveControl<TSpec>
        where TPrediction : class, IGameplayEffectPredictionControl<TSpec>
    {
        readonly IGameplayEffectControlPort<TApplication, TSpec, TActive, TPrediction, TTagQuery, TSavepoint> m_Port;
        readonly HashSet<ulong> m_ConfirmedInstances = new HashSet<ulong>();
        readonly HashSet<ulong> m_ChangedLifecycleSubjects = new HashSet<ulong>();
        readonly Predicate<ulong> m_RemoveUnreferencedLifecycleSubject;
        PendingAdditionalApplication[] m_PendingAdditional = Array.Empty<PendingAdditionalApplication>();
        int m_AdditionalHead;
        int m_AdditionalCount;
        TPrediction m_CurrentPrediction;

        public GameplayEffectControlRuntime(
            IGameplayEffectControlPort<TApplication, TSpec, TActive, TPrediction, TTagQuery, TSavepoint> port)
        {
            m_Port = port ?? throw new ArgumentNullException(nameof(port));
            m_RemoveUnreferencedLifecycleSubject = LifecycleSubjectIsUnreferenced;
        }

        internal void Reset()
        {
            ClearAdditional();
            CancelPrediction();
            m_ConfirmedInstances.Clear();
            m_ChangedLifecycleSubjects.Clear();
        }

        internal void Prepare(GameplayEffectRuntimeCapacityLayout capacity)
        {
            m_PendingAdditional = new PendingAdditionalApplication[capacity.AdditionalQueueWidth];
            m_ConfirmedInstances.EnsureCapacity(capacity.Actor.PredictionRecordsPerKey);
            m_ChangedLifecycleSubjects.EnsureCapacity(capacity.ActiveEffectWidth + capacity.PredictionRecordWidth + capacity.ApplicationsPerTick);
        }

        public GameplayEffectApplyResult Apply(TApplication application)
        {
            GameplayEffectApplicationIdentity identity = m_Port.DescribeApplication(application);
            m_Port.TrimLifecycleRevisions();

            TSavepoint savepoint = m_Port.CreateSavepoint();
            int changeCount = m_Port.ChangeCount;
            try
            {
                GameplayEffectApplyResult result = ApplyCore(application, identity);
                if (!result.AcceptedMutation)
                {
                    GameplayEffectExecutionFailure failure = DescribeFailure(result, identity.EffectId,
                        result.InstanceId, result.Kind == GameplayEffectApplyResultKind.OverflowRejected
                            ? GameplayEffectLifecycleKind.Overflow : GameplayEffectLifecycleKind.Applied,
                        identity.EffectId, identity.Context);
                    RestoreOperation(savepoint, changeCount);
                    m_Port.EmitFailure(failure);
                    return new GameplayEffectApplyResult(failure);
                }
                if (!FlushAdditional(out GameplayEffectExecutionFailure additionalFailure))
                {
                    RestoreOperation(savepoint, changeCount);
                    m_Port.EmitFailure(additionalFailure);
                    return new GameplayEffectApplyResult(additionalFailure);
                }
                if (!m_Port.TryReserveChanges(changeCount, out GameplayEffectExecutionFailure outputFailure))
                {
                    RestoreOperation(savepoint, changeCount);
                    m_Port.EmitFailure(outputFailure);
                    return new GameplayEffectApplyResult(outputFailure);
                }
                m_Port.Release(savepoint);
                return result;
            }
            catch
            {
                if (m_Port.SavepointIsActive(savepoint))
                    RestoreOperation(savepoint, changeCount);
                throw;
            }
        }

        public GameplayEffectRemoveResult Remove(GameplayEffectRemoveRequest<TTagQuery> request)
        {
            if (m_Port.IsDisposed)
                return new GameplayEffectRemoveResult(GameplayEffectRemoveResultKind.Disposed, default);
            if (!RemovalRequestIsValid(request))
                return new GameplayEffectRemoveResult(GameplayEffectRemoveResultKind.InvalidRequest, default);
            m_Port.TrimLifecycleRevisions();
            var context = new GameplayEffectContextIdentity(m_Port.ActorId, m_Port.ActorId, 0, 0, 0, m_Port.Tick, false);
            var identity = new GameplayEffectApplicationIdentity(request.EffectId, request.Handle, 0, context);

            m_Port.BeginRemoveResult();
            TSavepoint savepoint = m_Port.CreateSavepoint();
            int changeCount = m_Port.ChangeCount;
            try
            {
                IReadOnlyList<TActive> activeEffects = m_Port.AcquireActiveEffects();
                GameplayEffectExecutionFailure failure = default;
                try
                {
                    for (int i = 0; i < activeEffects.Count; i++)
                    {
                        TActive active = activeEffects[i];
                        if (!MatchesRemoval(active, request))
                            continue;
                        m_Port.AppendRemovedHandle(active.Handle);
                        failure = RemoveActive(active, DescribeLifecycleSpec(active), GameplayEffectLifecycleKind.Removed, 0);
                        if (failure.IsValid)
                            break;
                        if (request.Selector == GameplayEffectRemoveSelector.Handle)
                            break;
                    }
                }
                finally
                {
                    m_Port.ReleaseActiveEffects(activeEffects);
                }
                if (failure.IsValid || !FlushAdditional(out failure) || !m_Port.TryReserveChanges(changeCount, out failure))
                {
                    RestoreOperation(savepoint, changeCount);
                    m_Port.EmitFailure(failure);
                    return new GameplayEffectRemoveResult(GameplayEffectRemoveResultKind.ExecutionFailed,
                        default, failure);
                }
                m_Port.Release(savepoint);
                return new GameplayEffectRemoveResult(m_Port.RemovedHandles.IsEmpty
                    ? GameplayEffectRemoveResultKind.NoMatch : GameplayEffectRemoveResultKind.Removed,
                    m_Port.RemovedHandles);
            }
            catch
            {
                if (m_Port.SavepointIsActive(savepoint))
                    RestoreOperation(savepoint, changeCount);
                throw;
            }
        }

        public GameplayEffectExecutionFailure Advance()
        {
            m_Port.TrimLifecycleRevisions();
            if (!m_Port.HasActiveEffects)
                return default;
            TSavepoint savepoint = m_Port.CreateSavepoint();
            int changeCount = m_Port.ChangeCount;
            try
            {
                IReadOnlyList<TActive> activeEffects = m_Port.AcquireActiveEffects();
                GameplayEffectExecutionFailure failure = default;
                try
                {
                    for (int i = 0; i < activeEffects.Count; i++)
                    {
                        TActive active = activeEffects[i];
                        if (RemovalRequirementMet(active))
                        {
                            failure = RemoveActive(active, DescribeLifecycleSpec(active), GameplayEffectLifecycleKind.Removed, 0);
                            if (failure.IsValid)
                                break;
                            continue;
                        }
                        GameplayEffectPreparedSpec<TSpec> lifecycleSpec = DescribeLifecycleSpec(active);
                        m_Port.RegisterCause(active.Handle, lifecycleSpec, active.InstanceId);
                        bool ongoing = OngoingRequirementsMet(active);
                        if (!ongoing && !active.Inhibited)
                        {
                            bool prediction = BeginLifecyclePrediction(active, lifecycleSpec, GameplayEffectLifecycleKind.Inhibited, out failure);
                            if (failure.IsValid)
                                break;
                            m_Port.DeactivatePersistent(active);
                            active.Inhibited = true;
                            active.LifecycleContext = lifecycleSpec.Context;
                            active.LifecycleRevision = checked(active.LifecycleRevision + 1);
                            m_Port.RefreshActiveEffectsDirty();
                            m_Port.EmitLifecycle(active, GameplayEffectLifecycleKind.Inhibited);
                            if (prediction)
                                CompletePrediction();
                        }
                        else if (ongoing && active.Inhibited)
                        {
                            bool prediction = BeginLifecyclePrediction(active, lifecycleSpec, GameplayEffectLifecycleKind.Resumed, out failure);
                            if (failure.IsValid)
                                break;
                            active.Inhibited = false;
                            ActivatePersistent(active);
                            active.LifecycleContext = lifecycleSpec.Context;
                            active.LifecycleRevision = checked(active.LifecycleRevision + 1);
                            m_Port.RefreshActiveEffectsDirty();
                            m_Port.EmitLifecycle(active, GameplayEffectLifecycleKind.Resumed);
                            if (prediction)
                                CompletePrediction();
                        }
                        if (!active.Inhibited)
                        {
                            GameplayEffectPreparedSpec<TSpec> spec = m_Port.DescribeSpec(active.Spec);
                            ulong nextPeriod = m_Port.GetNextPeriod(active.InstanceId);
                            while (spec.PeriodTicks > 0 && nextPeriod <= m_Port.Tick &&
                                   (active.EndTick == 0 || nextPeriod < active.EndTick))
                            {
                                failure = ExecutePeriod(active, DescribeLifecycleSpec(active), 0);
                                if (failure.IsValid)
                                    break;
                                nextPeriod = CheckedAdd(nextPeriod, spec.PeriodTicks);
                                m_Port.SetNextPeriod(active.InstanceId, nextPeriod);
                            }
                            if (failure.IsValid)
                                break;
                            failure = InvokeWhileActive(active);
                            if (failure.IsValid)
                                break;
                        }
                        if (active.EndTick != 0 && m_Port.Tick >= active.EndTick)
                        {
                            failure = RemoveActive(active, DescribeLifecycleSpec(active), GameplayEffectLifecycleKind.Expired, 0);
                            if (failure.IsValid)
                                break;
                        }
                    }
                }
                finally
                {
                    m_Port.ReleaseActiveEffects(activeEffects);
                }
                if (failure.IsValid || !FlushAdditional(out failure) || !m_Port.TryReserveChanges(changeCount, out failure))
                {
                    RestoreOperation(savepoint, changeCount);
                    m_Port.EmitFailure(failure);
                    return failure;
                }
                m_Port.Release(savepoint);
                return default;
            }
            catch
            {
                if (m_Port.SavepointIsActive(savepoint))
                    RestoreOperation(savepoint, changeCount);
                throw;
            }
        }

        public GameplayEffectReconciliationResult ApplyLifecycle(GameplayEffectLifecycleCommand<TApplication> command)
        {
            if (m_Port.IsDisposed)
                return LifecycleResult(command, GameplayEffectReconciliationResultKind.Disposed);
            bool reconciliation = IsReconciliation(command.Kind);
            if (reconciliation && !ScopeIsValid(command.Scope))
            {
                GameplayEffectReconciliationResult rejected = LifecycleResult(command,
                    GameplayEffectReconciliationResultKind.InvalidRequest, reason: "ReconciliationScopeMissing");
                m_Port.EmitReconciliation(rejected, command.Context);
                return rejected;
            }
            m_Port.TrimLifecycleRevisions();
            GameplayEffectLifecycleRevisionKey key = LifecycleRevisionKey(command);
            bool retained = m_Port.TryGetLifecycleRevision(key, out GameplayEffectLifecycleRevisionEntry previous);
            if (retained && command.LifecycleRevision <= previous.Revision)
            {
                if (command.LifecycleRevision == previous.Revision && !previous.Result.Succeeded)
                    return previous.Result;
                return LifecycleResult(command, GameplayEffectReconciliationResultKind.Duplicate,
                    previous.Result.RecordCount, previous.Result.PredictedInstanceId,
                    previous.Result.PredictedRevision, previous.Revision, reason: "LifecycleRevisionAlreadyHandled");
            }

            if (!retained && m_Port.LifecycleRevisionCount == m_Port.LifecycleRevisionCapacity)
            {
                var failure = new GameplayEffectExecutionFailure(command.EffectId, command.InstanceId,
                    command.Kind, command.EffectId, command.Context, m_Port.Tick,
                    GameplayEffectApplyResultKind.CapacityExceeded, "LifecycleRevisionCapacityExceeded",
                    capacityDimension: "LifecycleRevisions", required: m_Port.LifecycleRevisionCount + 1,
                    available: m_Port.LifecycleRevisionCapacity);
                GameplayEffectReconciliationResult denied = LifecycleResult(command,
                    GameplayEffectReconciliationResultKind.ExecutionFailed, failure: failure);
                m_Port.EmitFailure(failure);
                if (reconciliation)
                    m_Port.EmitReconciliation(denied, command.Context);
                return denied;
            }
            TSavepoint savepoint = m_Port.CreateSavepoint();
            int changeCount = m_Port.ChangeCount;
            try
            {
                GameplayEffectReconciliationResult result;
                if (!command.Context.IsValid || command.Context.TargetActor != m_Port.ActorId)
                {
                    var contextFailure = new GameplayEffectExecutionFailure(command.EffectId, command.InstanceId,
                        command.Kind, command.EffectId, command.Context, m_Port.Tick,
                        GameplayEffectApplyResultKind.InvalidContext, "LifecycleContextInvalid");
                    result = LifecycleResult(command, GameplayEffectReconciliationResultKind.ExecutionFailed, failure: contextFailure);
                }
                else
                    result = ApplyLifecycleCore(command);
                if (result.Succeeded && !FlushAdditional(out GameplayEffectExecutionFailure failure))
                    result = LifecycleResult(command, GameplayEffectReconciliationResultKind.ExecutionFailed, failure: failure);
                if (!result.Succeeded)
                {
                    RestoreOperation(savepoint, changeCount);
                    RecordLifecycleResult(key, command, result, false);
                    if (result.Failure.IsValid)
                        m_Port.EmitFailure(result.Failure);
                    if (reconciliation)
                        m_Port.EmitReconciliation(result, command.Context);
                    return result;
                }
                if (reconciliation && result.Kind != GameplayEffectReconciliationResultKind.Duplicate)
                    m_Port.EmitReconciliation(result, command.Context);
                bool touchesActive = m_ChangedLifecycleSubjects.Count != 0;
                if (!TryAdmitLifecycleSubjectRevisions(key, command, out GameplayEffectExecutionFailure revisionFailure))
                {
                    RestoreOperation(savepoint, changeCount);
                    result = LifecycleResult(command, GameplayEffectReconciliationResultKind.ExecutionFailed, failure: revisionFailure);
                    RecordLifecycleResult(key, command, result, false);
                    m_Port.EmitFailure(revisionFailure);
                    if (reconciliation)
                        m_Port.EmitReconciliation(result, command.Context);
                    return result;
                }
                if (!m_Port.TryReserveChanges(changeCount, out GameplayEffectExecutionFailure outputFailure))
                {
                    RestoreOperation(savepoint, changeCount);
                    result = LifecycleResult(command, GameplayEffectReconciliationResultKind.ExecutionFailed, failure: outputFailure);
                    RecordLifecycleResult(key, command, result, false);
                    m_Port.EmitFailure(outputFailure);
                    if (reconciliation)
                        m_Port.EmitReconciliation(result, command.Context);
                    return result;
                }
                RecordLifecycleSubjects(command);
                RecordLifecycleResult(key, command, result, touchesActive);
                m_Port.Release(savepoint);
                return result;
            }
            catch
            {
                if (m_Port.SavepointIsActive(savepoint))
                    RestoreOperation(savepoint, changeCount);
                throw;
            }
            finally
            {
                m_ChangedLifecycleSubjects.Clear();
            }
        }

        public void RecordRestoredLifecycleResult(GameplayEffectLifecycleCommand<TApplication> command,
            in GameplayEffectReconciliationResult result)
        {
            m_Port.TrimLifecycleRevisions();
            GameplayEffectLifecycleRevisionKey key = LifecycleRevisionKey(command);
            if (result.Failure.Code == GameplayEffectApplyResultKind.CapacityExceeded &&
                result.Failure.CapacityDimension == "LifecycleRevisions" &&
                !m_Port.TryGetLifecycleRevision(key, out _) && m_Port.LifecycleRevisionCount == m_Port.LifecycleRevisionCapacity)
                return;
            RecordLifecycleResult(key, command, result, false);
        }

        static GameplayEffectLifecycleRevisionKey LifecycleRevisionKey(GameplayEffectLifecycleCommand<TApplication> command) =>
            IsReconciliation(command.Kind)
                ? GameplayEffectLifecycleRevisionKey.ForReconciliation(command.Context.SourceActor, command.Scope)
                : GameplayEffectLifecycleRevisionKey.ForInstance(command.InstanceId);

        void RecordLifecycleResult(in GameplayEffectLifecycleRevisionKey key,
            GameplayEffectLifecycleCommand<TApplication> command, in GameplayEffectReconciliationResult result, bool touchesActive)
        {
            var entry = new GameplayEffectLifecycleRevisionEntry(command.LifecycleRevision, m_Port.Tick, m_Port.ChangeCursor,
                command.Kind, touchesActive, result);
            m_Port.SetLifecycleRevision(key, entry);
        }

        bool TryAdmitLifecycleSubjectRevisions(in GameplayEffectLifecycleRevisionKey requestKey,
            GameplayEffectLifecycleCommand<TApplication> command, out GameplayEffectExecutionFailure failure)
        {
            m_Port.TrimLifecycleSubjectRevisions();
            m_ChangedLifecycleSubjects.RemoveWhere(m_RemoveUnreferencedLifecycleSubject);
            int required = m_Port.LifecycleRevisionCount;
            if (!m_Port.TryGetLifecycleRevision(requestKey, out _))
                required++;
            foreach (ulong instanceId in m_ChangedLifecycleSubjects)
            {
                GameplayEffectLifecycleRevisionKey key = GameplayEffectLifecycleRevisionKey.ForActiveSubject(instanceId);
                if (!m_Port.TryGetLifecycleRevision(key, out _))
                    required++;
            }
            if (required > m_Port.LifecycleRevisionCapacity)
            {
                failure = new GameplayEffectExecutionFailure(command.EffectId, command.InstanceId,
                    command.Kind, command.EffectId, command.Context, m_Port.Tick,
                    GameplayEffectApplyResultKind.CapacityExceeded, "LifecycleRevisionCapacityExceeded",
                    capacityDimension: "LifecycleRevisions", required: required, available: m_Port.LifecycleRevisionCapacity);
                return false;
            }
            failure = default;
            return true;
        }

        void RecordLifecycleSubjects(GameplayEffectLifecycleCommand<TApplication> command)
        {
            foreach (ulong instanceId in m_ChangedLifecycleSubjects)
            {
                GameplayEffectLifecycleRevisionKey key = GameplayEffectLifecycleRevisionKey.ForActiveSubject(instanceId);
                GameplayEffectLifecycleRevisionEntry entry = GameplayEffectLifecycleRevisionEntry.ForActiveSubject(
                    command.LifecycleRevision, m_Port.Tick, m_Port.ChangeCursor, command.Kind);
                m_Port.SetLifecycleRevision(key, entry);
            }
        }

        bool LifecycleSubjectIsUnreferenced(ulong instanceId) =>
            !m_Port.HasLifecycleSubjectReferences(instanceId, m_Port.ChangeCursor);

        GameplayEffectReconciliationResult ApplyLifecycleCore(GameplayEffectLifecycleCommand<TApplication> command)
        {
            switch (command.Kind)
            {
                case GameplayEffectLifecycleKind.Confirmed:
                    return ConfirmPrediction(command);
                case GameplayEffectLifecycleKind.Rejected:
                    return RejectPrediction(command);
                case GameplayEffectLifecycleKind.Corrected:
                    return CorrectPrediction(command);
                case GameplayEffectLifecycleKind.Applied:
                {
                    GameplayEffectApplyResult applied = ApplyCore(command.AuthoritativeApplication,
                        new GameplayEffectApplicationIdentity(command.EffectId, command.InstanceId,
                            command.LifecycleRevision, command.Context));
                    if (applied.AcceptedMutation)
                    {
                        m_ChangedLifecycleSubjects.Add(applied.InstanceId);
                        return new GameplayEffectReconciliationResult(GameplayEffectReconciliationResultKind.Applied,
                            command.Scope, applied.InstanceId, command.LifecycleRevision, 0);
                    }
                    GameplayEffectExecutionFailure failure = DescribeFailure(applied, command.EffectId,
                        command.InstanceId, command.Kind, command.EffectId, command.Context);
                    return LifecycleResult(command, GameplayEffectReconciliationResultKind.ExecutionFailed, failure: failure);
                }
            }

            TActive active = m_Port.FindActiveByInstance(command.InstanceId);
            if (active == null || !m_Port.MatchesEffectId(active.Spec, command.EffectId))
            {
                var failure = new GameplayEffectExecutionFailure(command.EffectId, command.InstanceId,
                    command.Kind, command.EffectId, command.Context, m_Port.Tick,
                    GameplayEffectApplyResultKind.InvalidContext, "LifecycleInstanceMismatch");
                return LifecycleResult(command, GameplayEffectReconciliationResultKind.ExecutionFailed, failure: failure);
            }
            GameplayEffectPreparedSpec<TSpec> lifecycleSpec = DescribeMutationSpec(active, ConfirmedContext(command.Context));
            m_Port.RegisterCause(active.Handle, lifecycleSpec, active.InstanceId);
            GameplayEffectExecutionFailure operationFailure = default;
            switch (command.Kind)
            {
                case GameplayEffectLifecycleKind.Removed:
                case GameplayEffectLifecycleKind.Expired:
                    operationFailure = RemoveActive(active, lifecycleSpec, command.Kind, command.LifecycleRevision);
                    break;
                case GameplayEffectLifecycleKind.StackChanged:
                {
                    m_Port.DeactivatePersistent(active);
                    active.StackCount = command.StackCount;
                    active.StartTick = command.StartTick;
                    active.EndTick = command.EndTick;
                    active.LifecycleRevision = command.LifecycleRevision;
                    active.LifecycleContext = lifecycleSpec.Context;
                    m_Port.RefreshActiveEffectsDirty();
                    if (!active.Inhibited)
                        ActivatePersistent(active);
                    m_Port.EmitLifecycle(active, GameplayEffectLifecycleKind.StackChanged);
                    break;
                }
                case GameplayEffectLifecycleKind.Inhibited:
                case GameplayEffectLifecycleKind.Resumed:
                {
                    if (command.Kind == GameplayEffectLifecycleKind.Inhibited && !active.Inhibited)
                    {
                        m_Port.DeactivatePersistent(active);
                        active.Inhibited = true;
                    }
                    else if (command.Kind == GameplayEffectLifecycleKind.Resumed && active.Inhibited)
                    {
                        active.Inhibited = false;
                        ActivatePersistent(active);
                    }
                    active.LifecycleRevision = command.LifecycleRevision;
                    active.LifecycleContext = lifecycleSpec.Context;
                    m_Port.RefreshActiveEffectsDirty();
                    m_Port.EmitLifecycle(active, command.Kind);
                    break;
                }
                case GameplayEffectLifecycleKind.PeriodExecuted:
                    operationFailure = ExecutePeriod(active, lifecycleSpec, command.LifecycleRevision);
                    break;
                default:
                    return LifecycleResult(command, GameplayEffectReconciliationResultKind.InvalidRequest, reason: "LifecycleOperationNotAccepted");
            }
            if (!operationFailure.IsValid)
                m_ChangedLifecycleSubjects.Add(active.InstanceId);
            return LifecycleResult(command, operationFailure.IsValid
                ? GameplayEffectReconciliationResultKind.ExecutionFailed : GameplayEffectReconciliationResultKind.Applied,
                failure: operationFailure);
        }

        public void ClearConfirmedAction(ulong actionInstanceId)
        {
            if (actionInstanceId == 0)
                return;
            IReadOnlyList<ulong> keys = m_Port.AcquirePredictionKeys();
            bool removed = false;
            try
            {
                for (int keyIndex = 0; keyIndex < keys.Count; keyIndex++)
                {
                    if (!m_Port.TryGetPredictions(keys[keyIndex], out IReadOnlyList<TPrediction> records) || records.Count == 0)
                        continue;
                    for (int recordIndex = records.Count - 1; recordIndex >= 0; recordIndex--)
                    {
                        TPrediction record = records[recordIndex];
                        if (record.Confirmed && record.Context.SourceActor == m_Port.ActorId &&
                            record.SourceActionInstanceId == actionInstanceId)
                        {
                            m_Port.RemovePrediction(keys[keyIndex], record);
                            removed = true;
                        }
                    }
                }
            }
            finally
            {
                m_Port.ReleasePredictionKeys(keys);
            }
            if (removed)
                m_Port.RefreshJournalDirty();
        }

        GameplayEffectApplyResult ApplyCore(TApplication application, GameplayEffectApplicationIdentity identity)
        {
            if (!m_Port.TryPrepare(application, out GameplayEffectPreparedSpec<TSpec> spec, out GameplayEffectApplyResult failure))
                return failure;
            if (!ApplicationRequirementsMet(spec, out string reason))
                return new GameplayEffectApplyResult(GameplayEffectApplyResultKind.RequirementFailed, 0, 0, reason);
            if (spec.Descriptor.Duration == GameplayEffectDurationKind.Instant)
            {
                if (identity.AuthoritativeInstanceId != 0 && m_Port.FindActiveByInstance(identity.AuthoritativeInstanceId) != null)
                    return new GameplayEffectApplyResult(GameplayEffectApplyResultKind.InvalidContext,
                        0, identity.AuthoritativeInstanceId, "AuthoritativeInstanceConflict");
                return ApplyInstant(spec, identity.AuthoritativeInstanceId, identity.AuthoritativeLifecycleRevision);
            }
            TActive stack = FindStack(spec);
            if (stack == null && identity.AuthoritativeInstanceId != 0 && m_Port.FindActiveByInstance(identity.AuthoritativeInstanceId) != null)
                return new GameplayEffectApplyResult(GameplayEffectApplyResultKind.InvalidContext,
                    0, identity.AuthoritativeInstanceId, "AuthoritativeInstanceConflict");
            return stack == null
                ? ApplyNewActive(spec, identity.AuthoritativeInstanceId, identity.AuthoritativeLifecycleRevision)
                : ApplyStack(spec, stack);
        }

        GameplayEffectApplyResult ApplyInstant(
            GameplayEffectPreparedSpec<TSpec> spec,
            ulong authoritativeInstanceId,
            ulong authoritativeRevision)
        {
            ulong handle = m_Port.AllocateHandle();
            ulong instanceId = ResolveInstanceId(authoritativeInstanceId);
            ulong revision = authoritativeRevision > 0 ? authoritativeRevision : 1;
            m_Port.RegisterCause(handle, spec, instanceId);
            if (!BeginPrediction(spec, handle, instanceId, false, null, GameplayEffectLifecycleKind.Applied, out GameplayEffectExecutionFailure failure))
                return new GameplayEffectApplyResult(failure);
            if (!InvokeApplied(spec, instanceId, out failure) ||
                !InvokeExecute(spec, null, handle, instanceId, 1, false, out failure))
                return new GameplayEffectApplyResult(failure);
            m_Port.EmitLifecycle(spec, instanceId, GameplayEffectLifecycleKind.Applied, m_Port.Tick, m_Port.Tick, 1, revision, true);
            CompletePrediction();
            return new GameplayEffectApplyResult(GameplayEffectApplyResultKind.Applied, handle, instanceId, string.Empty);
        }

        GameplayEffectApplyResult ApplyNewActive(
            GameplayEffectPreparedSpec<TSpec> spec,
            ulong authoritativeInstanceId,
            ulong authoritativeRevision)
        {
            if (!m_Port.TryAdmitActive(spec, GameplayEffectLifecycleKind.Applied, out GameplayEffectExecutionFailure failure))
                return new GameplayEffectApplyResult(failure);
            ulong handle = m_Port.AllocateHandle();
            ulong instanceId = ResolveInstanceId(authoritativeInstanceId);
            ulong revision = authoritativeRevision > 0 ? authoritativeRevision : 1;
            ulong endTick = spec.Descriptor.Duration == GameplayEffectDurationKind.Duration
                ? CheckedAdd(m_Port.Tick, spec.DurationTicks)
                : 0;
            TActive active = m_Port.CreateActive(
                spec,
                handle,
                instanceId,
                m_Port.Tick,
                endTick,
                m_Port.AllocateHandle(),
                revision);
            m_Port.AddActive(active);
            if (spec.PeriodTicks > 0)
                m_Port.SetNextPeriod(instanceId, CheckedAdd(m_Port.Tick, spec.PeriodTicks));
            m_Port.RegisterCause(handle, spec, instanceId);
            if (!BeginPrediction(spec, handle, instanceId, true, null, GameplayEffectLifecycleKind.Applied, out failure))
                return new GameplayEffectApplyResult(failure);
            ActivatePersistent(active);
            if (!InvokeApplied(spec, instanceId, out failure))
                return new GameplayEffectApplyResult(failure);
            m_Port.EmitLifecycle(active, GameplayEffectLifecycleKind.Applied);
            if (spec.Descriptor.ExecuteOnApplication)
            {
                failure = ExecutePeriod(active, spec, 0);
                if (failure.IsValid)
                    return new GameplayEffectApplyResult(failure);
            }
            CompletePrediction();
            return new GameplayEffectApplyResult(GameplayEffectApplyResultKind.Applied, handle, instanceId, string.Empty);
        }

        GameplayEffectApplyResult ApplyStack(GameplayEffectPreparedSpec<TSpec> incoming, TActive active)
        {
            m_Port.RegisterCause(active.Handle, incoming, active.InstanceId);
            if (active.StackCount >= incoming.Descriptor.MaximumStacks)
                return ApplyOverflow(incoming, active);
            if (!BeginPrediction(incoming, active.Handle, active.InstanceId, false, active, GameplayEffectLifecycleKind.StackChanged, out GameplayEffectExecutionFailure failure))
                return new GameplayEffectApplyResult(failure);
            if (!active.Inhibited)
                m_Port.DeactivatePersistent(active);
            active.StackCount++;
            active.LifecycleContext = incoming.Context;
            active.LifecycleRevision = checked(active.LifecycleRevision + 1);
            UpdateStackTime(active, incoming);
            m_Port.RefreshActiveEffectsDirty();
            if (!active.Inhibited)
                ActivatePersistent(active);
            if (!InvokeApplied(incoming, active.InstanceId, out failure))
                return new GameplayEffectApplyResult(failure);
            m_Port.EmitLifecycle(incoming, active.InstanceId, GameplayEffectLifecycleKind.StackChanged,
                active.StartTick, active.EndTick, active.StackCount, active.LifecycleRevision, false);
            if (incoming.Descriptor.ExecuteOnApplication && !active.Inhibited)
            {
                failure = ExecutePeriod(active, DescribeMutationSpec(active, incoming.Context), 0);
                if (failure.IsValid)
                    return new GameplayEffectApplyResult(failure);
            }
            CompletePrediction();
            return new GameplayEffectApplyResult(GameplayEffectApplyResultKind.Applied, active.Handle, active.InstanceId, string.Empty);
        }

        GameplayEffectApplyResult ApplyOverflow(GameplayEffectPreparedSpec<TSpec> spec, TActive active)
        {
            switch (spec.Descriptor.Overflow)
            {
                case GameplayEffectOverflowKind.ReplaceOldest:
                {
                    GameplayEffectExecutionFailure failure = RemoveActive(active, DescribeMutationSpec(active, spec.Context), GameplayEffectLifecycleKind.Removed, 0);
                    if (failure.IsValid)
                        return new GameplayEffectApplyResult(failure);
                    return ApplyNewActive(spec, 0, 0);
                }
                case GameplayEffectOverflowKind.ApplyOverflowEffects:
                    if (!BeginPrediction(spec, active.Handle, active.InstanceId, false, active,
                        GameplayEffectLifecycleKind.Overflow, out GameplayEffectExecutionFailure overflowFailure))
                        return new GameplayEffectApplyResult(overflowFailure);
                    if (!InvokeOverflow(spec, active, out overflowFailure))
                        return new GameplayEffectApplyResult(overflowFailure);
                    active.LifecycleContext = spec.Context;
                    active.LifecycleRevision = checked(active.LifecycleRevision + 1);
                    m_Port.RefreshActiveEffectsDirty();
                    m_Port.EmitLifecycle(spec, active.InstanceId, GameplayEffectLifecycleKind.Overflow,
                        active.StartTick, active.EndTick, active.StackCount, active.LifecycleRevision, false);
                    CompletePrediction();
                    return new GameplayEffectApplyResult(GameplayEffectApplyResultKind.OverflowEffectsApplied, active.Handle, active.InstanceId, string.Empty);
                default:
                    return new GameplayEffectApplyResult(GameplayEffectApplyResultKind.OverflowRejected, active.Handle, active.InstanceId, "MaxStacksReached");
            }
        }

        GameplayEffectExecutionFailure ExecutePeriod(TActive active, GameplayEffectPreparedSpec<TSpec> spec, ulong authoritativeRevision)
        {
            if (!m_Port.TryAdmitLifecycle(spec, GameplayEffectLifecycleKind.PeriodExecuted, out GameplayEffectExecutionFailure failure))
                return failure;
            m_Port.RegisterCause(active.Handle, spec, active.InstanceId);
            bool prediction = BeginLifecyclePrediction(active, spec, GameplayEffectLifecycleKind.PeriodExecuted, out failure);
            if (failure.IsValid)
                return failure;
            if (!InvokeExecute(spec, active, active.Handle, active.InstanceId, active.StackCount, true, out failure))
                return failure;
            active.LifecycleContext = spec.Context;
            active.LifecycleRevision = authoritativeRevision > 0
                ? authoritativeRevision
                : checked(active.LifecycleRevision + 1);
            m_Port.RefreshActiveEffectsDirty();
            m_Port.EmitLifecycle(active, GameplayEffectLifecycleKind.PeriodExecuted);
            if (prediction)
                CompletePrediction();
            return default;
        }

        GameplayEffectPreparedSpec<TSpec> DescribeLifecycleSpec(TActive active)
        {
            GameplayEffectPreparedSpec<TSpec> spec = m_Port.DescribeSpec(active.Spec);
            GameplayEffectContextIdentity context = m_CurrentPrediction != null && m_CurrentPrediction.Handle == active.Handle
                ? m_CurrentPrediction.Context : active.LifecycleContext;
            return new GameplayEffectPreparedSpec<TSpec>(spec.Descriptor, context, spec.TargetSpec, spec.DurationTicks, spec.PeriodTicks);
        }

        GameplayEffectPreparedSpec<TSpec> DescribeMutationSpec(TActive active, GameplayEffectContextIdentity context)
        {
            GameplayEffectPreparedSpec<TSpec> captured = m_Port.DescribeSpec(active.Spec);
            return new GameplayEffectPreparedSpec<TSpec>(captured.Descriptor, context, captured.TargetSpec,
                captured.DurationTicks, captured.PeriodTicks);
        }

        static GameplayEffectContextIdentity ConfirmedContext(GameplayEffectContextIdentity context) =>
            new GameplayEffectContextIdentity(context.SourceActor, context.TargetActor,
                context.SourceActionInstanceId, context.PredictionKey, context.GameplayResultId, context.SourceTick, false,
                context.Scope, context.Holder, context.Success);

        bool BeginLifecyclePrediction(TActive active, GameplayEffectPreparedSpec<TSpec> spec,
            GameplayEffectLifecycleKind mutationKind, out GameplayEffectExecutionFailure failure)
        {
            failure = default;
            if (m_CurrentPrediction != null || !spec.Context.Predicted)
                return false;
            return BeginPrediction(spec, active.Handle, active.InstanceId, false, active, mutationKind, out failure);
        }

        GameplayEffectExecutionFailure RemoveActive(TActive active, GameplayEffectPreparedSpec<TSpec> spec,
            GameplayEffectLifecycleKind lifecycle, ulong authoritativeRevision)
        {
            m_Port.RegisterCause(active.Handle, spec, active.InstanceId);
            bool prediction = BeginLifecyclePrediction(active, spec, lifecycle, out GameplayEffectExecutionFailure failure);
            if (failure.IsValid)
                return failure;
            m_Port.DeactivatePersistent(active);
            if (!InvokeRemoved(active, spec, lifecycle, out failure))
                return failure;
            active.LifecycleContext = spec.Context;
            active.LifecycleRevision = authoritativeRevision > 0
                ? authoritativeRevision : checked(active.LifecycleRevision + 1);
            m_Port.RefreshActiveEffectsDirty();
            m_Port.EmitLifecycle(active, lifecycle);
            if (prediction)
                CompletePrediction();
            m_Port.RemoveActive(active);
            return default;
        }

        void RestoreActive(TActive active, GameplayEffectActiveControlSnapshot snapshot)
        {
            if (active.InstanceId != snapshot.InstanceId)
                m_Port.RemapActiveInstance(active, snapshot.InstanceId);
            active.StartTick = snapshot.StartTick;
            active.EndTick = snapshot.EndTick;
            active.StackCount = snapshot.StackCount;
            active.Inhibited = snapshot.Inhibited;
            active.LifecycleRevision = snapshot.LifecycleRevision;
            active.LifecycleContext = snapshot.LifecycleContext;
            m_Port.RefreshActiveEffectsDirty();
            m_Port.SetNextPeriod(active.InstanceId, snapshot.NextPeriodTick);
            if (!active.Inhibited)
                ActivatePersistent(active);
        }

        void UpdateStackTime(TActive active, GameplayEffectPreparedSpec<TSpec> incoming)
        {
            GameplayEffectPreparedSpec<TSpec> existing = m_Port.DescribeSpec(active.Spec);
            if (existing.Descriptor.Duration == GameplayEffectDurationKind.Duration)
            {
                switch (existing.Descriptor.DurationUpdate)
                {
                    case GameplayEffectDurationUpdateKind.Refresh:
                        active.StartTick = m_Port.Tick;
                        active.EndTick = CheckedAdd(m_Port.Tick, incoming.DurationTicks);
                        m_Port.RefreshActiveEffectsDirty();
                        break;
                    case GameplayEffectDurationUpdateKind.Extend:
                        active.EndTick = CheckedAdd(active.EndTick, incoming.DurationTicks);
                        m_Port.RefreshActiveEffectsDirty();
                        break;
                }
            }
            if (existing.Descriptor.PeriodUpdate == GameplayEffectPeriodUpdateKind.Reset && incoming.PeriodTicks > 0)
                m_Port.SetNextPeriod(active.InstanceId, CheckedAdd(m_Port.Tick, incoming.PeriodTicks));
        }

        bool ApplicationRequirementsMet(GameplayEffectPreparedSpec<TSpec> spec, out string reason)
        {
            int count = m_Port.ComponentCount(spec.TargetSpec);
            for (int i = 0; i < count; i++)
            {
                GameplayEffectComponentDescriptor component = m_Port.DescribeComponent(spec.TargetSpec, i);
                if (component.RequirementPhase != GameplayEffectRequirementPhase.Application)
                    continue;
                if (component.Kind == GameplayEffectComponentKind.TagRequirement &&
                    !m_Port.EvaluateTagRequirement(spec.TargetSpec, i))
                {
                    reason = "TagRequirementFailed";
                    return false;
                }
                if (component.Kind == GameplayEffectComponentKind.AttributeRequirement &&
                    !m_Port.EvaluateAttributeRequirement(spec.TargetSpec, i))
                {
                    reason = "AttributeRequirementFailed";
                    return false;
                }
            }
            reason = string.Empty;
            return true;
        }

        TActive FindStack(GameplayEffectPreparedSpec<TSpec> incoming)
        {
            if (incoming.Descriptor.Stacking == GameplayEffectStackingKind.None)
                return null;
            IReadOnlyList<TActive> activeEffects = m_Port.AcquireActiveEffects();
            try
            {
                for (int i = 0; i < activeEffects.Count; i++)
                {
                    TActive active = activeEffects[i];
                    GameplayEffectPreparedSpec<TSpec> existing = m_Port.DescribeSpec(active.Spec);
                    if (!string.Equals(existing.Descriptor.EffectId, incoming.Descriptor.EffectId, StringComparison.Ordinal))
                        continue;
                    if (!existing.Context.Scope.Equals(incoming.Context.Scope))
                        continue;
                    if (incoming.Descriptor.Stacking == GameplayEffectStackingKind.BySource &&
                        existing.Context.SourceActor != incoming.Context.SourceActor)
                    {
                        continue;
                    }
                    return active;
                }
                return null;
            }
            finally
            {
                m_Port.ReleaseActiveEffects(activeEffects);
            }
        }

        bool MatchesRemoval(TActive active, GameplayEffectRemoveRequest<TTagQuery> request)
        {
            return request.Selector switch
            {
                GameplayEffectRemoveSelector.Handle => active.Handle == request.Handle,
                GameplayEffectRemoveSelector.EffectId => m_Port.MatchesEffectId(active.Spec, request.EffectId),
                GameplayEffectRemoveSelector.SourceActor => m_Port.DescribeSpec(active.Spec).Context.SourceActor == request.SourceActor,
                GameplayEffectRemoveSelector.EffectTagQuery => m_Port.MatchesEffectTagQuery(active.Spec, request.TagQuery),
                GameplayEffectRemoveSelector.Holder => m_Port.DescribeSpec(active.Spec).Context.Holder.Equals(request.Holder),
                GameplayEffectRemoveSelector.HeldByAction => IsHeldByAction(active, request.HolderActionInstanceId),
                _ => false
            };
        }

        bool IsHeldByAction(TActive active, ulong actionInstanceId)
        {
            GameplayEffectHolderIdentity holder = m_Port.DescribeSpec(active.Spec).Context.Holder;
            return holder.Lifetime != GameplayEffectLifetimeKind.Independent &&
                holder.ActionInstanceId == actionInstanceId;
        }

        bool RemovalRequestIsValid(GameplayEffectRemoveRequest<TTagQuery> request)
        {
            return request.Selector switch
            {
                GameplayEffectRemoveSelector.Handle => request.Handle != 0,
                GameplayEffectRemoveSelector.EffectId => !request.EffectId.IsEmpty,
                GameplayEffectRemoveSelector.SourceActor => request.SourceActor.IsValid,
                GameplayEffectRemoveSelector.EffectTagQuery => m_Port.RemoveQueryIsValid(request.TagQuery),
                GameplayEffectRemoveSelector.Holder => request.Holder.Lifetime != GameplayEffectLifetimeKind.Independent,
                GameplayEffectRemoveSelector.HeldByAction => request.HolderActionInstanceId != 0,
                _ => false
            };
        }

        bool OngoingRequirementsMet(TActive active)
        {
            int count = m_Port.ComponentCount(active.Spec);
            for (int i = 0; i < count; i++)
            {
                GameplayEffectComponentDescriptor component = m_Port.DescribeComponent(active.Spec, i);
                if (component.RequirementPhase != GameplayEffectRequirementPhase.Ongoing)
                    continue;
                if (component.Kind == GameplayEffectComponentKind.TagRequirement &&
                    !m_Port.EvaluateTagRequirement(active.Spec, i))
                {
                    return false;
                }
                if (component.Kind == GameplayEffectComponentKind.AttributeRequirement &&
                    !m_Port.EvaluateAttributeRequirement(active.Spec, i))
                {
                    return false;
                }
            }
            return true;
        }

        bool RemovalRequirementMet(TActive active)
        {
            int count = m_Port.ComponentCount(active.Spec);
            for (int i = 0; i < count; i++)
            {
                GameplayEffectComponentDescriptor component = m_Port.DescribeComponent(active.Spec, i);
                if (component.RequirementPhase != GameplayEffectRequirementPhase.Removal)
                    continue;
                if (component.Kind == GameplayEffectComponentKind.TagRequirement &&
                    m_Port.EvaluateTagRequirement(active.Spec, i))
                {
                    return true;
                }
                if (component.Kind == GameplayEffectComponentKind.AttributeRequirement &&
                    m_Port.EvaluateAttributeRequirement(active.Spec, i))
                {
                    return true;
                }
            }
            return false;
        }

        void ActivatePersistent(TActive active)
        {
            int count = m_Port.ComponentCount(active.Spec);
            for (int i = 0; i < count; i++)
            {
                GameplayEffectComponentDescriptor component = m_Port.DescribeComponent(active.Spec, i);
                if (component.Kind == GameplayEffectComponentKind.Modifier &&
                    component.ModifierPhase == GameplayEffectModifierPhase.CurrentValue)
                {
                    m_Port.ActivateCurrentModifier(active, i);
                }
            }
            m_Port.ActivateGrantedTags(active);
        }

        bool InvokeApplied(GameplayEffectPreparedSpec<TSpec> spec, ulong instanceId, out GameplayEffectExecutionFailure failure)
        {
            int count = m_Port.ComponentCount(spec.TargetSpec);
            for (int i = 0; i < count; i++)
            {
                GameplayEffectComponentDescriptor component = m_Port.DescribeComponent(spec.TargetSpec, i);
                if (component.Kind == GameplayEffectComponentKind.AdditionalEffects)
                {
                    if (!EnqueueAdditional(spec, instanceId, i, GameplayEffectAdditionalTrigger.Applied, out failure))
                        return false;
                }
                else if (component.Kind == GameplayEffectComponentKind.Cue && component.CueTrigger == GameplayEffectCueTrigger.OnActive)
                    m_Port.EmitCue(spec, instanceId, i, true);
            }
            failure = default;
            return true;
        }

        bool InvokeExecute(
            GameplayEffectPreparedSpec<TSpec> spec,
            TActive active,
            ulong handle,
            ulong instanceId,
            int stackCount,
            bool period,
            out GameplayEffectExecutionFailure failure)
        {
            int count = m_Port.ComponentCount(spec.TargetSpec);
            for (int i = 0; i < count; i++)
            {
                GameplayEffectComponentDescriptor component = m_Port.DescribeComponent(spec.TargetSpec, i);
                if (component.Kind == GameplayEffectComponentKind.Execution ||
                    component.Kind == GameplayEffectComponentKind.Modifier && component.ModifierPhase == GameplayEffectModifierPhase.BaseValue)
                {
                    m_Port.ExecuteNumericComponent(spec, active, handle, stackCount, i);
                }
                else if (period && component.Kind == GameplayEffectComponentKind.AdditionalEffects)
                {
                    if (!EnqueueAdditional(spec, instanceId, i, GameplayEffectAdditionalTrigger.Period, out failure))
                        return false;
                }
                else if (component.Kind == GameplayEffectComponentKind.Cue && component.CueTrigger == GameplayEffectCueTrigger.Executed)
                {
                    m_Port.EmitCue(spec, instanceId, i, true);
                }
            }
            failure = default;
            return true;
        }

        GameplayEffectExecutionFailure InvokeWhileActive(TActive active)
        {
            GameplayEffectPreparedSpec<TSpec> spec = DescribeLifecycleSpec(active);
            int count = m_Port.ComponentCount(active.Spec);
            bool prediction = false;
            for (int i = 0; i < count; i++)
            {
                GameplayEffectComponentDescriptor component = m_Port.DescribeComponent(active.Spec, i);
                if (component.Kind != GameplayEffectComponentKind.Cue || component.CueTrigger != GameplayEffectCueTrigger.WhileActive)
                    continue;
                if (!prediction && spec.Context.Predicted)
                {
                    if (!BeginPrediction(spec, active.Handle, active.InstanceId, false, null, default,
                        out GameplayEffectExecutionFailure failure))
                        return failure;
                    prediction = true;
                }
                m_Port.EmitCue(spec, active.InstanceId, i, true);
            }
            if (prediction)
                CompletePrediction();
            return default;
        }

        bool InvokeRemoved(TActive active, GameplayEffectPreparedSpec<TSpec> spec,
            GameplayEffectLifecycleKind lifecycle, out GameplayEffectExecutionFailure failure)
        {
            GameplayEffectCueTrigger trigger = lifecycle == GameplayEffectLifecycleKind.Expired
                ? GameplayEffectCueTrigger.Expired
                : GameplayEffectCueTrigger.Removed;
            int count = m_Port.ComponentCount(active.Spec);
            for (int i = 0; i < count; i++)
            {
                GameplayEffectComponentDescriptor component = m_Port.DescribeComponent(active.Spec, i);
                if (component.Kind == GameplayEffectComponentKind.AdditionalEffects)
                {
                    if (!EnqueueAdditional(spec, active.InstanceId, i, GameplayEffectAdditionalTrigger.Removed, out failure, lifecycle))
                        return false;
                }
                else if (component.Kind == GameplayEffectComponentKind.Cue && component.CueTrigger == trigger)
                    m_Port.EmitCue(spec, active.InstanceId, i, true);
            }
            failure = default;
            return true;
        }

        bool InvokeOverflow(GameplayEffectPreparedSpec<TSpec> spec, TActive active, out GameplayEffectExecutionFailure failure)
        {
            int count = m_Port.ComponentCount(spec.TargetSpec);
            for (int i = 0; i < count; i++)
            {
                GameplayEffectComponentDescriptor component = m_Port.DescribeComponent(spec.TargetSpec, i);
                if (component.Kind == GameplayEffectComponentKind.AdditionalEffects)
                {
                    if (!EnqueueAdditional(spec, active.InstanceId, i, GameplayEffectAdditionalTrigger.Overflow, out failure))
                        return false;
                }
            }
            failure = default;
            return true;
        }

        bool EnqueueAdditional(
            GameplayEffectPreparedSpec<TSpec> spec,
            ulong ownerInstanceId,
            int componentIndex,
            GameplayEffectAdditionalTrigger trigger,
            out GameplayEffectExecutionFailure failure,
            GameplayEffectLifecycleKind lifecycle = default)
        {
            int count = m_Port.AdditionalEffectCount(spec.TargetSpec, componentIndex);
            for (int i = 0; i < count; i++)
            {
                if (m_Port.DescribeAdditionalEffectTrigger(spec.TargetSpec, componentIndex, i) != trigger)
                    continue;
                GameplayEffectLifecycleKind cause = lifecycle != 0 ? lifecycle : AdditionalLifecycle(trigger);
                if (m_AdditionalCount == m_PendingAdditional.Length)
                {
                    failure = new GameplayEffectExecutionFailure(spec.Descriptor.EffectId, ownerInstanceId,
                        cause, m_Port.AdditionalEffectId(spec.TargetSpec, componentIndex, i), spec.Context, m_Port.Tick,
                        GameplayEffectApplyResultKind.CapacityExceeded, "AdditionalApplicationCapacityExceeded",
                        capacityDimension: "AdditionalApplications", required: m_AdditionalCount + 1,
                        available: m_PendingAdditional.Length);
                    return false;
                }
                TApplication application = m_Port.BuildAdditionalApplication(spec, ownerInstanceId, componentIndex, i);
                int tail = (m_AdditionalHead + m_AdditionalCount) % m_PendingAdditional.Length;
                m_PendingAdditional[tail] = new PendingAdditionalApplication(
                    application,
                    spec.Descriptor.EffectId,
                    ownerInstanceId,
                    cause,
                    spec.Context);
                m_AdditionalCount++;
            }
            failure = default;
            return true;
        }

        bool BeginPrediction(
            GameplayEffectPreparedSpec<TSpec> spec,
            ulong handle,
            ulong instanceId,
            bool createdActive,
            TActive activeBefore,
            GameplayEffectLifecycleKind mutationKind,
            out GameplayEffectExecutionFailure failure)
        {
            failure = default;
            if (!spec.Context.Predicted)
                return true;
            if (m_CurrentPrediction != null)
                throw new InvalidOperationException("Nested Gameplay Effect prediction records are not supported.");
            if (!m_Port.TryAdmitPrediction(spec, out failure))
                return false;
            GameplayEffectActiveControlSnapshot snapshot = activeBefore == null
                ? default
                : CaptureActive(activeBefore);
            m_CurrentPrediction = m_Port.CreatePrediction(
                spec,
                handle,
                instanceId,
                createdActive,
                snapshot,
                mutationKind,
                activeBefore == null ? 0 : activeBefore.InsertionSequence);
            m_Port.SetCurrentPrediction(m_CurrentPrediction);
            return true;
        }

        void CompletePrediction()
        {
            TPrediction record = m_CurrentPrediction;
            m_CurrentPrediction = null;
            m_Port.SetCurrentPrediction(null);
            if (record == null)
                return;
            m_Port.CompletePrediction(record);
            m_Port.AddPrediction(record);
            m_Port.RefreshJournalDirty();
        }

        void CancelPrediction()
        {
            TPrediction record = m_CurrentPrediction;
            m_CurrentPrediction = null;
            m_Port.SetCurrentPrediction(null);
            if (record != null)
                m_Port.CancelPrediction(record);
        }

        GameplayEffectReconciliationResult ConfirmPrediction(GameplayEffectLifecycleCommand<TApplication> command)
        {
            try
            {
                ulong key = command.Scope.PredictionKey;
                if (!m_Port.TryGetPredictions(key, out IReadOnlyList<TPrediction> records))
                    return LifecycleResult(command, GameplayEffectReconciliationResultKind.NoMatch);
                int matched = 0;
                int unresolved = 0;
                ulong selectedHandle = 0;
                for (int i = 0; i < records.Count; i++)
                {
                    TPrediction record = records[i];
                    if (!ScopeMatches(record, command.Scope, command.Context.SourceActor))
                        continue;
                    matched++;
                    if (record.Confirmed)
                    {
                        if (record.AuthoritativeLifecycleRevision == command.LifecycleRevision)
                            m_ConfirmedInstances.Add(record.InstanceId);
                        continue;
                    }
                    unresolved++;
                    if (command.InstanceId != 0 && selectedHandle != 0 && selectedHandle != record.Handle)
                        return LifecycleResult(command, GameplayEffectReconciliationResultKind.Conflict,
                            reason: "AuthoritativeInstanceRequiresInstanceScope", predictedInstanceId: record.InstanceId);
                    selectedHandle = record.Handle;
                    if (command.InstanceId != 0)
                    {
                        TActive collision = m_Port.FindActiveByInstance(command.InstanceId);
                        if (collision != null && collision.Handle != record.Handle)
                            return LifecycleResult(command, GameplayEffectReconciliationResultKind.Conflict,
                                reason: "AuthoritativeInstanceConflict", predictedInstanceId: record.InstanceId,
                                currentRevision: collision.LifecycleRevision);
                    }
                }
                if (matched == 0)
                    return LifecycleResult(command, GameplayEffectReconciliationResultKind.NoMatch);
                if (unresolved == 0)
                    return LifecycleResult(command, GameplayEffectReconciliationResultKind.Duplicate, matched);
                for (int i = 0; i < records.Count; i++)
                {
                    TPrediction record = records[i];
                    if (record.Confirmed || !ScopeMatches(record, command.Scope, command.Context.SourceActor))
                        continue;
                    ulong instanceId = command.InstanceId != 0 ? command.InstanceId : record.InstanceId;
                    TActive active = m_Port.FindActiveByHandle(record.Handle);
                    if (active != null && record.MutationKind != 0 &&
                        m_Port.TryResolveLifecyclePrediction(record.Handle, out TPrediction owner) && ReferenceEquals(owner, record))
                    {
                        if (instanceId != active.InstanceId)
                            m_Port.RemapActiveInstance(active, instanceId);
                        active.LifecycleRevision = command.LifecycleRevision;
                        active.LifecycleContext = ConfirmedContext(command.Context);
                        m_ChangedLifecycleSubjects.Add(instanceId);
                        m_Port.RefreshActiveEffectsDirty();
                    }
                    m_Port.ConfirmPrediction(record, instanceId, command.LifecycleRevision, ConfirmedContext(command.Context));
                    if (!m_ConfirmedInstances.Add(instanceId))
                        continue;
                    GameplayEffectPreparedSpec<TSpec> captured = m_Port.DescribeSpec(record.Spec);
                    GameplayEffectContextIdentity context = ConfirmedContext(command.Context);
                    var spec = new GameplayEffectPreparedSpec<TSpec>(captured.Descriptor, context, captured.TargetSpec,
                        captured.DurationTicks, captured.PeriodTicks);
                    m_Port.RegisterCause(record.Handle, spec, instanceId);
                    m_Port.EmitLifecycle(spec, instanceId, GameplayEffectLifecycleKind.Confirmed,
                        active == null ? context.SourceTick : active.StartTick,
                        active == null ? context.SourceTick : active.EndTick,
                        active == null ? 1 : active.StackCount, command.LifecycleRevision, active == null);
                }
                m_Port.RefreshJournalDirty();
                m_Port.RebuildCauses();
                return LifecycleResult(command, GameplayEffectReconciliationResultKind.Confirmed, unresolved);
            }
            finally
            {
                m_ConfirmedInstances.Clear();
            }
        }

        GameplayEffectReconciliationResult RejectPrediction(GameplayEffectLifecycleCommand<TApplication> command)
        {
            if (!m_Port.TryGetPredictions(command.Scope.PredictionKey, out IReadOnlyList<TPrediction> records))
                return LifecycleResult(command, GameplayEffectReconciliationResultKind.NoMatch);
            int removed = 0;
            for (int i = records.Count - 1; i >= 0; i--)
            {
                TPrediction record = records[i];
                if (record.Confirmed || !ScopeMatches(record, command.Scope, command.Context.SourceActor))
                    continue;
                TActive active = m_Port.FindActiveByHandle(record.Handle);
                bool removedActive = record.MutationKind == GameplayEffectLifecycleKind.Removed ||
                    record.MutationKind == GameplayEffectLifecycleKind.Expired;
                if (removedActive && active != null)
                    return LifecycleResult(command, GameplayEffectReconciliationResultKind.Conflict,
                        reason: "PredictionRemovedEffectReplaced", predictedInstanceId: record.InstanceId,
                        predictedRevision: record.AfterLifecycleRevision, currentRevision: active.LifecycleRevision);
                if (active != null && (record.CreatedActive || record.HasActiveBefore) &&
                    (!m_Port.TryResolveLifecyclePrediction(record.Handle, out TPrediction owner) || !ReferenceEquals(owner, record)))
                {
                    return LifecycleResult(command, GameplayEffectReconciliationResultKind.Conflict,
                        reason: "PredictionEffectRevisionConflict", predictedInstanceId: record.InstanceId,
                        predictedRevision: record.AfterLifecycleRevision, currentRevision: active.LifecycleRevision);
                }
                if ((record.CreatedActive || record.HasActiveBefore) && active == null && !removedActive)
                    return LifecycleResult(command, GameplayEffectReconciliationResultKind.Conflict,
                        reason: "PredictionEffectMissing", predictedInstanceId: record.InstanceId,
                        predictedRevision: record.AfterLifecycleRevision);
                if (removedActive)
                {
                    TActive replacement = m_Port.FindActiveByInstance(record.InstanceId);
                    if (replacement != null)
                        return LifecycleResult(command, GameplayEffectReconciliationResultKind.Conflict,
                            reason: "PredictionRemovedEffectReplaced", predictedInstanceId: record.InstanceId,
                            predictedRevision: record.AfterLifecycleRevision, currentRevision: replacement.LifecycleRevision);
                    if (m_Port.TryGetAuthoritativeLifecycleAfter(record.InstanceId, record.AfterChangeCursor,
                        out GameplayEffectLifecycleRevisionEntry entry))
                        return LifecycleResult(command, GameplayEffectReconciliationResultKind.Conflict,
                            reason: "PredictionEffectRevisionConflict", predictedInstanceId: record.InstanceId,
                            predictedRevision: record.AfterLifecycleRevision, currentRevision: entry.Revision);
                }
                if (!m_Port.CanRestorePredictionAttributes(record, out GameplayEffectReconciliationResult conflict))
                {
                    return LifecycleResult(command, GameplayEffectReconciliationResultKind.Conflict,
                        reason: conflict.Reason, predictedInstanceId: record.InstanceId,
                        predictedRevision: conflict.PredictedRevision, currentRevision: conflict.CurrentRevision,
                        attributeId: conflict.AttributeId);
                }
                GameplayEffectPreparedSpec<TSpec> captured = m_Port.DescribeSpec(record.Spec);
                var spec = new GameplayEffectPreparedSpec<TSpec>(captured.Descriptor, record.Context, captured.TargetSpec,
                    captured.DurationTicks, captured.PeriodTicks);
                m_Port.RegisterCause(record.Handle, spec, record.InstanceId);
                if (record.CreatedActive || record.HasActiveBefore)
                    m_ChangedLifecycleSubjects.Add(record.InstanceId);
                if (removedActive)
                {
                    if (!m_Port.TryAdmitActive(spec, GameplayEffectLifecycleKind.Rejected, out GameplayEffectExecutionFailure failure))
                        return LifecycleResult(command, GameplayEffectReconciliationResultKind.ExecutionFailed, failure: failure);
                    GameplayEffectActiveControlSnapshot before = record.ActiveBefore;
                    active = m_Port.CreateActive(spec, record.Handle, before.InstanceId, before.StartTick, before.EndTick,
                        record.ActiveBeforeInsertionSequence, before.LifecycleRevision);
                    m_Port.AddActive(active);
                    RestoreActive(active, before);
                }
                else if (record.CreatedActive)
                {
                    m_Port.DeactivatePersistent(active);
                    m_Port.RemoveActive(active);
                }
                else if (record.HasActiveBefore)
                {
                    m_Port.DeactivatePersistent(active);
                    RestoreActive(active, record.ActiveBefore);
                }
                m_Port.RestorePredictionAttributes(record);
                for (int cueIndex = 0; cueIndex < record.CueIds.Count; cueIndex++)
                    m_Port.EmitPredictionCueRemoval(record, record.CueIds[cueIndex]);
                m_Port.EmitLifecycle(
                    spec,
                    record.InstanceId,
                    GameplayEffectLifecycleKind.Rejected,
                    m_Port.Tick,
                    m_Port.Tick,
                    0,
                    command.LifecycleRevision,
                    spec.Descriptor.Duration == GameplayEffectDurationKind.Instant);
                m_Port.RemovePrediction(command.Scope.PredictionKey, record);
                removed++;
            }
            if (removed == 0)
                return LifecycleResult(command, GameplayEffectReconciliationResultKind.NoMatch);
            m_Port.RefreshJournalDirty();
            m_Port.RebuildCauses();
            return LifecycleResult(command, GameplayEffectReconciliationResultKind.Rejected, removed);
        }

        GameplayEffectReconciliationResult CorrectPrediction(GameplayEffectLifecycleCommand<TApplication> command)
        {
            if (command.Correction == null)
                return LifecycleResult(command, GameplayEffectReconciliationResultKind.IncompleteCorrection,
                    reason: "AuthoritativeCorrectionMissing");
            if (!m_Port.TryGetPredictions(command.Scope.PredictionKey, out IReadOnlyList<TPrediction> records))
                return LifecycleResult(command, GameplayEffectReconciliationResultKind.NoMatch);
            int matched = 0;
            for (int i = 0; i < records.Count; i++)
            {
                TPrediction record = records[i];
                if (record.Confirmed || !ScopeMatches(record, command.Scope, command.Context.SourceActor))
                    continue;
                matched++;
                if (!m_Port.CorrectionCoversPrediction(record, command.Correction, out GameplayEffectReconciliationResult missing))
                    return LifecycleResult(command, GameplayEffectReconciliationResultKind.IncompleteCorrection,
                        reason: missing.Reason, predictedInstanceId: record.InstanceId,
                        attributeId: missing.AttributeId, failure: missing.Failure);
            }
            if (matched == 0)
                return LifecycleResult(command, GameplayEffectReconciliationResultKind.NoMatch);
            if (!m_Port.TryApplyCorrection(command, out GameplayEffectReconciliationResult correctionResult))
                return correctionResult;
            for (int i = records.Count - 1; i >= 0; i--)
            {
                TPrediction record = records[i];
                if (record.Confirmed || !ScopeMatches(record, command.Scope, command.Context.SourceActor))
                    continue;
                m_Port.RemovePrediction(command.Scope.PredictionKey, record);
            }
            m_Port.RefreshJournalDirty();
            m_Port.RebuildCauses();
            if ((command.Correction.Coverage & GameplayEffectCorrectionCoverage.ActiveEffect) != 0)
                m_ChangedLifecycleSubjects.Add(correctionResult.AuthoritativeInstanceId);
            return new GameplayEffectReconciliationResult(GameplayEffectReconciliationResultKind.Corrected,
                command.Scope, correctionResult.AuthoritativeInstanceId, command.LifecycleRevision, matched);
        }

        bool ScopeMatches(TPrediction record, GameplayEffectReconciliationScope scope, ActorId sourceActor)
        {
            if (record.PredictionKey != scope.PredictionKey || record.Context.SourceActor != sourceActor)
                return false;
            if (scope.Kind == GameplayEffectReconciliationScopeKind.PredictionKey)
                return true;
            if (!m_Port.MatchesEffectId(record.Spec, scope.EffectId))
                return false;
            return scope.Kind != GameplayEffectReconciliationScopeKind.Instance ||
                record.PredictedInstanceId == scope.PredictedInstanceId;
        }

        static bool ScopeIsValid(GameplayEffectReconciliationScope scope)
        {
            return scope.PredictionKey != 0 && (scope.Kind == GameplayEffectReconciliationScopeKind.PredictionKey ||
                !scope.EffectId.IsEmpty && (scope.Kind == GameplayEffectReconciliationScopeKind.Effect ||
                    scope.Kind == GameplayEffectReconciliationScopeKind.Instance && scope.PredictedInstanceId != 0));
        }

        static bool IsReconciliation(GameplayEffectLifecycleKind kind) =>
            kind == GameplayEffectLifecycleKind.Confirmed || kind == GameplayEffectLifecycleKind.Rejected ||
            kind == GameplayEffectLifecycleKind.Corrected;

        static GameplayEffectReconciliationResult LifecycleResult(
            GameplayEffectLifecycleCommand<TApplication> command,
            GameplayEffectReconciliationResultKind kind,
            int recordCount = 0,
            ulong predictedInstanceId = 0,
            ulong predictedRevision = 0,
            ulong currentRevision = 0,
            CanonicalTextValue attributeId = default,
            CanonicalTextValue reason = default,
            GameplayEffectExecutionFailure failure = default)
        {
            return new GameplayEffectReconciliationResult(kind, command.Scope, command.InstanceId,
                command.LifecycleRevision, recordCount, predictedInstanceId, predictedRevision,
                currentRevision, attributeId, reason, failure);
        }

        bool FlushAdditional(out GameplayEffectExecutionFailure failure)
        {
            while (m_AdditionalCount > 0)
            {
                PendingAdditionalApplication pending = m_PendingAdditional[m_AdditionalHead];
                m_PendingAdditional[m_AdditionalHead] = default;
                m_AdditionalHead = (m_AdditionalHead + 1) % m_PendingAdditional.Length;
                m_AdditionalCount--;
                try
                {
                    GameplayEffectApplicationIdentity requested = m_Port.DescribeApplication(pending.Application);
                    GameplayEffectApplyResult result = ApplyCore(pending.Application, requested);
                    if (!result.AcceptedMutation)
                    {
                        failure = DescribeFailure(result, pending.OwnerEffectId, pending.OwnerInstanceId,
                            pending.Trigger, requested.EffectId, pending.Context);
                        return false;
                    }
                }
                finally
                {
                    m_Port.ReleaseAdditionalApplication(pending.Application);
                }
            }
            failure = default;
            return true;
        }

        void ClearAdditional()
        {
            while (m_AdditionalCount > 0)
            {
                PendingAdditionalApplication pending = m_PendingAdditional[m_AdditionalHead];
                m_PendingAdditional[m_AdditionalHead] = default;
                m_AdditionalHead = (m_AdditionalHead + 1) % m_PendingAdditional.Length;
                m_AdditionalCount--;
                m_Port.ReleaseAdditionalApplication(pending.Application);
            }
            m_AdditionalHead = 0;
        }

        void RestoreOperation(TSavepoint savepoint, int changeCount)
        {
            ClearAdditional();
            CancelPrediction();
            m_Port.Restore(savepoint);
            m_Port.TrimChanges(changeCount);
            m_Port.RebuildCauses();
        }

        GameplayEffectExecutionFailure DescribeFailure(GameplayEffectApplyResult result, CanonicalTextValue ownerEffectId,
            ulong ownerInstanceId, GameplayEffectLifecycleKind trigger, CanonicalTextValue requestedEffectId,
            GameplayEffectContextIdentity context)
        {
            return result.Failure.IsValid ? result.Failure : new GameplayEffectExecutionFailure(ownerEffectId,
                ownerInstanceId, trigger, requestedEffectId, context, m_Port.Tick, result.Kind,
                result.Reason, result.ParameterId);
        }

        static GameplayEffectLifecycleKind AdditionalLifecycle(GameplayEffectAdditionalTrigger trigger) => trigger switch
        {
            GameplayEffectAdditionalTrigger.Applied => GameplayEffectLifecycleKind.Applied,
            GameplayEffectAdditionalTrigger.Period => GameplayEffectLifecycleKind.PeriodExecuted,
            GameplayEffectAdditionalTrigger.Removed => GameplayEffectLifecycleKind.Removed,
            GameplayEffectAdditionalTrigger.Overflow => GameplayEffectLifecycleKind.Overflow,
            _ => throw new ArgumentOutOfRangeException(nameof(trigger))
        };

        GameplayEffectActiveControlSnapshot CaptureActive(TActive active)
        {
            return new GameplayEffectActiveControlSnapshot(
                active.InstanceId,
                active.StartTick,
                active.EndTick,
                m_Port.GetNextPeriod(active.InstanceId),
                active.StackCount,
                active.Inhibited,
                active.LifecycleRevision,
                active.LifecycleContext);
        }

        ulong ResolveInstanceId(ulong authoritative)
        {
            if (authoritative != 0)
                return authoritative;
            ulong value;
            do value = m_Port.AllocateHandle();
            while (m_Port.FindActiveByInstance(value) != null);
            return value;
        }

        static ulong CheckedAdd(ulong left, ulong right)
        {
            if (ulong.MaxValue - left < right)
                throw new OverflowException("Gameplay Effect tick range overflowed.");
            return left + right;
        }

        readonly struct PendingAdditionalApplication
        {
            public PendingAdditionalApplication(
                TApplication application,
                string ownerEffectId,
                ulong ownerInstanceId,
                GameplayEffectLifecycleKind trigger,
                GameplayEffectContextIdentity context)
            {
                Application = application;
                OwnerEffectId = ownerEffectId;
                OwnerInstanceId = ownerInstanceId;
                Trigger = trigger;
                Context = context;
            }

            public TApplication Application { get; }
            public string OwnerEffectId { get; }
            public ulong OwnerInstanceId { get; }
            public GameplayEffectLifecycleKind Trigger { get; }
            public GameplayEffectContextIdentity Context { get; }
        }
    }
}
