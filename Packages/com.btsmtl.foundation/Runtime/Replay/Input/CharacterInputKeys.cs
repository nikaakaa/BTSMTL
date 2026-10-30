using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using InputVector2 = System.Numerics.Vector2;

namespace BTSMTL.Replay.Input
{
    public enum CharacterInputKeyKind : byte
    {
        Button,
        Scalar,
        Vector2
    }

    // 一个按键在一帧内的状态：开关键看 Pressed／PressedThisFrame，数值键看 Scalar，方向键看 Vector2。
    public readonly struct CharacterInputKeyState
    {
        public CharacterInputKeyState(bool pressed, bool pressedThisFrame, float scalar, InputVector2 vector2,
            bool releasedThisFrame = false)
        {
            Pressed = pressed;
            PressedThisFrame = pressedThisFrame;
            ReleasedThisFrame = releasedThisFrame;
            Scalar = scalar;
            Vector2 = vector2;
        }

        public bool Pressed { get; }
        public bool PressedThisFrame { get; }
        public bool ReleasedThisFrame { get; }
        public float Scalar { get; }
        public InputVector2 Vector2 { get; }

        public bool HasSameValue(in CharacterInputKeyState other) =>
            Pressed == other.Pressed && Scalar == other.Scalar && Vector2.Equals(other.Vector2);
    }

    [DataContract]
    public sealed class CharacterInputKeyValue
    {
        public CharacterInputKeyValue(string inputValueId, CharacterInputValueType valueType, float minimumHoldSeconds)
        {
            InputValueId = inputValueId;
            ValueType = valueType;
            MinimumHoldSeconds = minimumHoldSeconds;
        }

        [DataMember] public string InputValueId { get; }
        [DataMember] public CharacterInputValueType ValueType { get; }
        [DataMember] public float MinimumHoldSeconds { get; }
    }

    [DataContract]
    public sealed class CharacterInputKeyRequest
    {
        public CharacterInputKeyRequest(string requestId, bool repeatWhileHeld, float minimumHoldSeconds,
            int releaseValueSlot)
        {
            RequestId = requestId;
            RepeatWhileHeld = repeatWhileHeld;
            MinimumHoldSeconds = minimumHoldSeconds;
            ReleaseValueSlot = releaseValueSlot;
        }

        [DataMember] public string RequestId { get; }
        [DataMember] public bool RepeatWhileHeld { get; }
        [DataMember] public float MinimumHoldSeconds { get; }
        [DataMember] public int ReleaseValueSlot { get; }
    }

    // 输入档案的按键层：输入值与动作请求都由源按键产生，同一按键可同时给出按住值与请求。
    // 设备采样与预览都只经这一份换算，从按键状态得到输入值与请求。
    [DataContract]
    public sealed class CharacterInputKeyLayout
    {
        readonly Guid[] m_Actions;
        readonly string[] m_Names;
        readonly CharacterInputKeyKind[] m_Kinds;
        readonly int[] m_ValueKeys;
        readonly int[] m_RequestKeys;
        readonly CharacterInputKeyValue[] m_Values;
        readonly CharacterInputKeyRequest[] m_Requests;
        readonly bool[] m_CancelRequests;

        public CharacterInputKeyLayout(IReadOnlyList<Guid> actions, IReadOnlyList<string> names,
            IReadOnlyList<CharacterInputKeyKind> kinds, IReadOnlyList<int> valueKeys, IReadOnlyList<int> requestKeys,
            IReadOnlyList<CharacterInputKeyValue> values, IReadOnlyList<CharacterInputKeyRequest> requests,
            IReadOnlyList<bool> cancelRequests)
        {
            m_Actions = actions.ToArray();
            m_Names = names.ToArray();
            m_Kinds = kinds.ToArray();
            m_ValueKeys = valueKeys.ToArray();
            m_RequestKeys = requestKeys.ToArray();
            m_Values = values.ToArray();
            m_Requests = requests.ToArray();
            m_CancelRequests = cancelRequests.ToArray();
        }

        [DataMember] public IReadOnlyList<Guid> Actions => m_Actions;
        [DataMember] public IReadOnlyList<string> Names => m_Names;
        [DataMember] public IReadOnlyList<CharacterInputKeyKind> Kinds => m_Kinds;
        [DataMember] public IReadOnlyList<int> ValueKeys => m_ValueKeys;
        [DataMember] public IReadOnlyList<int> RequestKeys => m_RequestKeys;
        [DataMember] public IReadOnlyList<CharacterInputKeyValue> Values => m_Values;
        [DataMember] public IReadOnlyList<CharacterInputKeyRequest> Requests => m_Requests;
        [DataMember] public IReadOnlyList<bool> CancelRequests => m_CancelRequests;
        public int Count => m_Actions.Length;
        public int ValueCount => m_ValueKeys.Length;
        public int RequestCount => m_RequestKeys.Length;
        public Guid ActionId(int key) => m_Actions[key];
        public string Name(int key) => m_Names[key];
        public CharacterInputKeyKind Kind(int key) => m_Kinds[key];
        public int ValueKey(int value) => m_ValueKeys[value];
        public int RequestKey(int request) => m_RequestKeys[request];
        public CharacterInputKeyValue ValueDefinition(int value) => m_Values[value];
        public CharacterInputKeyRequest RequestDefinition(int request) => m_Requests[request];
        public bool Cancels(int request, int other) => m_CancelRequests[request * RequestCount + other];

        public bool TryFind(string name, out int key)
        {
            for (key = 0; key < m_Names.Length; key++)
                if (string.Equals(m_Names[key], name, StringComparison.Ordinal))
                    return true;
            key = -1;
            return false;
        }
    }
}
