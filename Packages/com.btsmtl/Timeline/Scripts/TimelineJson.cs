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
        public static TimelineData Read(JObject source, Func<JToken, Type, object> readValue)
        {
            var timeline = new TimelineData();
            ReadFields(timeline, source, readValue, "m_Tracks");
            foreach (JObject stored in source["m_Tracks"])
            {
                var track = (Track)Activator.CreateInstance(TimelineAuthoringTypeCatalog.RequireTrackType((string)stored["ContractKind"]));
                ReadFields(track, stored, readValue, "m_Clips");
                foreach (JObject clip in stored["m_Clips"])
                    track.Clips.Add((Clip)readValue(clip, track.ClipType));
                timeline.Tracks.Add(track);
            }
            return timeline;
        }

        public static JObject Write(TimelineData source, Func<object, Type, JToken> writeValue)
        {
            JObject result = WriteFields(source, writeValue, "m_Tracks");
            result["m_Tracks"] = new JArray(source.Tracks.Select(track =>
            {
                JObject stored = WriteFields(track, writeValue, "m_Clips");
                stored["ContractKind"] = track.ContractKind;
                stored["m_Clips"] = new JArray(track.Clips.Select(clip =>
                {
                    JObject value = WriteFields(clip, writeValue);
                    value["ContractKind"] = clip.ContractKind;
                    return value;
                }));
                return stored;
            }));
            return result;
        }

        public static IReadOnlyDictionary<string, FieldInfo> StoredFields(Type type)
        {
            var fields = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
            for (Type current = type; current != null && current != typeof(object); current = current.BaseType)
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
