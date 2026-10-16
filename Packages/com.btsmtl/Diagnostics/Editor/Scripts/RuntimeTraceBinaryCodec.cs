using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ThirdPersonSimulation;
using UnityEngine;

namespace BTSMTL.Diagnostics.Editor
{
    sealed class RuntimeTraceBinaryCodec : IDisposable
    {
        internal const int EventSize = 1055;
        const int Version = 7;
        const int MaximumStringsPerEvent = 41;
        readonly Dictionary<string, int> m_StringIds;
        readonly List<string> m_Strings;
        readonly BinaryWriter m_Symbols;
        readonly byte[] m_Guid = new byte[16];
        readonly byte[] m_EventId = new byte[32];

        internal RuntimeTraceBinaryCodec(Stream symbols, int maximumEventsPerPage)
        {
            m_Symbols = new BinaryWriter(symbols, Encoding.UTF8, true);
            int capacity = maximumEventsPerPage * MaximumStringsPerEvent;
            m_StringIds = new Dictionary<string, int>(capacity, StringComparer.Ordinal);
            m_Strings = new List<string>(capacity);
        }

        internal int SymbolCount => m_Strings.Count;

        internal void Reset() => ClearSymbols();

        internal void WriteSourceMetadata(BinaryWriter writer, in RuntimeCaptureSpanSource source)
        {
            WriteRuntimeSourceElementHandle(writer, source.Handle);
            WriteRuntimeContentRevision(writer, source.Revision);
            writer.Write(source.EventCount);
        }

        internal RuntimeCaptureSpanSource ReadSourceMetadata(BinaryReader reader) => new RuntimeCaptureSpanSource
        {
            Handle = ReadRuntimeSourceElementHandle(reader),
            Revision = ReadRuntimeContentRevision(reader),
            EventCount = reader.ReadInt32()
        };

        internal void WriteClockMetadata(BinaryWriter writer, (RuntimeTraceDomain Domain, Guid Branch, ulong Epoch) key,
            double time)
        {
            writer.Write((int)key.Domain);
            WriteGuid(writer, key.Branch);
            writer.Write(key.Epoch);
            writer.Write(time);
        }

        internal void ReadClockMetadata(BinaryReader reader, RuntimeCaptureSpanAccumulator facts)
        {
            var domain = (RuntimeTraceDomain)reader.ReadInt32();
            Guid branch = ReadGuid(reader);
            ulong epoch = reader.ReadUInt64();
            double time = reader.ReadDouble();
            facts.ImportClock(domain, branch, epoch, time);
        }

        internal long BeginPage()
        {
            ClearSymbols();
            m_Symbols.BaseStream.Position = m_Symbols.BaseStream.Length;
            long offset = m_Symbols.BaseStream.Position;
            m_Symbols.Write(Version);
            return offset;
        }

        internal void BeginReadSymbols(BinaryReader reader, long offset)
        {
            m_StringIds.Clear();
            m_Strings.Clear();
            reader.BaseStream.Position = offset;
            if (reader.ReadInt32() != Version)
                throw new InvalidDataException("预览事件文件的格式版本无效。");
        }

        internal void ReadSymbols(BinaryReader reader, int count)
        {
            for (int i = 0; i < count; i++)
            {
                int length = reader.ReadInt32();
                if (length < 0 || (length & 1) != 0)
                    throw new InvalidDataException("预览事件文件的文本长度无效。");
                string text = string.Create(length / 2, reader, static (characters, source) =>
                {
                    for (int character = 0; character < characters.Length; character++)
                        characters[character] = (char)source.ReadUInt16();
                });
                m_Strings.Add(text);
            }
        }

        internal void Write(BinaryWriter writer, in RuntimeTraceEvent value)
        {
            RuntimeTracePayload payload = value.Payload;
            if (payload.DetailKind == RuntimeTraceDetailKind.Motion &&
                payload.MotionDetail.Kind == SimulationMotionTraceDetailKind.Channel &&
                string.IsNullOrEmpty(payload.MotionIdentityTableId))
                throw new InvalidDataException("V2 运动来源指纹必须同时保存实际冻结身份表的身份。");
            writer.Write(value.Sequence);
            writer.Write(value.TimeSeconds);
            writer.Write((int)value.Domain);
            writer.Write(value.Position);
            WriteGuid(writer, value.SessionId);
            writer.Write(value.RuntimeEpoch);
            WriteGuid(writer, value.ExecutionBranchId);
            WriteRuntimeContentRevision(writer, value.ContentRevision);
            writer.Write((int)value.Channel);
            WriteRuntimeInstanceKey(writer, value.RuntimeInstance);
            WriteRuntimeSourceElementHandle(writer, value.Source);
            writer.Write((int)value.Kind);
            WriteRuntimeTracePayload(writer, payload);
        }

        internal RuntimeTraceEvent Read(BinaryReader reader)
        {
            ulong sequence = reader.ReadUInt64();
            double time = reader.ReadDouble();
            var domain = (RuntimeTraceDomain)reader.ReadInt32();
            ulong position = reader.ReadUInt64();
            Guid session = ReadGuid(reader);
            ulong epoch = reader.ReadUInt64();
            Guid branch = ReadGuid(reader);
            RuntimeContentRevision revision = ReadRuntimeContentRevision(reader);
            var channel = (RuntimeTraceChannel)reader.ReadInt32();
            RuntimeInstanceKey instance = ReadRuntimeInstanceKey(reader);
            RuntimeSourceElementHandle source = ReadRuntimeSourceElementHandle(reader);
            var kind = (RuntimeTraceEventKind)reader.ReadInt32();
            return new RuntimeTraceEvent(session, revision, epoch, branch, domain, channel,
                position, time, sequence, instance, source, kind, ReadRuntimeTracePayload(reader));
        }

        void WriteString(BinaryWriter writer, string value)
        {
            if (value == null)
            {
                writer.Write(-1);
                return;
            }
            if (!m_StringIds.TryGetValue(value, out int id))
            {
                id = m_Strings.Count;
                m_Strings.Add(value);
                m_StringIds.Add(value, id);
                m_Symbols.Write(checked(value.Length * sizeof(char)));
                ReadOnlySpan<char> characters = value.AsSpan();
                for (int character = 0; character < characters.Length; character++)
                    m_Symbols.Write((ushort)characters[character]);
            }
            writer.Write(id);
        }

        string ReadString(BinaryReader reader)
        {
            int id = reader.ReadInt32();
            return id == -1 ? null : m_Strings[id];
        }

        void ClearSymbols()
        {
            m_StringIds.Clear();
            m_Strings.Clear();
        }

        void WriteGuid(BinaryWriter writer, Guid value)
        {
            value.TryWriteBytes(m_Guid);
            writer.Write(m_Guid);
        }

        Guid ReadGuid(BinaryReader reader)
        {
            int offset = 0;
            while (offset != m_Guid.Length)
            {
                int read = reader.Read(m_Guid, offset, m_Guid.Length - offset);
                if (read == 0)
                    throw new EndOfStreamException("预览事件文件中的 Guid 不完整。");
                offset += read;
            }
            return new Guid(m_Guid);
        }

        static void WriteVector3(BinaryWriter writer, Vector3 value)
        {
            writer.Write(value.x);
            writer.Write(value.y);
            writer.Write(value.z);
        }

        static Vector3 ReadVector3(BinaryReader reader) => new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

        static void WriteVector4(BinaryWriter writer, Vector4 value)
        {
            writer.Write(value.x);
            writer.Write(value.y);
            writer.Write(value.z);
            writer.Write(value.w);
        }

        static Vector4 ReadVector4(BinaryReader reader) => new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

        void WriteRuntimeTracePayload(BinaryWriter writer, in RuntimeTracePayload value)
        {
            WriteString(writer, value.Status);
            WriteString(writer, value.Name);
            WriteString(writer, value.Detail);
            WriteString(writer, value.Cause);
            WriteString(writer, value.SourceClockId);
            WriteString(writer, value.SourceTickKind);
            WriteString(writer, value.AnimationChannelId);
            WriteString(writer, value.OwnerId);
            WriteString(writer, value.RelatedElementId);
            WriteString(writer, value.SkillId);
            writer.Write(value.InputSequence);
            WriteString(writer, value.CharacterStateHash);
            WriteString(writer, value.WorldHash);
            writer.Write(value.ActionInstanceId);
            WriteString(writer, value.CallSiteId);
            writer.Write(value.ActivationGeneration);
            writer.Write(value.SkillExecutionGeneration);
            WriteString(writer, value.RuleSourceSkillId);
            writer.Write(value.RuleSourceInstanceId);
            writer.Write(value.RuleSourceSkillExecutionGeneration);
            writer.Write(value.RuleSourceSegmentGeneration);
            writer.Write(value.GraphInvocationGeneration);
            writer.Write(value.ParentInvocationGeneration);
            WriteString(writer, value.ParentInvocationPath);
            writer.Write(value.StartTick);
            writer.Write(value.EndTick);
            writer.Write(value.Revision);
            writer.Write(value.ActionPhase);
            writer.Write(value.ActionState);
            writer.Write(value.ActionResult);
            writer.Write(value.LifecycleOperation);
            writer.Write(value.StackCount);
            writer.Write((int)value.DetailKind);
            writer.Write(value.BeforeCount);
            writer.Write(value.Time);
            writer.Write(value.SecondaryTime);
            writer.Write(value.NormalizedTime);
            writer.Write(value.Weight);
            writer.Write(value.FinalWeight);
            writer.Write(value.Priority);
            writer.Write(value.Cycle);
            writer.Write(value.LoopIteration);
            writer.Write(value.BlackboardStateSlot);
            writer.Write(value.BlackboardScope);
            writer.Write(value.BlackboardOwnerIndex);
            writer.Write(value.BlackboardOwnerGeneration);
            writer.Write(value.BlackboardLifetime);
            writer.Write(value.BlackboardIsActive);
            writer.Write(value.TrackIndex);
            writer.Write(value.ClipIndex);
            writer.Write(value.Flag);
            WriteDebugValueSnapshot(writer, value.Value);
            WriteRuntimeTimelinePlaybackProvenance(writer, value.TimelinePlayback);
            WriteRuntimeFootPlacementTraceSnapshot(writer, value.FootPlacement);
            WriteMotionDetail(writer, in value.MotionDetail);
            WriteString(writer, value.MotionIdentityTableId);
            WriteEventId(writer, value.SourceEventId);
        }

        RuntimeTracePayload ReadRuntimeTracePayload(BinaryReader reader) => new RuntimeTracePayload
        {
            Status = ReadString(reader),
            Name = ReadString(reader),
            Detail = ReadString(reader),
            Cause = ReadString(reader),
            SourceClockId = ReadString(reader),
            SourceTickKind = ReadString(reader),
            AnimationChannelId = ReadString(reader),
            OwnerId = ReadString(reader),
            RelatedElementId = ReadString(reader),
            SkillId = ReadString(reader),
            InputSequence = reader.ReadUInt64(),
            CharacterStateHash = ReadString(reader),
            WorldHash = ReadString(reader),
            ActionInstanceId = reader.ReadUInt64(),
            CallSiteId = ReadString(reader),
            ActivationGeneration = reader.ReadUInt64(),
            SkillExecutionGeneration = reader.ReadUInt64(),
            RuleSourceSkillId = ReadString(reader),
            RuleSourceInstanceId = reader.ReadUInt64(),
            RuleSourceSkillExecutionGeneration = reader.ReadUInt64(),
            RuleSourceSegmentGeneration = reader.ReadUInt64(),
            GraphInvocationGeneration = reader.ReadUInt64(),
            ParentInvocationGeneration = reader.ReadUInt64(),
            ParentInvocationPath = ReadString(reader),
            StartTick = reader.ReadUInt64(),
            EndTick = reader.ReadUInt64(),
            Revision = reader.ReadUInt64(),
            ActionPhase = reader.ReadInt32(),
            ActionState = reader.ReadInt32(),
            ActionResult = reader.ReadInt32(),
            LifecycleOperation = reader.ReadInt32(),
            StackCount = reader.ReadInt32(),
            DetailKind = (RuntimeTraceDetailKind)reader.ReadInt32(),
            BeforeCount = reader.ReadInt32(),
            Time = reader.ReadSingle(),
            SecondaryTime = reader.ReadSingle(),
            NormalizedTime = reader.ReadSingle(),
            Weight = reader.ReadSingle(),
            FinalWeight = reader.ReadSingle(),
            Priority = reader.ReadInt32(),
            Cycle = reader.ReadInt32(),
            LoopIteration = reader.ReadInt32(),
            BlackboardStateSlot = reader.ReadInt32(),
            BlackboardScope = reader.ReadInt32(),
            BlackboardOwnerIndex = reader.ReadInt32(),
            BlackboardOwnerGeneration = reader.ReadUInt64(),
            BlackboardLifetime = reader.ReadInt32(),
            BlackboardIsActive = reader.ReadBoolean(),
            TrackIndex = reader.ReadInt32(),
            ClipIndex = reader.ReadInt32(),
            Flag = reader.ReadBoolean(),
            Value = ReadDebugValueSnapshot(reader),
            TimelinePlayback = ReadRuntimeTimelinePlaybackProvenance(reader),
            FootPlacement = ReadRuntimeFootPlacementTraceSnapshot(reader),
            MotionDetail = ReadMotionDetail(reader),
            MotionIdentityTableId = ReadString(reader),
            SourceEventId = ReadEventId(reader),
        };

        void WriteEventId(BinaryWriter writer, EventId value)
        {
            writer.Write(value.IsValid);
            if (value.IsValid)
                value.CopyTo(m_EventId);
            else
                Array.Clear(m_EventId, 0, m_EventId.Length);
            writer.Write(m_EventId);
        }

        EventId ReadEventId(BinaryReader reader)
        {
            bool isValid = reader.ReadBoolean();
            int offset = 0;
            while (offset != m_EventId.Length)
            {
                int read = reader.Read(m_EventId, offset, m_EventId.Length - offset);
                if (read == 0)
                    throw new EndOfStreamException("预览事件文件中的 EventId 不完整。");
                offset += read;
            }
            return isValid ? EventId.FromBytes(m_EventId) : default;
        }

        void WriteMotionDetail(BinaryWriter writer, in SimulationMotionTraceDetail value)
        {
            writer.Write((byte)value.Kind);
            writer.Write(value.DisplacementXRaw);
            writer.Write(value.DisplacementYRaw);
            writer.Write(value.DisplacementZRaw);
            writer.Write(value.YawDegreesRaw);
            writer.Write(value.Channel);
            writer.Write(value.BlendMode);
            writer.Write(value.Priority);
            writer.Write(value.WeightRaw);
            writer.Write(value.PlanarBasisXRaw);
            writer.Write(value.PlanarBasisYRaw);
            writer.Write(value.ClaimsLowerChannels);
            writer.Write(value.HasMotion);
            writer.Write(value.ParticipatingSourceCount);
            writer.Write(value.ParticipatingSourceFingerprint);
            writer.Write(value.MovementClock.IsValid);
            WriteString(writer, value.MovementClock.OwnerIdentity);
            writer.Write(value.MovementClock.Generation);
            writer.Write(value.MovementClock.AuthorityTick.Value);
            writer.Write(value.MovementClock.ContinuousTicks);
            writer.Write(value.MovementClock.TickRate);
        }

        SimulationMotionTraceDetail ReadMotionDetail(BinaryReader reader)
        {
            var kind = (SimulationMotionTraceDetailKind)reader.ReadByte();
            long x = reader.ReadInt64();
            long y = reader.ReadInt64();
            long z = reader.ReadInt64();
            long yaw = reader.ReadInt64();
            int channel = reader.ReadInt32();
            int blend = reader.ReadInt32();
            int priority = reader.ReadInt32();
            long weight = reader.ReadInt64();
            long basisX = reader.ReadInt64();
            long basisY = reader.ReadInt64();
            bool claim = reader.ReadBoolean();
            bool hasMotion = reader.ReadBoolean();
            int sources = reader.ReadInt32();
            ulong fingerprint = reader.ReadUInt64();
            bool hasClock = reader.ReadBoolean();
            string clockOwner = ReadString(reader);
            ulong clockGeneration = reader.ReadUInt64();
            ulong clockTick = reader.ReadUInt64();
            double clockTime = reader.ReadDouble();
            int clockRate = reader.ReadInt32();
            CommittedMovementPlaybackClock clock = hasClock
                ? new CommittedMovementPlaybackClock(clockOwner, clockGeneration,
                    new SimulationTick(clockTick), clockTime, clockRate)
                : default;
            return new SimulationMotionTraceDetail(kind, x, y, z, yaw, in clock,
                channel, blend, priority, weight, basisX, basisY, claim, hasMotion, sources, fingerprint);
        }

        void WriteRuntimeFootPlacementTraceSnapshot(BinaryWriter writer, in RuntimeFootPlacementTraceSnapshot value)
        {
            writer.Write(value.IsAvailable);
            writer.Write(value.FrameSequence);
            writer.Write(value.CompletionIdentity);
            WriteRuntimeFootPlacementFootTraceSnapshot(writer, value.Left);
            WriteRuntimeFootPlacementFootTraceSnapshot(writer, value.Right);
        }

        RuntimeFootPlacementTraceSnapshot ReadRuntimeFootPlacementTraceSnapshot(BinaryReader reader) => new RuntimeFootPlacementTraceSnapshot
        {
            IsAvailable = reader.ReadBoolean(),
            FrameSequence = reader.ReadUInt64(),
            CompletionIdentity = reader.ReadUInt64(),
            Left = ReadRuntimeFootPlacementFootTraceSnapshot(reader),
            Right = ReadRuntimeFootPlacementFootTraceSnapshot(reader),
        };

        void WriteRuntimeFootPlacementFootTraceSnapshot(BinaryWriter writer, in RuntimeFootPlacementFootTraceSnapshot value)
        {
            writer.Write(value.IsAvailable);
            WriteString(writer, value.Side);
            WriteString(writer, value.State);
            WriteString(writer, value.RejectReason);
            WriteString(writer, value.StepSource);
            writer.Write(value.LandingEventIdentity);
            writer.Write(value.TrajectoryGeneration);
            writer.Write(value.LandingConfidence);
            writer.Write(value.TimeToLandingSeconds);
            WriteVector3(writer, value.RootLocalLanding);
            WriteVector3(writer, value.CurrentAnimatedSole);
            WriteVector3(writer, value.RawLanding);
            writer.Write(value.QueryAvailable);
            WriteString(writer, value.QueryShape);
            WriteString(writer, value.QueryPurpose);
            WriteVector3(writer, value.QueryOrigin);
            WriteVector3(writer, value.QueryDirection);
            writer.Write(value.QueryRadius);
            writer.Write(value.QueryMaximumDistance);
            writer.Write(value.QueryLayerMask);
            writer.Write(value.QueryMinimumGroundNormalDot);
            writer.Write(value.Accepted);
            writer.Write(value.SurfaceIdentity);
            WriteVector3(writer, value.LandingPoint);
            WriteVector3(writer, value.LandingNormal);
            writer.Write(value.QueryDistance);
        }

        RuntimeFootPlacementFootTraceSnapshot ReadRuntimeFootPlacementFootTraceSnapshot(BinaryReader reader) => new RuntimeFootPlacementFootTraceSnapshot
        {
            IsAvailable = reader.ReadBoolean(),
            Side = ReadString(reader),
            State = ReadString(reader),
            RejectReason = ReadString(reader),
            StepSource = ReadString(reader),
            LandingEventIdentity = reader.ReadUInt64(),
            TrajectoryGeneration = reader.ReadUInt64(),
            LandingConfidence = reader.ReadSingle(),
            TimeToLandingSeconds = reader.ReadSingle(),
            RootLocalLanding = ReadVector3(reader),
            CurrentAnimatedSole = ReadVector3(reader),
            RawLanding = ReadVector3(reader),
            QueryAvailable = reader.ReadBoolean(),
            QueryShape = ReadString(reader),
            QueryPurpose = ReadString(reader),
            QueryOrigin = ReadVector3(reader),
            QueryDirection = ReadVector3(reader),
            QueryRadius = reader.ReadSingle(),
            QueryMaximumDistance = reader.ReadSingle(),
            QueryLayerMask = reader.ReadInt32(),
            QueryMinimumGroundNormalDot = reader.ReadSingle(),
            Accepted = reader.ReadBoolean(),
            SurfaceIdentity = reader.ReadInt32(),
            LandingPoint = ReadVector3(reader),
            LandingNormal = ReadVector3(reader),
            QueryDistance = reader.ReadSingle(),
        };

        void WriteRuntimeContentRevision(BinaryWriter writer, in RuntimeContentRevision value)
        {
            WriteString(writer, value.RuntimeId);
            WriteString(writer, value.SourceRevision);
            WriteString(writer, value.ContentHash);
        }

        RuntimeContentRevision ReadRuntimeContentRevision(BinaryReader reader)
        {
            string runtimeId = ReadString(reader);
            string sourceRevision = ReadString(reader);
            string contentHash = ReadString(reader);
            return runtimeId == null ? default : new RuntimeContentRevision(
                runtimeId,
                sourceRevision,
                contentHash);
        }

        void WriteRuntimeSourceElementHandle(BinaryWriter writer, in RuntimeSourceElementHandle value)
        {
            writer.Write(value.Value);
            writer.Write((int)value.Kind);
        }

        RuntimeSourceElementHandle ReadRuntimeSourceElementHandle(BinaryReader reader) => new RuntimeSourceElementHandle(
            reader.ReadInt32(),
            (RuntimeSourceElementKind)reader.ReadInt32());

        void WriteRuntimeInstanceKey(BinaryWriter writer, in RuntimeInstanceKey value)
        {
            writer.Write((int)value.Kind);
            WriteGuid(writer, value.CharacterRuntimeId);
            WriteGuid(writer, value.GraphRuntimeId);
            WriteString(writer, value.StateId);
            writer.Write(value.ActivationGeneration);
            writer.Write(value.TimelinePlaybackId);
            writer.Write(value.TreeClipCycle);
            writer.Write(value.ActionInstanceId);
            WriteString(writer, value.CallSiteId);
            writer.Write(value.InvocationGeneration);
            writer.Write(value.SourceOperationIndex);
            writer.Write(value.TreeClipOperationIndex);
        }

        RuntimeInstanceKey ReadRuntimeInstanceKey(BinaryReader reader)
        {
            RuntimeInstanceKind kind = (RuntimeInstanceKind)reader.ReadInt32();
            Guid characterRuntimeId = ReadGuid(reader);
            Guid graphRuntimeId = ReadGuid(reader);
            string stateId = ReadString(reader);
            ulong activationGeneration = reader.ReadUInt64();
            ulong timelinePlaybackId = reader.ReadUInt64();
            int treeClipCycle = reader.ReadInt32();
            ulong actionInstanceId = reader.ReadUInt64();
            string callSiteId = ReadString(reader);
            ulong invocationGeneration = reader.ReadUInt64();
            int sourceOperationIndex = reader.ReadInt32();
            int treeClipOperationIndex = reader.ReadInt32();
            return stateId == null ? default : new RuntimeInstanceKey(
                kind,
                characterRuntimeId,
                graphRuntimeId,
                stateId,
                activationGeneration,
                timelinePlaybackId,
                treeClipCycle,
                actionInstanceId,
                callSiteId,
                invocationGeneration,
                sourceOperationIndex,
                treeClipOperationIndex);
        }

        void WriteDebugValueSnapshot(BinaryWriter writer, in DebugValueSnapshot value)
        {
            writer.Write((int)value.Kind);
            writer.Write(value.Boolean);
            writer.Write(value.Signed);
            writer.Write(value.Unsigned);
            writer.Write(value.Number);
            WriteString(writer, value.Text);
            WriteVector4(writer, value.Vector);
        }

        DebugValueSnapshot ReadDebugValueSnapshot(BinaryReader reader)
        {
            DebugValueKind kind = (DebugValueKind)reader.ReadInt32();
            bool boolean = reader.ReadBoolean();
            long signed = reader.ReadInt64();
            ulong unsigned = reader.ReadUInt64();
            double number = reader.ReadDouble();
            string text = ReadString(reader);
            Vector4 vector = ReadVector4(reader);
            return text == null ? default : new DebugValueSnapshot(
                kind,
                boolean,
                signed,
                unsigned,
                number,
                text,
                vector);
        }

        void WriteRuntimeTimelinePlaybackProvenance(BinaryWriter writer, in RuntimeTimelinePlaybackProvenance value)
        {
            WriteString(writer, value.SourceGraphAuthoringId);
            WriteString(writer, value.SourceNodeAuthoringId);
            WriteGuid(writer, value.SourceGraphRuntimeId);
            writer.Write(value.SourceActivationGeneration);
            WriteString(writer, value.StateMachineGraphAuthoringId);
            WriteString(writer, value.StateId);
            WriteGuid(writer, value.StateMachineGraphRuntimeId);
            writer.Write(value.StateActivationGeneration);
            WriteString(writer, value.SourceName);
            WriteString(writer, value.SourceInvocationPath);
            writer.Write(value.SourceOperationIndex);
            writer.Write(value.SkillExecutionGeneration);
        }

        RuntimeTimelinePlaybackProvenance ReadRuntimeTimelinePlaybackProvenance(BinaryReader reader)
        {
            string sourceGraphAuthoringId = ReadString(reader);
            string sourceNodeAuthoringId = ReadString(reader);
            Guid sourceGraphRuntimeId = ReadGuid(reader);
            ulong sourceActivationGeneration = reader.ReadUInt64();
            string stateMachineGraphAuthoringId = ReadString(reader);
            string stateId = ReadString(reader);
            Guid stateMachineGraphRuntimeId = ReadGuid(reader);
            ulong stateActivationGeneration = reader.ReadUInt64();
            string sourceName = ReadString(reader);
            string sourceInvocationPath = ReadString(reader);
            int sourceOperationIndex = reader.ReadInt32();
            ulong skillExecutionGeneration = reader.ReadUInt64();
            return sourceGraphAuthoringId == null ? default : new RuntimeTimelinePlaybackProvenance(
                sourceGraphAuthoringId,
                sourceNodeAuthoringId,
                sourceGraphRuntimeId,
                sourceActivationGeneration,
                stateMachineGraphAuthoringId,
                stateId,
                stateMachineGraphRuntimeId,
                stateActivationGeneration,
                sourceName,
                sourceInvocationPath,
                sourceOperationIndex,
                skillExecutionGeneration);
        }

        public void Dispose()
        {
            ClearSymbols();
            m_Symbols.Dispose();
        }
    }
}
