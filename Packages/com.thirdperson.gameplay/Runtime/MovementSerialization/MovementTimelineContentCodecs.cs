using System;
using System.IO;
using BTSMTL.Timeline;

namespace ThirdPersonSimulation.Serialization
{
    public static class MovementTimelineContentCodecs
    {
        const byte PublishedCurvesVersion = 1;

        static MovementTimelineContentCodecs()
        {
            MemoryPackContent.Initialize();
            MemoryPackGeneratedFormatters.Register();
            Motion = new TimelineContentParameterCodec<TimelineContentMotion>(TimelineContentMotion.Kind,
                value => MemoryPackContent.Serialize(value),
                bytes => MemoryPackContent.Deserialize<TimelineContentMotion>(bytes));
        }

        public static ITimelineContentParameterCodec Motion { get; }

        public static byte[] WriteCurves<TScalar>(GameplayAbilityCurve<TScalar>[] curves)
            where TScalar : struct, IComparable<TScalar>, IEquatable<TScalar>
        {
            byte[] payload = MemoryPackContent.Serialize(curves);
            var bytes = new byte[payload.Length + 1];
            bytes[0] = PublishedCurvesVersion;
            payload.CopyTo(bytes, 1);
            return bytes;
        }

        public static GameplayAbilityCurve<TScalar>[] ReadCurves<TScalar>(ReadOnlySpan<byte> bytes, int clipCount)
            where TScalar : struct, IComparable<TScalar>, IEquatable<TScalar>
        {
            if (bytes.Length == 0 || bytes[0] != PublishedCurvesVersion)
                throw new InvalidDataException("Timeline 运动曲线产物版本无效，请按正式发布入口重新生成。");
            GameplayAbilityCurve<TScalar>[] curves =
                MemoryPackContent.Deserialize<GameplayAbilityCurve<TScalar>[]>(bytes.Slice(1));
            if (curves.Length != checked(clipCount * 4))
                throw new InvalidDataException("Timeline 运动曲线产物必须按片段顺序保存四条正式曲线。");
            return curves;
        }
    }
}
