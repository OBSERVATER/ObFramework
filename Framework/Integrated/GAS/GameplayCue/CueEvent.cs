using Tag = ObFramework.Integrated.GAS.GameplayTag.GameplayTag;

namespace ObFramework.Integrated.GAS.GameplayCue
{
    public readonly struct CueEvent
    {
        public Tag Tag { get; }
        public ECueEventType Type { get; }

        public AbilitySystem Source { get; }
        public AbilitySystem Target { get; }

        public float? Magnitude { get; }

        public CueEvent(
            Tag tag,
            ECueEventType type,
            AbilitySystem source,
            AbilitySystem target,
            float? magnitude = null)
        {
            Tag = tag;
            Type = type;
            Source = source;
            Target = target;
            Magnitude = magnitude;
        }
    }
}