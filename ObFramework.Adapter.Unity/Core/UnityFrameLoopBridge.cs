using System;
using System.Linq;
using ObFramework.Core.FrameLoop;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace ObFramework.Adapter.Unity.Core
{
    internal struct ObFTick { }
    internal struct ObFFixedTick { }
    internal struct ObFLateTick { }

    public sealed class UnityFrameLoopBridge : IFrameLoopDriver
    {
        private FrameDispatcher _dispatcher;
        private bool _installed;

        public void Bind(FrameDispatcher dispatcher)
        {
            if (dispatcher == null)
                throw new ArgumentNullException(nameof(dispatcher));
            if (_installed)
                throw new InvalidOperationException("Unity frame loop is already bound.");

            var loop = PlayerLoop.GetCurrentPlayerLoop();
            if (Contains(loop, typeof(ObFTick)) ||
                Contains(loop, typeof(ObFFixedTick)) ||
                Contains(loop, typeof(ObFLateTick)))
                throw new InvalidOperationException("Another OBF Unity frame loop is installed.");

            InsertBefore(ref loop, typeof(FixedUpdate), typeof(FixedUpdate.ScriptRunBehaviourFixedUpdate),
                new PlayerLoopSystem { type = typeof(ObFFixedTick), updateDelegate = OnFixedTick });
            InsertBefore(ref loop, typeof(Update), typeof(Update.ScriptRunBehaviourUpdate),
                new PlayerLoopSystem { type = typeof(ObFTick), updateDelegate = OnTick });
            InsertBefore(ref loop, typeof(PreLateUpdate), typeof(PreLateUpdate.ScriptRunBehaviourLateUpdate),
                new PlayerLoopSystem { type = typeof(ObFLateTick), updateDelegate = OnLateTick });

            _dispatcher = dispatcher;
            try
            {
                PlayerLoop.SetPlayerLoop(loop);
                _installed = true;
            }
            catch
            {
                _dispatcher = null;
                throw;
            }
        }

        public void Unbind()
        {
            if (!_installed)
                return;
            _dispatcher = null;
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            var changed = RemoveOwned(ref loop, typeof(ObFTick)) |
                          RemoveOwned(ref loop, typeof(ObFFixedTick)) |
                          RemoveOwned(ref loop, typeof(ObFLateTick));
            if (changed)
                PlayerLoop.SetPlayerLoop(loop);
            _installed = false;
        }

        private void OnTick() => _dispatcher?.Dispatch();
        private void OnFixedTick() => _dispatcher?.DispatchFixed();
        private void OnLateTick() => _dispatcher?.DispatchLate();

        private static bool Contains(PlayerLoopSystem root, Type type)
        {
            if (root.type == type)
                return true;
            var children = root.subSystemList;
            return children != null && children.Any(t => Contains(t, type));
        }

        private static void InsertBefore(ref PlayerLoopSystem root, Type parentType,
            Type beforeType, PlayerLoopSystem inserted)
        {
            var parents = root.subSystemList ?? throw new InvalidOperationException("PlayerLoop root has no children.");
            for (var i = 0; i < parents.Length; i++)
            {
                if (parents[i].type != parentType)
                    continue;
                var source = parents[i].subSystemList ?? throw new InvalidOperationException($"{parentType.Name} has no children.");
                var index = Array.FindIndex(source, item => item.type == beforeType);
                if (index < 0)
                    throw new InvalidOperationException($"{beforeType.Name} was not found.");
                var target = new PlayerLoopSystem[source.Length + 1];
                Array.Copy(source, 0, target, 0, index);
                target[index] = inserted;
                Array.Copy(source, index, target, index + 1, source.Length - index);
                parents[i].subSystemList = target;
                root.subSystemList = parents;
                return;
            }
            throw new InvalidOperationException($"{parentType.Name} was not found.");
        }

        private bool RemoveOwned(ref PlayerLoopSystem root, Type type)
        {
            var children = root.subSystemList;
            if (children == null)
                return false;
            for (var i = 0; i < children.Length; i++)
            {
                var callback = children[i].updateDelegate;
                if (children[i].type == type && callback != null && ReferenceEquals(callback.Target, this))
                {
                    var target = new PlayerLoopSystem[children.Length - 1];
                    Array.Copy(children, 0, target, 0, i);
                    Array.Copy(children, i + 1, target, i, children.Length - i - 1);
                    root.subSystemList = target;
                    return true;
                }
                if (RemoveOwned(ref children[i], type))
                {
                    root.subSystemList = children;
                    return true;
                }
            }
            return false;
        }
    }
}