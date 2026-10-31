using System.Linq;
using UnityEditor;

namespace BTSMTL.Timeline.Editor
{
    public sealed class TimelineAssetAuthoringDocument : ITimelineAuthoringDocument
    {
        public TimelineAssetAuthoringDocument(UnityEngine.Object source)
        {
            SourcePath = AssetDatabase.GetAssetPath(source);
        }

        public string SourcePath { get; }
        public bool HasUnsavedChanges => AssetDatabase.LoadAllAssetsAtPath(SourcePath).Any(EditorUtility.IsDirty);

        public void Save()
        {
            if (!HasUnsavedChanges)
                return;
            TimelineAuthoringSave.NotifySaving(SourcePath);
            AssetDatabase.SaveAssetIfDirty(AssetDatabase.GUIDFromAssetPath(SourcePath));
        }
    }
}
