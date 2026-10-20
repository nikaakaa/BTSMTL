using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BTSMTL.Timeline.Editor
{
    // 预览入口的具名输入方案：与作者 Timeline 并列存为编辑器侧数据资产，可提交，不参与运行时。
    public sealed class TimelinePreviewInputSchemes : ScriptableObject
    {
        public List<TimelineAssetPreviewPreferences> Schemes = new();
        public int Selected;

        public TimelineAssetPreviewPreferences Current => Schemes[Mathf.Clamp(Selected, 0, Schemes.Count - 1)];

        public static string PathFor(TimelineAsset source)
        {
            string path = AssetDatabase.GetAssetPath(source);
            return path.Length == 0 ? string.Empty
                : System.IO.Path.ChangeExtension(path, null).Replace('\\', '/') + ".PreviewInputs.asset";
        }

        public static TimelinePreviewInputSchemes Load(TimelineAsset source)
        {
            string path = PathFor(source);
            return path.Length == 0 ? null : AssetDatabase.LoadAssetAtPath<TimelinePreviewInputSchemes>(path);
        }

        public static TimelinePreviewInputSchemes Create(TimelineAsset source, TimelineAssetPreviewPreferences first)
        {
            string path = PathFor(source);
            if (path.Length == 0)
                throw new System.InvalidOperationException($"Timeline '{source.name}' 不是独立资产，无法保存输入方案。");
            var schemes = CreateInstance<TimelinePreviewInputSchemes>();
            schemes.Schemes.Add(first);
            AssetDatabase.CreateAsset(schemes, path);
            return schemes;
        }
    }
}
