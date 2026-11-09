using System;
using System.Collections.Generic;

namespace CarSales.Foundation
{
    /// <summary>
    /// Typed publish/subscribe. Systems publish small event structs; UI and other systems listen.
    /// A throwing handler never stops the other handlers (the error goes to <see cref="OnHandlerError"/>).
    /// </summary>
    public static class EventBus
    {
        static readonly List<Action> resetters = new List<Action>();

        /// <summary>Set by the runtime to Debug.LogException.</summary>
        public static Action<Exception> OnHandlerError;

        static class Channel<T>
        {
            public static Action<T> handlers;
            static Channel() { lock (resetters) resetters.Add(() => handlers = null); }
        }

        public static void Subscribe<T>(Action<T> handler) { if (handler != null) Channel<T>.handlers += handler; }

        public static void Unsubscribe<T>(Action<T> handler) { if (handler != null) Channel<T>.handlers -= handler; }

        public static void Publish<T>(T evt)
        {
            var h = Channel<T>.handlers;
            if (h == null) return;
            foreach (var d in h.GetInvocationList())
            {
                try { ((Action<T>)d)(evt); }
                catch (Exception e) { OnHandlerError?.Invoke(e); }
            }
        }

        public static bool HasSubscribers<T>() => Channel<T>.handlers != null;

        /// <summary>Removes every subscription (called when play mode starts, and by tests).</summary>
        public static void Clear()
        {
            lock (resetters) foreach (var r in resetters) r();
        }
    }
}
