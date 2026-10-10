#if UNITY_EDITOR
using UnityEditor;

namespace BTSMTL.Editor
{
    public static class BTSMTLEditorUtility
    {
        [InitializeOnLoadMethod]
        static void OnLoaded()
        {
            DeltaTime = LastTimeSinceStartup = 0;
            EditorApplication.update += SetEditorDeltaTime;
        }

        public static double DeltaTime { get; private set; } = 0;
        static double LastTimeSinceStartup = 0f;
        static void SetEditorDeltaTime()
        {
            if (LastTimeSinceStartup == 0f)
            {
                LastTimeSinceStartup = EditorApplication.timeSinceStartup;
            }
            DeltaTime = EditorApplication.timeSinceStartup - LastTimeSinceStartup;
            LastTimeSinceStartup = EditorApplication.timeSinceStartup;
        }
    }
}
#endif