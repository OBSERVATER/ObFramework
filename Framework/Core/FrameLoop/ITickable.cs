namespace ObFramework.Core.FrameLoop
{
    public interface IFrameTickable { }

    public interface ITickable : IFrameTickable
    {
        void Tick();
    }

    public interface IFixedTickable : IFrameTickable
    {
        void FixedTick();
    }

    public interface ILateTickable : IFrameTickable
    {
        void LateTick();
    }
}