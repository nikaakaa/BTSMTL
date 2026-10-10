using System;
using UnityEditor;

namespace BTSMTL.Editor
{
    public sealed class EditorCoroutine
    {
        Action stop;

        internal EditorCoroutine(Action stop)
        {
            this.stop = stop;
        }

        public void Stop()
        {
            stop?.Invoke();
            stop = null;
        }
    }

    public static class EditorCoroutineHelper
    {
        public static EditorCoroutine Delay(Action callback, float timer)
        {
            double start = EditorApplication.timeSinceStartup;
            EditorApplication.CallbackFunction update = null;
            EditorCoroutine handle = null;
            update = () =>
            {
                if (EditorApplication.timeSinceStartup - start < timer)
                    return;

                EditorApplication.update -= update;
                callback?.Invoke();
                handle?.Stop();
            };
            handle = new EditorCoroutine(() => EditorApplication.update -= update);
            EditorApplication.update += update;
            return handle;
        }

        public static EditorCoroutine WaitWhile(Action callback, Func<bool> func)
        {
            EditorApplication.CallbackFunction update = null;
            EditorCoroutine handle = null;
            update = () =>
            {
                if (func != null && func())
                    return;

                EditorApplication.update -= update;
                callback?.Invoke();
                handle?.Stop();
            };
            handle = new EditorCoroutine(() => EditorApplication.update -= update);
            EditorApplication.update += update;
            return handle;
        }

        public static EditorCoroutine WaitOneFrame(Action callback)
        {
            bool waited = false;
            EditorApplication.CallbackFunction update = null;
            EditorCoroutine handle = null;
            update = () =>
            {
                if (!waited)
                {
                    waited = true;
                    return;
                }

                EditorApplication.update -= update;
                callback?.Invoke();
                handle?.Stop();
            };
            handle = new EditorCoroutine(() => EditorApplication.update -= update);
            EditorApplication.update += update;
            return handle;
        }
    }
}
