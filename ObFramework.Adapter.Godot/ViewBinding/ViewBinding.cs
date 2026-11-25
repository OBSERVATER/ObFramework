using System;
using System.Collections.Generic;
using Godot;
using ObFramework.Core;
using ObFramework.Extension.Integrated;
using ObFramework.Integrated.Clock;
using ObFramework.Integrated.UI;

namespace ObFramework.Adapter.Godot.ViewBinding
{
    internal sealed class ViewBinding
    {
        private readonly GodotViewBindings _owner;
        private readonly Queue<bool> _transitions = new();
        private IController? _view;
        private List<IFrameTickable>? _ticks;
        private bool _requestedEnabled;
        private bool _notifiedEnabled;
        private bool _ready;
        private bool _processing;
        private bool _released;
        private bool _ticking;

        public Node Controller { get; }
        public ulong Id { get; }
        public string Description { get; }
        public IController? View => _view;
        public bool IsReleased => _released;
        public bool RequestedEnabled => _requestedEnabled;

        public ViewBinding(GodotViewBindings owner, Node controller, bool enabled)
        {
            _owner = owner;
            Controller = controller;
            Id = controller.GetInstanceId();
            Description = $"'{controller.GetPath()}' ({controller.GetType().FullName})";
            _requestedEnabled = enabled;
        }

        public void Initialize(IController view)
        {
            _view = view;
            if (_released) FinishRelease();
        }

        public void CompleteReady()
        {
            if (_released || _ready || _view == null) return;
            _processing = true;
            try
            {
                if (Controller is not IUIWindow && _view is AUIView childView)
                    childView.InitView(null);
                if (_released) return;
                if (_view is ITickable tick) AddRegistration(new UpdateRegistration(this, tick));
                if (_view is IFixedTickable fixedTick) AddRegistration(new FixedRegistration(this, fixedTick));
                if (_view is ILateTickable lateTick) AddRegistration(new LateRegistration(this, lateTick));
                if (_view is ILogicTickable logicTick) AddRegistration(new LogicRegistration(this, logicTick));
                _ready = true;
            }
            finally
            {
                _processing = false;
                if (_released) FinishRelease();
            }
            if (_requestedEnabled) ApplyEnabled(true);
        }

        public void ChangeEnabled(bool enabled)
        {
            if (_released || _requestedEnabled == enabled || (enabled && !_owner.IsReady)) return;
            _requestedEnabled = enabled;
            if (_ready) ApplyEnabled(enabled);
        }

        private void ApplyEnabled(bool enabled)
        {
            if (_released || _view == null) return;
            if (!enabled) StopTick();
            _transitions.Enqueue(enabled);
            if (_processing) return;
            _processing = true;
            try
            {
                while (!_released && _transitions.Count > 0)
                {
                    var nextEnabled = _transitions.Dequeue();
                    if (nextEnabled == _notifiedEnabled || (nextEnabled && !_owner.IsReady)) continue;
                    _notifiedEnabled = nextEnabled;
                    if (nextEnabled) (_view as IViewEnabled)?.OnViewEnabled();
                    else (_view as IViewDisabled)?.OnViewDisabled();
                }
            }
            finally
            {
                _processing = false;
                if (_released) FinishRelease();
            }
            if (CanTick) StartTick();
        }

        private bool CanTick => !_released && _ready && _requestedEnabled && _notifiedEnabled && _owner.IsReady &&
            GodotObject.IsInstanceValid(Controller) && Controller.IsInsideTree() && !Controller.IsQueuedForDeletion();

        private void AddRegistration(IFrameTickable tick) => (_ticks ??= new List<IFrameTickable>(4)).Add(tick);

        private void StartTick()
        {
            if (_ticking || _ticks == null) return;
            _ticking = true;
            foreach (var tick in _ticks) tick.Register(_view!);
        }

        private void StopTick()
        {
            if (!_ticking || _ticks == null) return;
            _ticking = false;
            foreach (var tick in _ticks) tick.UnRegister(_view!);
        }

        public void Release()
        {
            if (!_released)
            {
                _released = true;
                try { StopTick(); }
                finally
                {
                    _ticks = null;
                    _transitions.Clear();
                    if (!_processing) FinishRelease();
                }
            }
            else if (!_processing) FinishRelease();
        }

        private void FinishRelease()
        {
            var view = _view;
            _view = null;
            if (view == null) return;
            Exception? disableError = null;
            if (_notifiedEnabled)
            {
                _notifiedEnabled = false;
                try { (view as IViewDisabled)?.OnViewDisabled(); }
                catch (Exception error) { disableError = error; }
            }
            try { (view as IDisposable)?.Dispose(); }
            catch (Exception error) when (disableError != null) { throw new AggregateException(disableError, error); }
            if (disableError != null) throw new AggregateException(disableError);
        }

        private sealed class UpdateRegistration : ITickable
        {
            private readonly ViewBinding _binding;
            private readonly ITickable _view;
            public UpdateRegistration(ViewBinding binding, ITickable view) { _binding = binding; _view = view; }
            public void Tick() { if (_binding.CanTick) _view.Tick(); }
        }
        private sealed class FixedRegistration : IFixedTickable
        {
            private readonly ViewBinding _binding;
            private readonly IFixedTickable _view;
            public FixedRegistration(ViewBinding binding, IFixedTickable view) { _binding = binding; _view = view; }
            public void FixedTick() { if (_binding.CanTick) _view.FixedTick(); }
        }
        private sealed class LogicRegistration : ILogicTickable
        {
            private readonly ViewBinding _binding;
            private readonly ILogicTickable _view;
            public LogicRegistration(ViewBinding binding, ILogicTickable view) { _binding = binding; _view = view; }
            public void LogicTick() { if (_binding.CanTick) _view.LogicTick(); }
        }
        private sealed class LateRegistration : ILateTickable
        {
            private readonly ViewBinding _binding;
            private readonly ILateTickable _view;
            public LateRegistration(ViewBinding binding, ILateTickable view) { _binding = binding; _view = view; }
            public void LateTick() { if (_binding.CanTick) _view.LateTick(); }
        }
    }
}
