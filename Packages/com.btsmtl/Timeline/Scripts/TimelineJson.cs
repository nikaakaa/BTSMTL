#if UNITY_EDITOR || BTSMTL_AUTHORING
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;

namespace BTSMTL.Timeline
{
    public static class TimelineJson
    {
        public const string ContractKindMember = "ContractKind";
        public const string ClipsMember = "m_Clips";
        const string TracksMember = "m_Tracks";

        // 轨道与片段中由时间轴结构本身编辑的成员（身份、合同种类、所含片段与标记、片段起止与片段图），不作为普通字段编辑。
        public static readonly IReadOnlyList<string> TrackStructureMembers =
            new[] { "m_AuthoringId", ContractKindMember, ClipsMember, "m_Markers" };
        public static readonly IReadOnlyList<string> ClipStructureMembers =
            new[] { "m_AuthoringId", ContractKindMember, "m_AssetTree", "m_StartTimeFPRawValue", "m_EndTimeFPRawValue" };

        public static TimelineData Read(JObject source, Func<JToken, Type, object> readValue)
        {
            var timeline = new TimelineData();
            ReadFields(timeline, source, readValue, TracksMember);
            foreach (JObject stored in source[TracksMember])
            {
                var track = (Track)Activator.CreateInstance(TimelineAuthoringTypeCatalog.RequireTrackType((string)stored[ContractKindMember]));
                ReadFields(track, stored, readValue, ClipsMember);
                foreach (JObject clip in stored[ClipsMember])
                    track.Clips.Add((Clip)readValue(clip, track.ClipType));
                timeline.Tracks.Add(track);
            }
            return timeline;
        }

        public static JObject Write(TimelineData source, Func<object, Type, JToken> writeValue)
        {
            JObject result = WriteFields(source, writeValue, TracksMember);
            result[TracksMember] = new JArray(source.Tracks.Select(track =>
            {
                JObject stored = WriteFields(track, writeValue, ClipsMember);
                stored[ContractKindMember] = track.ContractKind;
                stored[ClipsMember] = new JArray(track.Clips.Select(clip =>
                {
                    JObject value = WriteFields(clip, writeValue);
                    value[ContractKindMember] = clip.ContractKind;
                    return value;
                }));
                return stored;
            }));
            return result;
        }

        public static IReadOnlyDictionary<string, FieldInfo> StoredFields(Type type)
        {
            var fields = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
            for (Type current = type; current != null && current != typeof(object)
#if UNITY_EDITOR
                 && current != typeof(UnityEngine.Object)
#endif
                 ; current = current.BaseType)
                foreach (FieldInfo field in current.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (field.IsDefined(typeof(NonSerializedAttribute)) || typeof(Delegate).IsAssignableFrom(field.FieldType))
                        continue;
                    string name = field.IsDefined(typeof(CompilerGeneratedAttribute)) && field.Name.StartsWith("<", StringComparison.Ordinal)
                        ? field.Name.Substring(1, field.Name.IndexOf('>') - 1) : field.Name;
                    fields.Add(type.Namespace == "System.Numerics" ? name.ToLowerInvariant() : name, field);
                }
            return fields;
        }

        static void ReadFields(object target, JObject source, Func<JToken, Type, object> readValue, string excluded)
        {
            foreach (var field in StoredFields(target.GetType()))
                if (field.Key != excluded && source.TryGetValue(field.Key, StringComparison.Ordinal, out JToken stored))
                    field.Value.SetValue(target, readValue(stored, field.Value.FieldType));
        }

        static JObject WriteFields(object source, Func<object, Type, JToken> writeValue, string excluded = null) =>
            new JObject(StoredFields(source.GetType()).Where(field => field.Key != excluded)
                .Select(field => new JProperty(field.Key, writeValue(field.Value.GetValue(source), field.Value.FieldType))));
    }
}
#endif
