using System;
using Godot;
using ObFramework.Integrated.Clock;

namespace ObFramework.Adapter.Godot.Core
{
    public sealed partial class GodotFrameLoopBridge : Node, IFrameLoopDriver
    {
        private readonly Func<bool> _canDispatch;
        private ClockPlugin? _clock;
        private GodotLateFrameLoopNode? _late;

        public GodotFrameLoopBridge() => _canDispatch = () => _clock != null;

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
            if (_clock != null)
                throw new InvalidOperationException("Godot frame loop is already bound.");
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
            if (_late != null && GodotObject.IsInstanceValid(_late))
                _late.SetProcess(false);
        }

        public override void _ExitTree() => Unbind();
        public override void _Process(double delta) => _clock?.DispatchUpdate((float)delta, _canDispatch);
        public override void _PhysicsProcess(double delta) => _clock?.DispatchFixed(_canDispatch);
        internal void DispatchLate() => _clock?.DispatchLate(_canDispatch);
    }
}
