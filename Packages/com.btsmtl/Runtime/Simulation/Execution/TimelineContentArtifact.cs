using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using BTSMTL.Timeline;
using ThirdPersonSimulation.Fixed;

namespace ThirdPersonSimulation
{
    public sealed class TimelineContentArtifact
    {
        public const string CodecIdentity = "timeline-content-artifact/v6";
        readonly ReadOnlyCollection<TimelineContentUnit> m_Timelines;

        public TimelineContentArtifact(IEnumerable<TimelineContentUnit> timelines)
        {
            var values = new List<TimelineContentUnit>(timelines);
            values.Sort((left, right) => string.CompareOrdinal(left.Identity, right.Identity));
            for (int i = 1; i < values.Count; i++)
                if (values[i - 1].Identity == values[i].Identity)
                    throw new ArgumentException("Timeline artifact contains duplicate content identity.", nameof(timelines));
            m_Timelines = values.AsReadOnly();
            using var writer = new CanonicalWriter();
            writer.WriteText(CodecIdentity);
            writer.WriteInt32(values.Count);
            for (int i = 0; i < values.Count; i++)
                TimelineContentArtifactCodec.WriteTimeline(writer, values[i]);
            ContentHash = SimulationCanonicalPayloadHash.Compute(writer.WrittenSpan);
        }

        public IReadOnlyList<TimelineContentUnit> Timelines => m_Timelines;
        public StableHash ContentHash { get; }

        public TimelineContentUnit Require(CanonicalTextValue identity)
        {
            for (int i = 0; i < m_Timelines.Count; i++)
                if (identity.Equals(m_Timelines[i].Identity))
                    return m_Timelines[i];
            throw new KeyNotFoundException($"Timeline '{identity}' is absent from the published artifact.");
        }
    }

    public static class TimelineContentArtifactCodec
    {
        const uint Magic = 0x4c544150;
        const int Version = 6;

        public static byte[] Write(TimelineContentArtifact artifact)
        {
            using var writer = new CanonicalWriter();
            writer.WriteUInt32(Magic);
            writer.WriteInt32(Version);
            writer.WriteText(TimelineContentArtifact.CodecIdentity);
            writer.WriteInt32(artifact.Timelines.Count);
            for (int i = 0; i < artifact.Timelines.Count; i++)
                WriteTimeline(writer, artifact.Timelines[i]);
            writer.WriteHash(artifact.ContentHash);
            return writer.ToArray();
        }

        public static TimelineContentArtifact Read(byte[] bytes)
        {
            var reader = new CanonicalReader(bytes);
            if (reader.ReadUInt32() != Magic || reader.ReadInt32() != Version || Text(reader) != TimelineContentArtifact.CodecIdentity)
                throw new InvalidDataException("Timeline content artifact version does not match this runtime.");
            var contents = new TimelineContentUnit[Count(reader)];
            for (int i = 0; i < contents.Length; i++)
                contents[i] = ReadTimeline(reader);
            StableHash expected = reader.ReadHash();
            reader.RequireComplete();
            var artifact = new TimelineContentArtifact(contents);
            if (!artifact.ContentHash.Equals(expected))
                throw new InvalidDataException("Timeline content artifact hash is invalid.");
            if (!Write(artifact).AsSpan().SequenceEqual(bytes))
                throw new InvalidDataException("Timeline content artifact is not canonical.");
            return artifact;
        }

        internal static void WriteTimeline(CanonicalWriter writer, TimelineContentUnit content)
        {
            writer.WriteText(content.Identity);
            writer.WriteText(content.AuthoringId);
            writer.WriteText(content.Revision);
            writer.WriteText(content.SourceRevision);
            writer.WriteText(content.Name);
            writer.WriteInt64(content.Duration.Raw);
            writer.WriteBoolean(content.Loop);
            writer.WriteInt32(content.Tracks.Count);
            for (int i = 0; i < content.Tracks.Count; i++)
            {
                TimelineContentTrack track = content.Tracks[i];
                writer.WriteText(track.AuthoringId);
                writer.WriteText(track.ContractKind);
                writer.WriteByte((byte)track.ExecutionPolicy.Domain);
                writer.WriteInt32(track.Order);
                writer.WriteBoolean(track.Muted);
                writer.WriteText(track.Name);
                writer.WriteInt32(track.ClipAuthoringIds.Count);
                for (int j = 0; j < track.ClipAuthoringIds.Count; j++)
                    writer.WriteText(track.ClipAuthoringIds[j]);
            }
            writer.WriteInt32(content.Clips.Count);
            for (int i = 0; i < content.Clips.Count; i++)
            {
                TimelineContentClip clip = content.Clips[i];
                writer.WriteText(clip.AuthoringId);
                writer.WriteText(clip.ContractKind);
                writer.WriteText(clip.TrackAuthoringId);
                writer.WriteByte((byte)clip.ExecutionPolicy.Domain);
                clip.TimeRange.Write(writer);
                writer.WriteByte((byte)clip.ExecutionPhase);
                writer.WriteByte((byte)clip.ExitSource);
                writer.WriteInt32((int)clip.Capabilities);
                writer.WriteBoolean(clip.SupportsStop);
                writer.WriteBoolean(clip.TrackMuted);
                writer.WriteText(clip.GraphBinding.GraphId);
                writer.WriteText(clip.GraphBinding.Revision);
                writer.WriteInt32(clip.Bindings.Count);
                for (int j = 0; j < clip.Bindings.Count; j++)
                {
                    TimelineContentBindingUse use = clip.Bindings[j];
                    writer.WriteText(use.BindingId);
                    writer.WriteByte((byte)use.ValueKind);
                    writer.WriteByte((byte)use.Access);
                    writer.WriteByte((byte)use.Lifetime);
                    writer.WriteText(use.TargetBindingId);
                }
                writer.WriteInt32(clip.Curves.Count);
                for (int j = 0; j < clip.Curves.Count; j++)
                    WriteCurve(writer, clip.Curves[j]);
                WriteParameters(writer, clip.Parameters);
            }
            writer.WriteInt32(content.Markers.Count);
            for (int i = 0; i < content.Markers.Count; i++)
            {
                TimelineContentMarker marker = content.Markers[i];
                writer.WriteText(marker.MarkerId);
                writer.WriteText(marker.TrackAuthoringId);
                writer.WriteByte((byte)marker.ExecutionPolicy.Domain);
                writer.WriteInt64(marker.Time.Raw);
                writer.WriteBoolean(marker.ForceTriggerOnInitialSeek);
                writer.WriteText(marker.GraphId);
                writer.WriteText(marker.GraphRevision);
                writer.WriteBoolean(marker.TrackMuted);
            }
            writer.WriteInt32(content.Sections.Count);
            for (int i = 0; i < content.Sections.Count; i++)
            {
                TimelineContentSection section = content.Sections[i];
                writer.WriteText(section.AuthoringId);
                writer.WriteInt64(section.Time.Raw);
                writer.WriteText(section.NextSectionId);
                writer.WriteText(section.BranchId);
            }
            writer.WriteInt32(content.Bindings.Count);
            for (int i = 0; i < content.Bindings.Count; i++)
            {
                TimelineBindingDeclaration binding = content.Bindings[i];
                writer.WriteText(binding.BindingId);
                writer.WriteText(binding.Domain);
                writer.WriteText(binding.ParameterId);
                writer.WriteByte((byte)binding.ValueKind);
                writer.WriteByte((byte)binding.Access);
                writer.WriteByte((byte)binding.Lifetime);
            }
            writer.WriteInt32(content.Dependencies.Count);
            for (int i = 0; i < content.Dependencies.Count; i++)
            {
                TimelineContentDependency dependency = content.Dependencies[i];
                writer.WriteText(dependency.Identity);
                writer.WriteText(dependency.Kind);
                writer.WriteText(dependency.SourcePath);
                writer.WriteText(dependency.ContentHash);
            }
        }

        static TimelineContentUnit ReadTimeline(CanonicalReader reader)
        {
            string identity = Text(reader), authoringId = Text(reader), revision = Text(reader), sourceRevision = Text(reader), name = Text(reader);
            FixedScalar duration = Scalar(reader);
            bool loop = reader.ReadBoolean();
            var tracks = new TimelineContentTrack[Count(reader)];
            for (int i = 0; i < tracks.Length; i++)
            {
                string trackId = Text(reader), kind = Text(reader);
                var policy = TimelineClipExecutionPolicy.FromDomain((TimelineExecutionDomain)reader.ReadByte());
                int order = reader.ReadInt32();
                bool muted = reader.ReadBoolean();
                string trackName = Text(reader);
                var clipIds = new string[Count(reader)];
                for (int j = 0; j < clipIds.Length; j++)
                    clipIds[j] = Text(reader);
                tracks[i] = new TimelineContentTrack(trackId, kind, policy, order, muted, clipIds, trackName);
            }
            var clips = new TimelineContentClip[Count(reader)];
            for (int i = 0; i < clips.Length; i++)
            {
                string clipId = Text(reader), kind = Text(reader), trackId = Text(reader);
                var policy = TimelineClipExecutionPolicy.FromDomain((TimelineExecutionDomain)reader.ReadByte());
                TimelineClipTimeRange range = TimelineClipTimeRange.Read(reader);
                var phase = (TimelineClipExecutionPhase)reader.ReadByte();
                var exit = (TimelineClipExitSource)reader.ReadByte();
                var capability = (TimelineCapability)reader.ReadInt32();
                bool stop = reader.ReadBoolean(), muted = reader.ReadBoolean();
                var graph = new TimelineGraphBinding(Text(reader), Text(reader));
                var uses = new TimelineContentBindingUse[Count(reader)];
                for (int j = 0; j < uses.Length; j++)
                    uses[j] = new TimelineContentBindingUse(Text(reader), (TimelineBindingValueKind)reader.ReadByte(),
                        (TimelineBindingAccess)reader.ReadByte(), (TimelineBindingLifetime)reader.ReadByte(), Text(reader));
                var curves = new TimelineContentCurve[Count(reader)];
                for (int j = 0; j < curves.Length; j++)
                    curves[j] = ReadCurve(reader);
                clips[i] = new TimelineContentClip(clipId, kind, trackId, policy, range, phase, exit,
                    capability, stop, muted, ReadParameters(reader), uses, curves, graph);
            }
            var markers = new TimelineContentMarker[Count(reader)];
            for (int i = 0; i < markers.Length; i++)
                markers[i] = new TimelineContentMarker(Text(reader), Text(reader),
                    TimelineClipExecutionPolicy.FromDomain((TimelineExecutionDomain)reader.ReadByte()), Scalar(reader),
                    reader.ReadBoolean(), Text(reader), Text(reader), reader.ReadBoolean());
            var sections = new TimelineContentSection[Count(reader)];
            for (int i = 0; i < sections.Length; i++)
                sections[i] = new TimelineContentSection(Text(reader), Scalar(reader), Text(reader), Text(reader));
            var bindings = new TimelineBindingDeclaration[Count(reader)];
            for (int i = 0; i < bindings.Length; i++)
                bindings[i] = new TimelineBindingDeclaration(Text(reader), Text(reader), Text(reader),
                    (TimelineBindingValueKind)reader.ReadByte(), (TimelineBindingAccess)reader.ReadByte(), (TimelineBindingLifetime)reader.ReadByte());
            var dependencies = new TimelineContentDependency[Count(reader)];
            for (int i = 0; i < dependencies.Length; i++)
                dependencies[i] = new TimelineContentDependency(Text(reader), Text(reader), Text(reader), Text(reader));
            return new TimelineContentUnit(identity, authoringId, revision, sourceRevision, name, duration, loop,
                tracks, clips, markers, sections, bindings, dependencies);
        }

        static void WriteCurve(CanonicalWriter writer, TimelineContentCurve curve)
        {
            writer.WriteText(curve.ChannelId);
            writer.WriteText(curve.DisplayName);
            writer.WriteByte((byte)curve.TimeDomain);
            writer.WriteBoolean(curve.ValueDomain.IsBounded);
            WriteSingle(writer, curve.ValueDomain.Minimum);
            WriteSingle(writer, curve.ValueDomain.Maximum);
            WriteSingle(writer, curve.ValueDomain.Zero);
            writer.WriteText(curve.ValueDomain.Unit);
            writer.WriteInt32(curve.PreWrapMode);
            writer.WriteInt32(curve.PostWrapMode);
            writer.WriteInt32(curve.Keys.Count);
            for (int i = 0; i < curve.Keys.Count; i++)
            {
                TimelineContentCurveKey key = curve.Keys[i];
                WriteSingle(writer, key.Time);
                WriteSingle(writer, key.Value);
                WriteSingle(writer, key.InTangent);
                WriteSingle(writer, key.OutTangent);
                WriteSingle(writer, key.InWeight);
                WriteSingle(writer, key.OutWeight);
                writer.WriteInt32(key.WeightedMode);
            }
        }

        static TimelineContentCurve ReadCurve(CanonicalReader reader)
        {
            string channel = Text(reader), display = Text(reader);
            var domain = (TimelineCurveTimeDomain)reader.ReadByte();
            bool bounded = reader.ReadBoolean();
            float minimum = Single(reader), maximum = Single(reader), zero = Single(reader);
            string unit = Text(reader);
            TimelineCurveValueDomain values = bounded ? TimelineCurveValueDomain.Bounded(minimum, maximum, unit)
                : TimelineCurveValueDomain.Unbounded(zero, unit);
            int pre = reader.ReadInt32(), post = reader.ReadInt32();
            var keys = new TimelineContentCurveKey[Count(reader)];
            for (int i = 0; i < keys.Length; i++)
                keys[i] = new TimelineContentCurveKey(Single(reader), Single(reader), Single(reader),
                    Single(reader), Single(reader), Single(reader), reader.ReadInt32());
            return new TimelineContentCurve(channel, display, domain, values, pre, post, keys);
        }

        static void WriteParameters(CanonicalWriter writer, TimelineContentParameters parameters)
        {
            switch (parameters)
            {
                case TimelineContentAnimation animation:
                    writer.WriteByte(1);
                    writer.WriteText(animation.ResourceId);
                    writer.WriteText(animation.ChannelId.Value);
                    writer.WriteText(animation.SlotId);
                    writer.WriteText(animation.BlendProfileId);
                    writer.WriteByte((byte)animation.Extrapolation);
                    break;
                case TimelineContentMotion motion:
                    writer.WriteByte(2);
                    writer.WriteText(motion.ResourceId);
                    writer.WriteText(motion.CurveId);
                    writer.WriteByte((byte)motion.Space);
                    writer.WriteByte((byte)motion.Channel);
                    writer.WriteByte((byte)motion.BlendMode);
                    writer.WriteInt32(motion.Priority);
                    writer.WriteBoolean(motion.ConsumeLowerChannels);
                    WriteCurve(writer, motion.X);
                    WriteCurve(writer, motion.Y);
                    WriteCurve(writer, motion.Z);
                    WriteCurve(writer, motion.Yaw);
                    break;
                case TimelineContentTree tree:
                    writer.WriteByte(3);
                    writer.WriteBoolean(tree.HasDeclaredEndTime);
                    break;
                case TimelineContentCameraState state:
                    writer.WriteByte(4);
                    writer.WriteByte((byte)state.Mode);
                    writer.WriteText(state.SequenceId);
                    writer.WriteInt32(state.Priority);
                    WriteSingle(writer, state.BlendInSeconds);
                    WriteSingle(writer, state.BlendOutSeconds);
                    writer.WriteText(state.TargetKey);
                    writer.WriteByte((byte)state.InterruptPolicy);
                    break;
                case TimelineContentCameraResponse response:
                    writer.WriteByte(5);
                    writer.WriteByte((byte)response.LookResponse);
                    WriteSingle(writer, response.ManualOrbitWeight);
                    WriteSingle(writer, response.PitchResponseWeight);
                    WriteSingle(writer, response.YawResponseWeight);
                    writer.WriteInt32(response.Priority);
                    break;
                case TimelineContentCameraEffect effect:
                    writer.WriteByte(6);
                    writer.WriteByte((byte)effect.Kind);
                    writer.WriteText(effect.ResourceId);
                    writer.WriteInt32(effect.Priority);
                    WriteSingle(writer, effect.SourceStartTime);
                    WriteSingle(writer, effect.SourceEndTime);
                    writer.WriteText(effect.ResourceRevision);
                    break;
                case TimelineContentSceneParameter scene:
                    writer.WriteByte(7);
                    writer.WriteText(scene.TargetBindingId);
                    writer.WriteText(scene.ParameterBindingId);
                    writer.WriteByte((byte)scene.ValueKind);
                    break;
                default:
                    throw new InvalidDataException("Timeline content parameter contract is not registered.");
            }
        }

        static TimelineContentParameters ReadParameters(CanonicalReader reader) => reader.ReadByte() switch
        {
            1 => new TimelineContentAnimation(Text(reader), new AnimationChannelId(Text(reader)), Text(reader),
                Text(reader), (ExtraPolationMode)reader.ReadByte()),
            2 => new TimelineContentMotion(Text(reader), Text(reader), (TimelineMotionContributionSpace)reader.ReadByte(),
                (TimelineMotionChannel)reader.ReadByte(), (TimelineMotionBlendMode)reader.ReadByte(), reader.ReadInt32(),
                reader.ReadBoolean(), ReadCurve(reader), ReadCurve(reader), ReadCurve(reader), ReadCurve(reader)),
            3 => new TimelineContentTree(reader.ReadBoolean()),
            4 => new TimelineContentCameraState((TimelineCameraMode)reader.ReadByte(), Text(reader), reader.ReadInt32(),
                Single(reader), Single(reader), Text(reader), (TimelineCameraInterruptPolicy)reader.ReadByte()),
            5 => new TimelineContentCameraResponse((TimelineCameraLookResponseMode)reader.ReadByte(), Single(reader),
                Single(reader), Single(reader), reader.ReadInt32()),
            6 => new TimelineContentCameraEffect((TimelineCameraResourceKind)reader.ReadByte(), Text(reader), reader.ReadInt32(),
                Single(reader), Single(reader), Text(reader)),
            7 => new TimelineContentSceneParameter(Text(reader), Text(reader), (TimelineBindingValueKind)reader.ReadByte()),
            _ => throw new InvalidDataException("Timeline content parameter type is invalid.")
        };

        static string Text(CanonicalReader reader) => CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
        static FixedScalar Scalar(CanonicalReader reader) => FixedScalar.FromRaw(reader.ReadInt64());
        static float Single(CanonicalReader reader) => BitConverter.Int32BitsToSingle(reader.ReadInt32());
        static void WriteSingle(CanonicalWriter writer, float value) => writer.WriteInt32(BitConverter.SingleToInt32Bits(value));
        static int Count(CanonicalReader reader)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > 65536)
                throw new InvalidDataException("Timeline content record count is outside the codec limits.");
            return count;
        }
    }
}
