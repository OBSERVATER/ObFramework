using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ObFramework.Core;
using ObFramework.Integrated.Clock;
using ObFramework.Integrated.Config;
using ObFramework.Integrated.GAS.Attributes.Core;
using ObFramework.Integrated.GAS.Config;
using ObFramework.Integrated.GAS.GameplayEffect.Components;
using Effect = ObFramework.Integrated.GAS.GameplayEffect.GameplayEffect;
using Ability = ObFramework.Integrated.GAS.GameplayAbility.GameplayAbility;

namespace ObFramework.Integrated.GAS
{
    public sealed class GASPlugin : IPlugin, ILogicTickable
    {
        private ClockPlugin? _clock;

        private readonly List<AbilitySystem?> _systems = new();

        private bool _ticking;
        private readonly bool _loadConfig;
        private readonly GASConfigLibrary _library = new();

        public GASPlugin(bool loadConfig = false, IEnumerable<AttributeKey<float>>? attributes = null)
        {
            _loadConfig = loadConfig;

            if (attributes == null)
                return;

            foreach (var key in attributes)
                _library.RegisterAttribute(key);
        }

        void IPlugin.Register(PluginRegistration registration)
        {
            registration.Require<ClockPlugin>();

            if (_loadConfig)
                registration.Require<ConfigPlugin>();
        }

        public Task InitAsync(IPluginContext plugins, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (_loadConfig)
            {
                var config = plugins.Get<ConfigPlugin>();

                _library.Load(config.Get<EffectConfig>(), config.Get<AbilityConfig>());
            }

            _clock = plugins.Get<ClockPlugin>();
            _clock.RegisterTickable(this);

            return Task.CompletedTask;
        }

        public Task StopAsync(IPluginContext plugins)
        {
            _clock?.UnRegisterTickable(this);
            _clock = null;

            _systems.Clear();
            _library.Unload();

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
        
        public Effect GetEffect(int id) => _library.GetEffect(id);
        public Ability GetAbility(int id) => _library.GetAbility(id);
        public AttributeKey<float> GetAttribute(string name) => _library.GetAttribute(name);
        
        public void RegisterComponent(string type, Func<JObject, AEffectComponent> factory)
            => _library.RegisterComponent(type, factory);
        public void RegisterBehavior(string type, GASConfigLibrary.BehaviorBuilder builder) 
            => _library.RegisterBehavior(type, builder);
    }
}