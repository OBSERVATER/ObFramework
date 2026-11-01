using MessagePipe;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ObFramework.Core.Service
{
    internal interface IEventBus : IService, IDisposable
    {
        IDisposable Register<T>(Action<T> handler);
        IDisposable RegisterAsync<T>(Func<T, CancellationToken, ValueTask> handler);
        ValueTask SendAsync<T>(T msg, AsyncPublishStrategy strategy = AsyncPublishStrategy.Parallel, CancellationToken cts = default);
        void Send<T>(T message);
        void Send<T>() where T : new();
    }
    internal class EventBus : IEventBus
    {
        private Dictionary<Type, IEventChannel> _bus;
        private Dictionary<Type, IEventChannel> _asyncBus;
        private EventFactory _factory;

        public EventBus(EventFactory factory)
        {
            _bus = new Dictionary<Type, IEventChannel>();
            _asyncBus = new Dictionary<Type, IEventChannel>();
            _factory = factory;
        }
        public void Dispose()
        {
            foreach (var item in _bus)
            {
                item.Value.Dispose();
            }
            foreach (var item in _asyncBus)
            {
                item.Value.Dispose();
            }
            _bus.Clear();
            _asyncBus.Clear();
        }

        public IDisposable Register<T>(Action<T> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            if (!_bus.TryGetValue(typeof(T), out var channel))
            {
                var (publisher, subscriber) = _factory.CreateEvent<T>();
                channel = new EventChannel<T>(publisher, subscriber);
                _bus.Add(typeof(T), channel);
            }
            return ((EventChannel<T>)channel).Subscriber.Subscribe(handler);
        }

        public IDisposable RegisterAsync<T>(Func<T, CancellationToken, ValueTask> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            if (!_asyncBus.TryGetValue(typeof(T), out var channel))
            {
                var (publisher, subscriber) = _factory.CreateAsyncEvent<T>();
                channel = new AsyncEventChannel<T>(publisher, subscriber);
                _asyncBus.Add(typeof(T), channel);
            }
            return ((AsyncEventChannel<T>)channel).Subscriber.Subscribe(handler);
        }

        public void Send<T>(T message)
        {
            if (_bus.TryGetValue(typeof(T), out var channel))
            {
                ((EventChannel<T>)channel).Publisher.Publish(message);
            }
        }

        public void Send<T>() where T : new() => Send(new T());

        public async ValueTask SendAsync<T>(T msg, AsyncPublishStrategy strategy = AsyncPublishStrategy.Parallel, CancellationToken cts = default)
        {
            if (_asyncBus.TryGetValue(typeof(T), out var channel))
            {
                await ((AsyncEventChannel<T>)channel).Publisher.PublishAsync(msg, strategy, cts);
            }
        }
    }
    internal interface IEventChannel : IDisposable { }
    internal class EventChannel<T> : IEventChannel
    {
        public readonly IDisposablePublisher<T> Publisher;
        public readonly ISubscriber<T> Subscriber;

        public EventChannel(IDisposablePublisher<T> publisher, ISubscriber<T> subscriber)
        {
            Publisher = publisher;
            Subscriber = subscriber;
        }

        public void Dispose() => Publisher.Dispose();
    }
    internal class AsyncEventChannel<T> : IEventChannel
    {
        public readonly IDisposableAsyncPublisher<T> Publisher;
        public readonly IAsyncSubscriber<T> Subscriber;

        public AsyncEventChannel(IDisposableAsyncPublisher<T> publisher, IAsyncSubscriber<T> subscriber)
        {
            Publisher = publisher;
            Subscriber = subscriber;
        }
        public void Dispose() => Publisher.Dispose();
    }
}