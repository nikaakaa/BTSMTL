using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using System;
using ThirdPersonSimulation.Collision;

namespace ThirdPersonSimulation.FPGameplay
{
    internal sealed class FPPreparedAttackPart
    {
        public FPPreparedAttackPart(int shapeIndex, AttackSweepPartData<FP> data)
        {
            ShapeIndex = shapeIndex;
            Data = data;
            switch (data.Kind)
            {
                case SweepShapeKind.Box:
                    Radius = FPAttackSweepMath.Length(data.HalfExtents);
                    break;
                case SweepShapeKind.Sphere:
                    Radius = data.Radius;
                    break;
                case SweepShapeKind.Capsule:
                    Radius = data.Radius + data.HalfLength;
                    break;
                case SweepShapeKind.ConvexHull:
                    for (int i = 0; i < data.Vertices.Length; i++)
                        Radius = FPMath.Max(Radius, FPAttackSweepMath.Length(data.Vertices[i]));
                    break;
            }
            for (int i = 0; i < data.Samples.Length; i++)
            {
                var key = data.Samples[i];
                data.Samples[i] = new SweepSample<FP>(key.Time,
                    new SweepPose<FP>(key.Pose.Position, FPAttackSweepMath.Normalize(key.Pose.Orientation)));
            }
        }

        public int ShapeIndex { get; }
        public AttackSweepPartData<FP> Data { get; }
        public FP Radius { get; }

        public int KeyAt(FP time)
        {
            int low = 0, high = Data.Samples.Length - 1;
            while (low < high)
            {
                int middle = (low + high + 1) / 2;
                if (Data.Samples[middle].Time <= time)
                    low = middle;
                else
                    high = middle - 1;
            }
            return low;
        }

        public SweepPose<FP> Sample(FP time) => Sample(time, KeyAt(time));

        public SweepPose<FP> Sample(FP time, int key)
        {
            var start = Data.Samples[key];
            if (key == Data.Samples.Length - 1 || time <= start.Time)
                return start.Pose;
            var end = Data.Samples[key + 1];
            return FPAttackSweepMath.Lerp(start.Pose, end.Pose, (time - start.Time) / (end.Time - start.Time));
        }
    }

    internal static class FPAttackSweepMath
    {
        static FPVector3 Vector(SweepVector3<FP> value) => new FPVector3(value.X, value.Y, value.Z);
        static FPQuaternion Rotation(SweepQuaternion<FP> value) => new FPQuaternion(value.X, value.Y, value.Z, value.W);
        static SweepVector3<FP> Vector(FPVector3 value) => new SweepVector3<FP>(value.X, value.Y, value.Z);
        static SweepQuaternion<FP> Rotation(FPQuaternion value) => new SweepQuaternion<FP>(value.X, value.Y, value.Z, value.W);

        public static FP Length(SweepVector3<FP> value) => Vector(value).Magnitude;
        public static SweepQuaternion<FP> Normalize(SweepQuaternion<FP> value) => Rotation(FPQuaternion.Normalize(Rotation(value)));

        public static SweepPose<FP> Lerp(SweepPose<FP> a, SweepPose<FP> b, FP fraction)
        {
            FPVector3 start = Vector(a.Position), end = Vector(b.Position);
            FPVector3 position = start + (end - start) * fraction;
            FPQuaternion rotation = FPQuaternion.SlerpUnclamped(Rotation(a.Orientation), Rotation(b.Orientation), fraction);
            return new SweepPose<FP>(Vector(position), Rotation(rotation));
        }

        public static SweepPose<FP> Compose(SweepPose<FP> parent, SweepPose<FP> local)
        {
            FPQuaternion root = Rotation(parent.Orientation), child = Rotation(local.Orientation);
            FPVector3 worldOffset = root * Vector(local.Position);
            return new SweepPose<FP>(Vector(Vector(parent.Position) + worldOffset), Rotation(root * child));
        }

        public static FP Angle(SweepQuaternion<FP> a, SweepQuaternion<FP> b)
        {
            FP dot = FPMath.Abs(FPQuaternion.Dot(Rotation(a), Rotation(b)));
            return FPMath.Acos(FPMath.Min(dot, FP._1)) * FP._2;
        }

        public static FP Distance(SweepVector3<FP> a, SweepVector3<FP> b) =>
            (Vector(a) - Vector(b)).Magnitude;

        public static int Subdivisions(FP bound, FP error) =>
            Math.Max(1, FPMath.CeilToInt(FPMath.Sqrt(bound / error)));
    }

    internal sealed class FPAttackSweepSampling
    {
        struct Interval
        {
            public FPPreparedAttackPart Part;
            public SweepPose<FP> RootStart;
            public SweepPose<FP> RootEnd;
            public SweepPose<FP> LocalStart;
            public SweepPose<FP> LocalEnd;
            public FP StartTime;
            public FP EndTime;
            public int Count;
        }

        readonly Interval[] m_Intervals;

        public FPAttackSweepSampling(int intervalCapacity) => m_Intervals = new Interval[intervalCapacity];

        public int Write(FPPreparedAttack attack, FPCombatQuery request,
            SweepPose<FP> bodyStart, SweepPose<FP> bodyEnd,
            FP tickStart, FP tickDuration, SweepSegment<FP>[] destination)
        {
            int intervalCount = 0, segmentCount = 0;
            FP previous = request.PreviousTime - request.WindowStartTime;
            FP current = request.Time - request.WindowStartTime;
            FP cursorSpan = current - previous;
            for (int partIndex = 0; partIndex < attack.Parts.Length; partIndex++)
            {
                FPPreparedAttackPart part = attack.Parts[partIndex];
                FP start = previous;
                int key = part.KeyAt(start);
                FP a = cursorSpan == FP._0 ? FP._0 : (start - previous) / cursorSpan;
                FP startFraction = request.BodyStartFraction + (request.BodyEndFraction - request.BodyStartFraction) * a;
                SweepPose<FP> rootStart = FPAttackSweepMath.Lerp(bodyStart, bodyEnd, startFraction);
                SweepPose<FP> localStart = part.Sample(start, key);
                do
                {
                    FP end = start < part.Data.Samples[0].Time
                        ? FPMath.Min(current, part.Data.Samples[0].Time)
                        : key + 1 < part.Data.Samples.Length
                            ? FPMath.Min(current, part.Data.Samples[key + 1].Time) : current;
                    FP b = cursorSpan == FP._0 ? FP._1 : (end - previous) / cursorSpan;
                    FP endFraction = request.BodyStartFraction + (request.BodyEndFraction - request.BodyStartFraction) * b;
                    SweepPose<FP> rootEnd = FPAttackSweepMath.Lerp(bodyStart, bodyEnd, endFraction);
                    int endKey = part.KeyAt(end);
                    SweepPose<FP> localEnd = part.Sample(end, endKey);
                    FP bodyAngle = FPAttackSweepMath.Angle(rootStart.Orientation, rootEnd.Orientation);
                    FP localAngle = FPAttackSweepMath.Angle(localStart.Orientation, localEnd.Orientation);
                    FP positionRadius = FPMath.Max(FPAttackSweepMath.Length(localStart.Position), FPAttackSweepMath.Length(localEnd.Position));
                    FP displacement = FPAttackSweepMath.Distance(localStart.Position, localEnd.Position);
                    FP sumAngle = bodyAngle + localAngle;
                    // 合成运动与段内直线/Slerp 的二阶位移上界决定细分数。
                    FP errorBound = (bodyAngle * bodyAngle * positionRadius +
                        default(QuantumSimulationMath).FromInt64(2) * bodyAngle * displacement +
                        default(QuantumSimulationMath).FromInt64(2) * sumAngle * sumAngle * part.Radius) / default(QuantumSimulationMath).FromInt64(8);
                    int count = FPAttackSweepMath.Subdivisions(errorBound, attack.InterpolationError);
                    m_Intervals[intervalCount++] = new Interval
                    {
                        Part = part, RootStart = rootStart, RootEnd = rootEnd,
                        LocalStart = localStart, LocalEnd = localEnd,
                        StartTime = tickStart + tickDuration * startFraction,
                        EndTime = tickStart + tickDuration * endFraction, Count = count
                    };
                    segmentCount += count;
                    start = end;
                    key = endKey;
                    startFraction = endFraction;
                    rootStart = rootEnd;
                    localStart = localEnd;
                } while (start < current);
            }
            if (segmentCount > attack.SegmentCapacity)
                throw new InvalidOperationException("攻击轨迹超出声明的连续段容量：" + attack.ContentId);
            int written = 0;
            for (int intervalIndex = 0; intervalIndex < intervalCount; intervalIndex++)
            {
                ref readonly Interval interval = ref m_Intervals[intervalIndex];
                var track = new SweepTrack(((ulong)attack.Index + 1) << 32 | (ulong)interval.Part.ShapeIndex + 1,
                    interval.Part.ShapeIndex, SweepMotionModel.RigidRotation, interval.Part.Data.Id);
                SweepPose<FP> previousPose = FPAttackSweepMath.Compose(interval.RootStart, interval.LocalStart);
                FP previousTime = interval.StartTime;
                for (int segment = 1; segment <= interval.Count; segment++)
                {
                    FP fraction = default(QuantumSimulationMath).FromInt64(segment) / default(QuantumSimulationMath).FromInt64(interval.Count);
                    SweepPose<FP> pose = FPAttackSweepMath.Compose(
                        FPAttackSweepMath.Lerp(interval.RootStart, interval.RootEnd, fraction),
                        FPAttackSweepMath.Lerp(interval.LocalStart, interval.LocalEnd, fraction));
                    FP time = interval.StartTime + (interval.EndTime - interval.StartTime) * fraction;
                    destination[written++] = new SweepSegment<FP>(track,
                        new SweepSample<FP>(previousTime, previousPose), new SweepSample<FP>(time, pose));
                    previousPose = pose;
                    previousTime = time;
                }
            }
            return written;
        }
    }
}
