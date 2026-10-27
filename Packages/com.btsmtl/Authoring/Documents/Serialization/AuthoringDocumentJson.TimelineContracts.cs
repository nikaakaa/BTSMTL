using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BTSMTL.Orchestration;
using BTSMTL.Timeline;
using Newtonsoft.Json.Linq;

namespace BTSMTL.Authoring.Documents
{
    public static partial class AuthoringDocumentJson
    {
        public static string WriteTimelineContracts(IEnumerable<TimelineTrackContract> tracks,
            IEnumerable<TimelineClipDefinition> clips) => Write(new JObject
        {
            ["tracks"] = new JArray(tracks.OrderBy(track => track.Kind, StringComparer.Ordinal).Select(track => new JObject
            {
                ["kind"] = track.Kind,
                ["overlapPolicy"] = track.OverlapPolicy.ToString(),
                ["contentKind"] = track.ContentKind.ToString(),
                ["executionDomain"] = track.ExecutionPolicy.Domain.ToString(),
                ["outputKind"] = track.ExecutionPolicy.OutputKind.ToString(),
                ["allowedExecutionDomains"] = track.AllowedExecutionDomains.ToString(),
                ["logicSampleExtension"] = track.LogicSampleExtension.HasValue ? WriteLogicSample(track.LogicSampleExtension.Value) : JValue.CreateNull(),
                ["allowedClipKinds"] = new JArray(track.AllowedClipKinds)
            })),
            ["clips"] = new JArray(clips.OrderBy(clip => clip.Kind, StringComparer.Ordinal).Select(clip => new JObject
            {
                ["kind"] = clip.Kind,
                ["trackKind"] = clip.TrackKind,
                ["executionPhase"] = clip.SupportedExecutionPhases.ToString(),
                ["contentKind"] = clip.ContentKind.ToString(),
                ["supportsStop"] = clip.SupportsStop,
                ["requiresPositiveDuration"] = clip.RequiresPositiveDuration,
                ["executionDomain"] = clip.ExecutionPolicy.Domain.ToString(),
                ["outputKind"] = clip.ExecutionPolicy.OutputKind.ToString(),
                ["allowedExecutionDomains"] = clip.AllowedExecutionDomains.ToString(),
                ["bindings"] = new JArray(clip.Bindings.Select(binding => new JObject
                {
                    ["bindingId"] = binding.BindingId,
                    ["valueKind"] = binding.ValueKind.ToString(),
                    ["access"] = binding.Access.ToString(),
                    ["lifetime"] = binding.Lifetime.ToString()
                }))
            }))
        });

        public static TimelineDefinitionCatalog<TimelineClipDefinition> ReadTimelineContracts(
            string text, string sourcePath)
        {
            JObject document = ReadObject(text);
            TimelineTrackContract[] tracks = Array(document, "tracks", sourcePath).Select(track =>
            {
                JToken extension = Required(track, "logicSampleExtension", sourcePath);
                return new TimelineTrackContract(Text(track, "kind", sourcePath),
                    Enumeration<TimelineTrackOverlapPolicy>(track, "overlapPolicy", sourcePath),
                    Enumeration<TimelineContentKind>(track, "contentKind", sourcePath),
                    Enumeration<TimelineExecutionDomain>(track, "executionDomain", sourcePath),
                    Enumeration<TimelineOutputKind>(track, "outputKind", sourcePath),
                    ExecutionDomains(track, "allowedExecutionDomains", sourcePath),
                    extension.Type == JTokenType.Null ? null : ReadLogicSample(extension, sourcePath),
                    Array(track, "allowedClipKinds", sourcePath).Select(value => Text(value, sourcePath)).ToArray());
            }).ToArray();
            TimelineClipDefinition[] clips = Array(document, "clips", sourcePath).Select(clip =>
                new TimelineClipDefinition(Text(clip, "kind", sourcePath), Text(clip, "trackKind", sourcePath),
                    Enumeration<TimelineClipExecutionPhase>(clip, "executionPhase", sourcePath),
                    Enumeration<TimelineContentKind>(clip, "contentKind", sourcePath),
                    Boolean(clip, "supportsStop", sourcePath), Boolean(clip, "requiresPositiveDuration", sourcePath),
                    Enumeration<TimelineExecutionDomain>(clip, "executionDomain", sourcePath),
                    Enumeration<TimelineOutputKind>(clip, "outputKind", sourcePath),
                    ExecutionDomains(clip, "allowedExecutionDomains", sourcePath),
                    Array(clip, "bindings", sourcePath).Select(binding => new TimelineBindingRequirement(
                        Text(binding, "bindingId", sourcePath),
                        Enumeration<ThirdPersonSimulation.TimelineBindingValueKind>(binding, "valueKind", sourcePath),
                        Enumeration<ThirdPersonSimulation.TimelineBindingAccess>(binding, "access", sourcePath),
                        Enumeration<ThirdPersonSimulation.TimelineBindingLifetime>(binding, "lifetime", sourcePath))).ToArray())).ToArray();
            return ReceiveMembers(document, new TimelineDefinitionCatalog<TimelineClipDefinition>(tracks, clips), sourcePath);
        }

        static JObject WriteLogicSample(TimelineLogicSampleExtension extension) => new JObject
        {
            ["domainId"] = extension.Identity.DomainId,
            ["operationId"] = extension.Identity.OperationId,
            ["semanticVersion"] = extension.SemanticVersion.ToString(CultureInfo.InvariantCulture)
        };

        static TimelineLogicSampleExtension ReadLogicSample(JToken extension, string sourcePath) => new(
            new OrchestrationExtensionIdentity(Text(extension, "domainId", sourcePath), Text(extension, "operationId", sourcePath)),
            UInt32Text(extension, "semanticVersion", sourcePath));

        static TimelineExecutionDomainMask ExecutionDomains(JToken token, string name, string sourcePath)
        {
            string value = Text(token, name, sourcePath);
            TimelineExecutionDomainMask declared = System.Enum.GetValues(typeof(TimelineExecutionDomainMask))
                .Cast<TimelineExecutionDomainMask>().Aggregate(TimelineExecutionDomainMask.None, (mask, domain) => mask | domain);
            if (!System.Enum.TryParse(value, false, out TimelineExecutionDomainMask result) || (result & ~declared) != 0)
                throw new AuthoringDocumentFormatException(sourcePath, token[name].Path, $"未知 TimelineExecutionDomainMask 值 '{value}'。");
            return result;
        }

        static uint UInt32Text(JToken token, string name, string sourcePath)
        {
            string value = Text(token, name, sourcePath);
            if (!uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint result))
                throw new AuthoringDocumentFormatException(sourcePath, token[name].Path, "必须使用 UInt32 范围内的无损文本整数。");
            return result;
        }

        static bool Boolean(JToken token, string name, string sourcePath)
        {
            JToken value = Required(token, name, sourcePath);
            if (value.Type != JTokenType.Boolean)
                throw new AuthoringDocumentFormatException(sourcePath, value.Path, "必须使用 JSON Boolean。");
            return value.Value<bool>();
        }
    }
}
