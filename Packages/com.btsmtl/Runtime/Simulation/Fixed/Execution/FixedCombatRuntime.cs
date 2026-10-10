using System;
using System.Collections.Generic;
using System.Linq;
using ThirdPersonSimulation.Collision;

namespace ThirdPersonSimulation.Fixed
{
    internal sealed class FixedCombatAttackState
    {
        bool m_Active;
        public bool Active
        {
            get => m_Active;
            set
            {
                m_Active = value;
                if (!value)
                {
                    Identity = default;
                    EventId = default;
                }
            }
        }
        public int ActorIndex;
        public int DefinitionIndex;
        CombatAttackIdentity m_Identity;
        CanonicalTextValue m_EventId;
        public CombatAttackIdentity Identity
        {
            get => m_Identity;
            set { value.Retain(); m_Identity.Release(); m_Identity = value; }
        }
        public CanonicalTextValue EventId
        {
            get => m_EventId;
            set { value.Retain(); m_EventId.Release(); m_EventId = value; }
        }
        public FixedVector3 Origin;
        public FixedYaw Yaw;
        public int TotalCount;
        public int HitCount;
        public ulong LastQueryTick;
        public readonly int[] TargetCounts;
        public readonly ulong[] TargetTicks;

        public FixedCombatAttackState(int actorIndex, int targetCount)
        {
            ActorIndex = actorIndex;
            TargetCounts = new int[targetCount];
            TargetTicks = new ulong[targetCount];
        }

        public void Begin(FixedCombatQuery query, int definitionIndex)
        {
            Active = true;
            DefinitionIndex = definitionIndex;
            Identity = query.Identity;
            EventId = query.AttackEventId;
            Origin = query.Origin;
            Yaw = query.Yaw;
            TotalCount = 0;
            HitCount = 0;
            LastQueryTick = 0;
            Array.Clear(TargetCounts, 0, TargetCounts.Length);
            Array.Clear(TargetTicks, 0, TargetTicks.Length);
        }

        public void CopyFrom(FixedCombatAttackState value)
        {
            Active = value.Active;
            if (!Active)
                return;
            DefinitionIndex = value.DefinitionIndex;
            Identity = value.Identity;
            EventId = value.EventId;
            Origin = value.Origin;
            Yaw = value.Yaw;
            TotalCount = value.TotalCount;
            HitCount = value.HitCount;
            LastQueryTick = value.LastQueryTick;
            Array.Copy(value.TargetCounts, TargetCounts, TargetCounts.Length);
            Array.Copy(value.TargetTicks, TargetTicks, TargetTicks.Length);
        }
    }

    internal struct FixedCombatHalt
    {
        public int ActorIndex;
        public ulong ActionInstanceId;
        public ulong ResumeTick;
    }

    internal struct FixedCombatTimeSlowState
    {
        internal bool Active;
        internal FixedScalar Age;
        internal FixedScalar Rate;
        internal FixedScalar CurrentRate => Active ? Rate : FixedScalar.One;
    }

    public sealed class FixedCombatBatchResult
    {
        internal FixedCombatBatchResult(FixedCombatResultStorage hits, int cameraCapacity)
        {
            Hits = hits;
            Cameras = new FixedCombatCameraRequest[cameraCapacity];
        }
        public ulong Tick { get; internal set; }
        public IReadOnlyList<CombatHitResult> Hits { get; }
        internal FixedCombatCameraRequest[] Cameras { get; }
        internal int CameraCount { get; set; }
    }

    internal struct FixedCombatCameraRequest
    {
        public int ActorIndex;
        public int DefinitionIndex;
        public SimulationEventHeader Header;
        public ulong ActionInstanceId;
    }

    public sealed partial class FixedCombatRuntime
    {
        sealed class Participant
        {
            public ActorId ActorId;
            public int CharacterIndex;
            public int LocalEnemyIndex;
            public int FactionId;
            public bool Neutral;
            public CharacterCombatRuntimeBinding Character;
            public ActorId LocalTarget;
        }

        readonly FixedCharacterRuntime m_Characters;
        readonly FixedScalar m_TickDelta;
        readonly Participant[] m_Participants;
        readonly Dictionary<ActorId, int> m_ParticipantIndices = new Dictionary<ActorId, int>();
        readonly Dictionary<ulong, int> m_TargetIndices = new Dictionary<ulong, int>();
        readonly FixedPreparedAttack[] m_EnemyAttacks;
        readonly FixedCombatQuery m_EnemyQuery = new FixedCombatQuery();
        IReadOnlyList<FixedLocalEnemyInput> m_EnemyInputs;
        readonly int m_ResultCapacity;
        readonly Dictionary<(int Actor, string Content), FixedPreparedAttack> m_AttackLookup;
        readonly FixedPreparedAttack[] m_Attacks;
        readonly FixedSweepQueryBackend m_Backend;
        readonly SweepTarget<FixedScalar>[] m_Targets;
        readonly int[] m_TargetShapes;
        readonly FixedVector3[] m_TargetOffsets;
        readonly FixedScalar[] m_TargetHalfHeights;
        readonly SweepSegment<FixedScalar>[] m_Segments;
        readonly FixedAttackSweepSampling m_SweepSampling;
        readonly SweepResultBuffer<FixedScalar> m_QueryResults;
        readonly FixedCombatAttackState[] m_AttackStates;
        readonly int[] m_AttackStateOffsets;
        readonly FixedCombatHalt[] m_Halts;
        readonly FixedCombatTimeSlowState[] m_SelfTimeSlows;
        readonly FixedGameplayAbilityCurve[] m_TimeSlowCurves;
        FixedCombatTimeSlowState m_WorldTimeSlow;
        int m_WorldTimeSlowOwner;
        readonly FixedCombatResultStorage m_Pending;
        readonly FixedCombatBatchResult m_Batch;
        readonly PortableEffectDefinition[] m_LightEffects;
        readonly PortableEffectDefinition[] m_HeavyEffects;
        readonly PortableAttackPropertyComponent[] m_ParryLightProperties;
        readonly PortableAttackPropertyComponent[] m_ParryHeavyProperties;
        ulong m_NextResultId = 1;
        ulong m_LastTick;
#if UNITY_EDITOR
        readonly FixedCombatQueryDiagnostics m_QueryDiagnostics;
#endif

        public FixedCombatRuntime(FixedCharacterRuntime characters, IReadOnlyList<FixedLocalEnemyBinding> localEnemies = null)
        {
            m_Characters = characters;
            m_TimeSlowCurves = new FixedGameplayAbilityCurve[characters.Roster.Count];
            m_TickDelta = FixedScalar.One / FixedScalar.FromInt64(m_Characters.TickRate);
            localEnemies ??= Array.Empty<FixedLocalEnemyBinding>();
            int actorCount = characters.Roster.Count + localEnemies.Count;
            m_Participants = new Participant[actorCount];
            m_AttackStateOffsets = new int[actorCount + 1];
            m_EnemyAttacks = new FixedPreparedAttack[localEnemies.Count];
            m_Targets = new SweepTarget<FixedScalar>[actorCount];
            m_TargetShapes = new int[actorCount];
            m_TargetOffsets = new FixedVector3[actorCount];
            m_TargetHalfHeights = new FixedScalar[actorCount];
            m_LightEffects = new PortableEffectDefinition[actorCount];
            m_HeavyEffects = new PortableEffectDefinition[actorCount];
            m_ParryLightProperties = new PortableAttackPropertyComponent[actorCount];
            m_ParryHeavyProperties = new PortableAttackPropertyComponent[actorCount];
            var shapes = new List<SweepShape<FixedScalar>>();
            var attacks = new List<FixedPreparedAttack>();
            var states = new List<FixedCombatAttackState>();
            m_AttackLookup = new Dictionary<(int Actor, string Content), FixedPreparedAttack>();
            int resultCapacity = 0;
            int haltCapacity = 0;
            int cameraCapacity = 0;
            for (int actorIndex = 0; actorIndex < characters.Roster.Count; actorIndex++)
            {
                m_AttackStateOffsets[actorIndex] = states.Count;
                SimulationActorBinding actor = characters.Roster[actorIndex];
                CharacterCombatRuntimeBinding binding = actor.CombatBinding;
                m_Participants[actorIndex] = new Participant
                {
                    ActorId = actor.ActorId, CharacterIndex = actorIndex, LocalEnemyIndex = -1,
                    Character = binding, FactionId = binding?.FactionId ?? 0, Neutral = binding?.Neutral ?? false
                };
                m_TargetShapes[actorIndex] = -1;
                if (binding != null)
                {
                    if (!string.IsNullOrEmpty(binding.PerfectEvadeEvent))
                        m_TimeSlowCurves[actorIndex] = PrepareTimeSlowCurve(binding.PerfectEvadeTimeSlow);
                    m_TargetShapes[actorIndex] = shapes.Count;
                    shapes.Add(SweepShape<FixedScalar>.Capsule((ulong)shapes.Count + 1,
                        FixedScalar.FromDouble(binding.Radius), FixedScalar.FromDouble(binding.HalfLength)));
                    m_TargetOffsets[actorIndex] = new FixedVector3(FixedScalar.FromDouble(binding.CenterX),
                        FixedScalar.FromDouble(binding.CenterY), FixedScalar.FromDouble(binding.CenterZ));
                    m_TargetHalfHeights[actorIndex] = FixedScalar.FromDouble(binding.Radius) + FixedScalar.FromDouble(binding.HalfLength);
                    resultCapacity += binding.ResultCapacity;
                    cameraCapacity += binding.ResultCapacity + binding.AttackCapacity;
                    for (int i = 0; i < binding.AttackCapacity; i++)
                        states.Add(new FixedCombatAttackState(actorIndex, actorCount));
                    m_LightEffects[actorIndex] = PrepareResponseEffect(actor, binding.HitLightEffectId);
                    m_HeavyEffects[actorIndex] = PrepareResponseEffect(actor, binding.HitHeavyEffectId);
                    m_ParryLightProperties[actorIndex] = actor.AbilityInstallations.GameplayEffectCatalog
                        .RequireEffect(binding.ParryLightEffectId).Components.OfType<PortableAttackPropertyComponent>().Single();
                    m_ParryHeavyProperties[actorIndex] = actor.AbilityInstallations.GameplayEffectCatalog
                        .RequireEffect(binding.ParryHeavyEffectId).Components.OfType<PortableAttackPropertyComponent>().Single();
                }
                for (int skillIndex = 0; skillIndex < actor.AbilityInstallations.Installations.Count; skillIndex++)
                {
                    FixedGameplayAbilityExecutionInstallation installation = actor.AbilityInstallations.Installations[skillIndex];
                    haltCapacity += installation.Layout.ActionInstanceCapacity;
                    for (int operationIndex = 0; operationIndex < installation.Data.Operations.Count; operationIndex++)
                    {
                        SimulationOperation operation = installation.Data.Operations[operationIndex];
                        if (operation.Code != SimulationOperationCode.AttackQuery)
                            continue;
                        if (binding == null)
                            throw new InvalidOperationException($"Actor '{actor.ActorId}' declares attacks without a Combat receiver binding.");
                        PortableEffectDefinition definition = actor.AbilityInstallations.GameplayEffectCatalog.RequireEffect(operation.Text0);
                        var key = (actorIndex, definition.Id);
                        if (m_AttackLookup.ContainsKey(key))
                            continue;
                        FixedPreparedAttack prepared = FixedCombatGeometryPreparation.Prepare(attacks.Count, actorIndex, definition, shapes);
                        attacks.Add(prepared);
                        m_AttackLookup.Add(key, prepared);
                    }
                }
            }
            for (int i = 0; i < localEnemies.Count; i++)
            {
                FixedLocalEnemyDefinition enemy = localEnemies[i].Definition;
                int actorIndex = characters.Roster.Count + i;
                m_AttackStateOffsets[actorIndex] = states.Count;
                m_Participants[actorIndex] = new Participant
                {
                    ActorId = enemy.ActorId, CharacterIndex = -1, LocalEnemyIndex = i, FactionId = enemy.FactionId,
                    LocalTarget = enemy.TargetActorId
                };
                m_TargetShapes[actorIndex] = shapes.Count;
                shapes.Add(SweepShape<FixedScalar>.Capsule((ulong)shapes.Count + 1, enemy.Radius, enemy.HalfLength));
                m_TargetOffsets[actorIndex] = enemy.ReceiverCenter;
                m_TargetHalfHeights[actorIndex] = enemy.Radius + enemy.HalfLength;
                FixedPreparedAttack prepared = FixedCombatGeometryPreparation.Prepare(attacks.Count, actorIndex, enemy, shapes);
                attacks.Add(prepared);
                m_EnemyAttacks[i] = prepared;
                for (int slot = 0; slot < FixedLocalEnemyDefinition.WindowCapacity; slot++)
                    states.Add(new FixedCombatAttackState(actorIndex, actorCount));
                resultCapacity += actorCount * FixedLocalEnemyDefinition.WindowCapacity;
            }
            m_AttackStateOffsets[actorCount] = states.Count;
            for (int i = 0; i < actorCount; i++)
            {
                m_ParticipantIndices.Add(m_Participants[i].ActorId, i);
                m_TargetIndices.Add((ulong)i + 1, i);
            }
            m_ResultCapacity = resultCapacity;
            for (int i = 0; i < characters.Roster.Count; i++)
            {
                int outputCapacity = resultCapacity;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                outputCapacity = checked(outputCapacity + characters.Roster[i].SimulatedIncomingAttackResults.Length);
#endif
                characters.Roster[i].PrepareCombatResultCapacity(outputCapacity);
            }
            m_Attacks = attacks.ToArray();
            int segmentCapacity = 1, intervalCapacity = 1;
            for (int attack = 0; attack < m_Attacks.Length; attack++)
            {
                FixedPreparedAttack prepared = m_Attacks[attack];
                segmentCapacity = Math.Max(segmentCapacity, prepared.SegmentCapacity);
                if (prepared.Collision.Kind != PortableAttackCollisionKind.GeometrySweep)
                    continue;
                int intervals = 0;
                for (int part = 0; part < prepared.Parts.Length; part++)
                    intervals += prepared.Parts[part].Data.Samples.Length + 1;
                intervalCapacity = Math.Max(intervalCapacity, intervals);
            }
            m_Segments = new SweepSegment<FixedScalar>[segmentCapacity];
            m_SweepSampling = new FixedAttackSweepSampling(intervalCapacity);
            m_AttackStates = states.ToArray();
            m_Halts = new FixedCombatHalt[haltCapacity];
            m_SelfTimeSlows = new FixedCombatTimeSlowState[characters.Roster.Count];
            SweepShape<FixedScalar>[] preparedShapes = shapes.ToArray();
            m_Backend = new FixedSweepQueryBackend(preparedShapes);
#if UNITY_EDITOR
            m_QueryDiagnostics = new FixedCombatQueryDiagnostics(characters.Roster[0].ActorId,
                preparedShapes, states.Count, actorCount, segmentCapacity);
            for (int i = 0; i < characters.Roster.Count; i++)
                characters.Roster[i].CombatQueryDiagnostics = m_QueryDiagnostics;
#endif
            m_QueryResults = new SweepResultBuffer<FixedScalar>(actorCount);
            m_Pending = new FixedCombatResultStorage(resultCapacity);
            m_Batch = new FixedCombatBatchResult(m_Pending, cameraCapacity);
        }

        static PortableEffectDefinition PrepareResponseEffect(SimulationActorBinding actor, string effectId)
        {
            if (string.IsNullOrEmpty(effectId))
                return null;
            PortableEffectDefinition definition = actor.AbilityInstallations.GameplayEffectCatalog.RequireEffect(effectId);
            if (definition.SetByCallerParameters.Length != 0 || definition.SourceSnapshotAttributes.Count != 0)
                throw new NotSupportedException("Receiver hit Effect requires source parameters that are not declared by this combat binding.");
            return definition;
        }

        static FixedGameplayAbilityCurve PrepareTimeSlowCurve(CharacterTimeSlowRuntimeBinding binding)
        {
            var keys = new FixedGameplayAbilityCurveKey[binding.Curve.Keys.Count];
            for (int i = 0; i < keys.Length; i++)
            {
                CharacterCombatCurveKey key = binding.Curve.Keys[i];
                keys[i] = new FixedGameplayAbilityCurveKey(FixedScalar.FromDouble(key.Time),
                    FixedScalar.FromDouble(key.Value), FixedScalar.FromDouble(key.InTangent),
                    FixedScalar.FromDouble(key.OutTangent), FixedScalar.FromDouble(key.InWeight),
                    FixedScalar.FromDouble(key.OutWeight), key.WeightedMode, false);
            }
            return new FixedGameplayAbilityCurve(binding.PreWrapMode, binding.PostWrapMode, keys);
        }

        internal int ResultIngressCapacity => m_ResultCapacity;

        internal void CountPendingIngress(int actorIndex, out int entries, out int commands)
        {
            entries = 0;
            commands = 0;
            ActorId actorId = m_Characters.Roster[actorIndex].ActorId;
            for (int i = 0; i < m_Pending.Count; i++)
            {
                CombatHitResult hit = m_Pending[i];
                if (hit.SourceActor != actorId && hit.TargetActor != actorId)
                    continue;
                entries++;
                if (hit.TargetActor != actorId || hit.Disposition != CombatHitDisposition.Hit ||
                    hit.Attack.SourceKind == CombatAttackSourceKind.LocalEnemyAttack)
                    continue;
                PortableEffectDefinition effect = hit.Heavy ? m_HeavyEffects[actorIndex] : m_LightEffects[actorIndex];
                if (effect == null)
                    continue;
                entries++;
                commands++;
            }
        }
        internal int LocalEnemyCount => m_EnemyAttacks.Length;

        internal int CopyPendingIngress(int actorIndex, SimulationIngress[] destination, int count)
        {
            ActorId actorId = m_Characters.Roster[actorIndex].ActorId;
            for (int i = 0; i < m_Pending.Count; i++)
            {
                CombatHitResult hit = m_Pending[i];
                if (hit.SourceActor != actorId && hit.TargetActor != actorId)
                    continue;
                FixedPreparedAttack definition = m_Attacks[hit.DefinitionIndex];
                var header = new SimulationIngressHeader(m_Characters.NumericProfile, actorId,
                    hit.SourceTick, hit.ResultId * 2, definition.IngressIdentity, SimulationIngressKind.GameplayResult);
                destination[count++] = new SimulationIngress(header, new SimulationGameplayResultIngress(hit));
                if (hit.TargetActor != actorId || hit.Disposition != CombatHitDisposition.Hit ||
                    hit.Attack.SourceKind == CombatAttackSourceKind.LocalEnemyAttack)
                    continue;
                PortableEffectDefinition effect = hit.Heavy ? m_HeavyEffects[actorIndex] : m_LightEffects[actorIndex];
                if (effect == null)
                    continue;
                var context = new SimulationGameplayEffectContext(hit.SourceActor, hit.TargetActor,
                    hit.Attack.ActionInstanceId, 0, hit.ResultId, hit.SourceTick, SimulationGameplayEffectApplicationMode.Confirmed);
                var application = SimulationGameplayEffectApplication.FromCompiled(effect.Id, effect.Revision,
                    context, Array.Empty<SimulationSetByCallerValue>(),
                    Array.Empty<SimulationAttributeCapture>(), Array.Empty<string>());
                var effectHeader = new SimulationIngressHeader(m_Characters.NumericProfile, actorId,
                    hit.SourceTick, hit.ResultId * 2 + 1, definition.IngressIdentity, SimulationIngressKind.GameplayResult);
                destination[count++] = new SimulationIngress(effectHeader, new SimulationGameplayResultIngress(application));
            }
            return count;
        }

        public bool IsHalted(ActorId actor, ulong actionInstanceId, ulong tick)
        {
            int actorIndex = m_Characters.GetActorIndex(actor);
            for (int i = 0; i < m_Halts.Length; i++)
                if (m_Halts[i].ActorIndex == actorIndex && m_Halts[i].ActionInstanceId == actionInstanceId &&
                    tick < m_Halts[i].ResumeTick)
                    return true;
            return false;
        }

        public FixedScalar WorldTimeScale => m_WorldTimeSlow.CurrentRate;

        public FixedScalar OwnerTimeScale(ActorId actor) =>
            m_SelfTimeSlows[m_Characters.GetActorIndex(actor)].CurrentRate;

        public FixedScalar ActionTimeScale(ActorId actor) => WorldTimeScale * OwnerTimeScale(actor);

        internal void AdvanceTimeSlows()
        {
            if (m_WorldTimeSlow.Active)
                AdvanceTimeSlow(ref m_WorldTimeSlow,
                    m_Characters.Roster[m_WorldTimeSlowOwner].CombatBinding.PerfectEvadeTimeSlow,
                    m_TimeSlowCurves[m_WorldTimeSlowOwner], m_TickDelta);
            FixedScalar worldDelta = m_TickDelta * WorldTimeScale;
            for (int i = 0; i < m_SelfTimeSlows.Length; i++)
                if (m_SelfTimeSlows[i].Active)
                    AdvanceTimeSlow(ref m_SelfTimeSlows[i], m_Characters.Roster[i].CombatBinding.PerfectEvadeTimeSlow,
                        m_TimeSlowCurves[i], worldDelta);
        }

        static void AdvanceTimeSlow(ref FixedCombatTimeSlowState state,
            CharacterTimeSlowRuntimeBinding binding, FixedGameplayAbilityCurve curve, FixedScalar delta)
        {
            if (state.Age >= binding.Duration)
            {
                state = default;
                return;
            }
            state.Rate = curve.Evaluate(state.Age / binding.Duration, FixedScalar.One);
            state.Age += delta;
        }

        void StartPerfectEvadeTimeSlow(int actorIndex)
        {
            CharacterTimeSlowRuntimeBinding binding = m_Characters.Roster[actorIndex].CombatBinding.PerfectEvadeTimeSlow;
            var state = new FixedCombatTimeSlowState
            {
                Active = true,
                Rate = m_TimeSlowCurves[actorIndex].Evaluate(FixedScalar.Zero, FixedScalar.One)
            };
            m_SelfTimeSlows[actorIndex] = state;
            if (!m_WorldTimeSlow.Active ||
                binding.Priority >= m_Characters.Roster[m_WorldTimeSlowOwner].CombatBinding.PerfectEvadeTimeSlow.Priority)
            {
                m_WorldTimeSlowOwner = actorIndex;
                m_WorldTimeSlow = state;
            }
        }

        internal void OnActionCommitted(ActorId actor, CharacterSkillId skill)
        {
            int actorIndex = m_Characters.GetActorIndex(actor);
            if (m_SelfTimeSlows[actorIndex].Active &&
                skill == m_Characters.Roster[actorIndex].CombatBinding.PerfectEvadeTimeSlow.CounterSkillId)
                m_SelfTimeSlows[actorIndex] = default;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        internal void ApplySimulatedParryHalt(int actorIndex, ulong actionInstanceId, bool heavy, ulong tick)
        {
            PortableAttackPropertyComponent property = heavy ? m_ParryHeavyProperties[actorIndex] : m_ParryLightProperties[actorIndex];
            AddHalt(actorIndex, actionInstanceId, property.AttackerFrameHalt, tick - 1);
        }
#endif

        public bool HasAcceptedHit(ActorId actor, ulong actionInstanceId, ulong segmentGeneration, string attackEventId)
        {
            int actorIndex = m_Characters.GetActorIndex(actor);
            int start = m_AttackStateOffsets[actorIndex];
            int end = m_AttackStateOffsets[actorIndex + 1];
            for (int i = start; i < end; i++)
            {
                FixedCombatAttackState state = m_AttackStates[i];
                if (state.Active && state.Identity.ActionInstanceId == actionInstanceId &&
                    state.Identity.SegmentGeneration == segmentGeneration && state.HitCount != 0 &&
                    state.EventId.Equals(attackEventId))
                    return true;
            }
            return false;
        }

        internal FixedCombatBatchResult Resolve(FixedCharacterEvaluationResultBatch evaluations, WorldSolveBatchResult world,
            IReadOnlyList<FixedLocalEnemyInput> localEnemies)
        {
            m_EnemyInputs = localEnemies;
            ulong tick = world.Tick.Value;
#if UNITY_EDITOR
            if (m_QueryDiagnostics.CaptureEnabled)
                m_QueryDiagnostics.BeginTick(tick);
#endif
            m_Pending.Clear();
            m_Batch.Tick = tick;
            m_Batch.CameraCount = 0;
            for (int i = 0; i < m_AttackStates.Length; i++)
            {
                FixedCombatAttackState state = m_AttackStates[i];
                if (state.Active && !IsAttackScopeActive(evaluations, state))
                    state.Active = false;
            }
            for (int i = 0; i < m_Halts.Length; i++)
                if (m_Halts[i].ActionInstanceId != 0 &&
                    (m_Halts[i].ResumeTick <= tick || !IsActionActive(evaluations.Evaluations[m_Halts[i].ActorIndex].CandidateState, m_Halts[i].ActionInstanceId)))
                    m_Halts[i] = default;
            int queryCount = 0;
            for (int i = 0; i < evaluations.Evaluations.Count; i++)
                queryCount += evaluations.Evaluations[i].AttackQueries.Count;
            for (int i = 0; i < localEnemies.Count; i++)
                queryCount += localEnemies[i].WindowCount;
            FixedScalar tickStart = default;
            FixedScalar tickEnd = default;
            int targetCount = 0;
            if (queryCount != 0)
            {
                tickEnd = FixedScalar.FromInt64((long)tick) / FixedScalar.FromInt64(m_Characters.TickRate);
                tickStart = tickEnd - m_TickDelta;
                targetCount = PrepareTargets(world, tickStart, tickEnd);
            }
            for (int actorIndex = 0; actorIndex < evaluations.Evaluations.Count; actorIndex++)
            {
                FixedCombatQueryStorage queries = evaluations.Evaluations[actorIndex].AttackQueries;
                for (int queryIndex = 0; queryIndex < queries.Count; queryIndex++)
                    ResolveQuery(actorIndex, queries[queryIndex], m_AttackLookup[(actorIndex, queries[queryIndex].Definition.Id)],
                        evaluations, world, targetCount, tickStart, tickEnd);
            }
            for (int i = 0; i < localEnemies.Count; i++)
            {
                FixedLocalEnemyInput input = localEnemies[i];
                FixedPreparedAttack prepared = m_EnemyAttacks[i];
                for (int window = 0; window < input.WindowCount; window++)
                {
                    m_EnemyQuery.LocalWindow = input.Windows[window];
                    m_EnemyQuery.Identity = new CombatAttackIdentity(m_Participants[prepared.ActorIndex].ActorId,
                        m_EnemyQuery.LocalWindow.ReleaseSequence, prepared.ContentId);
                    m_EnemyQuery.AttackEventId = prepared.ContentId;
                    m_EnemyQuery.NodeId = prepared.ContentId;
                    m_EnemyQuery.RequestId = m_EnemyQuery.LocalWindow.ReleaseSequence;
                    m_EnemyQuery.Origin = m_EnemyQuery.LocalWindow.BeforePosition;
                    m_EnemyQuery.Yaw = m_EnemyQuery.LocalWindow.BeforeYaw;
                    ResolveQuery(prepared.ActorIndex, m_EnemyQuery, prepared, evaluations, world, targetCount, tickStart, tickEnd);
                }
            }
            m_LastTick = tick;
#if UNITY_EDITOR
            if (m_QueryDiagnostics.CaptureEnabled)
                m_QueryDiagnostics.CompleteTick();
#endif
            return m_Batch;
        }

        void ResolveQuery(int actorIndex, FixedCombatQuery request, FixedPreparedAttack prepared,
            FixedCharacterEvaluationResultBatch evaluations, WorldSolveBatchResult world, int targetCount,
            FixedScalar tickStart, FixedScalar tickEnd)
        {
            FixedCombatAttackState state = FindAttackState(actorIndex, request, prepared.Index);
            if (state.LastQueryTick == world.Tick.Value && request.Identity.SourceKind == CombatAttackSourceKind.CharacterAction)
                return;
            bool first = state.LastQueryTick == 0;
            state.LastQueryTick = world.Tick.Value;
            PortableAttackCollisionComponent collision = prepared.Collision;
            FixedScalar endTime = tickEnd;
            FixedScalar startTime = tickStart;
            bool continuous = collision.Kind is PortableAttackCollisionKind.BoxContinuous or PortableAttackCollisionKind.GeometrySweep;
            SweepPose<FixedScalar> startPose;
            SweepPose<FixedScalar> endPose;
            if (request.Identity.SourceKind == CombatAttackSourceKind.LocalEnemyAttack)
            {
                FixedLocalEnemyAttackWindow window = request.LocalWindow;
                FixedScalar duration = endTime - startTime;
                endTime = startTime + duration * window.EndFraction;
                startTime += duration * window.StartFraction;
                startPose = FixedCombatGeometryPreparation.Pose(window.BeforePosition, window.BeforeYaw, collision.CenterOffset);
                endPose = FixedCombatGeometryPreparation.Pose(window.Position, window.Yaw, collision.CenterOffset);
            }
            else
            {
                int characterIndex = m_Participants[actorIndex].CharacterIndex;
                WorldBodyState before = world.Request.Requests[characterIndex].BeforeBody;
                WorldBodyState after = world.Results[characterIndex].FinalBody;
                if (continuous && !collision.FollowAttacker)
                {
                    if (first)
                    {
                        state.Origin = before.Position + (after.Position - before.Position) * request.BodyStartFraction;
                        state.Yaw = new FixedYaw(before.Yaw.Degrees +
                            new FixedYaw(after.Yaw.Degrees - before.Yaw.Degrees).Degrees * request.BodyStartFraction);
                    }
                    startPose = FixedCombatGeometryPreparation.Pose(state.Origin, state.Yaw, collision.CenterOffset);
                    endPose = startPose;
                }
                else
                {
                    SweepPose<FixedScalar> bodyBefore = FixedCombatGeometryPreparation.Pose(before.Position, before.Yaw, collision.CenterOffset);
                    SweepPose<FixedScalar> bodyAfter = FixedCombatGeometryPreparation.Pose(after.Position, after.Yaw, collision.CenterOffset);
                    endPose = FixedAttackSweepMath.Lerp(bodyBefore, bodyAfter, request.BodyEndFraction);
                    startPose = continuous ? FixedAttackSweepMath.Lerp(bodyBefore, bodyAfter, request.BodyStartFraction) : endPose;
                }
                FixedScalar duration = endTime - startTime;
                endTime = startTime + duration * request.BodyEndFraction;
                startTime += duration * (continuous ? request.BodyStartFraction : request.BodyEndFraction);
            }
            var track = new SweepTrack((ulong)prepared.Index + 1, prepared.ShapeIndex,
                continuous ? SweepMotionModel.RigidRotation : SweepMotionModel.ShapeTranslation, request.AttackEventId);
            m_Segments[0] = new SweepSegment<FixedScalar>(track,
                new SweepSample<FixedScalar>(continuous ? startTime : endTime, startPose),
                new SweepSample<FixedScalar>(endTime, endPose));
            int segmentCount = 1;
            if (collision.Kind == PortableAttackCollisionKind.GeometrySweep)
            {
                int characterIndex = m_Participants[actorIndex].CharacterIndex;
                WorldBodyState before = world.Request.Requests[characterIndex].BeforeBody;
                WorldBodyState after = world.Results[characterIndex].FinalBody;
                SweepPose<FixedScalar> bodyStart;
                SweepPose<FixedScalar> bodyEnd;
                if (collision.FollowAttacker)
                {
                    bodyStart = FixedCombatGeometryPreparation.Pose(before.Position, before.Yaw, default);
                    bodyEnd = FixedCombatGeometryPreparation.Pose(after.Position, after.Yaw, default);
                }
                else
                {
                    bodyStart = FixedCombatGeometryPreparation.Pose(state.Origin, state.Yaw, default);
                    bodyEnd = bodyStart;
                }
                segmentCount = m_SweepSampling.Write(prepared, request, bodyStart, bodyEnd,
                    tickStart, m_TickDelta, m_Segments);
            }
            var source = new SweepQuerySource(request.RequestId, m_Participants[actorIndex].ActorId,
                world.Tick, request.Action, prepared.ContentId, request.NodeId,
                request.Identity.PlaybackGeneration, (ulong)request.Identity.Cycle + 1);
            var query = new SweepQuery<FixedScalar>(in source, m_Segments.AsSpan(0, segmentCount), m_Targets.AsSpan(0, targetCount),
                SweepTargetModel.RigidRotation, endTime,
                new SweepFilter(-1, true, (ulong)actorIndex + 1));
#if UNITY_EDITOR
            bool captureQuery = m_QueryDiagnostics.CaptureEnabled;
            FixedCombatQueryDiagnostic diagnostic = null;
            if (captureQuery)
                diagnostic = m_QueryDiagnostics.Append(m_Participants[actorIndex].ActorId, prepared.ContentId,
                    request.AttackEventId, request.NodeId, query.Segments,
                    query.TargetModel, query.Filter, query.Targets);
#endif
            try
            {
                SweepQueryStatus status = m_Backend.SweepAll(in query, m_QueryResults);
                if (status != SweepQueryStatus.Complete)
                    throw new InvalidOperationException($"Attack '{request.AttackEventId}' query failed: {status}.");
                ReadOnlySpan<SweepCandidate<FixedScalar>> candidates = m_QueryResults.Candidates;
                for (int i = 0; i < candidates.Length; i++)
                {
                    ref readonly SweepCandidate<FixedScalar> candidate = ref candidates[i];
                    int targetIndex = m_TargetIndices[candidate.Target.TargetId];
#if UNITY_EDITOR
                    int acceptedBefore = m_Pending.Count;
#endif
                    Accept(actorIndex, targetIndex, request, prepared, state, in candidate, evaluations, world);
#if UNITY_EDITOR
                    if (captureQuery)
                    {
                        bool accepted = m_Pending.Count != acceptedBefore;
                        diagnostic.AddContact(in candidate, accepted,
                            accepted ? m_Pending[m_Pending.Count - 1].Disposition : default);
                    }
#endif
                }
            }
            finally
            {
                m_QueryResults.Release();
            }
            if (first && prepared.Camera != null && prepared.Camera.ShakeOnNotHit)
                AddCamera(actorIndex, prepared.Index, request);
        }

        int PrepareTargets(WorldSolveBatchResult world, FixedScalar tickStart, FixedScalar tickEnd)
        {
            int count = 0;
            for (int i = 0; i < m_Participants.Length; i++)
            {
                if (m_TargetShapes[i] < 0)
                    continue;
                Participant participant = m_Participants[i];
                FixedVector3 afterPosition;
                FixedYaw afterYaw;
                FixedVector3 beforePosition;
                FixedYaw beforeYaw;
                if (participant.CharacterIndex >= 0)
                {
                    WorldBodyState after = world.Results[participant.CharacterIndex].FinalBody;
                    WorldBodyState before = world.Request.Requests[participant.CharacterIndex].BeforeBody;
                    afterPosition = after.Position;
                    afterYaw = after.Yaw;
                    beforePosition = before.Position;
                    beforeYaw = before.Yaw;
                }
                else
                {
                    FixedLocalEnemyInput input = m_EnemyInputs[participant.LocalEnemyIndex];
                    afterPosition = input.Position;
                    afterYaw = input.Yaw;
                    beforePosition = input.BeforePosition;
                    beforeYaw = input.BeforeYaw;
                }
                SweepPose<FixedScalar> end = FixedCombatGeometryPreparation.Pose(afterPosition, afterYaw, m_TargetOffsets[i]);
                SweepPose<FixedScalar> start = FixedCombatGeometryPreparation.Pose(beforePosition, beforeYaw, m_TargetOffsets[i]);
                m_Targets[count++] = new SweepTarget<FixedScalar>(new SweepTargetIdentity((ulong)i + 1, (ulong)m_TargetShapes[i] + 1),
                    m_TargetShapes[i], 0, true,
                    new SweepSample<FixedScalar>(tickStart, start),
                    new SweepSample<FixedScalar>(tickEnd, end));
            }
            return count;
        }

        void Accept(int sourceIndex, int targetIndex, FixedCombatQuery request, FixedPreparedAttack definition,
            FixedCombatAttackState state, in SweepCandidate<FixedScalar> candidate,
            FixedCharacterEvaluationResultBatch evaluations, WorldSolveBatchResult world)
        {
            Participant sourceParticipant = m_Participants[sourceIndex];
            Participant targetParticipant = m_Participants[targetIndex];
            if (sourceParticipant.LocalEnemyIndex >= 0 && targetParticipant.ActorId != sourceParticipant.LocalTarget)
                return;
            CharacterCombatRuntimeBinding sourceBinding = sourceParticipant.Character;
            CharacterCombatRuntimeBinding targetBinding = targetParticipant.Character;
            PortableAttackPropertyComponent property = definition.Property;
            bool allied = sourceParticipant.FactionId == targetParticipant.FactionId;
            if (targetParticipant.Neutral ? !property.HitsNeutral : allied ? !property.HitsAllied : !property.HitsEnemy)
                return;
            PortableAttackCollisionComponent collision = definition.Collision;
            ulong tick = world.Tick.Value;
            if (collision.AliveMaxHitCount > 0 && state.TotalCount >= collision.AliveMaxHitCount ||
                collision.UnitMaxHitCount > 0 && state.TargetCounts[targetIndex] >= collision.UnitMaxHitCount)
                return;
            if (state.TargetCounts[targetIndex] != 0 &&
                FixedScalar.FromInt64((long)(tick - state.TargetTicks[targetIndex])) / FixedScalar.FromInt64(m_Characters.TickRate) < collision.HitInterval)
                return;
            FixedCharacterEvaluationResult target = targetParticipant.CharacterIndex >= 0
                ? evaluations.Evaluations[targetParticipant.CharacterIndex] : null;
            ulong evadeAction = 0;
            if (targetParticipant.CharacterIndex >= 0 && FindWindow(target, m_Characters.Roster[targetParticipant.CharacterIndex], targetBinding.InvulnerableWindowTag) != 0)
            {
                evadeAction = FindWindow(target, m_Characters.Roster[targetParticipant.CharacterIndex], targetBinding.PerfectEvadeWindowTag);
                if (evadeAction == 0 || FindWindow(target, m_Characters.Roster[targetParticipant.CharacterIndex], targetBinding.PerfectEvadeReadyWindowTag) != 0)
                    return;
                for (int i = 0; i < m_Pending.Count; i++)
                    if (m_Pending[i].Disposition == CombatHitDisposition.Evaded &&
                        m_Pending[i].TargetActor == targetParticipant.ActorId &&
                        m_Pending[i].TargetActionInstanceId == evadeAction)
                        return;
            }
            ulong parryAction = evadeAction == 0 && targetParticipant.CharacterIndex >= 0 ? FindWindow(target, m_Characters.Roster[targetParticipant.CharacterIndex], targetBinding.ParryWindowTag) : 0;
            ulong targetAction = evadeAction != 0 ? evadeAction :
                parryAction != 0 ? parryAction : targetParticipant.CharacterIndex >= 0 ? FindActiveAction(target.CandidateState) : 0;
            CombatHitResult hit = m_Pending.Append();
            hit.ResultId = m_NextResultId++;
            hit.SourceTick = tick;
            hit.SourceActor = sourceParticipant.ActorId;
            FixedVector3 sourcePosition = sourceParticipant.CharacterIndex >= 0
                ? world.Results[sourceParticipant.CharacterIndex].FinalBody.Position : m_EnemyInputs[sourceParticipant.LocalEnemyIndex].Position;
            FixedYaw sourceYaw = sourceParticipant.CharacterIndex >= 0
                ? world.Results[sourceParticipant.CharacterIndex].FinalBody.Yaw : m_EnemyInputs[sourceParticipant.LocalEnemyIndex].Yaw;
            hit.SourceTargetSnapshot = new SimulationActionTargetSnapshot(
                hit.SourceActor.Value, sourcePosition, sourceYaw);
            hit.TargetActor = targetParticipant.ActorId;
            hit.TargetPosition = targetParticipant.CharacterIndex >= 0
                ? world.Results[targetParticipant.CharacterIndex].FinalBody.Position : m_EnemyInputs[targetParticipant.LocalEnemyIndex].Position;
            hit.TargetYaw = targetParticipant.CharacterIndex >= 0
                ? world.Results[targetParticipant.CharacterIndex].FinalBody.Yaw : m_EnemyInputs[targetParticipant.LocalEnemyIndex].Yaw;
            FixedScalar receiverCenterY = hit.TargetPosition.Y + m_TargetOffsets[targetIndex].Y;
            hit.ReceiverMinimumY = receiverCenterY - m_TargetHalfHeights[targetIndex];
            hit.ReceiverMaximumY = receiverCenterY + m_TargetHalfHeights[targetIndex];
            hit.Attack = request.Identity;
            hit.ContentId = definition.ContentId;
            hit.AttackEventId = request.AttackEventId;
            hit.SourceFeedbackEvent = evadeAction != 0 || sourceParticipant.CharacterIndex < 0 ? string.Empty : sourceBinding.LandedEvent;
            hit.TargetFeedbackEvent = targetParticipant.CharacterIndex < 0 ? string.Empty : evadeAction != 0 ? targetBinding.PerfectEvadeEvent : parryAction != 0
                ? property.IsHeavyAttack ? targetBinding.ParryHeavyEvent : targetBinding.ParryLightEvent
                : property.IsHeavyAttack ? targetBinding.HitHeavyEvent : targetBinding.HitLightEvent;
            hit.TargetActionInstanceId = targetAction;
            hit.Disposition = evadeAction != 0 ? CombatHitDisposition.Evaded :
                parryAction != 0 ? CombatHitDisposition.Parried : CombatHitDisposition.Hit;
            hit.Heavy = property.IsHeavyAttack;
            hit.HitType = property.HitType;
            hit.HitStrengthType = property.HitStrengthType;
            hit.DamageBreakLevel = property.DamageBreakLevel;
            SetFrameHalts(hit, targetIndex, property);
            hit.HitEffectId = targetParticipant.CharacterIndex < 0 || world.Results[targetParticipant.CharacterIndex].FinalBody.Grounded
                ? property.GroundHitEffectId : property.SkyHitEffectId;
            hit.ContactFields = candidate.Fields;
            hit.ContactTime = candidate.Time;
            hit.Point = new FixedVector3(candidate.Point.X, candidate.Point.Y, candidate.Point.Z);
            hit.Normal = new FixedVector3(candidate.Normal.X, candidate.Normal.Y, candidate.Normal.Z);
            hit.DefinitionIndex = definition.Index;
            if (evadeAction != 0)
            {
                StartPerfectEvadeTimeSlow(targetParticipant.CharacterIndex);
                return;
            }
            state.TotalCount++;
            state.TargetCounts[targetIndex]++;
            state.TargetTicks[targetIndex] = tick;
            if (parryAction == 0)
                state.HitCount++;
            if (sourceParticipant.CharacterIndex >= 0)
                AddHalt(sourceParticipant.CharacterIndex, request.Identity.ActionInstanceId, hit.AttackerFrameHalt, tick);
            if (targetParticipant.CharacterIndex >= 0)
                AddHalt(targetParticipant.CharacterIndex, targetAction, hit.FrameHalt, tick);
            if (parryAction == 0 && definition.Camera != null && !definition.Camera.ShakeOnNotHit)
                AddCamera(sourceIndex, definition.Index, request);
        }

        FixedCombatAttackState FindAttackState(int actorIndex, FixedCombatQuery request, int definitionIndex)
        {
            int start = m_AttackStateOffsets[actorIndex];
            int end = m_AttackStateOffsets[actorIndex + 1];
            FixedCombatAttackState unused = null;
            for (int i = start; i < end; i++)
            {
                FixedCombatAttackState previous = m_AttackStates[i];
                if (request.Identity.SourceKind == CombatAttackSourceKind.CharacterAction &&
                    previous.Active &&
                    previous.Identity.ActionInstanceId == request.Identity.ActionInstanceId &&
                    previous.EventId.Equals(request.AttackEventId) &&
                    (previous.Identity.TimelineRuntimeHandle != request.Identity.TimelineRuntimeHandle ||
                     previous.Identity.PlaybackGeneration != request.Identity.PlaybackGeneration ||
                     previous.Identity.Cycle != request.Identity.Cycle ||
                     previous.Identity.SegmentGeneration != request.Identity.SegmentGeneration))
                    previous.Active = false;
            }
            for (int i = start; i < end; i++)
            {
                FixedCombatAttackState state = m_AttackStates[i];
                if (!state.Active)
                {
                    if (unused == null)
                        unused = state;
                    continue;
                }
                if (state.Identity.Equals(request.Identity))
                    return state;
                if (request.Identity.SourceKind == CombatAttackSourceKind.CharacterAction &&
                    state.Identity.ActionInstanceId == request.Identity.ActionInstanceId &&
                    state.Identity.SkillId == request.Identity.SkillId && state.Identity.Operation.Equals(request.Identity.Operation) &&
                    state.Identity.ClipId.Equals(request.Identity.ClipId))
                {
                    state.Begin(request, definitionIndex);
                    return state;
                }
            }
            if (unused == null)
                throw new InvalidOperationException("Combat attack instance capacity exceeded.");
            unused.Begin(request, definitionIndex);
            return unused;
        }

        static bool IsActionActive(FixedCharacterRuntimeState state, ulong instanceId)
        {
            ReadOnlySpan<FixedActionInstanceState> actions = state.ActionInstances;
            for (int i = 0; i < actions.Length; i++)
                if (actions[i].InstanceId == instanceId)
                    return actions[i].IsActive;
            return false;
        }

        bool IsAttackScopeActive(FixedCharacterEvaluationResultBatch evaluations, FixedCombatAttackState state)
        {
            Participant participant = m_Participants[state.ActorIndex];
            return state.Identity.SourceKind == CombatAttackSourceKind.LocalEnemyAttack
                ? m_EnemyInputs[participant.LocalEnemyIndex].ActiveReleaseSequence == state.Identity.LocalReleaseSequence
                : IsAttackScopeActive(evaluations.Evaluations[participant.CharacterIndex].CandidateState, state.Identity);
        }

        static bool IsAttackScopeActive(FixedCharacterRuntimeState state, in CombatAttackIdentity identity)
        {
            ReadOnlySpan<FixedActionInstanceState> actions = state.ActionInstances;
            for (int i = 0; i < actions.Length; i++)
                if (actions[i].InstanceId == identity.ActionInstanceId)
                    return actions[i].IsActive && actions[i].SegmentGeneration == identity.SegmentGeneration;
            return false;
        }

        static ulong FindActiveAction(FixedCharacterRuntimeState state)
        {
            ulong instanceId = 0;
            ulong start = 0;
            ReadOnlySpan<FixedActionInstanceState> actions = state.ActionInstances;
            for (int i = 0; i < actions.Length; i++)
                if (actions[i].IsActive && actions[i].StartTick >= start)
                {
                    instanceId = actions[i].InstanceId;
                    start = actions[i].StartTick;
                }
            return instanceId;
        }

        static bool IsWindowSourceActive(FixedCharacterRuntimeState state, in GameplayAbilitySourceContext source)
        {
            ReadOnlySpan<FixedActionInstanceState> actions = state.ActionInstances;
            for (int i = 0; i < actions.Length; i++)
                if (actions[i].SourceContext.Equals(source))
                    return actions[i].IsActive;
            return false;
        }

        static ulong FindWindow(FixedCharacterEvaluationResult evaluation, SimulationActorBinding actor, string windowTag)
        {
            IReadOnlyList<GameplayFact> facts = evaluation.Output.Facts;
            for (int i = 0; i < facts.Count; i++)
                if (facts[i].Kind == GameplayFactKind.ActionWindow &&
                    actor.AbilityInstallations.GameplayEffectCatalog.IsTagOrParent(facts[i].ActionWindow.WindowTag, windowTag) &&
                    facts[i].ActionWindow.StartTick <= evaluation.Tick.Value && evaluation.Tick.Value <= facts[i].ActionWindow.EndTick &&
                    IsWindowSourceActive(evaluation.CandidateState, facts[i].ActionWindow.SourceContext))
                    return facts[i].ActionWindow.ActionInstanceId;
            return 0;
        }

        void AddHalt(int actorIndex, ulong actionInstanceId, int frames, ulong tick)
        {
            if (actionInstanceId == 0 || frames <= 0)
                return;
            ulong resume = tick + 1 + (ulong)((frames * (long)m_Characters.TickRate + 59) / 60);
            int unused = -1;
            for (int i = 0; i < m_Halts.Length; i++)
            {
                if (m_Halts[i].ActionInstanceId == 0)
                    unused = i;
                else if (m_Halts[i].ActorIndex == actorIndex && m_Halts[i].ActionInstanceId == actionInstanceId)
                {
                    m_Halts[i].ResumeTick = Math.Max(resume, m_Halts[i].ResumeTick);
                    return;
                }
            }
            if (unused < 0)
                throw new InvalidOperationException("Combat action halt capacity exceeded.");
            m_Halts[unused] = new FixedCombatHalt { ActorIndex = actorIndex, ActionInstanceId = actionInstanceId, ResumeTick = resume };
        }

        void SetFrameHalts(CombatHitResult hit, int targetIndex, PortableAttackPropertyComponent incoming)
        {
            if (hit.Disposition == CombatHitDisposition.Evaded)
            {
                hit.FrameHalt = 0;
                hit.AttackerFrameHalt = 0;
            }
            else if (hit.Disposition == CombatHitDisposition.Parried)
            {
                PortableAttackPropertyComponent feedback = hit.Heavy ? m_ParryHeavyProperties[targetIndex] : m_ParryLightProperties[targetIndex];
                hit.FrameHalt = feedback.AttackerFrameHalt;
                hit.AttackerFrameHalt = feedback.FrameHalt;
            }
            else
            {
                hit.FrameHalt = incoming.FrameHalt;
                hit.AttackerFrameHalt = incoming.AttackerFrameHalt;
            }
        }

        void AddCamera(int actorIndex, int definitionIndex, FixedCombatQuery query)
        {
            for (int i = 0; i < m_Batch.CameraCount; i++)
                if (m_Batch.Cameras[i].ActorIndex == actorIndex &&
                    m_Batch.Cameras[i].Header.EventId.Equals(query.Header.EventId))
                    return;
            if (m_Batch.CameraCount == m_Batch.Cameras.Length)
                throw new InvalidOperationException("Combat camera result capacity exceeded.");
            m_Batch.Cameras[m_Batch.CameraCount++] = new FixedCombatCameraRequest
            {
                ActorIndex = actorIndex, DefinitionIndex = definitionIndex,
                Header = query.Header, ActionInstanceId = query.Identity.ActionInstanceId
            };
        }

        internal void Project(int actorIndex, FixedCombatBatchResult batch, FixedCharacterEvaluationOutput output)
        {
            ActorId actor = m_Characters.Roster[actorIndex].ActorId;
            for (int i = 0; i < batch.Hits.Count; i++)
                if (batch.Hits[i].SourceActor == actor || batch.Hits[i].TargetActor == actor)
                    output.AttackResults.Append().CopyFrom(batch.Hits[i]);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            CombatHitResult[] simulated = m_Characters.Roster[actorIndex].SimulatedIncomingAttackResults;
            for (int i = 0; i < simulated.Length; i++)
                if (simulated[i].SourceTick == batch.Tick)
                    output.AttackResults.Append().CopyFrom(simulated[i]);
#endif
            for (int i = 0; i < batch.CameraCount; i++)
            {
                ref readonly FixedCombatCameraRequest item = ref batch.Cameras[i];
                if (item.ActorIndex != actorIndex)
                    continue;
                FixedPreparedAttack definition = m_Attacks[item.DefinitionIndex];
                var request = PresentationCameraRequest.Effect(PresentationCameraRequestLifecycle.Activate,
                    definition.Definition.Id, 4, definition.Camera.ResourceId, 0, 1f, true);
                output.Presentation.Add(new PresentationCommand(item.Header, PresentationCommandKind.Camera,
                    definition.Definition.Id, FixedScalar.Zero, FixedScalar.One,
                    item.Header.Activation.Generation, sourceActionInstanceId: item.ActionInstanceId, cameraRequest: request));
            }
        }
    }
}
