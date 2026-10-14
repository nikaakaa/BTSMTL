using System;
using BEPUutilities;
using FixMath.NET;
using ThirdPersonSimulation.Collision;

namespace ThirdPersonSimulation.Fixed
{
    internal sealed class FixedPreparedAttackPart
    {
        public FixedPreparedAttackPart(int shapeIndex, AttackSweepPartData<FixedScalar> data)
        {
            ShapeIndex = shapeIndex;
            Data = data;
            switch (data.Kind)
            {
                case SweepShapeKind.Box:
                    Radius = FixedAttackSweepMath.Length(data.HalfExtents);
                    break;
                case SweepShapeKind.Sphere:
                    Radius = data.Radius;
                    break;
                case SweepShapeKind.Capsule:
                    Radius = data.Radius + data.HalfLength;
                    break;
                case SweepShapeKind.ConvexHull:
                    for (int i = 0; i < data.Vertices.Length; i++)
                        Radius = FixedScalar.Max(Radius, FixedAttackSweepMath.Length(data.Vertices[i]));
                    break;
            }
            for (int i = 0; i < data.Samples.Length; i++)
            {
                var key = data.Samples[i];
                data.Samples[i] = new SweepSample<FixedScalar>(key.Time,
                    new SweepPose<FixedScalar>(key.Pose.Position, FixedAttackSweepMath.Normalize(key.Pose.Orientation)));
            }
        }

        public int ShapeIndex { get; }
        public AttackSweepPartData<FixedScalar> Data { get; }
        public FixedScalar Radius { get; }

        public int KeyAt(FixedScalar time)
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

        public SweepPose<FixedScalar> Sample(FixedScalar time) => Sample(time, KeyAt(time));

        public SweepPose<FixedScalar> Sample(FixedScalar time, int key)
        {
            var start = Data.Samples[key];
            if (key == Data.Samples.Length - 1 || time <= start.Time)
                return start.Pose;
            var end = Data.Samples[key + 1];
            return FixedAttackSweepMath.Lerp(start.Pose, end.Pose, (time - start.Time) / (end.Time - start.Time));
        }
    }

    internal static class FixedAttackSweepMath
    {
        static Fix64 Native(FixedScalar value) => Fix64.FromRaw(value.Raw);
        static FixedScalar Scalar(Fix64 value) => FixedScalar.FromRaw(value.RawValue);
        static Vector3 Native(SweepVector3<FixedScalar> value) => new Vector3(Native(value.X), Native(value.Y), Native(value.Z));
        static Quaternion Native(SweepQuaternion<FixedScalar> value) => new Quaternion(Native(value.X), Native(value.Y), Native(value.Z), Native(value.W));
        static SweepVector3<FixedScalar> Vector(Vector3 value) => new SweepVector3<FixedScalar>(Scalar(value.X), Scalar(value.Y), Scalar(value.Z));
        static SweepQuaternion<FixedScalar> Rotation(Quaternion value) => new SweepQuaternion<FixedScalar>(Scalar(value.X), Scalar(value.Y), Scalar(value.Z), Scalar(value.W));

        public static FixedScalar Length(SweepVector3<FixedScalar> value) => Scalar(Native(value).Length());
        public static SweepQuaternion<FixedScalar> Normalize(SweepQuaternion<FixedScalar> value) => Rotation(Quaternion.Normalize(Native(value)));

        public static SweepPose<FixedScalar> Lerp(SweepPose<FixedScalar> a, SweepPose<FixedScalar> b, FixedScalar fraction)
        {
            Vector3 start = Native(a.Position), end = Native(b.Position);
            Vector3 position = start + (end - start) * Native(fraction);
            Quaternion rotation = Quaternion.Slerp(Native(a.Orientation), Native(b.Orientation), Native(fraction));
            return new SweepPose<FixedScalar>(Vector(position), Rotation(rotation));
        }

        public static SweepPose<FixedScalar> Compose(SweepPose<FixedScalar> parent, SweepPose<FixedScalar> local)
        {
            Quaternion root = Native(parent.Orientation), child = Native(local.Orientation);
            Vector3 offset = Native(local.Position);
            Quaternion.Transform(ref offset, ref root, out Vector3 worldOffset);
            Quaternion.Concatenate(ref child, ref root, out Quaternion orientation);
            return new SweepPose<FixedScalar>(Vector(Native(parent.Position) + worldOffset), Rotation(orientation));
        }

        public static FixedScalar Angle(SweepQuaternion<FixedScalar> a, SweepQuaternion<FixedScalar> b)
        {
            Quaternion start = Native(a), end = Native(b);
            Fix64 dot = Fix64.Abs(start.X * end.X + start.Y * end.Y + start.Z * end.Z + start.W * end.W);
            return Scalar(Fix64.Acos(dot > Fix64.One ? Fix64.One : dot) * 2);
        }

        public static FixedScalar Distance(SweepVector3<FixedScalar> a, SweepVector3<FixedScalar> b) =>
            Scalar((Native(a) - Native(b)).Length());

        public static int Subdivisions(FixedScalar bound, FixedScalar error) =>
            Math.Max(1, (int)Fix64.Ceiling(Fix64.Sqrt(Native(bound / error))));
    }

    internal sealed class FixedAttackSweepSampling
    {
        struct Interval
        {
            public FixedPreparedAttackPart Part;
            public SweepPose<FixedScalar> RootStart;
            public SweepPose<FixedScalar> RootEnd;
            public SweepPose<FixedScalar> LocalStart;
            public SweepPose<FixedScalar> LocalEnd;
            public FixedScalar StartTime;
            public FixedScalar EndTime;
            public int Count;
        }

        readonly Interval[] m_Intervals;

        public FixedAttackSweepSampling(int intervalCapacity) => m_Intervals = new Interval[intervalCapacity];

        public int Write(FixedPreparedAttack attack, FixedCombatQuery request,
            SweepPose<FixedScalar> bodyStart, SweepPose<FixedScalar> bodyEnd,
            FixedScalar tickStart, FixedScalar tickDuration, SweepSegment<FixedScalar>[] destination)
        {
            int intervalCount = 0, segmentCount = 0;
            FixedScalar previous = request.PreviousTime - request.WindowStartTime;
            FixedScalar current = request.Time - request.WindowStartTime;
            FixedScalar cursorSpan = current - previous;
            for (int partIndex = 0; partIndex < attack.Parts.Length; partIndex++)
            {
                FixedPreparedAttackPart part = attack.Parts[partIndex];
                FixedScalar start = previous;
                int key = part.KeyAt(start);
                FixedScalar a = cursorSpan == FixedScalar.Zero ? FixedScalar.Zero : (start - previous) / cursorSpan;
                FixedScalar startFraction = request.BodyStartFraction + (request.BodyEndFraction - request.BodyStartFraction) * a;
                SweepPose<FixedScalar> rootStart = FixedAttackSweepMath.Lerp(bodyStart, bodyEnd, startFraction);
                SweepPose<FixedScalar> localStart = part.Sample(start, key);
                do
                {
                    FixedScalar end = start < part.Data.Samples[0].Time
                        ? FixedScalar.Min(current, part.Data.Samples[0].Time)
                        : key + 1 < part.Data.Samples.Length
                            ? FixedScalar.Min(current, part.Data.Samples[key + 1].Time) : current;
                    FixedScalar b = cursorSpan == FixedScalar.Zero ? FixedScalar.One : (end - previous) / cursorSpan;
                    FixedScalar endFraction = request.BodyStartFraction + (request.BodyEndFraction - request.BodyStartFraction) * b;
                    SweepPose<FixedScalar> rootEnd = FixedAttackSweepMath.Lerp(bodyStart, bodyEnd, endFraction);
                    int endKey = part.KeyAt(end);
                    SweepPose<FixedScalar> localEnd = part.Sample(end, endKey);
                    FixedScalar bodyAngle = FixedAttackSweepMath.Angle(rootStart.Orientation, rootEnd.Orientation);
                    FixedScalar localAngle = FixedAttackSweepMath.Angle(localStart.Orientation, localEnd.Orientation);
                    FixedScalar positionRadius = FixedScalar.Max(FixedAttackSweepMath.Length(localStart.Position), FixedAttackSweepMath.Length(localEnd.Position));
                    FixedScalar displacement = FixedAttackSweepMath.Distance(localStart.Position, localEnd.Position);
                    FixedScalar sumAngle = bodyAngle + localAngle;
                    // 合成运动与段内直线/Slerp 的二阶位移上界决定细分数。
                    FixedScalar errorBound = (bodyAngle * bodyAngle * positionRadius +
                        FixedScalar.FromInt64(2) * bodyAngle * displacement +
                        FixedScalar.FromInt64(2) * sumAngle * sumAngle * part.Radius) / FixedScalar.FromInt64(8);
                    int count = FixedAttackSweepMath.Subdivisions(errorBound, attack.InterpolationError);
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
                SweepPose<FixedScalar> previousPose = FixedAttackSweepMath.Compose(interval.RootStart, interval.LocalStart);
                FixedScalar previousTime = interval.StartTime;
                for (int segment = 1; segment <= interval.Count; segment++)
                {
                    FixedScalar fraction = FixedScalar.FromInt64(segment) / FixedScalar.FromInt64(interval.Count);
                    SweepPose<FixedScalar> pose = FixedAttackSweepMath.Compose(
                        FixedAttackSweepMath.Lerp(interval.RootStart, interval.RootEnd, fraction),
                        FixedAttackSweepMath.Lerp(interval.LocalStart, interval.LocalEnd, fraction));
                    FixedScalar time = interval.StartTime + (interval.EndTime - interval.StartTime) * fraction;
                    destination[written++] = new SweepSegment<FixedScalar>(track,
                        new SweepSample<FixedScalar>(previousTime, previousPose), new SweepSample<FixedScalar>(time, pose));
                    previousPose = pose;
                    previousTime = time;
                }
            }
            return written;
        }
    }
}
