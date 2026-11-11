using Godot;

namespace ObFramework.Adapter.Godot.Core
{
    public sealed partial class GodotLateFrameLoopNode : Node
    {
        internal GodotFrameLoopBridge? Bridge { get; set; }

        public override void _Process(double delta) => Bridge?.DispatchLate();
    }
}
