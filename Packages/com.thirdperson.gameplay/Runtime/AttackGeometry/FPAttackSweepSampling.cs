using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using System;
using ThirdPersonSimulation.Collision;

namespace ThirdPersonSimulation.AttackGeometry
{
    public sealed class FPPreparedAttackPart
    {
        public FPPreparedAttackPart(int shapeIndex, AttackSweepPartData data)
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
                data.Samples[i] = new SweepSample(key.Time,
                    new SweepPose(key.Pose.Position, FPAttackSweepMath.Normalize(key.Pose.Orientation)));
            }
        }

        public int ShapeIndex { get; }
        public AttackSweepPartData Data { get; }
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

        public SweepPose Sample(FP time) => Sample(time, KeyAt(time));

        public SweepPose Sample(FP time, int key)
        {
            var start = Data.Samples[key];
            if (key == Data.Samples.Length - 1 || time <= start.Time)
                return start.Pose;
            var end = Data.Samples[key + 1];
            return FPAttackSweepMath.Lerp(start.Pose, end.Pose, (time - start.Time) / (end.Time - start.Time));
        }
    }

    public static class FPAttackSweepMath
    {
        public static FP Length(FPVector3 value) => value.Magnitude;
        public static FPQuaternion Normalize(FPQuaternion value) => FPQuaternion.Normalize(value);

        public static SweepPose Lerp(SweepPose a, SweepPose b, FP fraction)
        {
            FPVector3 position = a.Position + (b.Position - a.Position) * fraction;
            FPQuaternion rotation = FPQuaternion.SlerpUnclamped(a.Orientation, b.Orientation, fraction);
            return new SweepPose(position, rotation);
        }

        public static SweepPose Compose(SweepPose parent, SweepPose local)
        {
            FPVector3 worldOffset = parent.Orientation * local.Position;
            return new SweepPose(parent.Position + worldOffset, parent.Orientation * local.Orientation);
        }

        public static FP Angle(FPQuaternion a, FPQuaternion b)
        {
            FP dot = FPMath.Abs(FPQuaternion.Dot(a, b));
            return FPMath.Acos(FPMath.Min(dot, FP._1)) * FP._2;
        }

        public static FP Distance(FPVector3 a, FPVector3 b) => (a - b).Magnitude;

        public static int Subdivisions(FP bound, FP error) =>
            Math.Max(1, FPMath.CeilToInt(FPMath.Sqrt(bound / error)));

        public static FP InterpolationBound(FP bodyAngle, FP localAngle, FP positionRadius,
            FP displacement, FP shapeRadius)
        {
            FP sumAngle = bodyAngle + localAngle;
            return (bodyAngle * bodyAngle * positionRadius +
                default(QuantumSimulationMath).FromInt64(2) * bodyAngle * displacement +
                default(QuantumSimulationMath).FromInt64(2) * sumAngle * sumAngle * shapeRadius) /
                default(QuantumSimulationMath).FromInt64(8);
        }
    }

    public readonly struct FPAttackSweepCapacity
    {
        public FPAttackSweepCapacity(int intervalCount, int segmentCount)
        {
            IntervalCount = intervalCount;
            SegmentCount = segmentCount;
        }

        public int IntervalCount { get; }
        public int SegmentCount { get; }
    }

    public sealed class FPAttackSweepSampling
    {
        struct Interval
        {
            public FPPreparedAttackPart Part;
            public SweepPose RootStart;
            public SweepPose RootEnd;
            public SweepPose LocalStart;
            public SweepPose LocalEnd;
            public FP StartTime;
            public FP EndTime;
            public int Count;
        }

        readonly Interval[] m_Intervals;

        public FPAttackSweepSampling(int intervalCapacity) => m_Intervals = new Interval[intervalCapacity];

        public static FPAttackSweepCapacity PrepareCapacity(ReadOnlySpan<FPPreparedAttackPart> parts,
            FP interpolationError)
        {
            int intervalCount = 0;
            int segmentCount = 0;
            FP maximumAngle = FPMath.Acos(FP._0) * FP._2;
            foreach (FPPreparedAttackPart part in parts)
            {
                FPVector3 maximumPosition = default;
                ReadOnlySpan<SweepSample> samples = part.Data.Samples;
                FPVector3 minimum = samples[0].Pose.Position;
                FPVector3 maximum = minimum;
                for (int index = 0; index < samples.Length; index++)
                {
                    FPVector3 position = samples[index].Pose.Position;
                    maximumPosition.X = FPMath.Max(maximumPosition.X, FPMath.Abs(position.X));
                    maximumPosition.Y = FPMath.Max(maximumPosition.Y, FPMath.Abs(position.Y));
                    maximumPosition.Z = FPMath.Max(maximumPosition.Z, FPMath.Abs(position.Z));
                    minimum.X = FPMath.Min(minimum.X, position.X);
                    minimum.Y = FPMath.Min(minimum.Y, position.Y);
                    minimum.Z = FPMath.Min(minimum.Z, position.Z);
                    maximum.X = FPMath.Max(maximum.X, position.X);
                    maximum.Y = FPMath.Max(maximum.Y, position.Y);
                    maximum.Z = FPMath.Max(maximum.Z, position.Z);
                }
                FP bound = FPAttackSweepMath.InterpolationBound(maximumAngle, maximumAngle,
                    FPAttackSweepMath.Length(maximumPosition), FPAttackSweepMath.Length(maximum - minimum), part.Radius);
                int partIntervals = checked(samples.Length + 1);
                intervalCount = checked(intervalCount + partIntervals);
                segmentCount = checked(segmentCount + checked(partIntervals *
                    FPAttackSweepMath.Subdivisions(bound, interpolationError)));
            }
            return new FPAttackSweepCapacity(intervalCount, segmentCount);
        }

        public bool TryWrite(ReadOnlySpan<FPPreparedAttackPart> parts, int attackIndex,
            int segmentCapacity, FP interpolationError, FP previous, FP current,
            FP bodyStartFraction, FP bodyEndFraction, SweepPose bodyStart, SweepPose bodyEnd,
            FP tickStart, FP tickDuration, SweepSegment[] destination, out int written)
        {
            written = 0;
            int intervalCount = 0, segmentCount = 0;
            FP cursorSpan = current - previous;
            for (int partIndex = 0; partIndex < parts.Length; partIndex++)
            {
                FPPreparedAttackPart part = parts[partIndex];
                FP start = previous;
                int key = part.KeyAt(start);
                FP a = cursorSpan == FP._0 ? FP._0 : (start - previous) / cursorSpan;
                FP startFraction = bodyStartFraction + (bodyEndFraction - bodyStartFraction) * a;
                SweepPose rootStart = FPAttackSweepMath.Lerp(bodyStart, bodyEnd, startFraction);
                SweepPose localStart = part.Sample(start, key);
                do
                {
                    FP end = start < part.Data.Samples[0].Time
                        ? FPMath.Min(current, part.Data.Samples[0].Time)
                        : key + 1 < part.Data.Samples.Length
                            ? FPMath.Min(current, part.Data.Samples[key + 1].Time) : current;
                    FP b = cursorSpan == FP._0 ? FP._1 : (end - previous) / cursorSpan;
                    FP endFraction = bodyStartFraction + (bodyEndFraction - bodyStartFraction) * b;
                    SweepPose rootEnd = FPAttackSweepMath.Lerp(bodyStart, bodyEnd, endFraction);
                    int endKey = part.KeyAt(end);
                    SweepPose localEnd = part.Sample(end, endKey);
                    FP bodyAngle = FPAttackSweepMath.Angle(rootStart.Orientation, rootEnd.Orientation);
                    FP localAngle = FPAttackSweepMath.Angle(localStart.Orientation, localEnd.Orientation);
                    FP positionRadius = FPMath.Max(FPAttackSweepMath.Length(localStart.Position), FPAttackSweepMath.Length(localEnd.Position));
                    FP displacement = FPAttackSweepMath.Distance(localStart.Position, localEnd.Position);
                    // 合成运动与段内直线/Slerp 的二阶位移上界决定细分数。
                    FP errorBound = FPAttackSweepMath.InterpolationBound(bodyAngle, localAngle,
                        positionRadius, displacement, part.Radius);
                    int count = FPAttackSweepMath.Subdivisions(errorBound, interpolationError);
                    if (count > segmentCapacity - segmentCount)
                        return false;
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
            for (int intervalIndex = 0; intervalIndex < intervalCount; intervalIndex++)
            {
                ref readonly Interval interval = ref m_Intervals[intervalIndex];
                var track = new SweepTrack(((ulong)attackIndex + 1) << 32 | (ulong)interval.Part.ShapeIndex + 1,
                    interval.Part.ShapeIndex, SweepMotionModel.RigidRotation, interval.Part.Data.Id);
                SweepPose previousPose = FPAttackSweepMath.Compose(interval.RootStart, interval.LocalStart);
                FP previousTime = interval.StartTime;
                for (int segment = 1; segment <= interval.Count; segment++)
                {
                    FP fraction = default(QuantumSimulationMath).FromInt64(segment) / default(QuantumSimulationMath).FromInt64(interval.Count);
                    SweepPose pose = FPAttackSweepMath.Compose(
                        FPAttackSweepMath.Lerp(interval.RootStart, interval.RootEnd, fraction),
                        FPAttackSweepMath.Lerp(interval.LocalStart, interval.LocalEnd, fraction));
                    FP time = interval.StartTime + (interval.EndTime - interval.StartTime) * fraction;
                    destination[written++] = new SweepSegment(track,
                        new SweepSample(previousTime, previousPose), new SweepSample(time, pose));
                    previousPose = pose;
                    previousTime = time;
                }
            }
            return true;
        }
    }
}
