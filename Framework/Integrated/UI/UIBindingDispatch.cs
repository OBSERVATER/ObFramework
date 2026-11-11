using System;
using System.Threading;

namespace ObFramework.Integrated.UI
{
    /// <summary>
    /// Main-thread marshaling seam for generated engine bindings. Engine adapters configure the UI
    /// main thread when their binding runtime starts; generated binding handlers post
    /// background-originated property/command notifications back to that thread instead of touching
    /// native controls from arbitrary threads. Business Views themselves are not made thread-safe.
    /// </summary>
    public static class UIBindingDispatch
    {
        private static int _mainThreadId = -1;
        private static Action<Action>? _post;

        /// <summary>
        /// True when the current thread is the configured UI main thread (or no main thread has been
        /// configured, in which case dispatched updates run inline for backward compatibility).
        /// </summary>
        public static bool IsMainThread =>
            _mainThreadId < 0 || Environment.CurrentManagedThreadId == _mainThreadId;

        /// <summary>
        /// Configures the UI main thread. <paramref name="post"/> schedules a callback on that thread
        /// (for Unity, the main-thread <see cref="SynchronizationContext"/>). Passing a negative
        /// thread id with a null callback clears the configuration, which is mainly useful in tests.
        /// </summary>
        public static void ConfigureMainThread(int mainThreadId, Action<Action>? post)
        {
            _mainThreadId = mainThreadId;
            _post = post;
        }

        /// <summary>
        /// Runs <paramref name="action"/> on the configured main thread. When already on it, or when
        /// no main-thread scheduler exists, the action runs inline.
        /// </summary>
        public static void Post(Action action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            if (IsMainThread)
            {
                action();
                return;
            }

            var post = _post;
            if (post == null)
            {
                action();
                return;
            }

            post(action);
        }
    }
}
