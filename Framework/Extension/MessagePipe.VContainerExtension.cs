using MessagePipe;
using System;
using VContainer;

namespace ObFramework.Extension
{
    internal static class VContainerExtension
    {
        public static void RegisterNuGetMessagePipe(this IContainerBuilder builder)
        {
            // MessagePipe 配置
            var options = new MessagePipeOptions
            {
                EnableAutoRegistration = false
            };

            builder.RegisterInstance(options);

            // 诊断
            builder.Register<MessagePipeDiagnosticsInfo>(Lifetime.Singleton);

            // Filter 元数据缓存
            builder.Register<AttributeFilterProvider<MessageHandlerFilterAttribute>>(Lifetime.Singleton);
            builder.Register<AttributeFilterProvider<AsyncMessageHandlerFilterAttribute>>(Lifetime.Singleton);

            // 适配 MessagePipe 依赖的 IServiceProvider
            builder.Register<IServiceProvider>(resolver => new VContainerServiceProvider(resolver), Lifetime.Singleton);

            // Filter Factory
            builder.Register<FilterAttachedMessageHandlerFactory>(Lifetime.Singleton);
            builder.Register<FilterAttachedAsyncMessageHandlerFactory>(Lifetime.Singleton);

            // EventFactory
            builder.Register<EventFactory>(Lifetime.Singleton);
        }

        private sealed class VContainerServiceProvider : IServiceProvider
        {
            private readonly IObjectResolver _resolver;

            public VContainerServiceProvider(IObjectResolver resolver)
            {
                _resolver = resolver;
            }
            public object GetService(Type serviceType) => _resolver.TryResolve(serviceType, out var instance) ? instance : null;
        }
    }
}
