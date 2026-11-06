using System;
using System.Collections.Generic;
using VContainer;

namespace ObFramework.Core
{
    public sealed class PluginRegistration
    {
        private readonly IContainerBuilder _builder;
        private readonly PluginDescriptor _descriptor;
        private readonly Dictionary<Type, RegistrationBuilder> _registrations = new Dictionary<Type, RegistrationBuilder>();
        private readonly Dictionary<Type, PluginDescriptor> _bindings;
        private bool _frozen;

        internal PluginRegistration(IContainerBuilder builder, PluginDescriptor descriptor, Dictionary<Type, PluginDescriptor> bindings)
        {
            _builder = builder;
            _descriptor = descriptor;
            _bindings = bindings;
        }

        public void Require<T>() where T : class
        {
            EnsureMutable();
            EnsureInterface(typeof(T));
            _descriptor.Dependencies.Add(typeof(T));
        }

        public void Export<TInterface, TImplementation>() where TInterface : class where TImplementation : class, TInterface
        {
            EnsureMutable();
            AddExport(typeof(TInterface));
            GetRegistration<TImplementation>().As<TInterface>();
        }

        public void ExportInstance<TInterface>(TInterface instance) where TInterface : class
        {
            EnsureMutable();
            if (instance == null)
                throw new ArgumentNullException(nameof(instance));
            AddExport(typeof(TInterface));
            _builder.RegisterInstance<TInterface>(instance);
        }

        public void Register<T>() where T : class
        {
            EnsureMutable();
            GetRegistration<T>();
        }

        public void RegisterInstance<T>(T instance) where T : class
        {
            EnsureMutable();
            if (instance == null)
                throw new ArgumentNullException(nameof(instance));
            Reserve(typeof(T));
            _builder.RegisterInstance<T>(instance);
        }

        internal void Freeze() => _frozen = true;

        private RegistrationBuilder GetRegistration<T>() where T : class
        {
            if (!_registrations.TryGetValue(typeof(T), out var registration))
            {
                if (!typeof(T).IsClass || typeof(T).IsAbstract)
                    throw new InvalidOperationException($"Service {typeof(T).FullName} must be a concrete class.");
                Reserve(typeof(T));
                registration = _builder.Register<T>(Lifetime.Singleton).AsSelf();
                _registrations.Add(typeof(T), registration);
            }
            return registration;
        }

        private void AddExport(Type type)
        {
            EnsureInterface(type);
            if (typeof(IPlugin).IsAssignableFrom(type) || type == typeof(IPluginContext))
                throw new InvalidOperationException($"{type.FullName} is a lifecycle contract, not a plugin capability.");
            Reserve(type);
            if (!_descriptor.Exports.Add(type))
                throw new InvalidOperationException($"Plugin {_descriptor.Plugin.GetType().FullName} exports {type.FullName} more than once.");
        }

        private void Reserve(Type type)
        {
            if (_bindings.TryGetValue(type, out var existing))
                throw new InvalidOperationException($"Service {type.FullName} is already registered by plugin {existing.Plugin.GetType().FullName}.");
            _bindings.Add(type, _descriptor);
        }

        private static void EnsureInterface(Type type)
        {
            if (!type.IsInterface)
                throw new InvalidOperationException($"Plugin capability {type.FullName} must be an interface.");
        }

        private void EnsureMutable()
        {
            if (_frozen)
                throw new InvalidOperationException("Plugin registration is frozen.");
        }
    }

    internal sealed class PluginDescriptor
    {
        internal readonly IPlugin Plugin;
        internal readonly HashSet<Type> Exports = new HashSet<Type>();
        internal readonly HashSet<Type> Dependencies = new HashSet<Type>();
        internal IPluginContext Context = null!;
        internal volatile bool Ready;
        internal bool Entered;

        internal PluginDescriptor(IPlugin plugin) => Plugin = plugin;
    }
}
