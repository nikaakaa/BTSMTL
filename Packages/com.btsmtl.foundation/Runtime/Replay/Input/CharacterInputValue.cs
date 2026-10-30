using InputVector2 = System.Numerics.Vector2;

namespace BTSMTL.Replay.Input
{
    public readonly struct CharacterInputValue
    {
        CharacterInputValue(string inputValueId, CharacterInputValueType valueType, bool boolValue, float floatValue, InputVector2 vector2Value)
        {
            InputValueId = inputValueId;
            ValueType = valueType;
            BoolValue = boolValue;
            FloatValue = floatValue;
            Vector2Value = vector2Value;
        }

        public string InputValueId { get; }
        public CharacterInputValueType ValueType { get; }
        public bool BoolValue { get; }
        public float FloatValue { get; }
        public InputVector2 Vector2Value { get; }

        public static CharacterInputValue Bool(string inputValueId, bool value)
        {
            return new CharacterInputValue(inputValueId, CharacterInputValueType.Bool, value, 0f, default);
        }

        public static CharacterInputValue Float(string inputValueId, float value)
        {
            return new CharacterInputValue(inputValueId, CharacterInputValueType.Float, false, value, default);
        }

        public static CharacterInputValue Vector2(string inputValueId, InputVector2 value)
        {
            return new CharacterInputValue(inputValueId, CharacterInputValueType.Vector2, false, 0f, value);
        }
    }
}
