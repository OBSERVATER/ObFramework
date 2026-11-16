using ObFramework.Core;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ObFramework.Extension
{
    public static class ICanExtension
    {
        public static T GetPlugin<T>(this ICanGetPlugin self) where T : class, IPlugin =>
            self.GetArchitecture().GetPlugin<T>();
        public static T GetModel<T>(this ICanGetModel self) where T : class, IModel =>
            self.GetArchitecture().GetModel<T>();
        public static T GetSystem<T>(this ICanGetSystem self) where T : class, ISystem =>
            self.GetArchitecture().GetSystem<T>();
        public static void SendCommand<T>(this ICanSendCommand self) where T : ICommand, new() =>
            self.GetArchitecture().SendCommand(new T());
        public static void SendCommand(this ICanSendCommand self, ICommand command) =>
            self.GetArchitecture().SendCommand(command);
        public static TResult SendCommand<TResult>(this ICanSendCommand self, ICommand<TResult> command) =>
            self.GetArchitecture().SendCommand(command);
        public static ValueTask SendCommandAsync<T>(this ICanSendCommand self, CancellationToken ct = default) where T : IAsyncCommand, new() =>
            self.GetArchitecture().SendCommandAsync(new T(), ct);
        public static ValueTask SendCommandAsync(this ICanSendCommand self, IAsyncCommand command, CancellationToken ct = default) =>
            self.GetArchitecture().SendCommandAsync(command, ct);
        public static ValueTask<TResult> SendCommandAsync<TResult>(this ICanSendCommand self, IAsyncCommand<TResult> command, CancellationToken ct = default) =>
            self.GetArchitecture().SendCommandAsync(command, ct);
        public static IDisposable RegisterEvent<T>(this ICanRegisterEvent self, Action<T> onEvent) where T : struct =>
            self.GetArchitecture().RegisterEvent(onEvent);
        public static void SendEvent<T>(this ICanSendEvent self, T e) where T : struct =>
            self.GetArchitecture().SendEvent(e);
        public static void SendEvent<T>(this ICanSendEvent self) where T : struct =>
            self.GetArchitecture().SendEvent<T>();
        public static void RegisterLocalService<I, T>(this ICanRegisterService self) where I : IService where T : class, I
            => self.GetArchitecture().RegisterLocalService<I, T>(self);
        public static T GetLocalService<T>(this ICanRegisterService self) where T : class, IService
            => self.GetArchitecture().GetLocalService<T>(self);
        public static TResult Query<TQuery, TResult>(this ICanQuery self)
            where TQuery : class, IQuery<TResult>, new()
            => self.GetArchitecture().SendQuery<TQuery, TResult>();
        public static TResult Query<TResult>(this ICanQuery self, IQuery<TResult> query)
            => self.GetArchitecture().SendQuery(query);
    }
}
