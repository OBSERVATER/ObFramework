using System.Threading;
using System.Threading.Tasks;

namespace ObFramework.Core
{
    public interface ICommand<TResult> : ICanSetArchitecture, ICanGetModel, ICanGetSystem, ICanSendEvent
    {
        TResult Execute();
    }
    public interface ICommand : ICanSetArchitecture, ICanGetModel, ICanGetSystem, ICanSendEvent
    {
        void Execute();
    }
    public interface IAsyncCommand<TResult> : ICanSetArchitecture, ICanGetModel, ICanGetSystem, ICanSendEvent
    {
        ValueTask<TResult> Execute(CancellationToken ct);
    }
    public interface IAsyncCommand : ICanSetArchitecture, ICanGetModel, ICanGetSystem, ICanSendEvent
    {
        ValueTask Execute(CancellationToken ct);
    }
    public abstract class ACommand : ICommand
    {
        private IArchitecture _architecture;
        public abstract void Execute();
        public IArchitecture GetArchitecture() => _architecture;
        public void SetArchitecture(IArchitecture architecture) => _architecture = architecture;
    }
    public abstract class ACommand<T> : ICommand<T>
    {
        private IArchitecture _architecture;
        public abstract T Execute();
        public IArchitecture GetArchitecture() => _architecture;
        public void SetArchitecture(IArchitecture architecture) => _architecture = architecture;
    }
    public abstract class AAsyncCommand<T> : IAsyncCommand<T>
    {
        private IArchitecture _architecture;
        public abstract ValueTask<T> Execute(CancellationToken ct);
        public IArchitecture GetArchitecture() => _architecture;
        public void SetArchitecture(IArchitecture architecture) => _architecture = architecture;
    }
    public abstract class AAsyncCommand : IAsyncCommand
    {
        private IArchitecture _architecture;
        public abstract ValueTask Execute(CancellationToken ct);
        public IArchitecture GetArchitecture() => _architecture;
        public void SetArchitecture(IArchitecture architecture) => _architecture = architecture;
    }
}