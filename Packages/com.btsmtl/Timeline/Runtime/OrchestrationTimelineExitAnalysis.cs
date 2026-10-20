using System;
using System.Collections.Generic;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;

namespace BTSMTL.Timeline.Runtime
{
    sealed class OrchestrationTimelineExitAnalysis<TValue, TScalar>
        where TValue : unmanaged
        where TScalar : unmanaged
    {
        [Flags]
        enum Result : byte
        {
            Success = 1, Failure = 2, Running = 4, Exited = 8,
            Unknown = Success | Failure | Running | Exited
        }

        [Flags]
        enum Truth : byte { False = 1, True = 2, Unknown = False | True }

        readonly struct Number
        {
            internal Number(bool time, bool constant, TScalar value)
            {
                Time = time;
                Constant = constant;
                Value = value;
            }

            internal bool Time { get; }
            internal bool Constant { get; }
            internal TScalar Value { get; }
        }

        readonly OrchestrationProgram<TValue> m_Program;
        readonly ReadOnlyMemory<OrchestrationExtensionReference> m_Extensions;
        readonly IOrchestrationScalarValueAccess<TValue, TScalar> m_Values;
        readonly IScalarMath<TScalar> m_Math;
        readonly IOrchestrationTimelineTimeProjection<OrchestrationTime, TScalar> m_Projection;
        readonly Result[][] m_Results;
        readonly bool[][] m_Active;
        OrchestrationExecutionContract m_Contract;
        OrchestrationTime m_Lower;
        OrchestrationTime m_Upper;
        int m_BlockingOperation;

        internal OrchestrationTimelineExitAnalysis(OrchestrationProgram<TValue> program,
            ReadOnlyMemory<OrchestrationExtensionReference> extensions,
            IOrchestrationScalarValueAccess<TValue, TScalar> values, IScalarMath<TScalar> math,
            IOrchestrationTimelineTimeProjection<OrchestrationTime, TScalar> projection)
        {
            m_Program = program;
            m_Extensions = extensions;
            m_Values = values;
            m_Math = math;
            m_Projection = projection;
            m_Results = new[] { new Result[program.OperationCount], new Result[program.OperationCount] };
            m_Active = new[] { new bool[program.OperationCount], new bool[program.OperationCount] };
        }

        internal bool TryProve(in OrchestrationExecutionEntry entry,
            in TimelineSchedulingClip<OrchestrationTime> clip,
            out OrchestrationTime exitTime, out int blockingOperation)
        {
            m_Contract = entry.Contract;
            m_Upper = clip.DeclaredEnd ? clip.End : OrchestrationTime.MaxValue;
            var candidates = new SortedSet<long> { clip.Start.RawValue };
            if (clip.Start == OrchestrationTime.Zero)
                candidates.Add(1);
            for (int i = 0; i < m_Program.OperationCount; i++)
            {
                if (m_Program.Operation(i).Code != OrchestrationOperationCode.Compare)
                    continue;
                ReadOnlySpan<OrchestrationValueInputBinding> inputs = m_Program.Inputs(i);
                Number left = Numeric(in inputs[0]);
                Number right = Numeric(in inputs[1]);
                if (!(left.Time && right.Constant || right.Time && left.Constant))
                    continue;
                TScalar constant = left.Constant ? left.Value : right.Value;
                if (TryLowerBound(constant, false, out long inclusive))
                    candidates.Add(inclusive);
                if (TryLowerBound(constant, true, out long exclusive))
                    candidates.Add(exclusive);
            }
            Array.Clear(m_Results[0], 0, m_Results[0].Length);
            blockingOperation = entry.OperationIndex;
            foreach (long candidate in candidates)
            {
                if (candidate < clip.Start.RawValue || candidate > m_Upper.RawValue)
                    continue;
                m_Lower = OrchestrationTime.FromRaw(candidate);
                m_BlockingOperation = entry.OperationIndex;
                Array.Clear(m_Results[1], 0, m_Results[1].Length);
                Result result = Control(entry.OperationIndex, false);
                // 失败不会采用本次推进；所有可采用路径都必须已经提交退出。
                if ((result & (Result.Success | Result.Running)) == 0 && (result & Result.Exited) != 0)
                {
                    exitTime = m_Lower;
                    blockingOperation = -1;
                    return true;
                }
                blockingOperation = m_BlockingOperation;
            }
            exitTime = default;
            return false;
        }

        Result Control(int index, bool before)
        {
            int phase = before ? 0 : 1;
            if (m_Results[phase][index] != 0)
                return m_Results[phase][index];
            if (m_Active[phase][index])
                return Unresolved(index, before);
            m_Active[phase][index] = true;
            Result result = Execute(index, before);
            m_Active[phase][index] = false;
            m_Results[phase][index] = result;
            return result;
        }

        Result Execute(int index, bool before)
        {
            ref readonly OrchestrationOperation operation = ref m_Program.Operation(index);
            ReadOnlySpan<int> children = m_Program.Outgoing(index, OrchestrationFlowKind.Child);
            switch (operation.Code)
            {
                case OrchestrationOperationCode.Root:
                    return m_Contract == OrchestrationExecutionContract.Complete
                        ? Sequence(children, 0, before, false) : Single(children, before);
                case OrchestrationOperationCode.TimelineEnter:
                case OrchestrationOperationCode.StateOnEnter:
                case OrchestrationOperationCode.StateOnExit:
                    return Single(children, before);
                case OrchestrationOperationCode.SubGraph:
                    return Single(m_Program.Outgoing(index, OrchestrationFlowKind.Enter), before);
                case OrchestrationOperationCode.Sequence:
                {
                    bool resumable = m_Contract == OrchestrationExecutionContract.Resumable;
                    Result result = Sequence(children, 0, before, resumable);
                    for (int i = 1; !before && resumable && i < children.Length; i++)
                        if (MaySuspend(m_Program.Edge(children[i]).TargetOperationIndex))
                            result |= Sequence(children, i, false, true);
                    return result;
                }
                case OrchestrationOperationCode.Selector:
                {
                    Result result = Select(children, 0, before);
                    for (int i = 0; !before && i < children.Length; i++)
                    {
                        ref readonly OrchestrationFlowEdge edge = ref m_Program.Edge(children[i]);
                        if (!MaySuspend(edge.TargetOperationIndex))
                            continue;
                        if ((edge.AbortPolicy == OrchestrationAbortPolicy.Self ||
                            edge.AbortPolicy == OrchestrationAbortPolicy.Both) && Condition(in edge, false) != Truth.True)
                            result |= Unresolved(edge.TargetOperationIndex, false);
                        result |= Otherwise(Control(edge.TargetOperationIndex, false), Select(children, i + 1, false));
                    }
                    return result;
                }
                case OrchestrationOperationCode.Loop:
                {
                    if (children.Length != 1)
                        return Result.Failure;
                    ref readonly OrchestrationFlowEdge edge = ref m_Program.Edge(children[0]);
                    Truth condition = Condition(in edge, before);
                    if (condition == Truth.False)
                        return Result.Failure;
                    Result child = Control(edge.TargetOperationIndex, before);
                    Result result = child & (Result.Exited | Result.Running);
                    if ((child & Result.Success) != 0)
                        result |= m_Program.Payload<OrchestrationModePayload>(index).Mode == 1 ? Result.Success : Result.Running;
                    if ((child & Result.Failure) != 0)
                        result |= m_Program.Payload<OrchestrationModePayload>(index).Mode == 2 ? Result.Failure : Result.Running;
                    if ((condition & Truth.False) != 0)
                        result |= Result.Failure;
                    return result;
                }
                case OrchestrationOperationCode.Parallel:
                    return Parallel(index, children, before);
                case OrchestrationOperationCode.Branch:
                {
                    Truth condition = Boolean(in m_Program.Inputs(index)[0], before);
                    Result result = 0;
                    if ((condition & Truth.True) != 0)
                        result |= Output(children, m_Program.Ports.BranchTrue, before);
                    if ((condition & Truth.False) != 0)
                        result |= Output(children, m_Program.Ports.BranchFalse, before);
                    return Then(result, Output(children, m_Program.Ports.BranchThen, before));
                }
                case OrchestrationOperationCode.Cycle:
                {
                    Result result = Result.Success;
                    foreach (int edge in children)
                        result |= Control(m_Program.Edge(edge).TargetOperationIndex, before);
                    return result;
                }
                case OrchestrationOperationCode.Succeed:
                    return Result.Success;
                case OrchestrationOperationCode.BlackboardSet:
                case OrchestrationOperationCode.EmitOutput:
                    return ThenLeaf(index, Result.Success, before);
                case OrchestrationOperationCode.TimelineClipExitRequest:
                {
                    Truth condition = Boolean(in m_Program.Inputs(index)[0], before);
                    Result result = (condition & Truth.True) != 0 ? Result.Exited : 0;
                    if (!before && condition != Truth.True)
                        m_BlockingOperation = index;
                    if ((condition & Truth.False) != 0)
                        result |= Result.Success;
                    return ThenLeaf(index, result, before);
                }
                case OrchestrationOperationCode.Extension:
                    if (m_Extensions.Span[m_Program.Payload<OrchestrationExtensionPayload>(index).BindingIndex].Kind == OrchestrationExtensionKind.Action)
                    {
                        Result result = Result.Success | Result.Failure;
                        if (m_Extensions.Span[m_Program.Payload<OrchestrationExtensionPayload>(index).BindingIndex].Completion == OrchestrationExtensionCompletion.Resumable)
                        {
                            result |= Result.Running;
                            if (!before)
                                m_BlockingOperation = index;
                        }
                        return ThenLeaf(index, result, before);
                    }
                    return ThenLeaf(index, Result.Success | Result.Failure, before);
                case OrchestrationOperationCode.ConditionResult:
                case OrchestrationOperationCode.StateRootCompleted:
                case OrchestrationOperationCode.StateExitCause:
                case OrchestrationOperationCode.Compare:
                case OrchestrationOperationCode.And:
                case OrchestrationOperationCode.Or:
                case OrchestrationOperationCode.Not:
                case OrchestrationOperationCode.Constant:
                case OrchestrationOperationCode.Calculate:
                case OrchestrationOperationCode.SelectValue:
                case OrchestrationOperationCode.IdentityValue:
                case OrchestrationOperationCode.TimelineTime:
                case OrchestrationOperationCode.BlackboardGet:
                case OrchestrationOperationCode.HostInput:
                case OrchestrationOperationCode.InputBoolean:
                case OrchestrationOperationCode.InputScalar:
                case OrchestrationOperationCode.InputVector2:
                case OrchestrationOperationCode.InputVector2Magnitude:
                case OrchestrationOperationCode.InputRequest:
                {
                    Truth condition = Boolean(index, before);
                    Result result = (condition & Truth.True) != 0 ? Result.Success : 0;
                    if ((condition & Truth.False) != 0)
                        result |= Result.Failure;
                    return ThenLeaf(index, result, before);
                }
                default:
                    return Unresolved(index, before);
            }
        }

        Result Single(ReadOnlySpan<int> children, bool before) => children.Length == 0
            ? Result.Success : children.Length == 1 ? Edge(children[0], before) : Result.Failure;

        Result Sequence(ReadOnlySpan<int> children, int start, bool before, bool conditions)
        {
            Result result = Result.Success;
            for (int i = start; i < children.Length; i++)
            {
                ref readonly OrchestrationFlowEdge edge = ref m_Program.Edge(children[i]);
                Result child = conditions ? Edge(children[i], before) : Control(edge.TargetOperationIndex, before);
                if (!before && conditions && MaySuspend(edge.TargetOperationIndex) &&
                    (edge.AbortPolicy == OrchestrationAbortPolicy.Self || edge.AbortPolicy == OrchestrationAbortPolicy.Both) &&
                    Condition(in edge, false) != Truth.True)
                    child |= Result.Running;
                result = Then(result, child);
                if ((result & Result.Success) == 0)
                    break;
            }
            return result;
        }

        Result Select(ReadOnlySpan<int> children, int start, bool before)
        {
            Result result = Result.Failure;
            for (int i = start; i < children.Length && (result & Result.Failure) != 0; i++)
                result = Otherwise(result, Edge(children[i], before));
            return result;
        }

        Result Parallel(int index, ReadOnlySpan<int> children, bool before)
        {
            int mode = m_Program.Payload<OrchestrationModePayload>(index).Mode;
            if (mode == 2)
            {
                if (children.Length == 0)
                    return Result.Success;
                Result first = Edge(children[0], before);
                return (first & ~Result.Exited) == 0 ? first : Unresolved(index, before);
            }
            bool retains = false;
            foreach (int edge in children)
                retains |= !before && MaySuspend(m_Program.Edge(edge).TargetOperationIndex);
            Result result = Result.Success;
            foreach (int edge in children)
            {
                int childIndex = m_Program.Edge(edge).TargetOperationIndex;
                Result child = Edge(edge, before);
                if (retains && mode != 1 &&
                    (Control(childIndex, true) & (Result.Success | Result.Failure)) != 0)
                    child |= Result.Success;
                Result combined = 0;
                if ((result & Result.Exited) != 0 || (child & Result.Exited) != 0)
                    combined |= Result.Exited;
                if ((result & ~Result.Exited) != 0 && (child & ~Result.Exited) != 0)
                {
                    if ((result & Result.Running) != 0 || (child & Result.Running) != 0)
                        combined |= Result.Running;
                    if ((result & (Result.Success | Result.Failure)) != 0 &&
                        (child & (Result.Success | Result.Failure)) != 0)
                        combined |= Result.Success;
                }
                result = combined;
            }
            return result;
        }

        Result Output(ReadOnlySpan<int> children, int port, bool before)
        {
            Result result = Result.Success;
            foreach (int index in children)
            {
                if ((result & Result.Success) == 0)
                    break;
                if (m_Program.Edge(index).SourcePortId == port)
                    result = Then(result, Control(m_Program.Edge(index).TargetOperationIndex, before));
            }
            return result;
        }

        Result ThenLeaf(int index, Result result, bool before) => m_Contract == OrchestrationExecutionContract.Complete
            ? Then(result, Sequence(m_Program.Outgoing(index, OrchestrationFlowKind.Child), 0, before, false)) : result;

        Result Edge(int index, bool before)
        {
            ref readonly OrchestrationFlowEdge edge = ref m_Program.Edge(index);
            Truth condition = Condition(in edge, before);
            Result result = (condition & Truth.False) != 0 ? Result.Failure : 0;
            if ((condition & Truth.True) != 0)
                result |= Control(edge.TargetOperationIndex, before);
            return result;
        }

        bool MaySuspend(int index) => (Control(index, true) & Result.Running) != 0;
        Result Unresolved(int index, bool before)
        {
            if (!before)
                m_BlockingOperation = index;
            return Result.Unknown;
        }

        static Result Then(Result left, Result right) => (left & ~Result.Success) |
            ((left & Result.Success) != 0 ? right : 0);
        static Result Otherwise(Result left, Result right) => (left & ~Result.Failure) |
            ((left & Result.Failure) != 0 ? right : 0);

        Truth Condition(in OrchestrationFlowEdge edge, bool before) => edge.HasCondition
            ? Boolean(edge.ConditionOperationIndex, before) : Truth.True;

        Truth Boolean(in OrchestrationValueInputBinding input, bool before)
        {
            if (input.Source == OrchestrationValueInputSource.Constant)
                return ConvertedConstantTruth(in m_Program.Constant(input.SourceIndex), input.ValueKind);
            if (input.Source != OrchestrationValueInputSource.Operation)
                return Truth.Unknown;
            ref readonly OrchestrationOperation source = ref m_Program.Operation(input.SourceIndex);
            if (source.Code == OrchestrationOperationCode.Constant)
                return ConvertedConstantTruth(in m_Program.Constant(m_Program.ValueOperation(input.SourceIndex).Argument), input.ValueKind);
            OrchestrationValueKind sourceKind = source.Code switch
            {
                OrchestrationOperationCode.Constant or OrchestrationOperationCode.ConditionResult or
                    OrchestrationOperationCode.Compare or OrchestrationOperationCode.And or
                    OrchestrationOperationCode.Or or OrchestrationOperationCode.Not => OrchestrationValueKind.Boolean,
                OrchestrationOperationCode.TimelineTime => OrchestrationValueKind.Scalar,
                OrchestrationOperationCode.IdentityValue => m_Program.Inputs(input.SourceIndex)[0].ValueKind,
                OrchestrationOperationCode.SelectValue => m_Program.Inputs(input.SourceIndex)[1].ValueKind,
                _ => 0
            };
            return sourceKind == input.ValueKind || sourceKind == OrchestrationValueKind.Boolean &&
                input.ValueKind == OrchestrationValueKind.Scalar ? Boolean(input.SourceIndex, before) : Truth.Unknown;
        }

        Truth Boolean(int index, bool before)
        {
            ref readonly OrchestrationOperation operation = ref m_Program.Operation(index);
            ReadOnlySpan<OrchestrationValueInputBinding> inputs = m_Program.Inputs(index);
            Truth result;
            switch (operation.Code)
            {
                case OrchestrationOperationCode.Constant:
                    result = ConstantTruth(in m_Program.Constant(m_Program.ValueOperation(index).Argument));
                    break;
                case OrchestrationOperationCode.ConditionResult:
                case OrchestrationOperationCode.IdentityValue:
                    result = Boolean(in inputs[0], before);
                    break;
                case OrchestrationOperationCode.Not:
                    result = Negate(Boolean(in inputs[0], before));
                    break;
                case OrchestrationOperationCode.And:
                case OrchestrationOperationCode.Or:
                {
                    Truth left = Boolean(in inputs[0], before);
                    bool and = operation.Code == OrchestrationOperationCode.And;
                    Truth shortCircuit = and ? Truth.False : Truth.True;
                    Truth right = left == shortCircuit ? shortCircuit : Boolean(in inputs[1], before);
                    result = 0;
                    if (and ? (left & Truth.True) != 0 && (right & Truth.True) != 0 :
                        (left & Truth.True) != 0 || (right & Truth.True) != 0)
                        result |= Truth.True;
                    if (and ? (left & Truth.False) != 0 || (right & Truth.False) != 0 :
                        (left & Truth.False) != 0 && (right & Truth.False) != 0)
                        result |= Truth.False;
                    break;
                }
                case OrchestrationOperationCode.SelectValue:
                {
                    Truth condition = Boolean(in inputs[0], before);
                    result = 0;
                    if ((condition & Truth.True) != 0)
                        result |= Boolean(in inputs[1], before);
                    if ((condition & Truth.False) != 0)
                        result |= Boolean(in inputs[2], before);
                    break;
                }
                case OrchestrationOperationCode.Compare:
                    result = Compare(m_Program.ColdOperation(index).Comparison, Numeric(in inputs[0]), Numeric(in inputs[1]), before);
                    break;
                case OrchestrationOperationCode.TimelineTime:
                    result = before ? Truth.Unknown :
                        m_Math.IsZero(m_Projection.Project(m_Lower)) ? Truth.Unknown : Truth.True;
                    break;
                default:
                    result = Truth.Unknown;
                    break;
            }
            return result;
        }

        Truth ConstantTruth(in TValue value) => m_Values.Kind(in value) switch
        {
            OrchestrationValueKind.Boolean => FromBoolean(m_Values.Boolean(in value)),
            OrchestrationValueKind.Int32 => FromBoolean(m_Values.Int32(in value) != 0),
            OrchestrationValueKind.UInt64 => FromBoolean(m_Values.UInt64(in value) != 0),
            OrchestrationValueKind.Scalar => FromBoolean(!m_Math.IsZero(m_Values.Scalar(in value))),
            OrchestrationValueKind.Identity => Truth.Unknown,
            _ => Truth.False
        };

        Truth ConvertedConstantTruth(in TValue value, OrchestrationValueKind expected)
        {
            if (m_Values.Kind(in value) == expected)
                return ConstantTruth(in value);
            if (expected != OrchestrationValueKind.Scalar)
                return Truth.Unknown;
            Number number = NumericConstant(in value);
            return number.Constant ? FromBoolean(!m_Math.IsZero(number.Value)) : Truth.Unknown;
        }

        Number Numeric(in OrchestrationValueInputBinding input) => input.ValueKind != OrchestrationValueKind.Scalar
            ? default : input.Source switch
        {
            OrchestrationValueInputSource.Constant => NumericConstant(in m_Program.Constant(input.SourceIndex)),
            OrchestrationValueInputSource.Operation => NumericOperation(input.SourceIndex),
            _ => default
        };

        Number NumericOperation(int index)
        {
            ref readonly OrchestrationOperation operation = ref m_Program.Operation(index);
            Number result = operation.Code switch
            {
                OrchestrationOperationCode.TimelineTime => new Number(true, false, default),
                OrchestrationOperationCode.Constant => NumericConstant(in m_Program.Constant(m_Program.ValueOperation(index).Argument)),
                OrchestrationOperationCode.IdentityValue => Numeric(in m_Program.Inputs(index)[0]),
                _ => default
            };
            return result;
        }

        Number NumericConstant(in TValue value) => m_Values.Kind(in value) switch
        {
            OrchestrationValueKind.Scalar => new Number(false, true, m_Values.Scalar(in value)),
            OrchestrationValueKind.Int32 => new Number(false, true, m_Math.FromInt32(m_Values.Int32(in value))),
            OrchestrationValueKind.UInt64 when m_Values.UInt64(in value) <= long.MaxValue =>
                new Number(false, true, m_Math.FromInt64((long)m_Values.UInt64(in value))),
            OrchestrationValueKind.Boolean => new Number(false, true, m_Values.Boolean(in value) ? m_Math.One : m_Math.Zero),
            _ => default
        };

        Truth Compare(OrchestrationComparisonKind kind, Number left, Number right, bool before)
        {
            if (!(left.Constant || left.Time) || !(right.Constant || right.Time))
                return Truth.Unknown;
            if (left.Time && right.Time)
                return FromBoolean(kind == OrchestrationComparisonKind.Equal ||
                    kind == OrchestrationComparisonKind.LessThanOrEqual || kind == OrchestrationComparisonKind.GreaterThanOrEqual);
            if (before && (left.Time || right.Time))
                return Truth.Unknown;
            TScalar leftMin = left.Time ? m_Projection.Project(m_Lower) : left.Value;
            TScalar leftMax = left.Time ? m_Projection.Project(m_Upper) : left.Value;
            TScalar rightMin = right.Time ? m_Projection.Project(m_Lower) : right.Value;
            TScalar rightMax = right.Time ? m_Projection.Project(m_Upper) : right.Value;
            switch (kind)
            {
                case OrchestrationComparisonKind.Equal:
                case OrchestrationComparisonKind.NotEqual:
                {
                    Truth equal = m_Math.Equal(leftMin, leftMax) && m_Math.Equal(rightMin, rightMax)
                        ? FromBoolean(m_Math.Equal(leftMin, rightMin))
                        : m_Math.GreaterThan(leftMin, rightMax) || m_Math.GreaterThan(rightMin, leftMax)
                            ? Truth.False : Truth.Unknown;
                    return kind == OrchestrationComparisonKind.Equal ? equal : Negate(equal);
                }
                case OrchestrationComparisonKind.GreaterThan:
                    return m_Math.GreaterThan(leftMin, rightMax) ? Truth.True :
                        m_Math.LessThanOrEqual(leftMax, rightMin) ? Truth.False : Truth.Unknown;
                case OrchestrationComparisonKind.GreaterThanOrEqual:
                    return m_Math.LessThanOrEqual(rightMax, leftMin) ? Truth.True :
                        m_Math.GreaterThan(rightMin, leftMax) ? Truth.False : Truth.Unknown;
                case OrchestrationComparisonKind.LessThan:
                    return m_Math.GreaterThan(rightMin, leftMax) ? Truth.True :
                        m_Math.LessThanOrEqual(rightMax, leftMin) ? Truth.False : Truth.Unknown;
                case OrchestrationComparisonKind.LessThanOrEqual:
                    return m_Math.LessThanOrEqual(leftMax, rightMin) ? Truth.True :
                        m_Math.GreaterThan(leftMin, rightMax) ? Truth.False : Truth.Unknown;
                default:
                    return Truth.Unknown;
            }
        }

        bool TryLowerBound(TScalar value, bool strict, out long raw)
        {
            long lower = 0;
            long upper = long.MaxValue;
            bool Matches(long candidate)
            {
                TScalar projected = m_Projection.Project(OrchestrationTime.FromRaw(candidate));
                return strict ? m_Math.GreaterThan(projected, value) : m_Math.LessThanOrEqual(value, projected);
            }
            if (!Matches(upper))
            {
                raw = 0;
                return false;
            }
            while (lower < upper)
            {
                long middle = lower + (upper - lower) / 2;
                if (Matches(middle))
                    upper = middle;
                else
                    lower = middle + 1;
            }
            raw = lower;
            return true;
        }

        static Truth FromBoolean(bool value) => value ? Truth.True : Truth.False;
        static Truth Negate(Truth value) => ((value & Truth.True) != 0 ? Truth.False : 0) |
            ((value & Truth.False) != 0 ? Truth.True : 0);
    }
}
