using System;
using System.Linq;
using UnityEditor;

namespace BTSMTL.Timeline.Editor
{
    public static class TimelineAuthoringSave
    {
        public static event Action<string[]> Saving;

        public static void Save(UnityEngine.Object asset)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            if (!AssetDatabase.LoadAllAssetsAtPath(path).Any(EditorUtility.IsDirty))
                return;
            Saving?.Invoke(new[] { path });
            AssetDatabase.SaveAssetIfDirty(asset);
        }
    }
}
