using System.Threading;
using System.Threading.Tasks;

namespace ObFramework.Core
{
    public interface ICommand<out TResult> : ICanGetPlugin, ICanSetArchitecture, ICanGetModel, ICanGetSystem, ICanSendEvent
    {
        TResult Execute();
    }
    public interface ICommand : ICanGetPlugin, ICanSetArchitecture, ICanGetModel, ICanGetSystem, ICanSendEvent
    {
        void Execute();
    }
    public interface IAsyncCommand<TResult> : ICanGetPlugin, ICanSetArchitecture, ICanGetModel, ICanGetSystem, ICanSendEvent
    {
        ValueTask<TResult> Execute(CancellationToken ct);
    }
    public interface IAsyncCommand : ICanGetPlugin, ICanSetArchitecture, ICanGetModel, ICanGetSystem, ICanSendEvent
    {
        ValueTask Execute(CancellationToken ct);
    }
    public abstract class ACommand : ICommand
    {
        private IArchitecture _architecture;
        public IPluginContext Plugins => (_architecture ?? throw new System.InvalidOperationException("Architecture has not been assigned.")).Plugins;
        public abstract void Execute();
        public IArchitecture GetArchitecture() => _architecture;
        public void SetArchitecture(IArchitecture architecture) => _architecture = architecture;
    }
    public abstract class ACommand<T> : ICommand<T>
    {
        private IArchitecture _architecture;
        public IPluginContext Plugins => (_architecture ?? throw new System.InvalidOperationException("Architecture has not been assigned.")).Plugins;
        public abstract T Execute();
        public IArchitecture GetArchitecture() => _architecture;
        public void SetArchitecture(IArchitecture architecture) => _architecture = architecture;
    }
    public abstract class AAsyncCommand<T> : IAsyncCommand<T>
    {
        private IArchitecture _architecture;
        public IPluginContext Plugins => (_architecture ?? throw new System.InvalidOperationException("Architecture has not been assigned.")).Plugins;
        public abstract ValueTask<T> Execute(CancellationToken ct);
        public IArchitecture GetArchitecture() => _architecture;
        public void SetArchitecture(IArchitecture architecture) => _architecture = architecture;
    }
    public abstract class AAsyncCommand : IAsyncCommand
    {
        private IArchitecture _architecture;
        public IPluginContext Plugins => (_architecture ?? throw new System.InvalidOperationException("Architecture has not been assigned.")).Plugins;
        public abstract ValueTask Execute(CancellationToken ct);
        public IArchitecture GetArchitecture() => _architecture;
        public void SetArchitecture(IArchitecture architecture) => _architecture = architecture;
    }
}