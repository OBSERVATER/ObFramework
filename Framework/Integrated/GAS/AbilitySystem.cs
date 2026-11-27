using System;
using ObFramework.Integrated.GAS.Attributes.Core;
using ObFramework.Integrated.GAS.GameplayAbility;
using ObFramework.Integrated.GAS.GameplayEffect;
using ObFramework.Integrated.GAS.GameplayTag;
using ObFramework.Integrated.GAS.GameplayCue;

namespace ObFramework.Integrated.GAS
{
    public sealed class AbilitySystem : IDisposable
    {
        public AttributeSet Attributes { get; } = new();
        public TagContainer Tags { get; } = new();

        public EffectManager Effects { get; }
        public AbilityManager Abilities { get; }
        public CueDispatcher Cues { get; }

        public bool IsDisposed { get; private set; }

        private bool _resetting;

        internal bool IsAvailable => !IsDisposed && !_resetting;

        public AbilitySystem()
        {
            Effects = new EffectManager(this);
            Abilities = new AbilityManager(this);
            Cues = new CueDispatcher(this);
        }

        public void Reset()
        {
            if (!IsAvailable)
                return;

            _resetting = true;

            try
            {
                Abilities.Clear();
                Effects.Clear();
                Tags.Clear();
                Cues.Clear();
            }
            finally
            {
                _resetting = false;
            }
        }

        public void Dispose()
        {
            if (IsDisposed || _resetting)
                return;

            Reset();
            Attributes.Clear();
            IsDisposed = true;
        }
    }
}