using System;
using BTSMTL.Orchestration;

namespace BTSMTL.Compilation
{
    public static partial class BtsmtlOrchestrationGraphCompiler
    {
        sealed partial class Compilation<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues, TLowering>
        {
            OrchestrationValueKind[] m_ValueKinds;
            OrchestrationValueOperation[] m_ValueOperations;

            void BuildTypedValues(OperationDraft[] operations, OrchestrationFlowEdge[] edges,
                OrchestrationProgramStructure structure)
            {
                int slots = structure.ValueEvaluationOffsets.Span[operations.Length];
                m_ValueKinds = new OrchestrationValueKind[slots];
                m_ValueOperations = new OrchestrationValueOperation[slots];
                for (int i = 0; i < operations.Length; i++)
                {
                    OperationDraft operation = operations[i];
                    if (i < m_Operations.Count && operation.Inputs.Count != m_Ports[i].Inputs.Length)
                    {
                        Error("value_input_missing", ProgramSourceTargetKind.Operation, i,
                            "正式输入端口必须各自连接一个来源。");
                        continue;
                    }
                    if (!CanEvaluate(operation.Code))
                        continue;
                    if (operation.Code == OrchestrationOperationCode.Constant && operation.ConstantIndex < 0)
                    {
                        Error("value_constant_missing", ProgramSourceTargetKind.Operation, i, "常量节点必须声明一个值常量。");
                        continue;
                    }
                    int offset = structure.ValueEvaluationOffsets.Span[i];
                    for (int output = -1; output < operation.OutputCount; output++)
                    {
                        int slot = offset + output + 1;
                        int port = output < 0 ? 0 : output;
                        OrchestrationValueKind kind = operation.Code switch
                        {
                            OrchestrationOperationCode.Constant => default(TValues).Kind(m_Constants[operation.ConstantIndex]),
                            OrchestrationOperationCode.BlackboardGet => m_Blackboards[operation.ValueSlot].ValueKind,
                            OrchestrationOperationCode.ConditionResult => OrchestrationValueKind.Boolean,
                            _ => m_Ports[i].Outputs.Length == 0 ? default : m_Ports[i].Outputs[port].Kind
                        };
                        m_ValueKinds[slot] = kind;
                        m_ValueOperations[slot] = TypedValue(i, operation, port);
                    }
                }
                var conditions = new System.Collections.Generic.HashSet<int>();
                foreach (OrchestrationFlowEdge edge in edges)
                    if (edge.HasCondition)
                        conditions.Add(edge.ConditionOperationIndex);
                foreach (var context in m_ControlContexts)
                {
                    OperationDraft operation = operations[context.Operation];
                    if (CanEvaluate(operation.Code) && operation.Code is not
                        (OrchestrationOperationCode.SubGraph or OrchestrationOperationCode.Cycle) &&
                        !(operation.Code == OrchestrationOperationCode.Extension &&
                          m_Extensions[operation.ExtensionBindingIndex].Kind == OrchestrationExtensionKind.Action))
                        conditions.Add(context.Operation);
                }
                foreach (int condition in conditions)
                    if (!BooleanKind(m_ValueKinds[structure.ValueEvaluationOffsets.Span[condition]]))
                        Error("condition_value_kind", ProgramSourceTargetKind.Operation, condition,
                            "控制条件必须输出 Boolean、Int32、UInt64、Scalar 或 Identity。");
            }

            OrchestrationValueOperation TypedValue(int operationIndex, OperationDraft operation, int port)
            {
                OrchestrationValueOpcode code;
                int argument = 0;
                switch (operation.Code)
                {
                    case OrchestrationOperationCode.ConditionResult:
                    case OrchestrationOperationCode.IdentityValue: code = OrchestrationValueOpcode.Copy; break;
                    case OrchestrationOperationCode.SelectValue: code = OrchestrationValueOpcode.Copy; argument = 1; break;
                    case OrchestrationOperationCode.And: code = OrchestrationValueOpcode.BooleanAnd; break;
                    case OrchestrationOperationCode.Or: code = OrchestrationValueOpcode.BooleanOr; break;
                    case OrchestrationOperationCode.Not: code = OrchestrationValueOpcode.BooleanNot; break;
                    case OrchestrationOperationCode.Compare:
                    {
                        bool integer = m_Inputs[operation.Inputs.Offset].ValueKind == OrchestrationValueKind.Int32 &&
                            m_Inputs[operation.Inputs.Offset + 1].ValueKind == OrchestrationValueKind.Int32;
                        code = (integer, operation.Comparison) switch
                        {
                            (true, OrchestrationComparisonKind.Equal) => OrchestrationValueOpcode.Int32Equal,
                            (true, OrchestrationComparisonKind.NotEqual) => OrchestrationValueOpcode.Int32NotEqual,
                            (true, OrchestrationComparisonKind.LessThan) => OrchestrationValueOpcode.Int32LessThan,
                            (true, OrchestrationComparisonKind.LessThanOrEqual) => OrchestrationValueOpcode.Int32LessThanOrEqual,
                            (true, OrchestrationComparisonKind.GreaterThanOrEqual) => OrchestrationValueOpcode.Int32GreaterThanOrEqual,
                            (true, OrchestrationComparisonKind.GreaterThan) => OrchestrationValueOpcode.Int32GreaterThan,
                            (false, OrchestrationComparisonKind.Equal) => OrchestrationValueOpcode.ScalarEqual,
                            (false, OrchestrationComparisonKind.NotEqual) => OrchestrationValueOpcode.ScalarNotEqual,
                            (false, OrchestrationComparisonKind.LessThan) => OrchestrationValueOpcode.ScalarLessThan,
                            (false, OrchestrationComparisonKind.LessThanOrEqual) => OrchestrationValueOpcode.ScalarLessThanOrEqual,
                            (false, OrchestrationComparisonKind.GreaterThanOrEqual) => OrchestrationValueOpcode.ScalarGreaterThanOrEqual,
                            (false, OrchestrationComparisonKind.GreaterThan) => OrchestrationValueOpcode.ScalarGreaterThan,
                            _ => throw new ArgumentOutOfRangeException(nameof(operation.Comparison))
                        };
                        break;
                    }
                    case OrchestrationOperationCode.Calculate:
                        code = operation.Calculation switch
                        {
                            OrchestrationCalculationKind.ScalarMultiply => OrchestrationValueOpcode.ScalarMultiply,
                            OrchestrationCalculationKind.ScalarDivide => OrchestrationValueOpcode.ScalarDivide,
                            OrchestrationCalculationKind.IdentityEqual => OrchestrationValueOpcode.IdentityEqual,
                            OrchestrationCalculationKind.ScalarAbs => OrchestrationValueOpcode.ScalarAbs,
                            OrchestrationCalculationKind.ScalarClamp => OrchestrationValueOpcode.ScalarClamp,
                            OrchestrationCalculationKind.ScalarMoveTowards => OrchestrationValueOpcode.ScalarMoveTowards,
                            OrchestrationCalculationKind.Vector3Planar => OrchestrationValueOpcode.Vector3Planar,
                            OrchestrationCalculationKind.Vector3Y => OrchestrationValueOpcode.Vector3Y,
                            OrchestrationCalculationKind.Vector2Magnitude => OrchestrationValueOpcode.Vector2Magnitude,
                            OrchestrationCalculationKind.Vector2Normalize => OrchestrationValueOpcode.Vector2Normalize,
                            OrchestrationCalculationKind.Vector2Subtract => OrchestrationValueOpcode.Vector2Subtract,
                            OrchestrationCalculationKind.Vector2SignedAngle => OrchestrationValueOpcode.Vector2SignedAngle,
                            OrchestrationCalculationKind.QuaternionForward => OrchestrationValueOpcode.QuaternionForward,
                            OrchestrationCalculationKind.QuaternionAngleAxis => OrchestrationValueOpcode.QuaternionAngleAxis,
                            _ => throw new ArgumentOutOfRangeException(nameof(operation.Calculation))
                        };
                        break;
                    case OrchestrationOperationCode.Constant: code = OrchestrationValueOpcode.Constant; argument = operation.ConstantIndex; break;
                    case OrchestrationOperationCode.InputBoolean:
                    case OrchestrationOperationCode.InputScalar:
                    case OrchestrationOperationCode.InputVector2:
                    case OrchestrationOperationCode.HostInput: code = OrchestrationValueOpcode.HostInput; argument = operation.InputSlot + port; break;
                    case OrchestrationOperationCode.InputVector2Magnitude: code = OrchestrationValueOpcode.InputVector2Magnitude; argument = operation.InputSlot; break;
                    case OrchestrationOperationCode.InputRequest: code = OrchestrationValueOpcode.InputRequest; break;
                    case OrchestrationOperationCode.BlackboardGet: code = OrchestrationValueOpcode.BlackboardGet; argument = operation.ValueSlot; break;
                    case OrchestrationOperationCode.SubGraph: code = OrchestrationValueOpcode.GraphCallOutput; argument = port; break;
                    case OrchestrationOperationCode.StateRootCompleted: code = OrchestrationValueOpcode.StateRootCompleted; break;
                    case OrchestrationOperationCode.StateExitCause: code = OrchestrationValueOpcode.StateExitCause; argument = operation.Integer0; break;
                    case OrchestrationOperationCode.Cycle: code = OrchestrationValueOpcode.Cycle; break;
                    case OrchestrationOperationCode.TimelineTime: code = OrchestrationValueOpcode.TimelineTime; break;
                    case OrchestrationOperationCode.TimelineSection: code = OrchestrationValueOpcode.TimelineSection; break;
                    case OrchestrationOperationCode.Extension: code = OrchestrationValueOpcode.Extension; argument = port; break;
                    default: throw new ArgumentOutOfRangeException(nameof(operation.Code));
                }
                return new OrchestrationValueOperation(code, CompactIndex(argument, operationIndex, "ValueArgument"));
            }

            static bool BooleanKind(OrchestrationValueKind kind) => kind is OrchestrationValueKind.Boolean or
                OrchestrationValueKind.Int32 or OrchestrationValueKind.UInt64 or OrchestrationValueKind.Scalar or OrchestrationValueKind.Identity;

            static OrchestrationValueOpcode BooleanConversion(OrchestrationValueKind kind) => kind switch
            {
                OrchestrationValueKind.Boolean => OrchestrationValueOpcode.Copy,
                OrchestrationValueKind.Int32 => OrchestrationValueOpcode.Int32ToBoolean,
                OrchestrationValueKind.UInt64 => OrchestrationValueOpcode.UInt64ToBoolean,
                OrchestrationValueKind.Scalar => OrchestrationValueOpcode.ScalarToBoolean,
                OrchestrationValueKind.Identity => OrchestrationValueOpcode.IdentityToBoolean,
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };

            OrchestrationValueOpcode InputConversion(OperationDraft operation, int input)
            {
                OrchestrationValueKind kind = m_Inputs[input].ValueKind;
                if (operation.Code is OrchestrationOperationCode.ConditionResult or OrchestrationOperationCode.And or
                    OrchestrationOperationCode.Or or OrchestrationOperationCode.Not)
                    return BooleanConversion(kind);
                if (operation.Code == OrchestrationOperationCode.Compare &&
                    !(m_Inputs[operation.Inputs.Offset].ValueKind == OrchestrationValueKind.Int32 &&
                      m_Inputs[operation.Inputs.Offset + 1].ValueKind == OrchestrationValueKind.Int32))
                    return kind switch
                    {
                        OrchestrationValueKind.Boolean => OrchestrationValueOpcode.BooleanToScalar,
                        OrchestrationValueKind.Int32 => OrchestrationValueOpcode.Int32ToScalar,
                        OrchestrationValueKind.UInt64 => OrchestrationValueOpcode.UInt64ToScalar,
                        OrchestrationValueKind.Scalar => OrchestrationValueOpcode.Copy,
                        _ => throw new ArgumentOutOfRangeException(nameof(kind))
                    };
                return OrchestrationValueOpcode.Copy;
            }

            OrchestrationAssignmentOpcode[] BuildTypedAssignments(OperationDraft[] operations)
            {
                var result = new OrchestrationAssignmentOpcode[operations.Length];
                for (int i = 0; i < operations.Length; i++)
                {
                    OperationDraft operation = operations[i];
                    if (operation.Code != OrchestrationOperationCode.BlackboardSet)
                        continue;
                    OrchestrationBlackboardSlot slot = m_Blackboards[operation.ValueSlot];
                    if (slot.Lifetime == OrchestrationBlackboardLifetime.Config)
                        Error("blackboard_config_write", ProgramSourceTargetKind.Operation, i, "配置黑板只允许读取。");
                    if (operation.Assignment == OrchestrationAssignmentKind.Set)
                        continue;
                    if (slot.ValueKind is not (OrchestrationValueKind.Int32 or OrchestrationValueKind.Scalar or OrchestrationValueKind.Vector3))
                    {
                        Error("blackboard_assignment_kind", ProgramSourceTargetKind.Operation, i,
                            $"黑板类型 '{slot.ValueKind}' 不支持 '{operation.Assignment}' 赋值。");
                        continue;
                    }
                    OrchestrationAssignmentOpcode first = slot.ValueKind switch
                    {
                        OrchestrationValueKind.Int32 => OrchestrationAssignmentOpcode.Int32Add,
                        OrchestrationValueKind.Scalar => OrchestrationAssignmentOpcode.ScalarAdd,
                        OrchestrationValueKind.Vector3 => OrchestrationAssignmentOpcode.Vector3Add,
                        _ => throw new ArgumentOutOfRangeException(nameof(slot.ValueKind))
                    };
                    result[i] = (OrchestrationAssignmentOpcode)((int)first + (int)operation.Assignment - (int)OrchestrationAssignmentKind.Add);
                }
                for (int i = 0; i < operations.Length; i++)
                {
                    OrchestrationRange range = operations[i].OutputAssignments;
                    for (int j = range.Offset; j < range.Offset + range.Count; j++)
                        if (m_Blackboards[m_BlackboardOutputs[j].ValueSlot].Lifetime == OrchestrationBlackboardLifetime.Config)
                            Error("blackboard_config_write", ProgramSourceTargetKind.Operation, i, "扩展输出不能写入配置黑板。");
                }
                return result;
            }
        }
    }
}
