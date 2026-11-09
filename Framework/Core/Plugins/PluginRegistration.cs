using System;
using System.Collections.Generic;

namespace ObFramework.Core
{
    public sealed class PluginRegistration
    {
        private readonly HashSet<Type> _dependencies;
        private bool _frozen;

        internal PluginRegistration(HashSet<Type> dependencies) => _dependencies = dependencies;

        public void Require<T>() where T : class, IPlugin
        {
            if (_frozen)
                throw new InvalidOperationException("Plugin registration is frozen.");
            _dependencies.Add(typeof(T));
        }

        internal void Freeze() => _frozen = true;
    }

    internal sealed class PluginDescriptor
    {
        internal readonly IPlugin Plugin;
        internal readonly HashSet<Type> Dependencies = new HashSet<Type>();
        internal IPluginContext Context = null!;
        internal bool Ready;
        internal bool Entered;

        internal PluginDescriptor(IPlugin plugin) => Plugin = plugin;
    }
}
