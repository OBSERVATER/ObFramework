namespace ObFramework.Integrated.Clock
{
    public interface IFrameLoopDriver
    {
        void Bind(ClockPlugin clock);
        void Unbind();
    }
}
