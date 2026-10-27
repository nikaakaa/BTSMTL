using System;
using System.Collections.Generic;
using BTSMTL.Authoring.Documents;

namespace BTSMTL.Compilation
{
    // 被引用内容的编译值读法：按条目种类把导出字段转为编译记录；Unity 资产来源与独立编译的身份表共用同一读法。
    public sealed class GraphCompileReferenceKinds
    {
        readonly Dictionary<Type, (string Kind, Func<GraphNodeFields, object> Read)> m_Readers = new();

        public GraphCompileReferenceKinds Add<T>(string kind, Func<GraphNodeFields, T> read) where T : class
        {
            m_Readers.Add(typeof(T), (kind, read));
            return this;
        }

        public T Read<T>(AuthoringReferenceEntry entry, IGraphCompileReferences references) where T : class
        {
            if (!m_Readers.TryGetValue(typeof(T), out var reader))
                throw new GraphNodeCompileException($"编译期引用类型 '{typeof(T).Name}' 没有登记读法。");
            if (!string.Equals(reader.Kind, entry.Kind, StringComparison.Ordinal))
                throw new GraphNodeCompileException($"引用 '{entry.Key}' 的种类是 '{entry.Kind}'，节点需要 '{reader.Kind}'。");
            return (T)reader.Read(new GraphNodeFields(entry.Fields, references));
        }
    }

    // 独立编译的引用来源：只认导出的身份表，表中没有的引用直接失败。
    public sealed class GraphCompileReferenceTable : IGraphCompileReferences
    {
        readonly Dictionary<string, AuthoringReferenceEntry> m_Entries = new(StringComparer.Ordinal);
        readonly GraphCompileReferenceKinds m_Kinds;

        public GraphCompileReferenceTable(AuthoringReferenceTable table, GraphCompileReferenceKinds kinds)
        {
            m_Kinds = kinds;
            foreach (AuthoringReferenceEntry entry in table.Entries.Span)
                if (!m_Entries.TryAdd(entry.Key, entry))
                    throw new GraphNodeCompileException($"身份表重复登记引用 '{entry.Key}'。");
        }

        public T Resolve<T>(AuthoringValue value) where T : class
        {
            string key = AuthoringReferenceEntry.KeyOf(value);
            return m_Entries.TryGetValue(key, out AuthoringReferenceEntry entry)
                ? m_Kinds.Read<T>(entry, this)
                : throw new GraphNodeCompileException($"引用 '{key}' 不在身份表中。");
        }
    }
}
