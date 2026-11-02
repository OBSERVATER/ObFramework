using MessagePipe;
using ObFramework.Core.Service;
using ObFramework.Extension;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VContainer;

namespace ObFramework.Core
{
    public interface IArchitecture
    {
        void RegisterSystem<I, T>() where I : ISystem where T : class, I, new();
        void RegisterModel<I, T>() where I : IModel where T : class, I, new();
        void RegisterLocalService<I, T>(ICanRegisterService self) where I : IService where T : class, I;

        IDisposable RegisterEvent<T>(Action<T> onEvent) where T : struct;
        IDisposable RegisterAsyncEvent<T>(Func<T, CancellationToken, ValueTask> func) where T : struct;
        T GetSystem<T>() where T : class, ISystem;
        T GetModel<T>() where T : class, IModel;
        T GetLocalService<T>(ICanRegisterService self) where T : class, IService;
        IRuntimeDataGroup<TResult> SendQuery<TQuery, TResult>(bool refresh = false) where TQuery : class, IQuery<TResult>, new() where TResult : class, IRuntimeData;

        void SendCommand<T>(T command) where T : ICommand;
        TResult SendCommand<TResult>(ICommand<TResult> command);
        ValueTask SendCommandAsync<T>(T command, CancellationToken ct = default) where T : IAsyncCommand;
        ValueTask<TResult> SendCommandAsync<TResult>(IAsyncCommand<TResult> command, CancellationToken cts = default);
        void SendEvent<T>() where T : struct;
        void SendEvent<T>(T e) where T : struct;
        ValueTask SendEventAsync<T>(T msg, AsyncPublishStrategy strategy = AsyncPublishStrategy.Parallel, CancellationToken cts = default);

        void Deinit();
    }
    public abstract class AArchitecture<T> : IArchitecture,IModelSource where T : AArchitecture<T>, new()
    {
        protected static T Architecture;
        protected IObjectResolver Resolver;
        private IArchitectureLifecycleCoordinator _lifecycleService;
        private bool _initialized = false;

        public bool Initialized => _initialized;
        public static IArchitecture Arch
        {
            get
            {
                if (Architecture == null)
                {
                    InitInterface();
                }
                return Architecture!;
            }
        }
        public static void InitInterface()
        {
            if (Architecture == null)
            {
                Architecture = new T();
                var builder = new ContainerBuilder();
                Architecture._lifecycleService = new ArchitectureLifecycleCoordinator(builder, Architecture);

                builder.RegisterNuGetMessagePipe();
                builder.RegisterInstance<IArchitecture>(Architecture);
                RegisterServiceInternal();

                Architecture.Init();

                Architecture.Resolver = builder.Build();
                Architecture._lifecycleService.Init(Architecture.Resolver);
                Architecture._initialized = true;
            }
        }
        protected abstract void Init();

        public void Deinit()
        {
            _lifecycleService.Deinit(Architecture.Resolver);
            Resolver.Dispose();
            Architecture = null;
            _lifecycleService = null;
        }

        public IDisposable RegisterEvent<T1>(Action<T1> onEvent) where T1 : struct => Resolver.Resolve<IEventBus>().Register(onEvent);
        public IDisposable RegisterAsyncEvent<T1>(Func<T1, CancellationToken, ValueTask> func) where T1 : struct => Resolver.Resolve<IEventBus>().RegisterAsync(func);
        public void RegisterModel<I, T1>() where I : IModel where T1 : class, I, new() => _lifecycleService.RegisterModel<I, T1>();
        public void RegisterSystem<I, T1>() where I : ISystem where T1 : class, I, new() => _lifecycleService.RegisterSystem<I, T1>();

        public TResult SendCommand<TResult>(ICommand<TResult> command)
        {
            command.SetArchitecture(this);
            return command.Execute();
        }
        public void SendCommand<T1>(T1 command) where T1 : ICommand
        {
            command.SetArchitecture(this);
            command.Execute();
        }
        public async ValueTask SendCommandAsync<T1>(T1 command, CancellationToken ct = default) where T1 : IAsyncCommand
        {
            command.SetArchitecture(this);
            await command.Execute(ct);
        }
        public async ValueTask<TResult> SendCommandAsync<TResult>(IAsyncCommand<TResult> command, CancellationToken ct = default)
        {
            command.SetArchitecture(this);
            return await command.Execute(ct);
        }
        public void SendEvent<T1>() where T1 : struct => Resolver.Resolve<IEventBus>().Send<T1>();
        public void SendEvent<T1>(T1 e) where T1 : struct => Resolver.Resolve<IEventBus>().Send(e);
        public async ValueTask SendEventAsync<T1>(T1 msg, AsyncPublishStrategy strategy = AsyncPublishStrategy.Parallel, CancellationToken cts = default)
            => await Resolver.Resolve<IEventBus>().SendAsync(msg, strategy, cts);

        T1 IArchitecture.GetModel<T1>() => Resolver.Resolve<T1>();
        T1 IArchitecture.GetSystem<T1>() => Resolver.Resolve<T1>();
        IRuntimeDataGroup<TResult> IArchitecture.SendQuery<TQuery, TResult>(bool refresh = false) => Resolver.Resolve<IQueryService>().SendQuery<TQuery, TResult>(refresh);
        T1 IArchitecture.GetLocalService<T1>(ICanRegisterService self) => _lifecycleService.GetLocalService<T1>(self);

        private void RegisterPubService<I, T1>() where I : IService where T1 : class, I => _lifecycleService.RegisterPubService<I, T1>();
        public void RegisterLocalService<I, T1>(ICanRegisterService self) where I : IService where T1 : class, I => _lifecycleService.RegisterLocalService<I, T1>(self);
        public IEnumerable<IModel> GetModelInsts() => Resolver.Resolve<IEnumerable<IModel>>();
        private static void RegisterServiceInternal()
        {
            Architecture.RegisterPubService<IEventBus, EventBus>();
            Architecture.RegisterPubService<IQueryService, QueryService>();
        }
    }
}
