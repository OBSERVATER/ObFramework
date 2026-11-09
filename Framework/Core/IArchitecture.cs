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
        T GetPlugin<T>() where T : class, IPlugin;
        
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
        private IPluginContext? _plugins;
        public IPluginContext Plugins => _plugins ?? throw new InvalidOperationException("Architecture has no plugin context.");

        public bool Initialized => _initialized;
        public static IArchitecture Interface
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
        public static void InitInterface() => Initialize(null);

        public static void InitInterface(IPluginContext plugins)
        {
            switch (plugins)
            {
                case null:
                    throw new ArgumentNullException(nameof(plugins));
                case PluginHost.PluginContext context:
                    context.EnsureReady();
                    break;
            }

            Initialize(plugins);
        }

        private static void Initialize(IPluginContext? plugins)
        {
            if (Architecture != null)
            {
                if (!ReferenceEquals(Architecture._plugins, plugins))
                    throw new InvalidOperationException("Architecture is already initialized with a different plugin context.");
                return;
            }
            var architecture = new T();
            Architecture = architecture;
            architecture._plugins = plugins;
            var builder = new ContainerBuilder();
            architecture._lifecycleService = new ArchitectureLifecycleCoordinator(builder, architecture);
            try
            {
                builder.RegisterNuGetMessagePipe();
                builder.RegisterInstance<IArchitecture>(architecture);
                if (plugins != null)
                    builder.RegisterInstance<IPluginContext>(plugins);
                RegisterServiceInternal();
                architecture.Init();
                architecture.Resolver = builder.Build();
                architecture._lifecycleService.Init(architecture.Resolver);
                architecture._initialized = true;
            }
            catch (Exception error)
            {
                try { architecture.Deinit(); }
                catch (Exception cleanupError) { error.Data["ArchitectureCleanupError"] = cleanupError; }
                throw;
            }
        }
        protected abstract void Init();

        public void Deinit()
        {
            var errors = new List<Exception>();
            try { _lifecycleService?.Deinit(); }
            catch (Exception error) { errors.Add(error); }
            try { Resolver?.Dispose(); }
            catch (Exception error) { errors.Add(error); }
            finally
            {
                if (ReferenceEquals(Architecture, this))
                    Architecture = null!;
                _lifecycleService = null!;
                Resolver = null!;
                _plugins = null;
                _initialized = false;
            }
            if (errors.Count > 0)
                throw new AggregateException("Architecture cleanup failed.", errors);
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
        public T1 GetPlugin<T1>() where T1 : class, IPlugin => Plugins.Get<T1>();
        IRuntimeDataGroup<TResult> IArchitecture.SendQuery<TQuery, TResult>(bool refresh = false) => Resolver.Resolve<IQueryService>().SendQuery<TQuery, TResult>(refresh);
        T1 IArchitecture.GetLocalService<T1>(ICanRegisterService self) => _lifecycleService.GetLocalService<T1>(self);

        protected void RegisterPubService<I, T1>() where I : IService where T1 : class, I => _lifecycleService.RegisterPubService<I, T1>();
        public void RegisterLocalService<I, T1>(ICanRegisterService self) where I : IService where T1 : class, I => _lifecycleService.RegisterLocalService<I, T1>(self);
        public IEnumerable<IModel> GetModelInsts() => Resolver.Resolve<IEnumerable<IModel>>();
        private static void RegisterServiceInternal()
        {
            Architecture.RegisterPubService<IEventBus, EventBus>();
            Architecture.RegisterPubService<IQueryService, QueryService>();
        }
    }
}
