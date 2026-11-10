namespace ObFramework.Core.FrameLoop
{
    public interface IFrameLoopDriver
    {
        void Bind(FrameDispatcher dispatcher);
        void Unbind();
    }
}