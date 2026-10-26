#if UNITY_5_3_OR_NEWER
namespace BTSMTL.Authoring.Graph
{
    public readonly partial struct GraphAuthoringColor
    {
        public static implicit operator GraphAuthoringColor(UnityEngine.Color value) =>
            new GraphAuthoringColor(value.r, value.g, value.b, value.a);

        public static implicit operator UnityEngine.Color(GraphAuthoringColor value) =>
            new UnityEngine.Color(value.Red, value.Green, value.Blue, value.Alpha);
    }
}
#endif
