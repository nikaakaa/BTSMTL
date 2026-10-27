using System;
using System.Linq;
using System.Text;

namespace BTSMTL.Authoring.Documents
{
    // 文档中被引用内容在导出时的编译值；独立编译只按此表取得被引用内容的身份与修订。
    public sealed class AuthoringReferenceTable
    {
        public AuthoringReferenceTable(ReadOnlyMemory<AuthoringReferenceEntry> entries) => Entries = entries;
        public ReadOnlyMemory<AuthoringReferenceEntry> Entries { get; }
    }

    // Source 是文档里出现的原值：资产引用，或带来源资产与记录身份的复合引用值。
    public sealed class AuthoringReferenceEntry
    {
        public AuthoringReferenceEntry(AuthoringValue source, string kind, ReadOnlyMemory<AuthoringNamedValue> fields)
        {
            Source = source;
            Kind = kind;
            Fields = fields;
            Key = KeyOf(source);
        }

        public AuthoringValue Source { get; }
        public string Kind { get; }
        public ReadOnlyMemory<AuthoringNamedValue> Fields { get; }
        public string Key { get; }

        // 引用值的内容身份：不含声明类型，同一资产或同一记录在不同字段中得到相同身份。
        public static string KeyOf(AuthoringValue value)
        {
            var text = new StringBuilder();
            Append(text, value);
            return text.ToString();
        }

        static void Append(StringBuilder text, AuthoringValue value)
        {
            switch (value)
            {
                case AuthoringNullValue:
                    text.Append("null");
                    break;
                case AuthoringScalarValue scalar:
                    text.Append('"').Append(scalar.Value.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');
                    break;
                case AuthoringReferenceValue reference:
                    text.Append('<').Append(reference.Target.RootId.Value).Append(':')
                        .Append(reference.Target.DocumentId.Value).Append(':').Append(reference.Target.ElementId.Value).Append('>');
                    break;
                case AuthoringObjectValue composite:
                    text.Append('{');
                    foreach (AuthoringNamedValue member in composite.Members.ToArray()
                                 .OrderBy(member => member.FieldId.Value, StringComparer.Ordinal))
                    {
                        text.Append(member.FieldId.Value).Append('=');
                        Append(text, member.Value);
                        text.Append(';');
                    }
                    text.Append('}');
                    break;
                case AuthoringSequenceValue sequence:
                    text.Append('[');
                    foreach (AuthoringValue item in sequence.Items.Span)
                    {
                        Append(text, item);
                        text.Append(';');
                    }
                    text.Append(']');
                    break;
                default:
                    throw new ArgumentException($"作者值类型 '{value.GetType()}' 没有引用身份。", nameof(value));
            }
        }
    }
}
