using System;

namespace ThirdPersonSimulation
{
    public readonly struct LocomotionMotionProfile<TScalar, TMath>
        where TScalar : struct, IComparable<TScalar>, IEquatable<TScalar>
        where TMath : struct, IScalarMath<TScalar>
    {
        public LocomotionMotionProfile(
            LocomotionInputMotionExecutionMode executionMode,
            LocomotionInputMotionDisplacementMode displacementMode,
            TScalar turnSpeed, TScalar moveSpeed, TScalar curveDuration, int durationTicks,
            GameplayAbilityCurve<TScalar, TMath> xCurve, GameplayAbilityCurve<TScalar, TMath> zCurve,
            int tickRate)
        {
            TMath math = default;
            ExecutionMode = executionMode;
            DisplacementMode = displacementMode;
            TurnSpeed = turnSpeed;
            MoveSpeed = moveSpeed;
            CurveDuration = curveDuration;
            DurationTicks = durationTicks;
            XCurve = xCurve;
            ZCurve = zCurve;
            TickRate = math.FromInt32(tickRate);
            TickDelta = math.Divide(math.One, TickRate);
            bool looping = displacementMode == LocomotionInputMotionDisplacementMode.ActionMotionCurve &&
                executionMode == LocomotionInputMotionExecutionMode.Continuous;
            XCycleTotal = looping ? xCurve.Evaluate(curveDuration, math.Zero) : math.Zero;
            ZCycleTotal = looping ? zCurve.Evaluate(curveDuration, math.Zero) : math.Zero;
        }

        public LocomotionInputMotionExecutionMode ExecutionMode { get; }
        public LocomotionInputMotionDisplacementMode DisplacementMode { get; }
        public TScalar TurnSpeed { get; }
        public TScalar MoveSpeed { get; }
        public TScalar CurveDuration { get; }
        public int DurationTicks { get; }
        public GameplayAbilityCurve<TScalar, TMath> XCurve { get; }
        public GameplayAbilityCurve<TScalar, TMath> ZCurve { get; }
        public TScalar TickRate { get; }
        public TScalar TickDelta { get; }
        public TScalar XCycleTotal { get; }
        public TScalar ZCycleTotal { get; }
    }

    public static class LocomotionMotionCalculation<TScalar, TVector2, TVector3, TYaw, TMath>
        where TScalar : struct, IComparable<TScalar>, IEquatable<TScalar>
        where TVector2 : struct
        where TVector3 : struct
        where TYaw : struct
        where TMath : struct, IScalarMath<TScalar>, IVectorMath<TScalar, TVector2, TVector3>, IYawMath<TScalar, TYaw>
    {
        public static TVector2 NormalizeInput(TVector2 move) =>
            default(TMath).GreaterThan(default(TMath).SqrMagnitude2(move), default(TMath).One)
                ? default(TMath).Normalize2(move) : move;

        public static void Evaluate(in LocomotionMotionProfile<TScalar, TMath> profile,
            TVector2 move, TYaw bodyYaw, int elapsedTicks, out TVector3 displacement, out TScalar yaw)
        {
            TMath math = default;
            TScalar maximumYaw = math.Multiply(profile.TurnSpeed, profile.TickDelta);
            displacement = ResolveDisplacement(in profile, move, elapsedTicks);
            yaw = math.Zero;
            if (!math.IsVector2Zero(move) && math.GreaterThan(maximumYaw, math.Zero))
            {
                TYaw desired = math.YawFromDirection(math.X2(move), math.Y2(move));
                yaw = math.Clamp(math.DeltaYaw(bodyYaw, desired), math.Negate(maximumYaw), maximumYaw);
            }
        }

        static TVector3 ResolveDisplacement(in LocomotionMotionProfile<TScalar, TMath> profile,
            TVector2 move, int elapsedTicks)
        {
            TMath math = default;
            if (profile.DisplacementMode == LocomotionInputMotionDisplacementMode.ConstantSpeed)
            {
                return math.Vector3(
                    math.Multiply(math.Multiply(math.X2(move), profile.MoveSpeed), profile.TickDelta),
                    math.Zero,
                    math.Multiply(math.Multiply(math.Y2(move), profile.MoveSpeed), profile.TickDelta));
            }
            if (math.IsVector2Zero(move))
                return math.ZeroVector3;
            TScalar fromTime = math.Divide(math.FromInt32(elapsedTicks), profile.TickRate);
            TScalar toTime = math.Divide(math.FromInt32(checked(elapsedTicks + 1)), profile.TickRate);
            bool looping = profile.ExecutionMode == LocomotionInputMotionExecutionMode.Continuous;
            TScalar localX = math.Subtract(
                SampleCumulative(profile.XCurve, toTime, profile.CurveDuration, looping, profile.XCycleTotal),
                SampleCumulative(profile.XCurve, fromTime, profile.CurveDuration, looping, profile.XCycleTotal));
            TScalar localZ = math.Subtract(
                SampleCumulative(profile.ZCurve, toTime, profile.CurveDuration, looping, profile.ZCycleTotal),
                SampleCumulative(profile.ZCurve, fromTime, profile.CurveDuration, looping, profile.ZCycleTotal));
            TVector2 forward = math.Normalize2(move);
            TScalar forwardX = math.X2(forward);
            TScalar forwardZ = math.Y2(forward);
            return math.Vector3(
                math.Add(math.Multiply(forwardZ, localX), math.Multiply(forwardX, localZ)),
                math.Zero,
                math.Add(math.Multiply(math.Negate(forwardX), localX), math.Multiply(forwardZ, localZ)));
        }

        static TScalar SampleCumulative(GameplayAbilityCurve<TScalar, TMath> curve,
            TScalar time, TScalar duration, bool looping, TScalar cycleTotal)
        {
            TMath math = default;
            if (!looping)
                return curve.Evaluate(math.Clamp(time, math.Zero, duration), math.Zero);
            TScalar cycle = math.FromInt32(math.FloorToInt32(math.Divide(time, duration)));
            TScalar localTime = math.Subtract(time, math.Multiply(duration, cycle));
            return math.Add(math.Multiply(cycleTotal, cycle), curve.Evaluate(localTime, math.Zero));
        }
    }
}
