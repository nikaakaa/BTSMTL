namespace BTSMTL.Authoring.Graph
{
    public readonly partial struct GraphAuthoringColor
    {
        public GraphAuthoringColor(float red, float green, float blue, float alpha = 1f)
        {
            Red = red;
            Green = green;
            Blue = blue;
            Alpha = alpha;
        }

        public float Red { get; }
        public float Green { get; }
        public float Blue { get; }
        public float Alpha { get; }
    }
}
