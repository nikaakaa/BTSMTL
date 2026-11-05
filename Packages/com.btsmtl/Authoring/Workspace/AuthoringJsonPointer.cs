using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTSMTL.Authoring.Workspace
{
    public static class AuthoringJsonPointer
    {
        public static string Of(JToken value)
        {
            var path = new Stack<string>();
            while (value.Parent != null)
                if (value.Parent is JProperty property)
                {
                    path.Push(Segment(property.Name));
                    value = property.Parent;
                }
                else
                {
                    int index = 0;
                    for (JToken previous = value.Previous; previous != null; previous = previous.Previous)
                        index++;
                    path.Push(index.ToString(CultureInfo.InvariantCulture));
                    value = value.Parent;
                }
            return "/" + string.Join("/", path);
        }

        public static string ReplaceStrings(string text, IEnumerable<string> pointers, string replacement)
        {
            var selected = new HashSet<string>(pointers, StringComparer.Ordinal);
            var lines = new List<int> { 0 };
            for (int index = 0; index < text.Length; index++)
                if (text[index] == '\n')
                    lines.Add(index + 1);
            using var reader = new JsonTextReader(new StringReader(text)) { DateParseHandling = DateParseHandling.None };
            var output = new StringBuilder();
            string serialized = JsonConvert.SerializeObject(replacement);
            int copied = 0;
            reader.Read();
            Visit("");
            output.Append(text, copied, text.Length - copied);
            return output.ToString();

            void Visit(string pointer)
            {
                if (reader.TokenType == JsonToken.StartObject)
                {
                    while (reader.Read() && reader.TokenType != JsonToken.EndObject)
                    {
                        if (reader.TokenType == JsonToken.Comment)
                            continue;
                        string member = Segment((string)reader.Value);
                        reader.Read();
                        while (reader.TokenType == JsonToken.Comment) reader.Read();
                        Visit(pointer + "/" + member);
                    }
                }
                else if (reader.TokenType == JsonToken.StartArray)
                {
                    int index = 0;
                    while (reader.Read() && reader.TokenType != JsonToken.EndArray)
                    {
                        if (reader.TokenType == JsonToken.Comment)
                            continue;
                        Visit(pointer + "/" + (index++).ToString(CultureInfo.InvariantCulture));
                    }
                }
                else if (reader.TokenType == JsonToken.String && selected.Contains(pointer))
                {
                    int end = lines[reader.LineNumber - 1] + reader.LinePosition;
                    int start = end - 2;
                    for (; start >= 0; start--)
                    {
                        if (text[start] != '"')
                            continue;
                        int escapes = 0;
                        for (int preceding = start - 1; preceding >= 0 && text[preceding] == '\\'; preceding--) escapes++;
                        if (escapes % 2 == 0)
                            break;
                    }
                    output.Append(text, copied, start - copied).Append(serialized);
                    copied = end;
                }
            }
        }

        static string Segment(string name) => name.Replace("~", "~0").Replace("/", "~1");
    }
}
