using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ObFramework.Core;
using ObFramework.Integrated.Clock;

namespace ObFramework.Integrated.GAS
{
    public sealed class GASPlugin : IPlugin, ILogicTickable
    {
        private ClockPlugin? _clock;

        private readonly List<AbilitySystem?> _systems = new();

        private bool _ticking;

        void IPlugin.Register(PluginRegistration registration)
        {
            registration.Require<ClockPlugin>();
        }

        public Task InitAsync(IPluginContext plugins, CancellationToken ct)
        {
            _clock = plugins.Get<ClockPlugin>();
            _clock.RegisterTickable(this);

            return Task.CompletedTask;
        }

        public Task StopAsync(IPluginContext plugins)
        {
            _clock?.UnRegisterTickable(this);
            _clock = null;

            _systems.Clear();

            return Task.CompletedTask;
        }

        public bool Register(AbilitySystem system)
        {
            if (system == null)
                throw new ArgumentNullException(nameof(system));

            if (_systems.Contains(system))
                return false;

            _systems.Add(system);

            return true;
        }

        public bool UnRegister(AbilitySystem system)
        {
            var index = _systems.IndexOf(system);

            if (index < 0)
                return false;

            if (_ticking)
                _systems[index] = null;
            else
                _systems.RemoveAt(index);

            return true;
        }

        public void LogicTick()
        {
            if (_ticking)
                throw new InvalidOperationException("GAS tick cannot be reentered.");

            if (_clock == null)
                return;

            _ticking = true;

            try
            {
                var count = _systems.Count;

                for (var i = 0; i < count && _clock != null; i++)
                {
                    _systems[i]?.Effects.Tick();
                }

                for (var i = 0; i < count && _clock != null; i++)
                {
                    _systems[i]?.Abilities.Tick();
                }
            }
            finally
            {
                _systems.RemoveAll(static system => system == null);
                _ticking = false;
            }
        }
    }
}