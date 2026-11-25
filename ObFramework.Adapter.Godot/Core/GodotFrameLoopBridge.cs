using System;
using Godot;
using ObFramework.Adapter.Godot.ViewBinding;
using ObFramework.Integrated.Clock;

namespace ObFramework.Adapter.Godot.Core
{
    public sealed partial class GodotFrameLoopBridge : Node, IFrameLoopDriver
    {
        private readonly Func<bool> _canDispatch;
        private ClockPlugin? _clock;
        private GodotLateFrameLoopNode? _late;

        public GodotFrameLoopBridge() => _canDispatch = () => _clock != null && (GodotViewBindings.Current?.IsReady ?? true);

        public override void _Ready()
        {
            ProcessPriority = int.MinValue;
            ProcessPhysicsPriority = int.MinValue;
            SetProcess(false);
            SetPhysicsProcess(false);
            _late = new GodotLateFrameLoopNode { ProcessPriority = int.MaxValue, Bridge = this };
            AddChild(_late);
            _late.SetProcess(false);
        }

        public void Bind(ClockPlugin clock)
        {
            ArgumentNullException.ThrowIfNull(clock);
            if (_clock != null) throw new InvalidOperationException("Godot frame loop is already bound.");
            if (!IsInsideTree() || _late == null)
                throw new InvalidOperationException("Godot frame loop must be in the SceneTree before binding.");
            _clock = clock;
            SetProcess(true);
            SetPhysicsProcess(true);
            _late.SetProcess(true);
        }

        public void Unbind()
        {
            _clock = null;
            if (GodotObject.IsInstanceValid(this))
            {
                SetProcess(false);
                SetPhysicsProcess(false);
            }
            if (_late != null && GodotObject.IsInstanceValid(_late)) _late.SetProcess(false);
        }

        public override void _ExitTree() => Unbind();
        public override void _Process(double delta) => Dispatch((float)delta, static (clock, dt, canContinue) => clock.DispatchUpdate(dt, canContinue), true);
        public override void _PhysicsProcess(double delta) => Dispatch(0, static (clock, _, canContinue) => clock.DispatchFixed(canContinue));
        internal void DispatchLate() => Dispatch(0, static (clock, _, canContinue) => clock.DispatchLate(canContinue));

        private void Dispatch(float delta, Action<ClockPlugin, float, Func<bool>> phase, bool update = false)
        {
            var clock = _clock;
            if (clock == null || !_canDispatch()) return;
            var views = GodotViewBindings.Current;
            views?.BeginOperation();
            try
            {
                if (update) views?.BeforeUpdate();
                if (_canDispatch()) phase(clock, delta, _canDispatch);
            }
            finally { views?.EndOperation(); }
        }
    }
}
