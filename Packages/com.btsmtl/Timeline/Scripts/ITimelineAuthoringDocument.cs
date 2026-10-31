#if UNITY_EDITOR
namespace BTSMTL.Timeline
{
    public interface ITimelineAuthoringDocument
    {
        string SourcePath { get; }
        bool HasUnsavedChanges { get; }
        void Save();
    }
}
#endif
