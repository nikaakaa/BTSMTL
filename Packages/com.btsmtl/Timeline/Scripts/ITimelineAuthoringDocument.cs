#if UNITY_EDITOR
namespace BTSMTL.Timeline
{
    public interface ITimelineAuthoringDocument
    {
        string SourcePath { get; }
        bool HasUnsavedChanges { get; }
        void Prepare(TimelineData timeline, UnityEngine.ScriptableObject graph = null);
        UnityEngine.ScriptableObject ResolveGraph(string authoringId);
        void Save();
    }
}
#endif
