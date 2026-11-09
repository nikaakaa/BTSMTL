using System;
using System.Collections.Generic;
using System.Reflection;

namespace BTSMTL.Orchestration
{
    // 对象句柄的种类：作者期与编译期的约束身份，不进入运行时句柄值。
    // 核心只内置根种类“任意对象”；具体种类由领域声明并指定父种类，兼容关系即“等于或是其后代”。
    public sealed class OrchestrationObjectKind
    {
        public const string AnyId = "btsmtl.object/any";

        public static readonly OrchestrationObjectKind Any = new OrchestrationObjectKind(AnyId, "任意对象");

        OrchestrationObjectKind(string id, string displayName)
        {
            Id = id;
            DisplayName = displayName;
        }

        public OrchestrationObjectKind(string id, string displayName, OrchestrationObjectKind parent)
        {
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("对象种类需要身份。", nameof(id));
            if (string.IsNullOrEmpty(displayName))
                throw new ArgumentException("对象种类需要显示名。", nameof(displayName));
            Id = id;
            DisplayName = displayName;
            Parent = parent ?? throw new ArgumentNullException(nameof(parent));
        }

        public string Id { get; }
        public string DisplayName { get; }
        public OrchestrationObjectKind Parent { get; }

        public bool IsAssignableTo(OrchestrationObjectKind target)
        {
            if (target == null)
                return false;
            for (OrchestrationObjectKind current = this; current != null; current = current.Parent)
                if (ReferenceEquals(current, target))
                    return true;
            return false;
        }

        public override string ToString() => DisplayName;
    }

    // 领域在程序集上声明种类所在的静态类型；其公开静态 OrchestrationObjectKind 字段即领域种类。
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class OrchestrationObjectKindSetAttribute : Attribute
    {
        public OrchestrationObjectKindSetAttribute(Type declaringType) => DeclaringType = declaringType;

        public Type DeclaringType { get; }
    }

    // 按身份查找种类，供作者数据读取与作者工具使用；运行时执行不经过这里。
    public static class OrchestrationObjectKinds
    {
        static readonly object s_Gate = new object();
        static Dictionary<string, OrchestrationObjectKind> s_Kinds;
        static int s_AssemblyCount;

        public static OrchestrationObjectKind Require(string id) => TryGet(id, out OrchestrationObjectKind kind) ? kind
            : throw new InvalidOperationException($"对象种类 '{id}' 没有领域声明。");

        public static bool TryGet(string id, out OrchestrationObjectKind kind)
        {
            if (id == null)
            {
                kind = null;
                return false;
            }
            return Current().TryGetValue(id, out kind);
        }

        public static IReadOnlyCollection<OrchestrationObjectKind> All => Current().Values;

        static Dictionary<string, OrchestrationObjectKind> Current()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            lock (s_Gate)
            {
                if (s_Kinds != null && s_AssemblyCount == assemblies.Length)
                    return s_Kinds;
                var kinds = new Dictionary<string, OrchestrationObjectKind>(StringComparer.Ordinal) { [OrchestrationObjectKind.AnyId] = OrchestrationObjectKind.Any };
                foreach (Assembly assembly in assemblies)
                    foreach (OrchestrationObjectKindSetAttribute set in assembly.GetCustomAttributes<OrchestrationObjectKindSetAttribute>())
                        foreach (FieldInfo field in set.DeclaringType.GetFields(BindingFlags.Public | BindingFlags.Static))
                        {
                            if (field.FieldType != typeof(OrchestrationObjectKind))
                                continue;
                            var kind = (OrchestrationObjectKind)field.GetValue(null);
                            if (kinds.TryGetValue(kind.Id, out OrchestrationObjectKind existing) && !ReferenceEquals(existing, kind))
                                throw new InvalidOperationException($"对象种类身份 '{kind.Id}' 重复声明。");
                            kinds[kind.Id] = kind;
                        }
                s_Kinds = kinds;
                s_AssemblyCount = assemblies.Length;
                return kinds;
            }
        }
    }
}
