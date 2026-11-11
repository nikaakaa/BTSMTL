#if UNITY_EDITOR || BTSMTL_AUTHORING
using System;

namespace BTSMTL.Authoring.Graph
{
    public sealed partial class BtsmtlGraphDocument : IGraphAuthoringFieldSource
    {
        public GraphAuthoringTypeContract AuthoringFields => s_AuthoringFields;
        public static GraphAuthoringTypeContract AuthoringContract => s_AuthoringFields;

        static readonly GraphAuthoringTypeContract s_AuthoringFields = new(
            new[]
            {
                new GraphAuthoringMember(new GraphAuthoringFieldAttribute("name", GraphAuthoringFieldValueKind.String)
                    { DisplayName = "名称", DocumentMember = GraphAuthoringDocumentMember.DisplayName, NonEmpty = true },
                    typeof(string), null, new[] { "name" }, Array.Empty<GraphAuthoringSubtypeDescriptor>(),
                    target => ((BtsmtlGraphDocument)target).Name,
                    new Func<object, string>(target => ((BtsmtlGraphDocument)target).Name),
                    new Func<object, string>(candidate => ((NameCandidate)candidate).Name),
                    candidate => ((NameCandidate)candidate).Name,
                    (candidate, value) => ((NameCandidate)candidate).Name = (string)value,
                    candidate => ((NameCandidate)candidate).HasChanges, value => value,
                    target => ((BtsmtlGraphDocument)target).Name)
            }, Array.Empty<GraphAuthoringConfiguration>(), (target, complete) => new NameCandidate((BtsmtlGraphDocument)target));

        sealed class NameCandidate : GraphAuthoringCandidate
        {
            readonly string m_Original;
            public NameCandidate(BtsmtlGraphDocument target) => Name = m_Original = target.Name;
            public string Name { get; set; }
            public override bool HasChanges => Name != m_Original;
            public override object Apply(object target)
            {
                var document = (BtsmtlGraphDocument)target;
                document.Configure("name", new Newtonsoft.Json.Linq.JValue(Name));
                return document;
            }
        }
    }
}
#endif
