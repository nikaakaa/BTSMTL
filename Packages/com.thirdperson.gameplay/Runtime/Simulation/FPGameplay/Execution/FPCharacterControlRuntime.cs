using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using MotionContributionBuffer = ThirdPersonSimulation.MotionContributionBuffer<Photon.Deterministic.FP, Photon.Deterministic.FPVector2, Photon.Deterministic.FPVector3, ThirdPersonSimulation.QuantumNumerics.QuantumSimulationMath>;
using ResolvedGameplayMotion = ThirdPersonSimulation.ResolvedGameplayMotion<Photon.Deterministic.FP, Photon.Deterministic.FPVector2, Photon.Deterministic.FPVector3>;
using SimulationMotionContribution = ThirdPersonSimulation.SimulationMotionContribution<Photon.Deterministic.FP, Photon.Deterministic.FPVector2, Photon.Deterministic.FPVector3, ThirdPersonSimulation.QuantumNumerics.QuantumSimulationMath>;
using ResolvedMotionChannel = ThirdPersonSimulation.ResolvedMotionChannel<Photon.Deterministic.FP, Photon.Deterministic.FPVector2, Photon.Deterministic.FPVector3, ThirdPersonSimulation.QuantumNumerics.QuantumSimulationMath>;
using System;
using System.Collections.Generic;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.FPGameplay
{
    internal readonly struct FPCharacterControlMotionParameters
    {
        internal FPCharacterControlMotionParameters(CharacterControlMotionDescriptor descriptor, int tickRate)
        {
            Descriptor = descriptor;
            MoveSpeed = QuantumScalarBoundary.ConvertExternal(descriptor.MoveSpeed, descriptor.Binding);
            TurnSpeedDegrees = QuantumScalarBoundary.ConvertExternal(descriptor.TurnSpeedDegrees, descriptor.Binding);
            DurationTicks = descriptor.DisplacementMode == CharacterControlMotionDisplacementMode.SourceCurve ||
                descriptor.ExecutionMode != CharacterControlMotionExecutionMode.Timed
                ? 0
                : FPMath.CeilToInt(QuantumScalarBoundary.ConvertExternal(descriptor.DurationSeconds, descriptor.Binding) * tickRate);
        }

        internal readonly CharacterControlMotionDescriptor Descriptor;
        internal readonly FP MoveSpeed;
        internal readonly FP TurnSpeedDegrees;
        internal readonly int DurationTicks;
    }

    internal sealed class FPCharacterControlMotionRuntime
    {
        FPAbilityExecutionInput m_Input;
        FPAbilityBodyFacts m_Body;
        SimulationTick m_Tick;
        int m_TickRate;
        FP m_TimeScale;
        readonly FPCharacterControlMotionBindingCatalog m_ControlMotionBindings;
        SimulationMotionContribution[] m_Contributions = new SimulationMotionContribution[1];
        int m_ContributionCount;

        public FPCharacterControlMotionRuntime(
            FPCharacterControlMotionBindingCatalog controlMotionBindings)
        {
            m_ControlMotionBindings = controlMotionBindings;
        }

        internal void Begin(
            FPAbilityExecutionInput input,
            FPAbilityBodyFacts body,
            SimulationTick tick,
            int tickRate,
            FP timeScale)
        {
            m_Input = input ?? throw new ArgumentNullException(nameof(input));
            if (!body.IsValid)
                throw new ArgumentException("FP Character Control motion requires Body Facts.", nameof(body));
            if (!tick.IsValid || tickRate <= 0)
                throw new ArgumentException("FP Character Control motion identity is incomplete.");
            Array.Clear(m_Contributions, 0, m_ContributionCount);
            m_ContributionCount = 0;
            m_Body = body;
            m_Tick = tick;
            m_TickRate = tickRate;
            m_TimeScale = timeScale;
        }

        internal void CopyContributionsTo(MotionContributionBuffer contributions)
        {
            for (int i = 0; i < m_ContributionCount; i++)
                contributions.Append(m_Contributions[i]);
        }

        internal void ClearContributions()
        {
            Array.Clear(m_Contributions, 0, m_ContributionCount);
            m_ContributionCount = 0;
        }

        public void SubmitControl(
            CharacterControlMotionRequest request,
            in FPCharacterControlMotionParameters parameters)
        {
            SimulationInputValue input = m_Input.ReadValue(request.Input.Value, ProgramInputValueKind.Vector2);
            SubmitControl(
                input.Vector2,
                m_Body,
                m_Tick,
                m_TickRate,
                m_TimeScale,
                m_ControlMotionBindings,
                request,
                in parameters,
                ref m_Contributions,
                ref m_ContributionCount);
        }

        internal static void SubmitControl(
            FPVector2 move,
            FPAbilityBodyFacts body,
            SimulationTick tick,
            int tickRate,
            FP timeScale,
            FPCharacterControlMotionBindingCatalog controlMotionBindings,
            CharacterControlMotionRequest request,
            in FPCharacterControlMotionParameters parameters,
            ref SimulationMotionContribution[] contributions,
            ref int contributionCount)
        {
            CharacterControlMotionDescriptor descriptor = parameters.Descriptor;
            if (!body.IsValid || !tick.IsValid || tickRate <= 0)
                throw new ArgumentException("FP Character Control motion identity is incomplete.");
            if (request.Input != descriptor.Input || !string.Equals(request.Binding, descriptor.Binding, StringComparison.Ordinal))
                throw new InvalidOperationException($"Control motion request '{request.Binding}' does not match its declared motion.");
            if (move.SqrMagnitude > FP._1)
                move = move.Normalized;
            FP nominalDelta = FP._1 / default(QuantumSimulationMath).FromInt64(tickRate);
            FP delta = nominalDelta * timeScale;
            FP moveSpeed = parameters.MoveSpeed;
            FP turnSpeed = parameters.TurnSpeedDegrees;
            FP maxYaw = turnSpeed * delta;
            FPVector3 displacement;
            FP yaw;
            if (descriptor.DisplacementMode == CharacterControlMotionDisplacementMode.SourceCurve)
            {
                FPCharacterControlMotionBinding source = controlMotionBindings == null
                    ? throw new InvalidOperationException("FP Control motion bindings are not installed.")
                    : controlMotionBindings.Require(descriptor.SourceMotionIdentity);
                source.EvaluateDelta(
                    request.ContinuousTicks / default(QuantumSimulationMath).FromInt64(tickRate),
                    (request.ContinuousTicks + timeScale) / default(QuantumSimulationMath).FromInt64(tickRate),
                    out displacement,
                    out yaw);
            }
            else
            {
                displacement = new FPVector3(
                    move.X * moveSpeed * delta,
                    FP._0,
                    move.Y * moveSpeed * delta);
                yaw = FP._0;
                if (move != FPVector2.Zero && maxYaw > FP._0)
                {
                    FP desired = default(QuantumSimulationMath).YawFromDirection(move.X, move.Y);
                    yaw = FPMath.Clamp(default(QuantumSimulationMath).DeltaYaw(body.Yaw, desired), -maxYaw, maxYaw);
                }
            }
            FP continuousTicks = request.ContinuousTicks + timeScale;
            int durationTicks = parameters.DurationTicks;
            var movementPlaybackClock = new CommittedMovementPlaybackClock(
                request.SourceIdentity,
                request.PlaybackGeneration,
                tick,
                continuousTicks.AsDouble,
                tickRate);
            var locomotionTimeline = new CommittedLocomotionPlanarMotionTimeline(
                request.SourceIdentity,
                request.PlaybackGeneration,
                tick,
                tickRate,
                (displacement.X / nominalDelta).AsFloat,
                (displacement.Z / nominalDelta).AsFloat,
                (yaw / nominalDelta).AsFloat,
                (turnSpeed * timeScale).AsFloat,
                durationTicks,
                string.Empty,
                0f,
                0f);
            contributions[contributionCount++] = new SimulationMotionContribution(
                request.Source,
                request.SourceIdentity,
                default,
                request.PlaybackGeneration,
                displacement,
                yaw,
                descriptor.DisplacementMode == CharacterControlMotionDisplacementMode.SourceCurve
                    ? FPVector2.Zero
                    : move,
                descriptor.Space == CharacterControlMotionSpace.ActorLocal
                    ? SimulationMotionContributionSpace.ActorLocal
                    : SimulationMotionContributionSpace.World,
                FP._1,
                descriptor.Priority,
                SimulationMotionChannel.Locomotion,
                SimulationMotionBlendMode.Override,
                descriptor.ConsumeLowerChannels,
                movementPlaybackClock,
                locomotionTimeline);
        }

    }

    internal sealed class FPCharacterTraceSink
    {
        List<SimulationTraceRecord> m_Records;
        FPCharacterEvaluationOutput m_Output;
        ActorId m_ActorId;
        SimulationTick m_Tick;
        ulong m_Sequence;
        bool m_Enabled;
        bool m_MotionEnabled;

        public bool Enabled => m_Enabled;

        public FPCharacterTraceSink(
            List<SimulationTraceRecord> records)
        {
            m_Records = records ?? throw new ArgumentNullException(nameof(records));
        }

        internal void Bind(FPCharacterEvaluationOutput output)
        {
            m_Output = output;
            m_Records = output.CharacterTrace;
        }

        internal void Begin(
            ActorId actorId,
            SimulationTick tick,
            bool enabled,
            bool captureMotion)
        {
            m_ActorId = actorId;
            m_Tick = tick;
            m_Enabled = enabled;
            m_MotionEnabled = captureMotion;
            m_Sequence = 0;
        }

        public void AddMotionChannel(in ResolvedMotionChannel channel)
        {
            if (!m_MotionEnabled || !channel.TraceSource.IsValid)
                return;
            var detail = new SimulationMotionTraceDetail(
                SimulationMotionTraceDetailKind.Channel,
                channel.Displacement.X.RawValue, channel.Displacement.Y.RawValue, channel.Displacement.Z.RawValue,
                channel.YawDegrees.RawValue, channel.MovementPlaybackClock,
                channel: (int)channel.Channel,
                planarBasisXRaw: channel.PlanarBasis.X.RawValue, planarBasisYRaw: channel.PlanarBasis.Y.RawValue,
                claimsLowerChannels: channel.ClaimsLowerChannels,
                participatingSourceCount: channel.ParticipatingSourceCount,
                participatingSourceFingerprint: channel.ParticipatingSourceFingerprint);
            Append("Character.Motion", channel.TraceSource, "motion_channel_resolved", SimulationTraceSeverity.Detail,
                channel.ResolvedOwnerIdentity,
                channel.TraceSourceGeneration, motionDetail: in detail);
        }

        public void AddResolvedMotion(in ResolvedGameplayMotion motion, in ResolvedMotionChannel source)
        {
            if (!m_MotionEnabled)
                return;
            var detail = new SimulationMotionTraceDetail(
                SimulationMotionTraceDetailKind.Resolved,
                motion.Displacement.X.RawValue, motion.Displacement.Y.RawValue, motion.Displacement.Z.RawValue,
                motion.YawDegrees.RawValue, motion.MovementPlaybackClock,
                hasMotion: motion.HasMotion);
            Append("Character.Motion", source.TraceSource, "resolved_gameplay_motion", SimulationTraceSeverity.Information,
                string.Empty,
                source.TraceSourceGeneration, motionDetail: in detail);
        }

        public void Add(
            string category,
            SimulationExecutionSource source,
            string code,
            SimulationTraceSeverity severity,
            CanonicalTextValue detail,
            ulong generation = 0,
            in CharacterControlTraceDetail controlDetail = default)
        {
            if (!m_Enabled)
                return;
            Append(category, source, code, severity, detail, generation, controlDetail: in controlDetail);
        }

        public void AddRuleDecision(SimulationExecutionSource cause, string decision,
            in GameplayAbilitySourceContext source, ulong inputSequence)
        {
            if (m_Enabled)
                Append("Character.AbilityRules", cause, decision, SimulationTraceSeverity.Information,
                    string.Empty, 1, source.InstanceId, cause.AbilityId.Value, inputSequence, ruleSource: in source);
        }

        void Append(
            string category,
            SimulationExecutionSource source,
            string code,
            SimulationTraceSeverity severity,
            CanonicalTextValue detail,
            ulong generation,
            ulong actionInstanceId = 0,
            string skillId = "",
            ulong inputSequence = 0,
            in SimulationMotionTraceDetail motionDetail = default,
            in GameplayAbilitySourceContext ruleSource = default,
            in CharacterControlTraceDetail controlDetail = default)
        {
            m_Output.ReserveTraceRecord();
            ulong sequence = checked(++m_Sequence);
            if (generation == 0)
                generation = 1;
            var activation = new ActivationId(source, generation);
            var header = new SimulationTraceHeader(
                m_ActorId,
                m_Tick,
                activation,
                sequence);
            var record = new SimulationTraceRecord(
                header,
                severity,
                category,
                code,
                detail,
                actionInstanceId: actionInstanceId,
                skillId: skillId,
                inputSequence: inputSequence,
                motionDetail: in motionDetail,
                ruleSource: in ruleSource, controlDetail: in controlDetail);
            record.Retain();
            m_Records.Add(record);
        }
    }

    internal sealed class FPCharacterControlReadPort : ICharacterControlReadPort
    {
        readonly FPAbilityExecutionInput m_Input;
        IFPAbilityFrameReadPort m_ReadScope;
        readonly Dictionary<CharacterControlParameterId, FP> m_Parameters;
        FPAbilityBodyFacts m_Body;

        public FPCharacterControlReadPort(FPAbilityExecutionInput input,
            CharacterControlParameterSet parameters)
        {
            m_Input = input;
            m_Parameters = new Dictionary<CharacterControlParameterId, FP>(parameters.Values.Count);
            for (int index = 0; index < parameters.Values.Count; index++)
            {
                CharacterControlParameterValue parameter = parameters.Values[index];
                m_Parameters.Add(parameter.Id, QuantumScalarBoundary.ConvertExternal(parameter.NumericValue, parameter.Id.Value));
            }
        }

        internal void Begin(FPAbilityBodyFacts body) => m_Body = body;
        internal IFPAbilityFrameReadPort ExchangeReadScope(IFPAbilityFrameReadPort read)
        {
            IFPAbilityFrameReadPort previous = m_ReadScope;
            m_ReadScope = read;
            return previous;
        }
        SimulationInputValue ReadInput(SimulationInputValueId input) => m_ReadScope == null
            ? m_Input.ReadValue(input.Value, ProgramInputValueKind.Vector2)
            : m_ReadScope.ReadInputValue(input.Value, ProgramInputValueKind.Vector2);


        public bool CompareInputVector2Magnitude(
            SimulationInputValueId input,
            CharacterControlParameterId threshold,
            CharacterControlNumericComparison comparison)
        {
            FPVector2 value = ReadInput(input).Vector2;
            return Compare(value.Magnitude, m_Parameters[threshold], comparison);
        }

        public bool CompareInputDirectionToBodyYaw(
            SimulationInputValueId input,
            CharacterControlParameterId threshold,
            CharacterControlNumericComparison comparison)
        {
            FPVector2 value = ReadInput(input).Vector2;
            FP angle = value == FPVector2.Zero
                ? FP._0
                : FPMath.Abs(default(QuantumSimulationMath).DeltaYaw((m_ReadScope == null ? m_Body : m_ReadScope.BodyFacts).Yaw, default(QuantumSimulationMath).YawFromDirection(value.X, value.Y)));
            return Compare(angle, m_Parameters[threshold], comparison);
        }


        static bool Compare(
            FP value,
            FP threshold,
            CharacterControlNumericComparison comparison)
        {
            return comparison switch
            {
                CharacterControlNumericComparison.Less => value < threshold,
                CharacterControlNumericComparison.LessOrEqual => value <= threshold,
                CharacterControlNumericComparison.Equal => value == threshold,
                CharacterControlNumericComparison.Greater => value > threshold,
                CharacterControlNumericComparison.GreaterOrEqual => value >= threshold,
                _ => throw new ArgumentOutOfRangeException(nameof(comparison))
            };
        }

    }

    internal sealed class FPCharacterControlStatePort : ICharacterControlStatePort
    {
        readonly CharacterControlModuleContract m_Contract;
        readonly CharacterControlStateSchema m_Schema;
        CharacterControlRuntimeStateTransaction m_State;
        CharacterControlRuntimeState? m_ReadState;

        public FPCharacterControlStatePort(CharacterControlModuleContract contract)
        {
            m_Contract = contract;
            m_Schema = contract.StateSchema;
        }

        internal void Begin(CharacterControlRuntimeStateTransaction state)
        {
            m_State = state ?? throw new ArgumentNullException(nameof(state));
        }

        internal CharacterControlRuntimeState? ExchangeReadState(CharacterControlRuntimeState? state)
        {
            CharacterControlRuntimeState? previous = m_ReadState;
            m_ReadState = state;
            return previous;
        }
        CharacterControlStateValue ReadValue(CharacterControlStateFieldId field) =>
            !m_ReadState.HasValue ? m_State.Get(field) : m_ReadState.Value.Get(field);

        public CharacterControlStateId ReadState(CharacterControlStateFieldId field)
        {
            RequireKind(field, CharacterControlStateValueKind.Identity);
            CanonicalTextValue value = ReadValue(field).Identity;
            if (value.IsEmpty)
                return default;
            return new CharacterControlStateId(m_Schema.ResolvePublishedIdentity(m_Schema.RequireIndex(field), in value));
        }

        public void WriteState(CharacterControlStateFieldId field, CharacterControlStateId value)
        {
            m_State.Set(field, CharacterControlStateValue.FromIdentity(value.Value));
        }

        public CharacterControlTransitionId ReadTransition(CharacterControlStateFieldId field)
        {
            RequireKind(field, CharacterControlStateValueKind.Identity);
            CanonicalTextValue value = ReadValue(field).Identity;
            if (value.IsEmpty)
                return default;
            return new CharacterControlTransitionId(m_Schema.ResolvePublishedIdentity(m_Schema.RequireIndex(field), in value));
        }

        public void WriteTransition(CharacterControlStateFieldId field, CharacterControlTransitionId value)
        {
            m_State.Set(field, CharacterControlStateValue.FromIdentity(value.Value));
        }

        public bool ReadBoolean(CharacterControlStateFieldId field)
        {
            RequireKind(field, CharacterControlStateValueKind.Boolean);
            return ReadValue(field).Boolean;
        }

        public void WriteBoolean(CharacterControlStateFieldId field, bool value)
        {
            m_State.Set(field, CharacterControlStateValue.FromBoolean(value));
        }

        public int ReadInt32(CharacterControlStateFieldId field)
        {
            RequireKind(field, CharacterControlStateValueKind.Int32);
            return ReadValue(field).Int32;
        }

        public void WriteInt32(CharacterControlStateFieldId field, int value)
        {
            m_State.Set(field, CharacterControlStateValue.FromInt32(value));
        }

        public ulong ReadUInt64(CharacterControlStateFieldId field)
        {
            RequireKind(field, CharacterControlStateValueKind.UInt64);
            return ReadValue(field).UInt64;
        }

        public void WriteUInt64(CharacterControlStateFieldId field, ulong value)
        {
            m_State.Set(field, CharacterControlStateValue.FromUInt64(value));
        }

        public FP ReadNumber(CharacterControlStateFieldId field) => ReadValue(field).Number;

        public void WriteNumber(CharacterControlStateFieldId field, FP value) =>
            m_State.Set(field, CharacterControlStateValue.FromNumber(value));

        void RequireKind(CharacterControlStateFieldId field, CharacterControlStateValueKind expected)
        {
            if (m_Schema.RequireKind(field) != expected)
                throw new InvalidOperationException($"Control state field '{field}' is not '{expected}'.");
        }
    }

    internal sealed class FPCharacterControlOutputPort : ICharacterControlOutputPort
    {
        readonly CharacterControlModuleContract m_ControlModule;
        readonly FPCharacterControlMotionParameters[] m_Motions;
        readonly FPCharacterControlMotionRuntime m_Motion;
        readonly FPCharacterTraceSink m_Trace;

        public bool TraceEnabled => m_Trace.Enabled;

        public FPCharacterControlOutputPort(
            CharacterControlModuleContract controlModule,
            FPCharacterControlMotionRuntime motion,
            FPCharacterTraceSink trace,
            int tickRate)
        {
            m_ControlModule = controlModule ?? throw new ArgumentNullException(nameof(controlModule));
            m_Motion = motion ?? throw new ArgumentNullException(nameof(motion));
            m_Trace = trace ?? throw new ArgumentNullException(nameof(trace));
            m_Motions = new FPCharacterControlMotionParameters[controlModule.Motions.Count];
            for (int index = 0; index < m_Motions.Length; index++)
                m_Motions[index] = new FPCharacterControlMotionParameters(controlModule.Motions[index], tickRate);
        }

        public void SubmitMotion(CharacterControlMotionRequest request)
        {
            ref readonly FPCharacterControlMotionParameters parameters = ref RequireMotion(request.Binding);
            m_Motion.SubmitControl(request, in parameters);
        }

        public void Trace(SimulationExecutionSource source, string code, CanonicalTextValue detail, ulong generation, in CharacterControlTraceDetail controlDetail = default) =>
            m_Trace.Add("Character.Control", source, code, SimulationTraceSeverity.Information, detail, generation, in controlDetail);

        ref readonly FPCharacterControlMotionParameters RequireMotion(string binding)
        {
            for (int i = 0; i < m_Motions.Length; i++)
            {
                if (string.Equals(m_Motions[i].Descriptor.Binding, binding, StringComparison.Ordinal))
                    return ref m_Motions[i];
            }
            throw new InvalidOperationException($"Control module '{m_ControlModule.ModuleId}' has no motion '{binding}'.");
        }
    }

    internal sealed class FPCharacterControlRuntime
    {
        readonly ICharacterControlModule m_Control;
        readonly CharacterControlRuntimeBinding m_Binding;
        readonly CharacterControlStateSchema m_Schema;
        FPCharacterControlReadPort m_Read;
        FPCharacterControlOutputPort m_Output;
        FPCharacterControlStatePort m_StatePort;
        readonly ActorId m_ActorId;
        readonly int m_TickRate;
        SimulationTick m_Tick;
        CharacterControlRuntimeStateTransaction m_State;
        FP m_TimeScale;

        public FPCharacterControlRuntime(
            CharacterControlModuleCatalog controlModules,
            CharacterControlRuntimeBinding binding,
            FPAbilityExecutionInput input,
            ActorId actorId,
            int tickRate,
            FPCharacterControlMotionRuntime motion,
            FPCharacterTraceSink trace)
        {
            controlModules = controlModules ?? throw new ArgumentNullException(nameof(controlModules));
            m_Binding = binding ?? throw new ArgumentNullException(nameof(binding));
            if (!actorId.IsValid || tickRate <= 0)
                throw new ArgumentException("FP Character Control runtime identity is incomplete.");
            m_Control = controlModules.Require(binding.ModuleId);
            binding.RequireContract(m_Control.Contract);
            m_Schema = m_Control.Contract.StateSchema;
            m_ActorId = actorId;
            m_TickRate = tickRate;

            m_Read = new FPCharacterControlReadPort(
                input,
                binding.Parameters);
            m_Output = new FPCharacterControlOutputPort(
                m_Control.Contract,
                motion,
                trace,
                tickRate);
            m_StatePort = new FPCharacterControlStatePort(m_Control.Contract);
        }

        internal void Begin(
            IFPControlRuntimeStatePort controlState,
            SimulationTick tick,
            FPAbilityBodyFacts body,
            FP timeScale)
        {
            if (controlState == null)
                throw new ArgumentNullException(nameof(controlState));
            if (!tick.IsValid)
                throw new ArgumentException("FP Character Control runtime identity is incomplete.", nameof(tick));
            m_State = controlState.BindControl(m_Schema);
            m_StatePort.Begin(m_State);
            m_Read.Begin(body);
            m_Tick = tick;
            m_TimeScale = timeScale;
        }

        public CharacterControlRuntimeStateTransaction State => m_State;

        internal CharacterControlDecision ReadDecision(CharacterControlRuntimeState state,
            IFPAbilityFrameReadPort read)
        {
            CharacterControlRuntimeState? previousState = m_StatePort.ExchangeReadState(state);
            IFPAbilityFrameReadPort previousRead = m_Read.ExchangeReadScope(read);
            try { return m_Control.ReadDecision(m_Read, m_StatePort); }
            finally
            {
                m_Read.ExchangeReadScope(previousRead);
                m_StatePort.ExchangeReadState(previousState);
            }
        }

        public void PrepareDecision()
        {
            var context = new CharacterControlTickContext(m_ActorId, m_Tick, m_TickRate, m_TimeScale);
            m_State.BaseState.RequireBinding(m_Binding);
            m_Control.PrepareDecision(in context, m_Read, m_StatePort, m_Output);
        }

        public bool HasMoveInput => m_Control.HasMoveInput;
        public CharacterLocomotionMode LocomotionMode => m_Control.LocomotionMode;
        public void Tick(bool blockMovingTurn) => m_Control.Tick(blockMovingTurn);
        public void ApplyMovementIntent(bool continueRunning) => m_Control.ApplyMovementIntent(continueRunning);


    }
}
