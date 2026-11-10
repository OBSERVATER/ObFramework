using Godot;
using ObFramework.Core.FrameLoop;

namespace ObFramework.Adapter.Godot.Core
{
    public sealed partial class GodotLateFrameLoopNode : Node
    {
        internal FrameDispatcher? Dispatcher { get; set; }

        public override void _Process(double delta)
        {
            Dispatcher?.DispatchLate();
        }
    }
}
