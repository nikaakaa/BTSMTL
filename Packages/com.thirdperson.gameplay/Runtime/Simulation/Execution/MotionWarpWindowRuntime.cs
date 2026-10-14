using System;

namespace ThirdPersonSimulation
{
    internal interface IMotionWarpWindowRequest
    {
        SimulationExecutionSource Source { get; }
        ulong PlaybackGeneration { get; }
        int Cycle { get; }
        bool Enabled { get; }
        bool HasTarget { get; }
        ProgramMotionWarpRotationMethod RotationMethod { get; }
    }

    internal interface IMotionWarpWindowTarget<TRequest, TScalar, TVector2, TVector3, TYaw, TAction, TState, TMath>
        where TRequest : struct
        where TScalar : struct
        where TVector2 : struct
        where TVector3 : struct
        where TYaw : struct
        where TAction : struct
        where TState : struct
        where TMath : struct, IScalarMath<TScalar>, IVectorMath<TScalar, TVector2, TVector3>
    {
        TState ReadState(in TRequest request);
        void WriteState(in TRequest request, in TState state);
        TAction ReadAction(in TRequest request);
        void ReadTarget(in TRequest request, out TVector3 position, out TYaw yaw);
        TVector3 BodyPosition { get; }
        TYaw BodyYaw { get; }
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
        void ApplyResponse(in TRequest request, in MotionWarpParameters<TScalar> parameters, in TState state,
            ref ResolvedMotionChannel<TScalar, TVector2, TVector3, TMath> channel);
        void Trace(in TRequest request, string code);
    }

    internal static class MotionWarpWindowRuntime<TScalar, TVector2, TVector3, TYaw, TAction, TMath,
        TScalarCodec, TVector3Codec, TYawCodec>
        where TScalar : unmanaged, IEquatable<TScalar>
        where TVector2 : struct
        where TVector3 : unmanaged, IEquatable<TVector3>
        where TYaw : unmanaged, IEquatable<TYaw>
        where TAction : struct, IEquatable<TAction>, ICanonicalRecord<TAction>
        where TMath : struct, IScalarMath<TScalar>, IVectorMath<TScalar, TVector2, TVector3>,
            IPlanarRotation<TVector3, TYaw>, IYawMath<TScalar, TYaw>
        where TScalarCodec : struct, ICanonicalLeafCodec<TScalar>
        where TVector3Codec : struct, ICanonicalLeafCodec<TVector3>
        where TYawCodec : struct, ICanonicalLeafCodec<TYaw>
    {
        public static void Capture<TRequest, TTarget>(in TRequest request, TTarget target)
            where TRequest : struct, IMotionWarpWindowRequest
            where TTarget : IMotionWarpWindowTarget<TRequest, TScalar, TVector2, TVector3, TYaw, TAction, MotionWarpState<TScalar, TVector3, TYaw, TAction, TScalarCodec, TVector3Codec, TYawCodec>, TMath>
        {
            MotionWarpState<TScalar, TVector3, TYaw, TAction, TScalarCodec, TVector3Codec, TYawCodec> previous = target.ReadState(in request);
            if (previous.Active && previous.PlaybackGeneration == request.PlaybackGeneration && previous.Cycle == request.Cycle)
                return;
            TAction action = target.ReadAction(in request);
            MotionWarpWindowDecision decision = !request.Enabled ? MotionWarpWindowDecision.Disabled
                : !request.HasTarget && request.RotationMethod != ProgramMotionWarpRotationMethod.TargetResponse
                    ? MotionWarpWindowDecision.NoTarget : MotionWarpWindowDecision.Accepted;
            target.ReadTarget(in request, out TVector3 position, out TYaw yaw);
            var state = new MotionWarpState<TScalar, TVector3, TYaw, TAction, TScalarCodec, TVector3Codec, TYawCodec>(
                true, false, request.PlaybackGeneration, request.Cycle, action,
                default, default, default, default, default, default, default, default, default, default, default,
                request.Source.Operation, decision, request.HasTarget, position, yaw);
            target.WriteState(in request, in state);
            target.Trace(in request, decision == MotionWarpWindowDecision.Disabled ? MotionModifierDiagnosticCode.Disabled :
                decision == MotionWarpWindowDecision.NoTarget ? MotionModifierDiagnosticCode.NoTargetByOptionalPolicy :
                MotionModifierDiagnosticCode.WindowEntered);
        }

        public static void Apply<TRequest, TTarget>(in TRequest request, TTarget target,
            ref ResolvedMotionChannel<TScalar, TVector2, TVector3, TMath> channel)
            where TRequest : struct, IMotionWarpWindowRequest
            where TTarget : IMotionWarpWindowTarget<TRequest, TScalar, TVector2, TVector3, TYaw, TAction, MotionWarpState<TScalar, TVector3, TYaw, TAction, TScalarCodec, TVector3Codec, TYawCodec>, TMath>
        {
            MotionWarpState<TScalar, TVector3, TYaw, TAction, TScalarCodec, TVector3Codec, TYawCodec> state = target.ReadState(in request);
            if (state.Decision != MotionWarpWindowDecision.Accepted)
                return;
            MotionWarpParameters<TScalar> parameters = target.ReadParameters(in request);
            if (request.RotationMethod == ProgramMotionWarpRotationMethod.TargetResponse)
            {
                target.ApplyResponse(in request, in parameters, in state, ref channel);
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
                startBodyPosition = math.AddVector3(math.AddVector3(target.BodyPosition, channel.MotionCorrectionDisplacement),
                    math.RotatePlanar(prefix, target.BodyYaw));
                startBodyYaw = math.Yaw(math.Add(math.Add(math.Degrees(target.BodyYaw), channel.MotionCorrectionYawDegrees), prefixYaw));
                target.ResolveTarget(in request, in parameters, state.CapturedTargetPosition, state.CapturedTargetYaw,
                    startBodyPosition, startBodyYaw, out sourceStartPosition, out sourceStartYaw,
                    out resolvedTargetPosition, out resolvedTargetYaw, out limit);
                if (limit == ProgramMotionWarpLimitResult.PreservedByLimitPolicy)
                {
                    state = state.WithDecision(MotionWarpWindowDecision.PreservedByLimitPolicy);
                    target.WriteState(in request, in state);
                    target.Trace(in request, MotionModifierDiagnosticCode.PreservedByLimitPolicy);
                    channel.ApplyMotionWarp(rawDelta, sourceYawDelta, rawDelta, sourceYawDelta);
                    return;
                }
                target.EvaluatePose(in request, in parameters, true, startBodyPosition, startBodyYaw,
                    sourceStartPosition, sourceStartYaw, resolvedTargetPosition, resolvedTargetYaw,
                    out previousPosition, out previousYaw);
                target.ReadProgress(in request, true, out previousPositionProgress, out previousYawProgress);
                state = new MotionWarpState<TScalar, TVector3, TYaw, TAction, TScalarCodec, TVector3Codec, TYawCodec>(
                    true, true, request.PlaybackGeneration, request.Cycle, state.ActionInstance,
                    startBodyPosition, startBodyYaw, sourceStartPosition, sourceStartYaw,
                    resolvedTargetPosition, resolvedTargetYaw, limit, previousPosition, previousYaw,
                    previousPositionProgress, previousYawProgress, request.Source.Operation, state.Decision,
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
            target.EvaluatePose(in request, in parameters, false, startBodyPosition, startBodyYaw,
                sourceStartPosition, sourceStartYaw, resolvedTargetPosition, resolvedTargetYaw,
                out TVector3 currentPosition, out TYaw currentYaw);
            channel.ApplyMotionWarp(rawDelta, sourceYawDelta, math.SubtractVector3(currentPosition, previousPosition),
                math.DeltaYaw(previousYaw, currentYaw));
            state = state.WithProgress(currentPosition, currentYaw, positionProgress, yawProgress);
            target.WriteState(in request, in state);
            target.Trace(in request, limit == ProgramMotionWarpLimitResult.AppliedClamped
                ? MotionModifierDiagnosticCode.AppliedClamped : MotionModifierDiagnosticCode.Applied);
        }
    }
}
