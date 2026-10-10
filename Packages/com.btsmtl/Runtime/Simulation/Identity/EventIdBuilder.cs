using System;
using System.Buffers.Binary;
using System.Globalization;

namespace ThirdPersonSimulation
{
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
            builder.Append(treeClip.TreeGraphId);
            builder.Append(treeClip.TreeGraphRevision);
            builder.Append(treeClip.NodeAuthoringId);
            builder.Append(treeClip.PlaybackGeneration);
            builder.Append(treeClip.BranchRevision);
            builder.Append(treeClip.TimelineRuntimeHandle);
            builder.Append((ulong)treeClip.Time.Raw);
            builder.Append((ulong)treeClip.Cycle);
            builder.Append(treeClip.ActionInstanceId);
            return builder.Build();
        }

        static class Sha256RoundConstants
        {
            internal static readonly uint[] Values =
            {
                0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
                0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
                0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
                0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
                0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
                0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
                0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
                0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2
            };
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
            BeginField();
            SimulationExecutionSource source = value.Source;
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
            WriteNumber(value.Generation);
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
            for (int index = 0; index < value.Length; index++)
            {
                WriteByte((byte)value[index]);
                WriteByte((byte)(value[index] >> 8));
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
            while (m_Count != 56)
                WriteByte(0);
            BinaryPrimitives.WriteUInt64BigEndian(m_Block.Slice(56), bitLength);
            Transform();
            return new EventId(((ulong)m_A << 32) | m_B, ((ulong)m_C << 32) | m_D,
                ((ulong)m_E << 32) | m_F, ((ulong)m_G << 32) | m_H);
        }

        static uint Rotate(uint value, int bits) => (value >> bits) | (value << (32 - bits));

        void Transform()
        {
            Span<uint> words = stackalloc uint[64];
            for (int i = 0; i < 16; i++)
                words[i] = BinaryPrimitives.ReadUInt32BigEndian(m_Block.Slice(i * 4, 4));
            unchecked
            {
                for (int i = 16; i < words.Length; i++)
                {
                    uint x = words[i - 15], y = words[i - 2];
                    words[i] = words[i - 16] + (Rotate(x, 7) ^ Rotate(x, 18) ^ (x >> 3)) +
                        words[i - 7] + (Rotate(y, 17) ^ Rotate(y, 19) ^ (y >> 10));
                }
                uint a = m_A, b = m_B, c = m_C, d = m_D, e = m_E, f = m_F, g = m_G, h = m_H;
                for (int i = 0; i < words.Length; i++)
                {
                    uint t1 = h + (Rotate(e, 6) ^ Rotate(e, 11) ^ Rotate(e, 25)) +
                        ((e & f) ^ (~e & g)) + Sha256RoundConstants.Values[i] + words[i];
                    uint t2 = (Rotate(a, 2) ^ Rotate(a, 13) ^ Rotate(a, 22)) +
                        ((a & b) ^ (a & c) ^ (b & c));
                    h = g; g = f; f = e; e = d + t1; d = c; c = b; b = a; a = t1 + t2;
                }
                m_A += a; m_B += b; m_C += c; m_D += d;
                m_E += e; m_F += f; m_G += g; m_H += h;
            }
        }
    }
}
