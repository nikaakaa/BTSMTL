using System;
using System.Globalization;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace ThirdPersonSimulation
{
    public readonly struct EventIdentityPrefix
    {
        internal EventIdentityPrefix(SimulationExecutionSource source, ReadOnlySpan<byte> block, ulong length,
            uint a, uint b, uint c, uint d, uint e, uint f, uint g, uint h)
        {
            Source = source;
            Block = block.ToArray();
            Length = length;
            A = a; B = b; C = c; D = d; E = e; F = f; G = g; H = h;
        }

        public SimulationExecutionSource Source { get; }
        internal readonly byte[] Block;
        internal readonly ulong Length;
        internal readonly uint A, B, C, D, E, F, G, H;
    }

    public ref struct EventIdBuilder
    {
        public static EventId Create(GameplayContentHash sourceContent, ActorId actor,
            ActivationId activation, SimulationTick tick, ulong sequence, string channel)
        {
            if (!sourceContent.IsValid || !actor.IsValid || !activation.IsValid || !tick.IsValid || sequence == 0)
                throw new ArgumentException("Event identity is incomplete.");
            Span<byte> block = stackalloc byte[64];
            var builder = new EventIdBuilder(block);
            builder.Append(sourceContent.Value);
            builder.Append(actor.Value);
            builder.Append(activation);
            builder.Append(tick.Value);
            builder.Append(sequence);
            builder.Append(channel);
            return builder.Build();
        }

        public static EventId CreateTreeClip(
            GameplayContentHash sourceContent,
            ActorId actor,
            ActivationId activation,
            SimulationTick tick,
            ulong sequence,
            string channel,
            in AbilityTreeClipInvocation treeClip)
        {
            if (!sourceContent.IsValid || !actor.IsValid || !activation.IsValid ||
                !tick.IsValid || sequence == 0 || !treeClip.IsValid)
                throw new ArgumentException("TreeClip event identity is incomplete.");
            Span<byte> block = stackalloc byte[64];
            var builder = new EventIdBuilder(block);
            builder.Append(sourceContent.Value);
            builder.Append(actor.Value);
            builder.Append(activation);
            builder.Append(tick.Value);
            builder.Append(sequence);
            builder.Append(channel);
            builder.Append(in treeClip);
            return builder.Build();
        }

        readonly Span<byte> m_Block;
        int m_Count;
        ulong m_Length;
        bool m_HasField;
        bool m_Built;
        uint m_A, m_B, m_C, m_D, m_E, m_F, m_G, m_H;

        public EventIdBuilder(Span<byte> block)
        {
            if (block.Length < 64)
                throw new ArgumentException("Event identity builder requires a 64 byte block.", nameof(block));
            m_Block = block.Slice(0, 64);
            m_Count = 0;
            m_Length = 0;
            m_HasField = false;
            m_Built = false;
            m_A = 0x6a09e667; m_B = 0xbb67ae85; m_C = 0x3c6ef372; m_D = 0xa54ff53a;
            m_E = 0x510e527f; m_F = 0x9b05688c; m_G = 0x1f83d9ab; m_H = 0x5be0cd19;
        }

        public static EventIdentityPrefix Prepare(GameplayContentHash sourceContent, ActorId actor,
            SimulationExecutionSource source)
        {
            Span<byte> block = stackalloc byte[64];
            var builder = new EventIdBuilder(block);
            builder.Append(sourceContent.Value);
            builder.Append(actor.Value);
            builder.AppendActivationSource(source);
            return new EventIdentityPrefix(source, builder.m_Block.Slice(0, builder.m_Count), builder.m_Length,
                builder.m_A, builder.m_B, builder.m_C, builder.m_D, builder.m_E, builder.m_F, builder.m_G, builder.m_H);
        }

        public EventIdBuilder(Span<byte> block, in EventIdentityPrefix prefix)
        {
            m_Block = block.Slice(0, 64);
            prefix.Block.AsSpan().CopyTo(m_Block);
            m_Count = prefix.Block.Length;
            m_Length = prefix.Length;
            m_HasField = true;
            m_Built = false;
            m_A = prefix.A; m_B = prefix.B; m_C = prefix.C; m_D = prefix.D;
            m_E = prefix.E; m_F = prefix.F; m_G = prefix.G; m_H = prefix.H;
        }

        public void Append(in AbilityTreeClipInvocation treeClip)
        {
            Append(treeClip.TreeGraphId);
            Append(treeClip.TreeGraphRevision);
            Append(treeClip.NodeAuthoringId);
            Append(treeClip.PlaybackGeneration);
            Append(treeClip.BranchRevision);
            Append(treeClip.TimelineRuntimeHandle);
            Append((ulong)treeClip.Time.RawValue);
            Append((ulong)treeClip.Cycle);
            Append(treeClip.ActionInstanceId);
        }

        public void Append(CanonicalTextValue value, bool startField = true)
        {
            BeginField(startField);
            WriteText(value.AsSpan());
        }

        public void Append(ulong value, bool startField = true)
        {
            BeginField(startField);
            WriteNumber(value);
        }

        public void Append(EventId value)
        {
            BeginField();
            if (!value.IsValid)
                return;
            Span<char> characters = stackalloc char[64];
            value.Format(characters);
            for (int i = 0; i < characters.Length; i++)
                WriteByte((byte)characters[i]);
        }

        public void Append(StableHash value)
        {
            BeginField();
            if (!value.IsValid)
                return;
            Span<char> characters = stackalloc char[64];
            value.Format(characters);
            for (int i = 0; i < characters.Length; i++)
                WriteByte((byte)characters[i]);
        }

        public void Append(ActivationId value)
        {
            AppendActivationSource(value.Source);
            WriteNumber(value.Generation);
        }

        public void AppendActivationSource(SimulationExecutionSource source)
        {
            BeginField();
            if (source.IsAbilityRule)
            {
                WriteText("ability-rule:".AsSpan());
                WriteText(source.AbilityId.Value.AsSpan());
                WriteByte((byte)':');
                WriteText(source.RuleId.AsSpan());
                WriteByte((byte)':');
                WriteText((source.RulePhase == GameplayAbilityRulePhase.BeforeAbility ? "BeforeAbility" : "AfterAbility").AsSpan());
            }
            else if (source.IsSkillOperation)
            {
                WriteText("skill-operation:".AsSpan());
                WriteText(source.ExecutionPath.AsSpan());
            }
            else if (source.IsBlackboardCommand)
            {
                WriteText("blackboard-command:".AsSpan());
                WriteText(source.BlackboardDeclaration.AsSpan());
            }
            else if (source.IsGameplayEffect)
            {
                WriteText("gameplay-effect".AsSpan());
            }
            else
            {
                WriteText("character-control:".AsSpan());
                WriteText(source.ModuleId.Value.AsSpan());
                WriteByte((byte)':');
                WriteText(source.StateId.Value.AsSpan());
                WriteByte((byte)':');
                WriteText(source.TransitionId.Value.AsSpan());
            }
            WriteByte((byte)'/');
        }

        void BeginField(bool startField = true)
        {
            if (m_Built)
                throw new InvalidOperationException("Event identity builder has already completed.");
            if (!startField)
                return;
            if (m_HasField)
                WriteByte(0x1f);
            m_HasField = true;
        }

        void WriteNumber(ulong value)
        {
            Span<char> characters = stackalloc char[20];
            value.TryFormat(characters, out int count, provider: CultureInfo.InvariantCulture);
            for (int i = 0; i < count; i++)
                WriteByte((byte)characters[i]);
        }

        void WriteText(ReadOnlySpan<char> value)
        {
            if (BitConverter.IsLittleEndian)
            {
                WriteBytes(MemoryMarshal.AsBytes(value));
                return;
            }
            for (int index = 0; index < value.Length; index++)
            {
                WriteByte((byte)value[index]);
                WriteByte((byte)(value[index] >> 8));
            }
        }

        void WriteBytes(ReadOnlySpan<byte> value)
        {
            while (!value.IsEmpty)
            {
                int count = Math.Min(value.Length, 64 - m_Count);
                value.Slice(0, count).CopyTo(m_Block.Slice(m_Count));
                value = value.Slice(count);
                m_Count += count;
                m_Length += (ulong)count;
                if (m_Count == 64)
                {
                    Transform();
                    m_Count = 0;
                }
            }
        }

        void WriteByte(byte value)
        {
            m_Block[m_Count++] = value;
            m_Length++;
            if (m_Count == 64)
            {
                Transform();
                m_Count = 0;
            }
        }

        public EventId Build()
        {
            if (m_Built)
                throw new InvalidOperationException("Event identity builder has already completed.");
            m_Built = true;
            ulong bitLength = checked(m_Length * 8);
            WriteByte(0x80);
            if (m_Count > 56)
            {
                m_Block.Slice(m_Count).Clear();
                Transform();
                m_Count = 0;
            }
            m_Block.Slice(m_Count, 56 - m_Count).Clear();
            BinaryPrimitives.WriteUInt64BigEndian(m_Block.Slice(56), bitLength);
            Transform();
            return new EventId(((ulong)m_A << 32) | m_B, ((ulong)m_C << 32) | m_D,
                ((ulong)m_E << 32) | m_F, ((ulong)m_G << 32) | m_H);
        }

        void Transform()
        {
            Span<uint> state = stackalloc uint[8];
            Span<uint> words = stackalloc uint[64];
            state[0] = m_A; state[1] = m_B; state[2] = m_C; state[3] = m_D;
            state[4] = m_E; state[5] = m_F; state[6] = m_G; state[7] = m_H;
            SimulationCanonicalPayloadHash.ProcessBlock(m_Block, state, words);
            m_A = state[0]; m_B = state[1]; m_C = state[2]; m_D = state[3];
            m_E = state[4]; m_F = state[5]; m_G = state[6]; m_H = state[7];
        }
    }
}
