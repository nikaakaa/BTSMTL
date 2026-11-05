using System;

namespace BTSMTL.Authoring.Graph
{
    [Serializable]
    public struct GraphAuthoringResourceReference : IEquatable<GraphAuthoringResourceReference>
    {
        public GraphAuthoringResourceReference(string package, string keyKind, string key, string context)
        {
            Package = package;
            KeyKind = keyKind;
            Key = key;
            Context = context;
        }

#if UNITY_5_3_OR_NEWER
        [field: UnityEngine.SerializeField]
#endif
        public string Package { get; private set; }
#if UNITY_5_3_OR_NEWER
        [field: UnityEngine.SerializeField]
#endif
        public string KeyKind { get; private set; }
#if UNITY_5_3_OR_NEWER
        [field: UnityEngine.SerializeField]
#endif
        public string Key { get; private set; }
#if UNITY_5_3_OR_NEWER
        [field: UnityEngine.SerializeField]
#endif
        public string Context { get; private set; }
        public bool IsEmpty => Key == null;

        public bool Equals(GraphAuthoringResourceReference other) => Package == other.Package &&
            KeyKind == other.KeyKind && Key == other.Key && Context == other.Context;
        public override bool Equals(object obj) => obj is GraphAuthoringResourceReference other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Package, KeyKind, Key, Context);
        public static bool operator ==(GraphAuthoringResourceReference left, GraphAuthoringResourceReference right) => left.Equals(right);
        public static bool operator !=(GraphAuthoringResourceReference left, GraphAuthoringResourceReference right) => !left.Equals(right);
        public override string ToString() => IsEmpty ? string.Empty :
            $"{Package.Length}:{Package}{KeyKind.Length}:{KeyKind}{Key.Length}:{Key}{Context.Length}:{Context}";
    }
}
