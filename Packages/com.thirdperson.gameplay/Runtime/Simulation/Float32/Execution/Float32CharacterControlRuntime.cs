using MotionContributionBuffer = ThirdPersonSimulation.MotionContributionBuffer<ThirdPersonSimulation.Float32Scalar, ThirdPersonSimulation.Float32Vector2, ThirdPersonSimulation.Float32Vector3, ThirdPersonSimulation.Float32SimulationMath>;
using ResolvedGameplayMotion = ThirdPersonSimulation.ResolvedGameplayMotion<ThirdPersonSimulation.Float32Scalar, ThirdPersonSimulation.Float32Vector2, ThirdPersonSimulation.Float32Vector3>;
using SimulationMotionContribution = ThirdPersonSimulation.SimulationMotionContribution<ThirdPersonSimulation.Float32Scalar, ThirdPersonSimulation.Float32Vector2, ThirdPersonSimulation.Float32Vector3, ThirdPersonSimulation.Float32SimulationMath>;
using ResolvedMotionChannel = ThirdPersonSimulation.ResolvedMotionChannel<ThirdPersonSimulation.Float32Scalar, ThirdPersonSimulation.Float32Vector2, ThirdPersonSimulation.Float32Vector3, ThirdPersonSimulation.Float32SimulationMath>;
using System;
using System.Collections.Generic;
using Photon.Deterministic;

namespace ThirdPersonSimulation
{
    internal sealed class Float32CharacterControlMotionRuntime
    {
        Float32AbilityExecutionInput m_Input;
        Float32AbilityBodyFacts m_Body;
        SimulationTick m_Tick;
        int m_TickRate;
        readonly CharacterControlMotionBindingCatalog m_ControlMotionBindings;
        SimulationMotionContribution[] m_Contributions = new SimulationMotionContribution[1];
        int m_ContributionCount;

        public Float32CharacterControlMotionRuntime(
            CharacterControlMotionBindingCatalog controlMotionBindings)
        {
            m_ControlMotionBindings = controlMotionBindings;
        }

        internal void Begin(
            Float32AbilityExecutionInput input,
            Float32AbilityBodyFacts body,
            SimulationTick tick,
            int tickRate)
        {
            m_Input = input ?? throw new ArgumentNullException(nameof(input));
            if (!body.IsValid)
                throw new ArgumentException("Float32 Character Control motion requires Body Facts.", nameof(body));
            if (!tick.IsValid || tickRate <= 0)
                throw new ArgumentException("Float32 Character Control motion identity is incomplete.");
            Array.Clear(m_Contributions, 0, m_ContributionCount);
            m_ContributionCount = 0;
            m_Body = body;
            m_Tick = tick;
            m_TickRate = tickRate;
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
            CharacterControlMotionDescriptor descriptor)
        {
            SimulationInputValue input = m_Input.ReadValue(request.Input.Value, ProgramInputValueKind.Vector2);
            SubmitControl(
                input.Vector2,
                m_Body,
                m_Tick,
                m_TickRate,
                m_ControlMotionBindings,
                request,
                descriptor,
                ref m_Contributions,
                ref m_ContributionCount);
        }

        internal static void SubmitControl(
            Float32Vector2 move,
            Float32AbilityBodyFacts body,
            SimulationTick tick,
            int tickRate,
            CharacterControlMotionBindingCatalog controlMotionBindings,
            CharacterControlMotionRequest request,
            CharacterControlMotionDescriptor descriptor,
            ref SimulationMotionContribution[] contributions,
            ref int contributionCount)
        {
            if (!body.IsValid || !tick.IsValid || tickRate <= 0)
                throw new ArgumentException("Float32 Character Control motion identity is incomplete.");
            if (request.Input != descriptor.Input || !CanonicalTextValue.Equals(request.Binding, descriptor.Binding, StringComparison.Ordinal))
                throw new InvalidOperationException($"Control motion request '{request.Binding}' does not match its declared motion.");
            if (move.SqrMagnitude > Float32Scalar.One)
                move = move.Normalized;
            Float32Scalar delta = Float32Scalar.One / Float32Scalar.FromInt64(tickRate);
            Float32Scalar moveSpeed = Float32Scalar.FromDouble(descriptor.MoveSpeed);
            Float32Scalar turnSpeed = Float32Scalar.FromDouble(descriptor.TurnSpeedDegrees);
            Float32Scalar maxYaw = turnSpeed * delta;
            Float32Vector3 displacement;
            Float32Scalar yaw;
            if (descriptor.DisplacementMode == CharacterControlMotionDisplacementMode.SourceCurve)
            {
                CharacterControlMotionBinding source = controlMotionBindings == null
                    ? throw new InvalidOperationException("Float32 Control motion bindings are not installed.")
                    : controlMotionBindings.Require(descriptor.SourceMotionIdentity);
                CharacterControlMotionDelta curveDelta = source.EvaluateDelta(
                    request.ContinuousTicks.AsDouble / tickRate,
                    (request.ContinuousTicks + FP._1).AsDouble / tickRate);
                displacement = new Float32Vector3(
                    Float32Scalar.FromDouble(curveDelta.X),
                    Float32Scalar.FromDouble(curveDelta.Y),
                    Float32Scalar.FromDouble(curveDelta.Z));
                yaw = Float32Scalar.FromDouble(curveDelta.Yaw);
            }
            else
            {
                displacement = new Float32Vector3(
                    move.X * moveSpeed * delta,
                    Float32Scalar.Zero,
                    move.Y * moveSpeed * delta);
                yaw = Float32Scalar.Zero;
                if (move != Float32Vector2.Zero && maxYaw > Float32Scalar.Zero)
                {
                    Float32Yaw desired = Float32Angle.FromPlanarDirection(move);
                    yaw = Float32Scalar.Clamp(Float32Angle.Delta(body.Yaw, desired), -maxYaw, maxYaw);
                }
            }
            FP continuousTicks = request.ContinuousTicks + FP._1;
            int durationTicks = descriptor.DisplacementMode == CharacterControlMotionDisplacementMode.SourceCurve
                ? 0
                : descriptor.ExecutionMode == CharacterControlMotionExecutionMode.Timed
                ? checked((int)Math.Ceiling(descriptor.DurationSeconds * tickRate))
                : 0;
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
                (displacement.X / delta).ToSingle(),
                (displacement.Z / delta).ToSingle(),
                (yaw / delta).ToSingle(),
                turnSpeed.ToSingle(),
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
                    ? Float32Vector2.Zero
                    : move,
                descriptor.Space == CharacterControlMotionSpace.ActorLocal
                    ? SimulationMotionContributionSpace.ActorLocal
                    : SimulationMotionContributionSpace.World,
                Float32Scalar.One,
                descriptor.Priority,
                SimulationMotionChannel.Locomotion,
                SimulationMotionBlendMode.Override,
                descriptor.ConsumeLowerChannels,
                movementPlaybackClock,
                locomotionTimeline);
        }

    }

    internal sealed class Float32CharacterTraceSink
    {
        List<SimulationTraceRecord> m_Records;
        Float32CharacterEvaluationOutput m_Output;
        SimulationStructuredTraceStorage m_Structured;
        ActorId m_ActorId;
        SimulationTick m_Tick;
        ulong m_Sequence;
        bool m_Enabled;

        public bool Enabled => m_Enabled;

        public Float32CharacterTraceSink(
            List<SimulationTraceRecord> records)
        {
            m_Records = records ?? throw new ArgumentNullException(nameof(records));
        }

        internal void Bind(Float32CharacterEvaluationOutput output)
        {
            m_Output = output;
            m_Records = output.CharacterTrace; m_Structured = output.StructuredTraces;
        }

        internal void Begin(
            ActorId actorId,
            SimulationTick tick,
            bool enabled)
        {
            m_ActorId = actorId;
            m_Tick = tick;
            m_Enabled = enabled;
            m_Sequence = 0;
        }

        public void AddMotionChannel(in ResolvedMotionChannel channel)
        {
            if (!m_Enabled || !channel.TraceSource.IsValid)
                return;
            CommittedMovementPlaybackClock clock = channel.MovementPlaybackClock;
            var detail = new SimulationMotionTrace(SimulationMotionTraceDetailKind.Channel,
                channel.Displacement, channel.YawDegrees, in clock, owner: channel.ResolvedOwnerIdentity,
                channel: (int)channel.Channel, planarBasis: channel.PlanarBasis, claimsLowerChannels: channel.ClaimsLowerChannels,
                participatingSources: channel.ParticipatingSourceCount, fingerprint: channel.ParticipatingSourceFingerprint);
            Append("Character.Motion", channel.TraceSource, "motion_channel_resolved", SimulationTraceSeverity.Detail,
                string.Empty, channel.TraceSourceGeneration, structured: m_Structured.CaptureCharacterMotion(in detail));
        }

        public void AddResolvedMotion(in ResolvedGameplayMotion motion, in ResolvedMotionChannel source)
        {
            if (!m_Enabled)
                return;
            CommittedMovementPlaybackClock clock = motion.MovementPlaybackClock;
            var detail = new SimulationMotionTrace(SimulationMotionTraceDetailKind.Resolved,
                motion.Displacement, motion.YawDegrees, in clock, hasMotion: motion.HasMotion);
            Append("Character.Motion", source.TraceSource, "resolved_gameplay_motion", SimulationTraceSeverity.Information,
                string.Empty, source.TraceSourceGeneration, structured: m_Structured.CaptureCharacterMotion(in detail));
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
            in GameplayAbilitySourceContext ruleSource = default,
            SimulationStructuredTrace structured = default,
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
                ruleSource: in ruleSource, structured: structured, controlDetail: in controlDetail);
            record.Retain();
            m_Records.Add(record);
        }
    }

    internal sealed class Float32CharacterControlReadPort : ICharacterControlReadPort
    {
        readonly Float32AbilityExecutionInput m_Input;
        IFloat32AbilityFrameReadPort m_ReadScope;
        readonly Func<CharacterControlParameterId, Float32Scalar> m_ReadParameter;
        Float32AbilityBodyFacts m_Body;

        public Float32CharacterControlReadPort(Float32AbilityExecutionInput input,
            Func<CharacterControlParameterId, Float32Scalar> readParameter)
        {
            m_Input = input;
            m_ReadParameter = readParameter;
        }

        internal void Begin(Float32AbilityBodyFacts body) => m_Body = body;
        internal IFloat32AbilityFrameReadPort ExchangeReadScope(IFloat32AbilityFrameReadPort read)
        {
            IFloat32AbilityFrameReadPort previous = m_ReadScope;
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
            Float32Vector2 value = ReadInput(input).Vector2;
            return Compare(value.Magnitude, m_ReadParameter(threshold), comparison);
        }

        public bool CompareInputDirectionToBodyYaw(
            SimulationInputValueId input,
            CharacterControlParameterId threshold,
            CharacterControlNumericComparison comparison)
        {
            Float32Vector2 value = ReadInput(input).Vector2;
            Float32Scalar angle = value == Float32Vector2.Zero
                ? Float32Scalar.Zero
                : Float32Scalar.Abs(Float32Angle.Delta((m_ReadScope == null ? m_Body : m_ReadScope.BodyFacts).Yaw, Float32Angle.FromPlanarDirection(value)));
            return Compare(angle, m_ReadParameter(threshold), comparison);
        }


        static bool Compare(
            Float32Scalar value,
            Float32Scalar threshold,
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

    internal sealed class Float32CharacterControlStatePort : ICharacterControlStatePort
    {
        readonly CharacterControlStateSchema m_Schema;
        readonly CharacterControlModuleContract m_Contract;
        CharacterControlRuntimeStateTransaction m_State;
        CharacterControlRuntimeState? m_ReadState;

        public Float32CharacterControlStatePort(CharacterControlModuleContract contract)
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
            for (int i = 0; i < m_Contract.States.Count; i++)
                if (value.Equals(m_Contract.States[i].Id.Value))
                    return m_Contract.States[i].Id;
            throw new InvalidOperationException("Control state identity is absent from the published contract.");
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
            for (int i = 0; i < m_Contract.Transitions.Count; i++)
                if (value.Equals(m_Contract.Transitions[i].Id.Value))
                    return m_Contract.Transitions[i].Id;
            throw new InvalidOperationException("Control transition identity is absent from the published contract.");
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

    internal sealed class Float32CharacterControlOutputPort : ICharacterControlOutputPort
    {
        readonly CharacterControlModuleContract m_ControlModule;
        readonly Float32CharacterControlMotionRuntime m_Motion;
        readonly Float32CharacterTraceSink m_Trace;

        public bool TraceEnabled => m_Trace.Enabled;

        public Float32CharacterControlOutputPort(
            CharacterControlModuleContract controlModule,
            Float32CharacterControlMotionRuntime motion,
            Float32CharacterTraceSink trace)
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
                if (CanonicalTextValue.Equals(motion.Binding, binding, StringComparison.Ordinal))
                    return motion;
            }
            throw new InvalidOperationException($"Control module '{m_ControlModule.ModuleId}' has no motion '{binding}'.");
        }
    }

    internal sealed class Float32CharacterControlRuntime
    {
        readonly ICharacterControlModule m_Control;
        readonly CharacterControlRuntimeBinding m_Binding;
        readonly CharacterControlStateSchema m_Schema;
        Float32CharacterControlReadPort m_Read;
        Float32CharacterControlOutputPort m_Output;
        Float32CharacterControlStatePort m_StatePort;
        readonly ActorId m_ActorId;
        readonly int m_TickRate;
        SimulationTick m_Tick;
        CharacterControlRuntimeStateTransaction m_State;

        public Float32CharacterControlRuntime(
            CharacterControlModuleCatalog controlModules,
            CharacterControlRuntimeBinding binding,
            Float32AbilityExecutionInput input,
            ActorId actorId,
            int tickRate,
            Float32CharacterControlMotionRuntime motion,
            Float32CharacterTraceSink trace)
        {
            controlModules = controlModules ?? throw new ArgumentNullException(nameof(controlModules));
            m_Binding = binding ?? throw new ArgumentNullException(nameof(binding));
            if (!actorId.IsValid || tickRate <= 0)
                throw new ArgumentException("Float32 Character Control runtime identity is incomplete.");
            m_Control = controlModules.Require(binding.ModuleId);
            binding.RequireContract(m_Control.Contract);
            m_Schema = m_Control.Contract.StateSchema;
            m_ActorId = actorId;
            m_TickRate = tickRate;
            m_Read = new Float32CharacterControlReadPort(
                input,
                parameter => Float32Scalar.FromDouble(binding.Parameters.ReadNumeric(parameter)));
            m_Output = new Float32CharacterControlOutputPort(
                m_Control.Contract,
                motion,
                trace);
            m_StatePort = new Float32CharacterControlStatePort(m_Control.Contract);
        }

        internal void Begin(
            IFloat32ControlRuntimeStatePort controlState,
            SimulationTick tick,
            Float32AbilityBodyFacts body)
        {
            if (controlState == null)
                throw new ArgumentNullException(nameof(controlState));
            if (!tick.IsValid)
                throw new ArgumentException("Float32 Character Control runtime identity is incomplete.", nameof(tick));
            m_State = controlState.BindControl(m_Schema);
            m_StatePort.Begin(m_State);
            m_Read.Begin(body);
            m_Tick = tick;
        }

        public CharacterControlRuntimeStateTransaction State => m_State;

        internal CharacterControlDecision ReadDecision(CharacterControlRuntimeState state,
            IFloat32AbilityFrameReadPort read)
        {
            CharacterControlRuntimeState? previousState = m_StatePort.ExchangeReadState(state);
            IFloat32AbilityFrameReadPort previousRead = m_Read.ExchangeReadScope(read);
            try { return m_Control.ReadDecision(m_Read, m_StatePort); }
            finally
            {
                m_Read.ExchangeReadScope(previousRead);
                m_StatePort.ExchangeReadState(previousState);
            }
        }

        public void PrepareDecision()
        {
            var context = new CharacterControlTickContext(m_ActorId, m_Tick, m_TickRate, FP._1);
            m_State.BaseState.RequireBinding(m_Binding);
            m_Control.PrepareDecision(in context, m_Read, m_StatePort, m_Output);
        }

        public bool HasMoveInput => m_Control.HasMoveInput;
        public CharacterLocomotionMode LocomotionMode => m_Control.LocomotionMode;
        public void Tick(bool blockMovingTurn) => m_Control.Tick(blockMovingTurn);
        public void ApplyMovementIntent(bool continueRunning) => m_Control.ApplyMovementIntent(continueRunning);


    }
}
