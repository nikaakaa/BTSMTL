using System;

namespace ThirdPersonSimulation
{
    public static class MotionWarpCalculation<TScalar, TVector2, TVector3, TYaw, TMath>
        where TScalar : struct
        where TVector2 : struct
        where TVector3 : struct
        where TYaw : struct
        where TMath : struct, IScalarMath<TScalar>, IVectorMath<TScalar, TVector2, TVector3>,
            IPlanarRotation<TVector3, TYaw>, IYawMath<TScalar, TYaw>
    {
        public static void ResolveTarget(
            in MotionWarpParameters<TScalar> parameters,
            TVector3 sourceWindowDelta, TScalar sourceWindowYawDelta, TScalar duration,
            TVector3 targetPosition, TYaw targetYaw, TVector3 startBodyPosition, TYaw startBodyYaw,
            out TVector3 resolvedTargetPosition, out TYaw resolvedTargetYaw,
            out ProgramMotionWarpLimitResult limitResult)
        {
            TMath math = default;
            TVector3 nominalEnd = math.AddVector3(startBodyPosition, math.RotatePlanar(sourceWindowDelta, startBodyYaw));
            TVector3 requestedPosition = parameters.TranslationMode == ProgramMotionWarpTranslationMode.Disabled
                ? nominalEnd
                : ResolveTargetPosition(in parameters, targetPosition, targetYaw, startBodyPosition, startBodyYaw, math.Y3(nominalEnd));
            TVector3 correction = math.Vector3(
                math.Subtract(math.X3(requestedPosition), math.X3(nominalEnd)), math.Zero,
                math.Subtract(math.Z3(requestedPosition), math.Z3(nominalEnd)));
            TScalar maximumPosition = parameters.TranslationMode == ProgramMotionWarpTranslationMode.Disabled
                ? math.Zero : parameters.MaximumPosition;
            TScalar positionMagnitude = parameters.TranslationMode == ProgramMotionWarpTranslationMode.Disabled
                ? math.Zero : math.Magnitude2(math.Vector2(math.X3(correction), math.Z3(correction)));
            bool positionExceeded = math.GreaterThan(positionMagnitude, maximumPosition);
            TVector3 effectiveCorrection = correction;
            if (positionExceeded)
                effectiveCorrection = math.GreaterThan(maximumPosition, math.Zero)
                    ? math.MultiplyVector3(correction, math.Divide(maximumPosition, positionMagnitude)) : math.ZeroVector3;
            TScalar verticalCorrection = math.Subtract(math.Y3(requestedPosition), math.Y3(nominalEnd));
            bool verticalExceeded = math.GreaterThan(math.Abs(verticalCorrection), parameters.MaximumVertical);
            effectiveCorrection = math.Vector3(math.X3(effectiveCorrection),
                math.Clamp(verticalCorrection, math.Negate(parameters.MaximumVertical), parameters.MaximumVertical),
                math.Z3(effectiveCorrection));
            resolvedTargetPosition = math.AddVector3(nominalEnd, effectiveCorrection);
            TYaw nominalYaw = math.Yaw(math.Add(math.Degrees(startBodyYaw), sourceWindowYawDelta));
            TYaw requestedYaw = nominalYaw;
            if (parameters.RotationMode == ProgramMotionWarpRotationMode.MatchTargetYaw)
                requestedYaw = math.Yaw(math.Add(math.Degrees(targetYaw), parameters.YawOffset));
            else if (parameters.RotationMode == ProgramMotionWarpRotationMode.FaceTarget)
            {
                TScalar x = math.Subtract(math.X3(targetPosition), math.X3(resolvedTargetPosition));
                TScalar z = math.Subtract(math.Z3(targetPosition), math.Z3(resolvedTargetPosition));
                if (math.IsZero(x) && math.IsZero(z))
                    throw Failure(MotionModifierDiagnosticCode.FaceTargetZeroDirection,
                        "FaceTarget desired actor position equals the target planar position.");
                requestedYaw = math.Yaw(math.Add(math.Degrees(math.YawFromDirection(x, z)), parameters.YawOffset));
            }
            TScalar yawCorrection = parameters.RotationMode == ProgramMotionWarpRotationMode.Disabled
                ? math.Zero : math.DeltaYaw(nominalYaw, requestedYaw);
            TScalar maximumYaw = parameters.RotationMode == ProgramMotionWarpRotationMode.Disabled
                ? math.Zero : parameters.MaximumYaw;
            if (parameters.RotationMode != ProgramMotionWarpRotationMode.Disabled &&
                parameters.RotationMethod == ProgramMotionWarpRotationMethod.ConstantRate)
                maximumYaw = math.Min(maximumYaw, math.Multiply(parameters.MaximumYawRate, duration));
            bool yawExceeded = parameters.RotationMode != ProgramMotionWarpRotationMode.Disabled &&
                math.GreaterThan(math.Abs(yawCorrection), maximumYaw);
            TScalar effectiveYaw = math.Clamp(yawCorrection, math.Negate(maximumYaw), maximumYaw);
            resolvedTargetYaw = math.Yaw(math.Add(math.Degrees(nominalYaw), effectiveYaw));
            bool exceeded = positionExceeded || verticalExceeded || yawExceeded;
            limitResult = exceeded ? ProgramMotionWarpLimitResult.AppliedClamped : ProgramMotionWarpLimitResult.Applied;
            if (exceeded && parameters.LimitPolicy == ProgramMotionWarpLimitPolicy.PreserveSource)
                limitResult = ProgramMotionWarpLimitResult.PreservedByLimitPolicy;
        }

        static TVector3 ResolveTargetPosition(
            in MotionWarpParameters<TScalar> parameters,
            TVector3 targetPosition, TYaw targetYaw, TVector3 startBodyPosition, TYaw startBodyYaw, TScalar targetY)
        {
            TMath math = default;
            TVector3 localOffset = math.Vector3(parameters.OffsetX, parameters.OffsetY, parameters.OffsetZ);
            TVector3 worldOffset;
            switch (parameters.TargetOffsetSpace)
            {
                case ProgramMotionWarpTargetOffsetSpace.TargetLocal:
                    worldOffset = math.RotatePlanar(localOffset, targetYaw);
                    break;
                case ProgramMotionWarpTargetOffsetSpace.ApproachDirection:
                {
                    TVector2 outward = math.Vector2(
                        math.Subtract(math.X3(startBodyPosition), math.X3(targetPosition)),
                        math.Subtract(math.Z3(startBodyPosition), math.Z3(targetPosition)));
                    if (math.IsZero(math.SqrMagnitude2(outward)))
                        throw Failure(MotionModifierDiagnosticCode.ApproachDirectionZero,
                            "ApproachDirection requires distinct target and warp-start planar positions.");
                    TVector2 forward = math.Normalize2(outward);
                    worldOffset = math.Vector3(
                        math.Add(math.Multiply(math.Y2(forward), parameters.OffsetX), math.Multiply(math.X2(forward), parameters.OffsetZ)),
                        parameters.OffsetY,
                        math.Add(math.Multiply(math.Negate(math.X2(forward)), parameters.OffsetX), math.Multiply(math.Y2(forward), parameters.OffsetZ)));
                    break;
                }
                case ProgramMotionWarpTargetOffsetSpace.ActorStartLocal:
                    worldOffset = math.RotatePlanar(localOffset, startBodyYaw);
                    break;
                case ProgramMotionWarpTargetOffsetSpace.World:
                    worldOffset = localOffset;
                    break;
                default:
                    throw Failure(MotionModifierDiagnosticCode.InvalidState,
                        $"Unsupported target offset space '{parameters.TargetOffsetSpace}'.");
            }
            return math.Vector3(math.Add(math.X3(targetPosition), math.X3(worldOffset)),
                parameters.PositionAxes == ProgramMotionWarpPositionAxes.Full3D
                    ? math.Add(math.Y3(targetPosition), math.Y3(worldOffset)) : targetY,
                math.Add(math.Z3(targetPosition), math.Z3(worldOffset)));
        }

        public static void EvaluatePose(
            in MotionWarpParameters<TScalar> parameters,
            TVector3 sourceRelative, TScalar sourceYawRelative, TVector3 sourceEndRelative, TScalar sourceEndYawRelative,
            TScalar positionProgress, TScalar yawProgress, TScalar elapsed,
            TVector3 startBodyPosition, TYaw startBodyYaw, TVector3 resolvedTargetPosition, TYaw resolvedTargetYaw,
            out TVector3 warpedPosition, out TYaw warpedYaw)
        {
            TMath math = default;
            TYaw nominalYaw = math.Yaw(math.Add(math.Degrees(startBodyYaw), sourceYawRelative));
            TYaw nominalEndYaw = math.Yaw(math.Add(math.Degrees(startBodyYaw), sourceEndYawRelative));
            TScalar finalYawCorrection = parameters.RotationMode == ProgramMotionWarpRotationMode.Disabled
                ? math.Zero : math.DeltaYaw(nominalEndYaw, resolvedTargetYaw);
            TScalar currentYawCorrection;
            switch (parameters.RotationMode == ProgramMotionWarpRotationMode.Disabled
                ? ProgramMotionWarpRotationMethod.ProgressCurve : parameters.RotationMethod)
            {
                case ProgramMotionWarpRotationMethod.ProgressCurve:
                    currentYawCorrection = math.Multiply(finalYawCorrection, yawProgress);
                    break;
                case ProgramMotionWarpRotationMethod.ConstantRate:
                {
                    TScalar maximum = math.Multiply(parameters.MaximumYawRate, math.Max(math.Zero, elapsed));
                    currentYawCorrection = math.Clamp(finalYawCorrection, math.Negate(maximum), maximum);
                    break;
                }
                case ProgramMotionWarpRotationMethod.ScaleSourceYaw:
                {
                    if (!math.GreaterThan(math.Abs(sourceEndYawRelative), parameters.ScaleEpsilon))
                        throw Failure(MotionModifierDiagnosticCode.ScaleSourceYawZero,
                            "ScaleSourceYaw requires non-zero source window yaw.");
                    TScalar targetYawRelative = math.DeltaYaw(startBodyYaw, resolvedTargetYaw);
                    currentYawCorrection = math.Subtract(
                        math.Multiply(sourceYawRelative, math.Divide(targetYawRelative, sourceEndYawRelative)), sourceYawRelative);
                    break;
                }
                default:
                    throw Failure(MotionModifierDiagnosticCode.InvalidState,
                        $"Unsupported rotation method '{parameters.RotationMethod}'.");
            }
            warpedYaw = math.Yaw(math.Add(math.Degrees(nominalYaw), currentYawCorrection));
            TVector3 rotatedSource = math.RotatePlanar(sourceRelative,
                math.Yaw(math.Add(math.Degrees(startBodyYaw), currentYawCorrection)));
            TVector3 rotatedEnd = math.RotatePlanar(sourceEndRelative,
                math.Yaw(math.Add(math.Degrees(startBodyYaw), finalYawCorrection)));
            TVector3 targetRelative = math.SubtractVector3(resolvedTargetPosition, startBodyPosition);
            TVector3 warpedRelative;
            switch (parameters.TranslationMode)
            {
                case ProgramMotionWarpTranslationMode.Disabled:
                    warpedRelative = rotatedSource;
                    break;
                case ProgramMotionWarpTranslationMode.ScaleToTarget:
                    warpedRelative = ScalePlanarToTarget(rotatedSource, rotatedEnd, targetRelative, parameters.ScaleEpsilon);
                    break;
                case ProgramMotionWarpTranslationMode.SkewToTarget:
                {
                    TVector3 endpointCorrection = math.SubtractVector3(targetRelative, rotatedEnd);
                    if (parameters.PositionAxes == ProgramMotionWarpPositionAxes.Planar)
                        endpointCorrection = math.Vector3(math.X3(endpointCorrection), math.Zero, math.Z3(endpointCorrection));
                    warpedRelative = math.AddVector3(rotatedSource, math.MultiplyVector3(endpointCorrection, positionProgress));
                    break;
                }
                case ProgramMotionWarpTranslationMode.LinearToTarget:
                    warpedRelative = math.Vector3(math.Multiply(math.X3(targetRelative), positionProgress),
                        parameters.PositionAxes == ProgramMotionWarpPositionAxes.Full3D
                            ? math.Multiply(math.Y3(targetRelative), positionProgress) : math.Y3(sourceRelative),
                        math.Multiply(math.Z3(targetRelative), positionProgress));
                    break;
                default:
                    throw Failure(MotionModifierDiagnosticCode.InvalidState,
                        $"Unsupported translation mode '{parameters.TranslationMode}'.");
            }
            warpedPosition = math.AddVector3(startBodyPosition, warpedRelative);
        }

        public static void EvaluateSkewStep(
            in MotionWarpParameters<TScalar> parameters,
            TVector3 sourceDelta, TVector3 sourceRemaining, TVector3 sourceWindow,
            TScalar sourceYawDelta, TScalar sourceYawRemaining, TScalar deltaTime,
            TScalar previousPositionProgress, TScalar positionProgress,
            TScalar previousYawProgress, TScalar yawProgress,
            TVector3 bodyPosition, TYaw bodyYaw, TVector3 targetPosition, TYaw targetYaw,
            out TVector3 displacement, out TScalar yawDelta)
        {
            TMath math = default;
            TVector3 delta = math.RotatePlanar(sourceDelta, bodyYaw);
            TVector3 remaining = math.RotatePlanar(sourceRemaining, bodyYaw);
            TVector3 target = math.SubtractVector3(targetPosition, bodyPosition);
            if (parameters.PositionAxes == ProgramMotionWarpPositionAxes.Planar)
            {
                delta = math.Vector3(math.X3(delta), math.Zero, math.Z3(delta));
                remaining = math.Vector3(math.X3(remaining), math.Zero, math.Z3(remaining));
                target = math.Vector3(math.X3(target), math.Zero, math.Z3(target));
                sourceWindow = math.Vector3(math.X3(sourceWindow), math.Zero, math.Z3(sourceWindow));
            }
            if (math.IsVector3Zero(sourceWindow))
            {
                TScalar fraction = RemainingProgressFraction(previousPositionProgress, positionProgress);
                displacement = math.MultiplyVector3(target, fraction);
            }
            else
                displacement = SkewTranslation(delta, remaining, target);
            if (parameters.PositionAxes == ProgramMotionWarpPositionAxes.Planar)
                displacement = math.Vector3(math.X3(displacement), math.Y3(sourceDelta), math.Z3(displacement));
            yawDelta = sourceYawDelta;
            if (parameters.RotationMode == ProgramMotionWarpRotationMode.Disabled)
                return;
            TScalar correction = math.DeltaYaw(math.Yaw(math.Add(math.Degrees(bodyYaw), sourceYawRemaining)), targetYaw);
            switch (parameters.RotationMethod)
            {
                case ProgramMotionWarpRotationMethod.ProgressCurve:
                    correction = math.Multiply(correction, RemainingProgressFraction(previousYawProgress, yawProgress));
                    break;
                case ProgramMotionWarpRotationMethod.ConstantRate:
                    TScalar maximum = math.Multiply(parameters.MaximumYawRate, deltaTime);
                    correction = math.Clamp(correction, math.Negate(maximum), maximum);
                    break;
                case ProgramMotionWarpRotationMethod.ScaleSourceYaw:
                    correction = math.IsZero(sourceYawDelta) ? math.Zero :
                        math.Multiply(correction, math.Divide(sourceYawDelta, sourceYawRemaining));
                    break;
                default:
                    throw Failure(MotionModifierDiagnosticCode.InvalidState,
                        $"Unsupported SkewRootMotion rotation method '{parameters.RotationMethod}'.");
            }
            yawDelta = math.Add(yawDelta, correction);
        }

        public static TVector3 SkewTranslation(TVector3 delta, TVector3 remaining, TVector3 target)
        {
            TMath math = default;
            if (math.IsVector3Zero(delta))
                return math.ZeroVector3;
            if (math.IsZero(Dot3(target, remaining)))
                return math.MultiplyVector3(target, math.Divide(Magnitude3(delta), Magnitude3(remaining)));
            // UE SkewWarp's forward scale and two shears reduce to this rank-one transform.
            TScalar forwardFraction = math.Divide(Dot3(delta, remaining), Dot3(remaining, remaining));
            return math.AddVector3(delta,
                math.MultiplyVector3(math.SubtractVector3(target, remaining), forwardFraction));
        }

        static TScalar RemainingProgressFraction(TScalar previous, TScalar current)
        {
            TMath math = default;
            return math.Equal(previous, math.One) ? math.Zero :
                math.Divide(math.Subtract(current, previous), math.Subtract(math.One, previous));
        }

        static TScalar Dot3(TVector3 left, TVector3 right)
        {
            TMath math = default;
            return math.Add(math.Add(math.Multiply(math.X3(left), math.X3(right)),
                math.Multiply(math.Y3(left), math.Y3(right))), math.Multiply(math.Z3(left), math.Z3(right)));
        }

        static TScalar Magnitude3(TVector3 value)
        {
            TMath math = default;
            return math.Magnitude2(math.Vector2(math.Magnitude2(math.Vector2(math.X3(value), math.Z3(value))), math.Y3(value)));
        }

        public static void TargetResponse(
            in MotionWarpParameters<TScalar> parameters,
            bool hasTarget, TVector3 capturedTargetPosition, TYaw capturedTargetYaw,
            TScalar response, TScalar inputResponse, TVector2 inputDirection, TScalar delta,
            TVector3 localDelta, TVector3 bodyPosition, TYaw bodyYaw,
            TVector3 correctionDisplacement, TScalar correctionYaw,
            out TVector3 previousDirectionDelta, out TVector3 nextDirectionDelta, out TScalar yawDelta)
        {
            TMath math = default;
            TYaw currentYaw = math.Yaw(math.Add(math.Degrees(bodyYaw), correctionYaw));
            TYaw targetYaw = currentYaw;
            if (hasTarget && math.GreaterThan(response, math.Zero))
            {
                targetYaw = capturedTargetYaw;
                if (parameters.RotationMode == ProgramMotionWarpRotationMode.FaceTarget)
                {
                    TVector3 direction = math.SubtractVector3(capturedTargetPosition, math.AddVector3(bodyPosition, correctionDisplacement));
                    targetYaw = !math.IsZero(math.X3(direction)) || !math.IsZero(math.Z3(direction))
                        ? math.YawFromDirection(math.X3(direction), math.Z3(direction)) : currentYaw;
                }
            }
            else
            {
                response = inputResponse;
                if (math.GreaterThan(response, math.Zero) &&
                    (!math.IsZero(math.X2(inputDirection)) || !math.IsZero(math.Y2(inputDirection))))
                    targetYaw = math.YawFromDirection(math.X2(inputDirection), math.Y2(inputDirection));
            }
            TYaw desired = math.Yaw(math.Add(math.Degrees(targetYaw), parameters.YawOffset));
            yawDelta = MotionResponseCalculation<TScalar, TMath>.Evaluate(
                math.Clamp(math.DeltaYaw(currentYaw, desired), math.Negate(parameters.MaximumYaw), parameters.MaximumYaw),
                response, delta);
            previousDirectionDelta = math.RotatePlanar(localDelta, bodyYaw);
            nextDirectionDelta = math.RotatePlanar(localDelta, math.Yaw(math.Add(math.Degrees(currentYaw), yawDelta)));
        }

        static TVector3 ScalePlanarToTarget(TVector3 value, TVector3 sourceEnd, TVector3 targetEnd, TScalar epsilon)
        {
            TMath math = default;
            TScalar denominator = math.Add(math.Multiply(math.X3(sourceEnd), math.X3(sourceEnd)),
                math.Multiply(math.Z3(sourceEnd), math.Z3(sourceEnd)));
            if (!math.GreaterThan(denominator, epsilon))
                throw new InvalidOperationException("ScaleToTarget requires a non-zero source window planar endpoint.");
            TScalar dot = math.Add(math.Multiply(math.X3(sourceEnd), math.X3(targetEnd)),
                math.Multiply(math.Z3(sourceEnd), math.Z3(targetEnd)));
            TScalar cross = math.Subtract(math.Multiply(math.X3(sourceEnd), math.Z3(targetEnd)),
                math.Multiply(math.Z3(sourceEnd), math.X3(targetEnd)));
            return math.Vector3(
                math.Divide(math.Subtract(math.Multiply(dot, math.X3(value)), math.Multiply(cross, math.Z3(value))), denominator),
                math.Y3(value),
                math.Divide(math.Add(math.Multiply(cross, math.X3(value)), math.Multiply(dot, math.Z3(value))), denominator));
        }

        static InvalidOperationException Failure(string code, string detail) => new InvalidOperationException($"{code}: {detail}");
    }
}
