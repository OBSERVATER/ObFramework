using System.Threading;
using System.Threading.Tasks;

namespace ObFramework.Core
{
    public interface IPlugin
    {
        void Register(PluginRegistration registration);
        Task InitAsync(IPluginContext plugins, CancellationToken ct);
        Task StopAsync(IPluginContext plugins);
    }

    public interface IPluginContext
    {
        T Get<T>() where T : class, IPlugin;
    }
}
