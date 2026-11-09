using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTSMTL.Authoring.Workspace
{
    public class AuthoringJsonFormatException : Exception
    {
        public AuthoringJsonFormatException(string code, string message, IReadOnlyList<JObject> errors)
            : base(message)
        {
            Code = code;
            Errors = errors;
        }

        public string Code { get; }
        public IReadOnlyList<JObject> Errors { get; }

        public JObject Result() => new JObject
        {
            ["success"] = false, ["code"] = Code, ["message"] = Message, ["errors"] = new JArray(Errors)
        };
    }

    // 作者文档的规范文本：键按序号序排列，带符号零写成 -0.0，存盘缩进并以换行结尾。
    public static class AuthoringJsonText
    {
        sealed class SignedZeroWriter : JsonTextWriter
        {
            public SignedZeroWriter(TextWriter writer) : base(writer) { }

            public override void WriteValue(float value)
            {
                if (BitConverter.SingleToInt32Bits(value) == int.MinValue)
                    WriteRawValue("-0.0");
                else
                    base.WriteValue(value);
            }

            public override void WriteValue(double value)
            {
                if (BitConverter.DoubleToInt64Bits(value) == long.MinValue)
                    WriteRawValue("-0.0");
                else
                    base.WriteValue(value);
            }
        }

        public static string Text(JToken value) => Write(value, Formatting.Indented) + "\n";

        // HTTP 回执用：键序与带符号零写法同存盘格式，不缩进。
        public static string Compact(JToken value) => Write(value, Formatting.None);

        static string Write(JToken value, Formatting formatting)
        {
            using var text = new StringWriter(CultureInfo.InvariantCulture);
            using var writer = new SignedZeroWriter(text) { Formatting = formatting };
            Sort(value).WriteTo(writer);
            return text.ToString();
        }

        public static JObject Parse(string text)
        {
            using var source = new StringReader(text);
            using var reader = new JsonTextReader(source) { DateParseHandling = DateParseHandling.None };
            return JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        }

        public static bool SameValue(JToken left, JToken right)
        {
            if (left.Type != right.Type)
                return false;
            if (left is JObject leftObject && right is JObject rightObject)
                return leftObject.Count == rightObject.Count && leftObject.Properties().All(property =>
                    rightObject.TryGetValue(property.Name, out JToken value) && SameValue(property.Value, value));
            if (left is JArray leftArray && right is JArray rightArray)
            {
                if (leftArray.Count != rightArray.Count)
                    return false;
                for (int index = 0; index < leftArray.Count; index++)
                    if (!SameValue(leftArray[index], rightArray[index]))
                        return false;
                return true;
            }
            if (left.Type == JTokenType.Float)
            {
                double leftNumber = Convert.ToDouble(((JValue)left).Value, CultureInfo.InvariantCulture);
                double rightNumber = Convert.ToDouble(((JValue)right).Value, CultureInfo.InvariantCulture);
                if (leftNumber == 0d && rightNumber == 0d)
                    return BitConverter.DoubleToInt64Bits(leftNumber) == BitConverter.DoubleToInt64Bits(rightNumber);
            }
            return JToken.DeepEquals(left, right);
        }

        public static void ReplaceValue(JToken target, JToken value)
        {
            if (target is JValue current && value is JValue replacement &&
                current.Type == JTokenType.Float && replacement.Type == JTokenType.Float &&
                Convert.ToDouble(current.Value, CultureInfo.InvariantCulture) == 0d &&
                Convert.ToDouble(replacement.Value, CultureInfo.InvariantCulture) == 0d)
                current.Value = replacement.Value;
            else
                target.Replace(value);
        }

        static JToken Sort(JToken value) => value switch
        {
            JObject obj => new JObject(obj.Properties().OrderBy(property => property.Name, StringComparer.Ordinal)
                .Select(property => new JProperty(property.Name, Sort(property.Value)))),
            JArray array => new JArray(array.Select(Sort)),
            _ => value.DeepClone()
        };
    }
}
