#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ThirdPersonSimulation;
using UnityEngine;

namespace BTSMTL.Timeline
{
    public enum TimelineAuthoringPropertyKind : byte
    {
        Text,
        Boolean,
        Integer,
        Float,
        Enum,
        Vector2,
        Object
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = true)]
    public sealed class TimelineAuthoringPropertyAttribute : Attribute
    {
        public TimelineAuthoringPropertyAttribute(string propertyId, TimelineAuthoringPropertyKind kind)
        {
            PropertyId = propertyId;
            Kind = kind;
        }

        public TimelineAuthoringPropertyAttribute(string propertyId, Type enumType)
            : this(propertyId, TimelineAuthoringPropertyKind.Enum)
        {
            EnumType = enumType;
        }

        public string PropertyId { get; }
        public TimelineAuthoringPropertyKind Kind { get; }
        public Type EnumType { get; }
        public string MemberName { get; set; }
        public bool Optional { get; set; }
        public bool Trimmed { get; set; }
        public bool HasMinimum { get; set; }
        public double Minimum { get; set; }
        public bool HasMaximum { get; set; }
        public double Maximum { get; set; }
        public bool Finite { get; set; }
        public bool SourceMappingOnly { get; set; }
        public bool IsSourceAsset { get; set; }
        public TimelineBindingValueKind[] BindingValueKinds { get; set; } = Array.Empty<TimelineBindingValueKind>();
        public TimelineBindingAccess BindingAccess { get; set; }
        public TimelineBindingLifetime BindingLifetime { get; set; }
        public bool Supports(Clip clip) => Supports(clip, TimelineAuthoringClipBinding.SourceAsset(clip));
        public bool Supports(Clip clip, UnityEngine.Object source) =>
            !SourceMappingOnly || source is ITimelineAuthoringSourceAsset;
    }

    public interface ITimelineAuthoringClipSource
    {
        UnityEngine.Object SourceAsset { get; }
        Type SourceAssetType { get; }
        float GetSourceDuration(UnityEngine.Object source);
        string SourceMissingErrorCode { get; }
        string SourceMissingError { get; }
    }

    public sealed class TimelineAuthoringClipConfiguration
    {
        readonly Dictionary<string, object> m_Values = new Dictionary<string, object>(StringComparer.Ordinal);
        public object this[string propertyId]
        {
            get => m_Values[propertyId];
            set => m_Values[propertyId] = value;
        }
        public T Get<T>(string propertyId) => (T)m_Values[propertyId];
        public void Set(string propertyId, object value) => m_Values[propertyId] = value;
    }

    public static class TimelineAuthoringClipBinding
    {
        const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static UnityEngine.Object SourceAsset(Clip clip) =>
            clip is ITimelineAuthoringClipSource source ? source.SourceAsset : null;

        public static IReadOnlyList<TimelineAuthoringPropertyAttribute> Properties(Clip clip) =>
            clip.GetType().GetCustomAttributes(typeof(TimelineAuthoringPropertyAttribute), true)
                .Cast<TimelineAuthoringPropertyAttribute>()
                .OrderBy(property => property.PropertyId, StringComparer.Ordinal).ToArray();

        public static TimelineAuthoringClipConfiguration Read(Clip clip)
        {
            var result = new TimelineAuthoringClipConfiguration();
            foreach (TimelineAuthoringPropertyAttribute property in Properties(clip))
            {
                MemberInfo member = RequireMember(clip.GetType(), property.MemberName);
                result.Set(property.PropertyId, member is PropertyInfo info
                    ? info.GetValue(clip)
                    : ((FieldInfo)member).GetValue(clip));
            }
            return result;
        }

        public static TimelineAuthoringClipConfiguration Defaults(Clip clip) =>
            ((Clip)Activator.CreateInstance(clip.GetType(), clip.Track, clip.StartTime)).CaptureAuthoringDefaults();

        public static Type ValueType(Clip clip, TimelineAuthoringPropertyAttribute property)
        {
            MemberInfo member = RequireMember(clip.GetType(), property.MemberName);
            return member is PropertyInfo info ? info.PropertyType : ((FieldInfo)member).FieldType;
        }

        public static void Configure(TimelineData timeline, Clip clip,
            TimelineAuthoringClipConfiguration configuration) =>
            clip.ApplyAuthoringConfiguration(configuration);

        public static void ApplyMembers(Clip clip, TimelineAuthoringClipConfiguration configuration)
        {
            foreach (TimelineAuthoringPropertyAttribute property in Properties(clip))
            {
                MemberInfo member = RequireMember(clip.GetType(), property.MemberName);
                object value = configuration[property.PropertyId];
                if (member is PropertyInfo info)
                    info.SetValue(clip, value);
                else
                    ((FieldInfo)member).SetValue(clip, value);
            }
        }

        static MemberInfo RequireMember(Type owner, string name)
        {
            for (Type type = owner; type != null; type = type.BaseType)
            {
                MemberInfo member = type.GetProperty(name, Members | BindingFlags.DeclaredOnly) ??
                    (MemberInfo)type.GetField(name, Members | BindingFlags.DeclaredOnly);
                if (member != null)
                    return member;
            }
            throw new InvalidOperationException($"Timeline authoring member '{owner.FullName}.{name}' is not declared.");
        }
    }
}
#endif
