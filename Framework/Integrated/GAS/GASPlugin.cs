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
            ct.ThrowIfCancellationRequested();

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

            if (!system.IsAvailable || _systems.Contains(system))
                return false;

            _systems.Add(system);
            return true;
        }

        public bool UnRegister(AbilitySystem system)
        {
            if (system == null)
                throw new ArgumentNullException(nameof(system));

            int index = _systems.IndexOf(system);

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
                int count = _systems.Count;

                for (int i = 0; i < count && _clock != null; i++)
                {
                    var system = _systems[i];
                    if (system is { IsAvailable: true })
                        system.Effects.Tick();
                }

                // 第二阶段：推进所有 Ability
                for (int i = 0; i < count && _clock != null; i++)
                {
                    var system = _systems[i];

                    if (system is { IsAvailable: true })
                        system.Abilities.Tick();
                }
            }
            finally
            {
                _systems.RemoveAll(static system => system == null || system.IsDisposed);
                _ticking = false;
            }
        }
    }
}