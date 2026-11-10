using System.Threading;
using System.Threading.Tasks;

namespace ObFramework.Core.FrameLoop
{
    public interface IApplicationFrameLoopDriver : IFrameLoopDriver
    {
        void BeginStartup(CancellationToken stopping);
        // Called after Architecture is ready and before the application enters the game.
        void Prepare(IArchitecture architecture);
        // Called after Unbind while Architecture and plugins are still available.
        Task ReleaseAsync();
    }
}
