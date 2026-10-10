using UnityEngine.UIElements;

namespace BTSMTL.Editor
{
    public static class VisualElementCallbackExtensions
    {
        public static void RegisterCallbackOnce<TEventType>(
            this CallbackEventHandler target,
            EventCallback<TEventType> callback,
            TrickleDown trickleDown = TrickleDown.NoTrickleDown)
            where TEventType : EventBase<TEventType>, new()
        {
            EventCallback<TEventType> once = null;
            once = evt =>
            {
                target.UnregisterCallback(once, trickleDown);
                callback?.Invoke(evt);
            };

            target.RegisterCallback(once, trickleDown);
        }
    }
}
