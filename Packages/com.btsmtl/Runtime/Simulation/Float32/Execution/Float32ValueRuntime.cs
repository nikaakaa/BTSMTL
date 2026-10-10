using System;
using System.Collections.Generic;
using static ThirdPersonSimulation.Float32GraphValueRuntime;

namespace ThirdPersonSimulation
{
    internal sealed class Float32ValueRuntime : Float32OperationValueRuntime
    {
        readonly Float32InputRuntime m_Input;
        readonly IFloat32ActionContextReader m_Actions;
        readonly IFloat32ActionAdmissionQuery m_ActionAdmission;
        readonly IFloat32GameplayTagQuery m_GameplayTags;
        readonly Float32EquipmentRuntime m_Equipment;
        readonly IFloat32BlackboardPort m_Blackboard;
        readonly Float32AbilityExecutionFrame m_Frame;
        readonly Float32StatePort m_ControlState;
        readonly Float32GameplayAbilityExecutionAccess Access;
        protected override bool IsAvailabilityQuery => m_Frame.RuleEvaluationMode == GameplayAbilityRuleEvaluationMode.Availability;

        public Float32ValueRuntime(
            Float32GameplayAbilityExecutionAccess access,
            Float32InputRuntime input,
            IFloat32ActionContextReader actions,
            IFloat32ActionAdmissionQuery actionAdmission,
            IFloat32GameplayTagQuery gameplayTags,
            Float32EquipmentRuntime equipment,
            IFloat32BlackboardPort blackboard,
            Float32AbilityExecutionFrame frame,
            Float32StatePort controlState,
            Float32AbilityExecutionWorkspace workspace)
            : base(access.Data, access.Layout, workspace.Values)
        {
            m_Input = input;
            m_Actions = actions ?? throw new ArgumentNullException(nameof(actions));
            m_ActionAdmission = actionAdmission ?? throw new ArgumentNullException(nameof(actionAdmission));
            m_GameplayTags = gameplayTags;
            m_Equipment = equipment;
            m_Blackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));
            m_Frame = frame ?? throw new ArgumentNullException(nameof(frame));
            m_ControlState = controlState;
            Access = access;
        }

        protected override AbilityStateValue EvaluateDomainValue<TTarget>(OperationControlCursor<TTarget> cursor,
            SimulationOperation operation, string outputPort, Float32ValueInputLease inputs)
        {
            AbilityStateValue result;
            switch (operation.Code)
            {
                case SimulationOperationCode.InputBoolean:
						result = AbilityStateValue.FromBoolean(m_Input.ReadValue(operation.Text0, ProgramInputValueKind.Boolean).Boolean);
						break;
                case SimulationOperationCode.InputScalar:
						result = AbilityStateValue.FromScalar(m_Input.ReadValue(operation.Text0, ProgramInputValueKind.Scalar).Scalar);
						break;
                case SimulationOperationCode.ActivationEntry:
                        result = AbilityStateValue.FromBoolean(m_Actions.IsActivationEntry(operation.Text0));
                        break;
                    case SimulationOperationCode.TimelineTime:
                        result = AbilityStateValue.FromScalar(Float32Scalar.FromSingle(m_Frame.TreeClipInvocation.Time.ToSingle()));
                        break;
                case SimulationOperationCode.ActionTargetRead:
                    result = ReadActionTarget(cursor, operation, outputPort);
                    break;
                case SimulationOperationCode.MotionWarp:
                    result = AbilityStateValue.FromScalar(m_Layout.FindNamedConstant(operation.Handle, OperationNamedConstant.WindowEndTime).Scalar);
                    break;
                case SimulationOperationCode.InputVector2:
						result = AbilityStateValue.FromVector2(m_Input.ReadValue(operation.Text0, ProgramInputValueKind.Vector2).Vector2);
						break;
                case SimulationOperationCode.InputVector2Magnitude:
						result = AbilityStateValue.FromScalar(m_Input.ReadValue(operation.Text0, ProgramInputValueKind.Vector2).Vector2.Magnitude);
						break;
                case SimulationOperationCode.InputRequest:
						result = AbilityStateValue.FromBoolean(m_Input.HasRequest(operation.Text0, out _));
						break;
                case SimulationOperationCode.BlackboardGet:
						result = ReadBlackboard(cursor, operation);
						break;
                case SimulationOperationCode.ActionContextActive:
						result = AbilityStateValue.FromBoolean(
							CanonicalTextValue.IsNullOrEmpty(operation.Text0)
								? m_Actions.IsCurrentExecutionContextActive()
								: m_Actions.IsContextActive(operation.Text0));
						break;
                case SimulationOperationCode.ActionEventReceived:
                        result = AbilityStateValue.FromBoolean(
                            m_Actions.TryGetCurrentSkillExecution(out Float32ActionInstanceState eventAction) &&
                            m_Frame.HasActionEvent(eventAction.InstanceId, operation.Text0, default));
                        break;
                    case SimulationOperationCode.AbilityActive:
                        result = AbilityStateValue.FromBoolean(m_Frame.DecisionRead.IsAbilityActive(m_Ability.AbilityId, operation.Handle));
                        break;
                    case SimulationOperationCode.SelectedSourceWindowActive:
                        result = AbilityStateValue.FromBoolean(m_Frame.DecisionRead.IsSelectedSourceWindowActive(
                            in m_Frame.SelectedSource, m_Ability.AbilityId, operation.Handle, operation.Text0));
                        break;
                    case SimulationOperationCode.SelectedSourcePhase:
                        result = AbilityStateValue.FromBoolean(m_Frame.DecisionRead.IsSelectedSourcePhase(
                            in m_Frame.SelectedSource, (AbilityLifecyclePhase)operation.Integer0));
                        break;
                    case SimulationOperationCode.LocomotionDecision:
                        result = AbilityStateValue.FromBoolean(operation.Integer0 == 0
                            ? m_Frame.DecisionRead.HasMoveInput
                            : m_Frame.DecisionRead.LocomotionMode == (CharacterLocomotionMode)operation.Integer1);
                        break;
                    case SimulationOperationCode.ActionWindowActive:
						result = AbilityStateValue.FromBoolean(m_ActionAdmission.IsWindowActive(operation));
						break;
                case SimulationOperationCode.CanActivateAction:
						result = AbilityStateValue.FromBoolean(m_ActionAdmission.PreviewActivation(cursor, operation, in m_Frame.SelectedSource).Allowed);
						break;
                case SimulationOperationCode.GameplayEffectApply:
                        result = outputPort switch
                        {
                            "m_Handle" => m_ControlState.Get(Access.RequireOperationSlot(operation.Handle, ProgramStateSemantic.GameplayEffectAppliedHandle)),
                            "m_Applied" => AbilityStateValue.FromBoolean(cursor.ReadStatus(operation.Handle) == OperationRunnableStatus.Success),
                            _ => throw new InvalidOperationException($"Gameplay Effect apply output '{outputPort}' is unsupported.")
                        };
                        break;
                    case SimulationOperationCode.GameplayEffectRemove:
                        result = AbilityStateValue.FromBoolean(cursor.ReadStatus(operation.Handle) == OperationRunnableStatus.Success);
                        break;
                    case SimulationOperationCode.GameplayEffectHasTag:
                        result = AbilityStateValue.FromBoolean(RequireGameplayTags().HasTag(operation.Text0));
                        break;
                case SimulationOperationCode.GameplayEffectMatchTags:
                        result = AbilityStateValue.FromBoolean(
                            RequireGameplayTags().Matches(Access.Services.RequireTagQuery(operation.Handle)));
                        break;
                case SimulationOperationCode.GameplayAttributeRead:
                        result = RequireGameplayTags().ReadAttribute(operation, outputPort);
                        break;
                case SimulationOperationCode.CameraBasisRead:
						result = ReadCameraBasis(outputPort);
						break;
                case SimulationOperationCode.ReadEquipmentIdentity:
                case SimulationOperationCode.ReadEquipmentParameter:
                case SimulationOperationCode.RequestEquipmentChange:
                case SimulationOperationCode.BeginEquipmentChange:
                case SimulationOperationCode.CommitEquipmentChange:
                case SimulationOperationCode.CancelEquipmentChange:
                        result = RequireEquipment().Evaluate(operation, outputPort, inputs);
                        break;
                case SimulationOperationCode.StateRootCompleted:
						result = AbilityStateValue.FromBoolean(cursor.CurrentStateRootCompleted());
						break;
                case SimulationOperationCode.StateExitCause:
						result = AbilityStateValue.FromBoolean(operation.Integer0 == cursor.CurrentStateExitCause());
						break;
                case SimulationOperationCode.MoveFacingAngle:
						result = AbilityStateValue.FromScalar(ReadMoveFacingAngle(inputs));
						break;
                case SimulationOperationCode.CharacterStateRead:
						result = ReadCharacterState(operation.Text0);
						break;
                default:
                    throw new InvalidOperationException($"Operation '{operation.Handle}' code '{operation.Code}' is not a value operation.");
            }
            return result;
        }

        protected override void ResetGraphCallParameter(int slot) => m_Blackboard.ResetGraphCallParameter(slot);
        protected override void WriteGraphCallParameter(int slot, AbilityStateValue value) => m_Blackboard.WriteGraphCallParameter(slot, value);
        protected override AbilityStateValue ReadGraphCallParameter(int slot) => m_Blackboard.ReadGraphCallParameter(slot);

        protected override void TraceResult(SimulationOperation operation, string outputPort, AbilityStateValue value, bool predictive)
        {
            if (!predictive)
            {
                TraceValue(operation, value);
                m_Frame.Trace.AddValue(operation, outputPort, ProgramValuePortDirection.Output, value);
            }
        }

        protected override void TraceInput(SimulationOperation operation, CompiledValueInputBinding input,
            AbilityStateValue value, bool predictive)
        {
                    if (!predictive && (m_Frame.Trace.CaptureValues ||
                        m_Frame.Trace.CaptureControlFlow && input.SourceKind == CompiledValueInputSourceKind.Operation))
                    {
                        string port = GameplayAbilityValuePortContracts.Require(operation.Code, operation.Handle, m_Ability.GraphCallFrames)
                            .Inputs[input.TargetPortIndex].Identity;
                        m_Frame.Trace.AddValue(operation, port, ProgramValuePortDirection.Input, value);
                        m_Frame.Trace.AddValueEdge(operation, port);
                    }
        }

		void TraceValue(SimulationOperation operation, AbilityStateValue value)
		{
			if (!m_Frame.Trace.Enabled ||
			    operation.Code != SimulationOperationCode.InputVector2 &&
			    operation.Code != SimulationOperationCode.InputVector2Magnitude &&
			    operation.Code != SimulationOperationCode.MoveFacingAngle &&
			    operation.Code != SimulationOperationCode.CharacterStateRead &&
			    operation.Code != SimulationOperationCode.Compare &&
			    operation.Code != SimulationOperationCode.And &&
			    operation.Code != SimulationOperationCode.Or &&
			    operation.Code != SimulationOperationCode.Not &&
			    operation.Code != SimulationOperationCode.ConditionResult)
				return;
			m_Frame.Trace.Add(
				operation,
				"condition_value_evaluated",
				SimulationTraceSeverity.Detail,
				string.Empty, m_Frame.CaptureConditionTrace(operation.Code, in value));
		}

        AbilityStateValue ReadActionTarget<TTarget>(
            OperationControlCursor<TTarget> cursor,
            SimulationOperation operation,
            string outputPort)
            where TTarget : struct, IOperationControlTarget<TTarget>
        {
            SimulationActionTargetSnapshot target = ReadBlackboard(cursor, operation).ActionTargetSnapshot;
            Float32Vector3 direction = target.Position - m_Frame.BodyFacts.Position;
            return outputPort switch
            {
                "m_HasTarget" => AbilityStateValue.FromBoolean(target.HasTarget),
                "m_Position" => AbilityStateValue.FromVector3(target.Position),
                "m_Yaw" => AbilityStateValue.FromScalar(target.Yaw.Degrees),
                "m_PlanarDistance" => AbilityStateValue.FromScalar(new Float32Vector2(direction.X, direction.Z).Magnitude),
                _ => throw new InvalidOperationException($"Action target output '{outputPort}' is unsupported.")
            };
        }

        IFloat32GameplayTagQuery RequireGameplayTags() => m_GameplayTags ??
            throw new InvalidOperationException(
                "Ability value operation requires the declared Gameplay Effect service.");

        Float32EquipmentRuntime RequireEquipment() => m_Equipment ??
            throw new InvalidOperationException(
                "Ability value operation requires the declared Equipment service.");

        public void CaptureActionTarget<TTarget>(OperationControlCursor<TTarget> cursor, SimulationOperation operation)
            where TTarget : struct, IOperationControlTarget<TTarget>
        {
            if (!m_Actions.TryGetCurrentSkillExecution(out Float32ActionInstanceState action))
                throw new InvalidOperationException("捕获动作目标必须在当前技能动作内执行。");
            ProgramReference reference = m_Layout.Topology.FirstReference(operation.Handle, ProgramReferenceKind.StateSlot);
            m_Blackboard.Write(cursor, operation, reference.TargetIndex, AbilityStateValue.FromActionTargetSnapshot(action.TargetSnapshot));
        }

        public bool SetBlackboard<TTarget>(
            OperationControlCursor<TTarget> cursor,
            SimulationOperation operation)
            where TTarget : struct, IOperationControlTarget<TTarget>
        {
            ProgramReference reference = m_Layout.Topology.FirstReference(operation.Handle, ProgramReferenceKind.StateSlot);
            if (reference == null)
                return false;
            using Float32ValueInputLease values = ReadInputs(cursor, operation);
            if (values.Count == 0)
                return false;
            ProgramStateValueKind expected = m_Ability.StateSlots[reference.TargetIndex].ValueKind;
            m_Blackboard.Write(cursor, operation, reference.TargetIndex, ConvertValue(values[0], expected));
            return true;
        }

        AbilityStateValue ReadBlackboard<TTarget>(
            OperationControlCursor<TTarget> cursor,
            SimulationOperation operation)
            where TTarget : struct, IOperationControlTarget<TTarget>
        {
            ProgramReference reference = m_Layout.Topology.FirstReference(operation.Handle, ProgramReferenceKind.StateSlot);
            if (reference == null)
                throw new InvalidOperationException($"Blackboard operation '{operation.Handle}' has no state address.");
            return m_Blackboard.Read(cursor, operation, reference.TargetIndex);
        }

        Float32Scalar ReadMoveFacingAngle(Float32ValueInputLease inputs)
        {
            if (inputs.Count == 0 ||
                inputs[0].Kind != ProgramStateValueKind.Vector2 ||
                inputs[0].Vector2 == Float32Vector2.Zero)
                return Float32Scalar.Zero;
            Float32Yaw desired = Float32Angle.FromPlanarDirection(inputs[0].Vector2);
            return Float32Scalar.Abs(Float32Angle.Delta(m_Frame.BodyFacts.Yaw, desired));
        }

        AbilityStateValue ReadCharacterState(string field)
        {
            return field switch
            {
                CharacterStateProviderFields.Position => AbilityStateValue.FromVector3(m_Frame.BodyFacts.Position),
                CharacterStateProviderFields.Velocity => AbilityStateValue.FromVector3(m_Frame.BodyFacts.Velocity),
                CharacterStateProviderFields.VerticalVelocity => AbilityStateValue.FromScalar(m_Frame.BodyFacts.VerticalVelocity),
                CharacterStateProviderFields.BodyYaw => AbilityStateValue.FromYaw(m_Frame.BodyFacts.Yaw),
                CharacterStateProviderFields.Grounded => AbilityStateValue.FromBoolean(m_Frame.BodyFacts.Grounded),
                _ => throw new InvalidOperationException($"Character State field '{field}' is not supported by Float32 runtime.")
            };
        }

        AbilityStateValue ReadCameraBasis(string outputPort)
        {
            return outputPort switch
            {
                CameraProgramOperationSchema.BasisValidPortId => AbilityStateValue.FromBoolean(
                    m_Input.ReadValue(CameraProgramOperationSchema.BasisValidInputId, ProgramInputValueKind.Boolean).Boolean),
                CameraProgramOperationSchema.BasisPlanarForwardPortId => AbilityStateValue.FromVector3(
                    m_Input.ReadValue(CameraProgramOperationSchema.BasisPlanarForwardInputId, ProgramInputValueKind.Vector3).Vector3),
                CameraProgramOperationSchema.BasisPlanarRightPortId => AbilityStateValue.FromVector3(
                    m_Input.ReadValue(CameraProgramOperationSchema.BasisPlanarRightInputId, ProgramInputValueKind.Vector3).Vector3),
                CameraProgramOperationSchema.BasisLookDirectionPortId => AbilityStateValue.FromVector3(
                    m_Input.ReadValue(CameraProgramOperationSchema.BasisLookDirectionInputId, ProgramInputValueKind.Vector3).Vector3),
                CameraProgramOperationSchema.BasisAimPointPortId => AbilityStateValue.FromVector3(
                    m_Input.ReadValue(CameraProgramOperationSchema.BasisAimPointInputId, ProgramInputValueKind.Vector3).Vector3),
                CameraProgramOperationSchema.BasisYawPortId => AbilityStateValue.FromYaw(
                    m_Input.ReadValue(CameraProgramOperationSchema.BasisYawInputId, ProgramInputValueKind.Yaw).Yaw),
                CameraProgramOperationSchema.BasisPitchPortId => AbilityStateValue.FromScalar(
                    m_Input.ReadValue(CameraProgramOperationSchema.BasisPitchInputId, ProgramInputValueKind.Scalar).Scalar),
                _ => throw new InvalidOperationException($"Camera basis contains unknown output port '{outputPort}'.")
            };
        }

    }
}
