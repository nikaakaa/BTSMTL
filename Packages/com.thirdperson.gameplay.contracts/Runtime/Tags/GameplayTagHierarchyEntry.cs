namespace ThirdPersonGameplay.Tags
{
    public readonly struct GameplayTagHierarchyEntry
    {
        public GameplayTagHierarchyEntry(string id, string parent)
        {
            Id = id;
            Parent = parent;
        }

        public string Id { get; }
        public string Parent { get; }
    }
}
