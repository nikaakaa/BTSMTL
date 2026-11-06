using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BTSMTL.Authoring.Graph;
using Newtonsoft.Json.Linq;

namespace BTSMTL.Compilation
{
    public sealed class BtsmtlGraphJsonFields : IGraphNodeFieldReader
    {
        readonly JObject m_Source;
        readonly Func<JToken, Type, object> m_ReadValue;

        public BtsmtlGraphJsonFields(JObject source, Type nodeType, Func<JToken, Type, object> readValue)
        {
            m_Source = source;
            NodeType = nodeType;
            m_ReadValue = readValue;
        }

        public Type NodeType { get; }

        public T Read<T>(string fieldId)
        {
            var value = Value(fieldId);
            return (T)m_ReadValue(value.Token, typeof(T) == typeof(object) ? value.Type : typeof(T));
        }

        public bool IsNull(string fieldId) => Value(fieldId).Token.Type == JTokenType.Null;

        public IGraphNodeFieldReader Object(string fieldId)
        {
            var value = Value(fieldId);
            return value.Token.Type == JTokenType.Null ? null :
                new BtsmtlGraphJsonFields((JObject)value.Token, value.Type, m_ReadValue);
        }

        public IReadOnlyList<IGraphNodeFieldReader> Objects(string fieldId)
        {
            var value = Value(fieldId);
            Type element = value.Type.IsArray ? value.Type.GetElementType() : value.Type.GetGenericArguments().Single();
            return ((JArray)value.Token).Select(item => (IGraphNodeFieldReader)
                new BtsmtlGraphJsonFields((JObject)item, element, m_ReadValue)).ToArray();
        }

        (JToken Token, Type Type) Value(string fieldId)
        {
            if (fieldId.StartsWith("input:", StringComparison.Ordinal))
                return (Require((JObject)m_Source["_inputPortValues"], fieldId.Substring(6)), null);
            GraphAuthoringMember member = GraphAuthoringMemberBindings.Contract(NodeType)[fieldId];
            JToken value = m_Source;
            foreach (string part in member.SerializedMemberPath)
                value = Require((JObject)value, part);
            return (value, member.ValueType);
        }

        static JToken Require(JObject owner, string member) => owner.TryGetValue(member, out JToken value) ? value :
            throw new InvalidDataException($"节点作者字段 '{owner.Path}.{member}' 缺失。");
    }
}
