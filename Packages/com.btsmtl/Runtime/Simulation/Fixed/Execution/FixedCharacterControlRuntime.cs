using System;
using System.Collections.Generic;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.Fixed
{
    internal sealed class FixedCharacterControlMotionRuntime
    {
        FixedAbilityExecutionInput m_Input;
        FixedAbilityBodyFacts m_Body;
        SimulationTick m_Tick;
        int m_TickRate;
        FixedScalar m_TimeScale;
        readonly FixedCharacterControlMotionBindingCatalog m_ControlMotionBindings;
        SimulationMotionContribution[] m_Contributions = new SimulationMotionContribution[1];
        int m_ContributionCount;

        public FixedCharacterControlMotionRuntime(
            FixedCharacterControlMotionBindingCatalog controlMotionBindings)
        {
            m_ControlMotionBindings = controlMotionBindings;
        }

        internal void Begin(
            FixedAbilityExecutionInput input,
            FixedAbilityBodyFacts body,
            SimulationTick tick,
            int tickRate,
            FixedScalar timeScale)
        {
            m_Input = input ?? throw new ArgumentNullException(nameof(input));
            if (!body.IsValid)
                throw new ArgumentException("Fixed Character Control motion requires Body Facts.", nameof(body));
            if (!tick.IsValid || tickRate <= 0)
                throw new ArgumentException("Fixed Character Control motion identity is incomplete.");
            Array.Clear(m_Contributions, 0, m_ContributionCount);
            m_ContributionCount = 0;
            m_Body = body;
            m_Tick = tick;
            m_TickRate = tickRate;
            m_TimeScale = timeScale;
        }

        internal void CopyContributionsTo(FixedMotionContributionScratch contributions)
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
            CharacterControlMotionDescriptor descriptor)
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
                descriptor,
                ref m_Contributions,
                ref m_ContributionCount);
        }

        internal static void SubmitControl(
            FixedVector2 move,
            FixedAbilityBodyFacts body,
            SimulationTick tick,
            int tickRate,
            FixedScalar timeScale,
            FixedCharacterControlMotionBindingCatalog controlMotionBindings,
            CharacterControlMotionRequest request,
            CharacterControlMotionDescriptor descriptor,
            ref SimulationMotionContribution[] contributions,
            ref int contributionCount)
        {
            if (!body.IsValid || !tick.IsValid || tickRate <= 0)
                throw new ArgumentException("Fixed Character Control motion identity is incomplete.");
            if (request.Input != descriptor.Input || !string.Equals(request.Binding, descriptor.Binding, StringComparison.Ordinal))
                throw new InvalidOperationException($"Control motion request '{request.Binding}' does not match its declared motion.");
            if (move.SqrMagnitude > FixedScalar.One)
                move = move.Normalized;
            FixedScalar nominalDelta = FixedScalar.One / FixedScalar.FromInt64(tickRate);
            FixedScalar delta = nominalDelta * timeScale;
            FixedScalar moveSpeed = FixedScalar.FromDouble(descriptor.MoveSpeed);
            FixedScalar turnSpeed = FixedScalar.FromDouble(descriptor.TurnSpeedDegrees);
            FixedScalar maxYaw = turnSpeed * delta;
            FixedVector3 displacement;
            FixedScalar yaw;
            if (descriptor.DisplacementMode == CharacterControlMotionDisplacementMode.SourceCurve)
            {
                FixedCharacterControlMotionBinding source = controlMotionBindings == null
                    ? throw new InvalidOperationException("Fixed Control motion bindings are not installed.")
                    : controlMotionBindings.Require(descriptor.SourceMotionIdentity);
                source.EvaluateDelta(
                    FixedScalar.FromDouble(request.ContinuousTicks) / FixedScalar.FromInt64(tickRate),
                    (FixedScalar.FromDouble(request.ContinuousTicks) + timeScale) / FixedScalar.FromInt64(tickRate),
                    out displacement,
                    out yaw);
            }
            else
            {
                displacement = new FixedVector3(
                    move.X * moveSpeed * delta,
                    FixedScalar.Zero,
                    move.Y * moveSpeed * delta);
                yaw = FixedScalar.Zero;
                if (move != FixedVector2.Zero && maxYaw > FixedScalar.Zero)
                {
                    FixedYaw desired = FixedAngle.FromPlanarDirection(move);
                    yaw = FixedScalar.Clamp(FixedAngle.Delta(body.Yaw, desired), -maxYaw, maxYaw);
                }
            }
            double continuousTicks = request.ContinuousTicks + timeScale.ToDouble();
            int durationTicks = descriptor.DisplacementMode == CharacterControlMotionDisplacementMode.SourceCurve
                ? 0
                : descriptor.ExecutionMode == CharacterControlMotionExecutionMode.Timed
                ? checked((int)Math.Ceiling(descriptor.DurationSeconds * tickRate))
                : 0;
            var movementPlaybackClock = new CommittedMovementPlaybackClock(
                request.SourceIdentity,
                request.PlaybackGeneration,
                tick,
                continuousTicks,
                tickRate);
            var locomotionTimeline = new CommittedLocomotionPlanarMotionTimeline(
                request.SourceIdentity,
                request.PlaybackGeneration,
                tick,
                tickRate,
                (displacement.X / nominalDelta).ToSingle(),
                (displacement.Z / nominalDelta).ToSingle(),
                (yaw / nominalDelta).ToSingle(),
                (turnSpeed * timeScale).ToSingle(),
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
                    ? FixedVector2.Zero
                    : move,
                descriptor.Space == CharacterControlMotionSpace.ActorLocal
                    ? SimulationMotionContributionSpace.ActorLocal
                    : SimulationMotionContributionSpace.World,
                FixedScalar.One,
                descriptor.Priority,
                SimulationMotionChannel.Locomotion,
                SimulationMotionBlendMode.Override,
                descriptor.ConsumeLowerChannels,
                movementPlaybackClock,
                locomotionTimeline);
        }

    }

    internal sealed class FixedCharacterTraceSink
    {
        List<SimulationTraceRecord> m_Records;
        FixedCharacterEvaluationOutput m_Output;
        ActorId m_ActorId;
        SimulationTick m_Tick;
        ulong m_Sequence;
        bool m_Enabled;
        bool m_MotionEnabled;

        public bool Enabled => m_Enabled;

        public FixedCharacterTraceSink(
            List<SimulationTraceRecord> records)
        {
            m_Records = records ?? throw new ArgumentNullException(nameof(records));
        }

        internal void Bind(FixedCharacterEvaluationOutput output)
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
                channel.Displacement.X.Raw, channel.Displacement.Y.Raw, channel.Displacement.Z.Raw,
                channel.YawDegrees.Raw, channel.MovementPlaybackClock,
                channel: (int)channel.Channel,
                planarBasisXRaw: channel.PlanarBasis.X.Raw, planarBasisYRaw: channel.PlanarBasis.Y.Raw,
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
                motion.Displacement.X.Raw, motion.Displacement.Y.Raw, motion.Displacement.Z.Raw,
                motion.YawDegrees.Raw, motion.MovementPlaybackClock,
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

    internal sealed class FixedCharacterControlReadPort : ICharacterControlReadPort
    {
        readonly FixedAbilityExecutionInput m_Input;
        IFixedAbilityFrameReadPort m_ReadScope;
        readonly Func<CharacterControlParameterId, FixedScalar> m_ReadParameter;
        FixedAbilityBodyFacts m_Body;

        public FixedCharacterControlReadPort(FixedAbilityExecutionInput input,
            Func<CharacterControlParameterId, FixedScalar> readParameter)
        {
            m_Input = input;
            m_ReadParameter = readParameter;
        }

        internal void Begin(FixedAbilityBodyFacts body) => m_Body = body;
        internal IFixedAbilityFrameReadPort ExchangeReadScope(IFixedAbilityFrameReadPort read)
        {
            IFixedAbilityFrameReadPort previous = m_ReadScope;
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
            FixedVector2 value = ReadInput(input).Vector2;
            return Compare(value.Magnitude, m_ReadParameter(threshold), comparison);
        }

        public bool CompareInputDirectionToBodyYaw(
            SimulationInputValueId input,
            CharacterControlParameterId threshold,
            CharacterControlNumericComparison comparison)
        {
            FixedVector2 value = ReadInput(input).Vector2;
            FixedScalar angle = value == FixedVector2.Zero
                ? FixedScalar.Zero
                : FixedScalar.Abs(FixedAngle.Delta((m_ReadScope == null ? m_Body : m_ReadScope.BodyFacts).Yaw, FixedAngle.FromPlanarDirection(value)));
            return Compare(angle, m_ReadParameter(threshold), comparison);
        }


        static bool Compare(
            FixedScalar value,
            FixedScalar threshold,
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

    internal sealed class FixedCharacterControlStatePort : ICharacterControlStatePort
    {
        readonly CharacterControlModuleContract m_Contract;
        readonly CharacterControlStateSchema m_Schema;
        CharacterControlRuntimeStateTransaction m_State;
        CharacterControlRuntimeState? m_ReadState;

        public FixedCharacterControlStatePort(CharacterControlModuleContract contract)
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

        public double ReadNumber(CharacterControlStateFieldId field) => ReadValue(field).Number;

        public void WriteNumber(CharacterControlStateFieldId field, double value) =>
            m_State.Set(field, CharacterControlStateValue.FromNumber(value));

        void RequireKind(CharacterControlStateFieldId field, CharacterControlStateValueKind expected)
        {
            if (m_Schema.RequireKind(field) != expected)
                throw new InvalidOperationException($"Control state field '{field}' is not '{expected}'.");
        }
    }

    internal sealed class FixedCharacterControlOutputPort : ICharacterControlOutputPort
    {
        readonly CharacterControlModuleContract m_ControlModule;
        readonly FixedCharacterControlMotionRuntime m_Motion;
        readonly FixedCharacterTraceSink m_Trace;

        public bool TraceEnabled => m_Trace.Enabled;

        public FixedCharacterControlOutputPort(
            CharacterControlModuleContract controlModule,
            FixedCharacterControlMotionRuntime motion,
            FixedCharacterTraceSink trace)
        {
            m_ControlModule = controlModule ?? throw new ArgumentNullException(nameof(controlModule));
            m_Motion = motion ?? throw new ArgumentNullException(nameof(motion));
            m_Trace = trace ?? throw new ArgumentNullException(nameof(trace));
        }

        public void SubmitMotion(CharacterControlMotionRequest request)
        {
            CharacterControlMotionDescriptor descriptor = RequireMotion(request.Binding);
            m_Motion.SubmitControl(request, descriptor);
        }

        public void Trace(SimulationExecutionSource source, string code, CanonicalTextValue detail, ulong generation, in CharacterControlTraceDetail controlDetail = default) =>
            m_Trace.Add("Character.Control", source, code, SimulationTraceSeverity.Information, detail, generation, in controlDetail);

        CharacterControlMotionDescriptor RequireMotion(string binding)
        {
            for (int i = 0; i < m_ControlModule.Motions.Count; i++)
            {
                CharacterControlMotionDescriptor motion = m_ControlModule.Motions[i];
                if (string.Equals(motion.Binding, binding, StringComparison.Ordinal))
                    return motion;
            }
            throw new InvalidOperationException($"Control module '{m_ControlModule.ModuleId}' has no motion '{binding}'.");
        }
    }

    internal sealed class FixedCharacterControlRuntime
    {
        readonly ICharacterControlModule m_Control;
        readonly CharacterControlRuntimeBinding m_Binding;
        readonly CharacterControlStateSchema m_Schema;
        FixedCharacterControlReadPort m_Read;
        FixedCharacterControlOutputPort m_Output;
        FixedCharacterControlStatePort m_StatePort;
        readonly ActorId m_ActorId;
        readonly int m_TickRate;
        SimulationTick m_Tick;
        CharacterControlRuntimeStateTransaction m_State;
        FixedScalar m_TimeScale;

        public FixedCharacterControlRuntime(
            CharacterControlModuleCatalog controlModules,
            CharacterControlRuntimeBinding binding,
            FixedAbilityExecutionInput input,
            ActorId actorId,
            int tickRate,
            FixedCharacterControlMotionRuntime motion,
            FixedCharacterTraceSink trace)
        {
            controlModules = controlModules ?? throw new ArgumentNullException(nameof(controlModules));
            m_Binding = binding ?? throw new ArgumentNullException(nameof(binding));
            if (!actorId.IsValid || tickRate <= 0)
                throw new ArgumentException("Fixed Character Control runtime identity is incomplete.");
            m_Control = controlModules.Require(binding.ModuleId);
            binding.RequireContract(m_Control.Contract);
            m_Schema = m_Control.Contract.StateSchema;
            m_ActorId = actorId;
            m_TickRate = tickRate;

            m_Read = new FixedCharacterControlReadPort(
                input,
                parameter => FixedScalar.FromDouble(binding.Parameters.ReadNumeric(parameter)));
            m_Output = new FixedCharacterControlOutputPort(
                m_Control.Contract,
                motion,
                trace);
            m_StatePort = new FixedCharacterControlStatePort(m_Control.Contract);
        }

        internal void Begin(
            IFixedControlRuntimeStatePort controlState,
            SimulationTick tick,
            FixedAbilityBodyFacts body,
            FixedScalar timeScale)
        {
            if (controlState == null)
                throw new ArgumentNullException(nameof(controlState));
            if (!tick.IsValid)
                throw new ArgumentException("Fixed Character Control runtime identity is incomplete.", nameof(tick));
            m_State = controlState.BindControl(m_Schema);
            m_StatePort.Begin(m_State);
            m_Read.Begin(body);
            m_Tick = tick;
            m_TimeScale = timeScale;
        }

        public CharacterControlRuntimeStateTransaction State => m_State;

        internal CharacterControlDecision ReadDecision(CharacterControlRuntimeState state,
            IFixedAbilityFrameReadPort read)
        {
            CharacterControlRuntimeState? previousState = m_StatePort.ExchangeReadState(state);
            IFixedAbilityFrameReadPort previousRead = m_Read.ExchangeReadScope(read);
            try { return m_Control.ReadDecision(m_Read, m_StatePort); }
            finally
            {
                m_Read.ExchangeReadScope(previousRead);
                m_StatePort.ExchangeReadState(previousState);
            }
        }

        public void PrepareDecision()
        {
            var context = new CharacterControlTickContext(m_ActorId, m_Tick, m_TickRate, m_TimeScale.ToDouble());
            m_State.BaseState.RequireBinding(m_Binding);
            m_Control.PrepareDecision(in context, m_Read, m_StatePort, m_Output);
        }

        public bool HasMoveInput => m_Control.HasMoveInput;
        public CharacterLocomotionMode LocomotionMode => m_Control.LocomotionMode;
        public void Tick(bool blockMovingTurn) => m_Control.Tick(blockMovingTurn);
        public void ApplyMovementIntent(bool continueRunning) => m_Control.ApplyMovementIntent(continueRunning);


    }
}
