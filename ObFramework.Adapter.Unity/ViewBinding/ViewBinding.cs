using System;
using System.Collections.Generic;
using ObFramework.Core;
using ObFramework.Core.FrameLoop;
using UnityEngine;

namespace ObFramework.Adapter.Unity.ViewBinding
{
    internal sealed class ViewBinding
    {
        private readonly UnityViewBindings _owner;
        private readonly Queue<bool> _transitions = new();
        private object? _view;
        private List<IFrameTickable>? _ticks;
        private bool _requestedEnabled;
        private bool _notifiedEnabled;
        private bool _processing;
        private bool _released;
        private int _enabledFrame;

        public MonoBehaviour Controller { get; }
        public int Id { get; }
        public string Description { get; }

        /// <summary>
        /// The business View constructed by the generated binding, or null before initialization /
        /// after release. The adapter resolves the paired View through this single owner.
        /// </summary>
        public object? View => _view;

        public ViewBinding(UnityViewBindings owner, MonoBehaviour controller, string description)
        {
            _owner = owner;
            Controller = controller;
            Id = controller.GetInstanceID();
            Description = description;
        }

        public void Initialize(object view)
        {
            _view = view;
            if (_released)
                FinishRelease();
            else
            {
                // Each binding owns its registrations even if the business View overrides equality.
                if (view is ITickable tick)
                    AddRegistration(new UpdateRegistration(tick));
                if (view is IFixedTickable fixedTick)
                    AddRegistration(new FixedRegistration(fixedTick));
                if (view is ILateTickable lateTick)
                    AddRegistration(new LateRegistration(lateTick));

                ChangeEnabled(Controller && Controller.isActiveAndEnabled);
            }
        }

        public void ChangeEnabled(bool enabled)
        {
            if (_view == null || _released || _requestedEnabled == enabled || (enabled && !_owner.IsReady))
                return;

            _requestedEnabled = enabled;
            if (!enabled)
                StopTick();

            _transitions.Enqueue(enabled);
            if (_processing)
                return;

            _processing = true;
            try
            {
                while (!_released && _transitions.Count > 0)
                {
                    var nextEnabled = _transitions.Dequeue();
                    if (nextEnabled == _notifiedEnabled || (nextEnabled && !_owner.IsReady))
                        continue;
                    _notifiedEnabled = nextEnabled;
                    if (nextEnabled)
                        (_view as IViewEnabled)?.OnViewEnabled();
                    else
                    {
                        try { (_view as IViewDisabled)?.OnViewDisabled(); }
                        catch (Exception error)
                        {
                            Debug.LogError(new InvalidOperationException($"Failed to disable view for {Description}.", error), Controller);
                        }
                    }
                }
            }
            finally { _processing = false; }

            if (_released)
                FinishRelease();
            else if (CanTick && _ticks != null)
            {
                if (_view is IViewEnabled)
                {
                    // Native Destroy can be deferred even while isActiveAndEnabled remains true.
                    _enabledFrame = Time.frameCount;
                    _owner.DeferTick(this);
                }
                else
                    StartTick();
            }
        }

        private bool CanTick =>
            _notifiedEnabled && Controller && Controller.isActiveAndEnabled && _owner.IsReady;

        public bool TryStartDeferredTick()
        {
            if (!CanTick || _ticks == null)
                return true;
            if (_enabledFrame == Time.frameCount)
                return false;

            StartTick();
            return true;
        }

        private void AddRegistration(IFrameTickable tick) => (_ticks ??= new List<IFrameTickable>(3)).Add(tick);

        private void StartTick()
        {
            foreach (var tick in _ticks!)
                _owner.Frame.Register(tick);
        }

        private void StopTick()
        {
            _owner.CancelDeferredTick(this);
            if (_ticks == null)
                return;
            foreach (var tick in _ticks)
                _owner.Frame.UnRegister(tick);
        }

        public void Release()
        {
            if (!_released)
            {
                _released = true;
                StopTick();
                _ticks = null;
                _transitions.Clear();
            }

            if (!_processing)
                FinishRelease();
        }

        private void FinishRelease()
        {
            var view = _view;
            _view = null;
            if (view == null)
                return;

            Exception? disableError = null;
            if (_notifiedEnabled)
            {
                _notifiedEnabled = false;
                try { (view as IViewDisabled)?.OnViewDisabled(); }
                catch (Exception error) { disableError = error; }
            }

            try { (view as IDisposable)?.Dispose(); }
            catch (Exception error) when (disableError != null)
            {
                throw new AggregateException(disableError, error);
            }
            if (disableError != null)
                throw new AggregateException(disableError);
        }

        private sealed class UpdateRegistration : ITickable
        {
            private readonly ITickable _view;
            public UpdateRegistration(ITickable view) => _view = view;
            public void Tick() => _view.Tick();
        }

        private sealed class FixedRegistration : IFixedTickable
        {
            private readonly IFixedTickable _view;
            public FixedRegistration(IFixedTickable view) => _view = view;
            public void FixedTick() => _view.FixedTick();
        }

        private sealed class LateRegistration : ILateTickable
        {
            private readonly ILateTickable _view;
            public LateRegistration(ILateTickable view) => _view = view;
            public void LateTick() => _view.LateTick();
        }
    }
}
