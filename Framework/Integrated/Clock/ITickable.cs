namespace ObFramework.Integrated.Clock
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

    public interface ILogicTickable : IFrameTickable
    {
        void LogicTick();
    }
}