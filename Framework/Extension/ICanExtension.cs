using ObFramework.Core;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ObFramework.Extension
{
    public static class ICanExtension
    {
        public static T GetModel<T>(this ICanGetModel self) where T : class, IModel =>
            self.GetArchitecture().GetModel<T>();
        public static T GetSystem<T>(this ICanGetSystem self) where T : class, ISystem =>
            self.GetArchitecture().GetSystem<T>();
        public static void SendCommand<T>(this ICanSendCommand self) where T : ICommand, new() =>
            self.GetArchitecture().SendCommand(new T());
        public static TResult SendCommand<TResult>(this ICanSendCommand self, ICommand<TResult> command) =>
            self.GetArchitecture().SendCommand(command);
        public static ValueTask SendCommandAsync<T>(this ICanSendCommand self, CancellationTokenSource cts = default) where T : IAsyncCommand, new() =>
            self.GetArchitecture().SendCommandAsync(new T(), cts.Token);
        public static ValueTask<TResult> SendCommandAsync<TResult>(this ICanSendCommand self, IAsyncCommand<TResult> command, CancellationTokenSource cts = default) =>
            self.GetArchitecture().SendCommandAsync(command, cts.Token);
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
        public static IRuntimeDataGroup<TResult> Query<TQuery, TResult>(this ICanQuery self, bool refresh = false)
            where TQuery : class, IQuery<TResult>, new() where TResult : class, IRuntimeData
            => self.GetArchitecture().SendQuery<TQuery, TResult>(refresh);
    }
}