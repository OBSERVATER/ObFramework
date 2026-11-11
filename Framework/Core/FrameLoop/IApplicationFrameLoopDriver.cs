using System.Threading;
using System.Threading.Tasks;

namespace ObFramework.Core.FrameLoop
{
    public interface IApplicationFrameLoopDriver : IFrameLoopDriver
    {
        void BeginStartup(CancellationToken stopping);
        // Called after Architecture is ready and before Bind.
        void Prepare(IArchitecture architecture);
        // Called after frame delivery stops and tracked work drains, while Architecture and plugins are still available.
        Task ReleaseAsync();
    }
}
