using System;
using System.Collections.Generic;
using System.Linq;
using VContainer;

namespace ObFramework.Core
{
    internal interface IArchitectureLifecycleCoordinator
    {
        void RegisterSystem<I, T>() where I : ISystem where T : class, I;
        void RegisterModel<I, T>() where I : IModel where T : class, I;
        void RegisterPubService<I, T>() where I : IService where T : class, I;
        void RegisterLocalService<I, T>(ICanRegisterService self) where I : IService where T : class, I;

        void Init(IObjectResolver resolver);
        void Deinit();

        T GetLocalService<T>(ICanRegisterService self);
    }
    internal class ArchitectureLifecycleCoordinator : IArchitectureLifecycleCoordinator
    {
        private readonly IContainerBuilder _builder;
        private readonly IArchitecture _architecture;
        private IObjectResolver _root;
        private readonly Dictionary<ICanRegisterService, Dictionary<Type, Type>> _registrations;
        private readonly Dictionary<ICanRegisterService, IScopedObjectResolver> _scopes;
        private readonly List<ICanInit> _initializedObjects = new();
        public ArchitectureLifecycleCoordinator(IContainerBuilder builder, IArchitecture architecture)
        {
            _builder = builder;
            _architecture = architecture;
            _registrations = new Dictionary<ICanRegisterService, Dictionary<Type, Type>>();
            _scopes = new Dictionary<ICanRegisterService, IScopedObjectResolver>();
        }
        public void Init(IObjectResolver resolver)
        {
            _root = resolver;
            var models = resolver.Resolve<IEnumerable<IModel>>();
            var systems = resolver.Resolve<IEnumerable<ISystem>>();
            foreach (var model in models)
            {
                model.SetArchitecture(_architecture);
                model.Init();
                model.Initialized = true;
                _initializedObjects.Add(model);
            }
            foreach (var system in systems)
            {
                system.SetArchitecture(_architecture);
                system.Init();
                system.Initialized = true;
                _initializedObjects.Add(system);
            }
        }
        public void Deinit()
        {
            var errors = new List<Exception>();

            for (int i = _initializedObjects.Count - 1; i >= 0; i--)
            {
                var instance = _initializedObjects[i];

                try
                {
                    instance.Deinit();
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
                finally
                {
                    instance.Initialized = false;
                }
            }

            _initializedObjects.Clear();

            foreach (var scope in _scopes.Values)
            {
                try
                {
                    scope.Dispose();
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            }

            _scopes.Clear();
            _registrations.Clear();
            _root = null!;

            if (errors.Count > 0)
                throw new AggregateException("Architecture lifecycle cleanup failed.", errors);
        }
        public void RegisterModel<I, T>() where I : IModel where T : class, I => _builder.Register<T>(Lifetime.Singleton).AsSelf().As<I>().As<IModel>();
        public void RegisterSystem<I, T>() where I : ISystem where T : class, I => _builder.Register<T>(Lifetime.Singleton).AsSelf().As<I>().As<ISystem>();
        public void RegisterPubService<I, T>() where I : IService where T : class, I => _builder.Register<T>(Lifetime.Singleton).AsSelf().As<I>().As<IService>();
        public void RegisterLocalService<I, T>(ICanRegisterService self) where I : IService where T : class, I
        {
            if (_registrations.TryGetValue(self, out var dict))
            {
                dict.Add(typeof(I), typeof(T));
            }
            else
            {
                dict = new Dictionary<Type, Type>
                {
                    { typeof(I), typeof(T) }
                };
                _registrations.Add(self, dict);
            }
        }

        public T GetLocalService<T>(ICanRegisterService owner)
        {
            if (!_registrations.TryGetValue(owner, out var dict) || !dict.ContainsKey(typeof(T)))
            {
                throw new InvalidOperationException($"Local Service {typeof(T).Name} not registered.");
            }

            if (!_scopes.TryGetValue(owner, out var scope))
            {
                scope = _root.CreateScope(builder =>
                {
                    foreach (var pair in dict)
                    {
                        builder.Register(pair.Key, pair.Value, Lifetime.Scoped);
                    }
                });

                _scopes.Add(owner, scope);
            }

            return scope.Resolve<T>();
        }
    }
}
