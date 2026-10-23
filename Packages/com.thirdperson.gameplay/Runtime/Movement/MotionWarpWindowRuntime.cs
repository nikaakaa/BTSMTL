using System;

namespace ThirdPersonSimulation
{
    public interface IMotionWarpWindowRequest
    {
        OperationHandle SourceOperation { get; }
        ulong PlaybackGeneration { get; }
        int Cycle { get; }
        bool Enabled { get; }
        bool HasTarget { get; }
        ProgramMotionWarpRotationMethod RotationMethod { get; }
    }

    public interface IMotionWarpWindowTarget<TRequest, TScalar, TVector3, TYaw, TAction, TState>
        where TRequest : struct
        where TScalar : struct
        where TVector3 : struct
        where TYaw : struct
        where TAction : struct
        where TState : struct
    {
        TState ReadState(in TRequest request);
        void WriteState(in TRequest request, in TState state);
        TAction ReadAction(in TRequest request);
        void ReadTarget(in TRequest request, out TVector3 position, out TYaw yaw);
        TVector3 BodyPosition { get; }
        TYaw BodyYaw { get; }
        TVector3 CorrectionDisplacement { get; }
        TScalar CorrectionYaw { get; }
        void ApplyMotionWarp(TVector3 sourceDisplacement, TScalar sourceYaw,
            TVector3 warpedDisplacement, TScalar warpedYaw);
        MotionWarpParameters<TScalar> ReadParameters(in TRequest request);
        void ReadSourceDelta(in TRequest request, out TVector3 displacement, out TScalar yaw);
        void ReadSourcePrefix(in TRequest request, out TVector3 displacement, out TScalar yaw);
        void ReadProgress(in TRequest request, bool previous, out TScalar position, out TScalar yaw);
        void ResolveTarget(in TRequest request, in MotionWarpParameters<TScalar> parameters,
            TVector3 targetPosition, TYaw targetYaw, TVector3 bodyPosition, TYaw bodyYaw,
            out TVector3 sourceStartPosition, out TScalar sourceStartYaw,
            out TVector3 resolvedTargetPosition, out TYaw resolvedTargetYaw, out ProgramMotionWarpLimitResult limit);
        void EvaluatePose(in TRequest request, in MotionWarpParameters<TScalar> parameters, bool previous,
            TVector3 startBodyPosition, TYaw startBodyYaw, TVector3 sourceStartPosition, TScalar sourceStartYaw,
            TVector3 resolvedTargetPosition, TYaw resolvedTargetYaw, out TVector3 position, out TYaw yaw);
        void ApplyResponse(in TRequest request, in MotionWarpParameters<TScalar> parameters, in TState state);
        void EvaluateStep(in TRequest request, in MotionWarpParameters<TScalar> parameters, in TState state,
            out TVector3 position, out TYaw yaw);
        void Trace(in TRequest request, string code);
    }

    public static class MotionWarpWindowRuntime<TScalar, TVector2, TVector3, TYaw, TAction, TMath>
        where TScalar : unmanaged, IEquatable<TScalar>
        where TVector2 : struct
        where TVector3 : unmanaged, IEquatable<TVector3>
        where TYaw : unmanaged, IEquatable<TYaw>
        where TAction : unmanaged, IEquatable<TAction>
        where TMath : struct, IScalarMath<TScalar>, IVectorMath<TScalar, TVector2, TVector3>,
            IPlanarRotation<TVector3, TYaw>, IYawMath<TScalar, TYaw>
    {
        public static void Capture<TRequest, TTarget>(in TRequest request, TTarget target)
            where TRequest : struct, IMotionWarpWindowRequest
            where TTarget : IMotionWarpWindowTarget<TRequest, TScalar, TVector3, TYaw, TAction, MotionWarpState<TScalar, TVector3, TYaw, TAction>>
        {
            MotionWarpState<TScalar, TVector3, TYaw, TAction> previous = target.ReadState(in request);
            if (previous.Active && previous.PlaybackGeneration == request.PlaybackGeneration && previous.Cycle == request.Cycle)
                return;
            TAction action = target.ReadAction(in request);
            MotionWarpWindowDecision decision = !request.Enabled ? MotionWarpWindowDecision.Disabled
                : !request.HasTarget && request.RotationMethod != ProgramMotionWarpRotationMethod.TargetResponse
                    ? MotionWarpWindowDecision.NoTarget : MotionWarpWindowDecision.Accepted;
            target.ReadTarget(in request, out TVector3 position, out TYaw yaw);
            var state = new MotionWarpState<TScalar, TVector3, TYaw, TAction>(
                true, false, request.PlaybackGeneration, request.Cycle, action,
                default, default, default, default, default, default, default, default, default, default, default,
                request.SourceOperation, decision, request.HasTarget, position, yaw);
            target.WriteState(in request, in state);
            target.Trace(in request, decision == MotionWarpWindowDecision.Disabled ? MotionModifierDiagnosticCode.Disabled :
                decision == MotionWarpWindowDecision.NoTarget ? MotionModifierDiagnosticCode.NoTargetByOptionalPolicy :
                MotionModifierDiagnosticCode.WindowEntered);
        }

        public static void Apply<TRequest, TTarget>(in TRequest request, TTarget target)
            where TRequest : struct, IMotionWarpWindowRequest
            where TTarget : IMotionWarpWindowTarget<TRequest, TScalar, TVector3, TYaw, TAction, MotionWarpState<TScalar, TVector3, TYaw, TAction>>
        {
            MotionWarpState<TScalar, TVector3, TYaw, TAction> state = target.ReadState(in request);
            if (state.Decision != MotionWarpWindowDecision.Accepted)
                return;
            MotionWarpParameters<TScalar> parameters = target.ReadParameters(in request);
            if (request.RotationMethod == ProgramMotionWarpRotationMethod.TargetResponse)
            {
                target.ApplyResponse(in request, in parameters, in state);
                target.Trace(in request, MotionModifierDiagnosticCode.Applied);
                return;
            }
            TMath math = default;
            target.ReadSourceDelta(in request, out TVector3 sourceDelta, out TScalar sourceYawDelta);
            TVector3 rawDelta = math.RotatePlanar(sourceDelta, target.BodyYaw);
            TVector3 startBodyPosition;
            TYaw startBodyYaw;
            TVector3 sourceStartPosition;
            TScalar sourceStartYaw;
            TVector3 resolvedTargetPosition;
            TYaw resolvedTargetYaw;
            ProgramMotionWarpLimitResult limit;
            TVector3 previousPosition;
            TYaw previousYaw;
            TScalar previousPositionProgress;
            TScalar previousYawProgress;
            if (!state.Initialized)
            {
                target.ReadSourcePrefix(in request, out TVector3 prefix, out TScalar prefixYaw);
                startBodyPosition = math.AddVector3(math.AddVector3(target.BodyPosition, target.CorrectionDisplacement),
                    math.RotatePlanar(prefix, target.BodyYaw));
                startBodyYaw = math.Yaw(math.Add(math.Add(math.Degrees(target.BodyYaw), target.CorrectionYaw), prefixYaw));
                target.ResolveTarget(in request, in parameters, state.CapturedTargetPosition, state.CapturedTargetYaw,
                    startBodyPosition, startBodyYaw, out sourceStartPosition, out sourceStartYaw,
                    out resolvedTargetPosition, out resolvedTargetYaw, out limit);
                if (limit == ProgramMotionWarpLimitResult.PreservedByLimitPolicy)
                {
                    state = state.WithDecision(MotionWarpWindowDecision.PreservedByLimitPolicy);
                    target.WriteState(in request, in state);
                    target.Trace(in request, MotionModifierDiagnosticCode.PreservedByLimitPolicy);
                    target.ApplyMotionWarp(rawDelta, sourceYawDelta, rawDelta, sourceYawDelta);
                    return;
                }
                if (parameters.TranslationMode == ProgramMotionWarpTranslationMode.SkewRootMotion)
                {
                    previousPosition = startBodyPosition;
                    previousYaw = startBodyYaw;
                }
                else
                    target.EvaluatePose(in request, in parameters, true, startBodyPosition, startBodyYaw,
                        sourceStartPosition, sourceStartYaw, resolvedTargetPosition, resolvedTargetYaw,
                        out previousPosition, out previousYaw);
                target.ReadProgress(in request, true, out previousPositionProgress, out previousYawProgress);
                state = new MotionWarpState<TScalar, TVector3, TYaw, TAction>(
                    true, true, request.PlaybackGeneration, request.Cycle, state.ActionInstance,
                    startBodyPosition, startBodyYaw, sourceStartPosition, sourceStartYaw,
                    resolvedTargetPosition, resolvedTargetYaw, limit, previousPosition, previousYaw,
                    previousPositionProgress, previousYawProgress, request.SourceOperation, state.Decision,
                    state.HasTarget, state.CapturedTargetPosition, state.CapturedTargetYaw);
            }
            else
            {
                startBodyPosition = state.StartBodyPosition;
                startBodyYaw = state.StartBodyYaw;
                sourceStartPosition = state.SourceWindowStartPosition;
                sourceStartYaw = state.SourceWindowStartYaw;
                resolvedTargetPosition = state.ResolvedTargetPosition;
                resolvedTargetYaw = state.ResolvedTargetYaw;
                limit = state.LimitResult;
                previousPosition = state.PreviousWarpedPosition;
                previousYaw = state.PreviousWarpedYaw;
                previousPositionProgress = state.LastPositionProgress;
                previousYawProgress = state.LastYawProgress;
            }
            target.ReadProgress(in request, false, out TScalar positionProgress, out TScalar yawProgress);
            if (math.GreaterThan(previousPositionProgress, positionProgress) || math.GreaterThan(previousYawProgress, yawProgress))
                throw new InvalidOperationException(MotionModifierDiagnosticCode.InvalidState +
                    ": Cumulative Timeline MotionWarp progress moved backwards.");
            TVector3 currentPosition;
            TYaw currentYaw;
            if (parameters.TranslationMode == ProgramMotionWarpTranslationMode.SkewRootMotion)
                target.EvaluateStep(in request, in parameters, in state, out currentPosition, out currentYaw);
            else
                target.EvaluatePose(in request, in parameters, false, startBodyPosition, startBodyYaw,
                    sourceStartPosition, sourceStartYaw, resolvedTargetPosition, resolvedTargetYaw,
                    out currentPosition, out currentYaw);
            target.ApplyMotionWarp(rawDelta, sourceYawDelta, math.SubtractVector3(currentPosition, previousPosition),
                math.DeltaYaw(previousYaw, currentYaw));
            state = state.WithProgress(currentPosition, currentYaw, positionProgress, yawProgress);
            target.WriteState(in request, in state);
            target.Trace(in request, limit == ProgramMotionWarpLimitResult.AppliedClamped
                ? MotionModifierDiagnosticCode.AppliedClamped : MotionModifierDiagnosticCode.Applied);
        }
    }
}
