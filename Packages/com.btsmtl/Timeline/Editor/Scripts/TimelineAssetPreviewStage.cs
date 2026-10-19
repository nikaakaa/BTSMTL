using System;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BTSMTL.Timeline.Editor
{
    public sealed class TimelineAssetPreviewStage : PreviewSceneStage
    {
        static readonly Func<string, Scene> s_OpenScene = (Func<string, Scene>)typeof(EditorSceneManager)
            .GetMethod("OpenPreviewScene", BindingFlags.Static | BindingFlags.NonPublic, null,
                new[] { typeof(string) }, null).CreateDelegate(typeof(Func<string, Scene>));

        internal string ScenePath { get; set; }
        internal TimelineAssetPreviewHost Owner { get; set; }

        protected override bool OnOpenStage()
        {
            scene = s_OpenScene(ScenePath);
            return true;
        }

        protected override GUIContent CreateHeaderContent() => new("Timeline 资产预览 · 作者输入与假设");

        protected override void OnCloseStage()
        {
            try
            {
                Owner.ReleaseStage();
            }
            finally
            {
                base.OnCloseStage();
            }
        }
    }
}
