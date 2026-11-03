using System;
using System.Collections.ObjectModel;

namespace BTSMTL.Authoring
{
    // 声明公开读取属性所读的同实例存盘成员路径；引用方读不到属性源码时，作者字段的存盘路径只按此声明解析。
    [AttributeUsage(AttributeTargets.Property, Inherited = false)]
    public sealed class BtsmtlStoredMemberAttribute : Attribute
    {
        public BtsmtlStoredMemberAttribute(params string[] memberPath) =>
            MemberPath = Array.AsReadOnly(memberPath ?? Array.Empty<string>());

        public ReadOnlyCollection<string> MemberPath { get; }
    }
}
