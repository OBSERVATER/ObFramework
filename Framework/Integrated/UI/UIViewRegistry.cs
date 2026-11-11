using System;
using System.Collections.Generic;
using ObFramework.Core;

namespace ObFramework.Integrated.UI
{
    /// <summary>
    /// One explicit View-to-VC-to-resource mapping owned by an engine adapter. Registration is
    /// explicit at startup; it never scans assemblies, preloads resources or reads setup.json.
    /// </summary>
    public readonly struct UIViewRegistration
    {
        public Type ViewType { get; }
        public Type WindowType { get; }
        public string ResourceKey { get; }

        /// <summary>
        /// Optional View construction delegate for engines whose adapters do not use generated
        /// ViewBinding code. Unity keeps this null because the generated binding constructs the View.
        /// </summary>
        public Func<IArchitecture, AUIView>? Factory { get; }

        public UIViewRegistration(Type viewType, Type windowType, string resourceKey, Func<IArchitecture, AUIView>? factory)
        {
            ViewType = viewType ?? throw new ArgumentNullException(nameof(viewType));
            WindowType = windowType ?? throw new ArgumentNullException(nameof(windowType));
            if (string.IsNullOrWhiteSpace(resourceKey))
                throw new ArgumentException("A resource key is required.", nameof(resourceKey));
            ResourceKey = resourceKey;
            Factory = factory;
        }
    }

    /// <summary>
    /// Engine-adapter-owned mapping from business View types to engine VC types and native
    /// resource keys. The common registration entry point used by generated <c>RegisterAll</c>
    /// code and by hand-written registrations.
    /// </summary>
    public sealed class UIViewRegistry
    {
        private readonly Dictionary<Type, UIViewRegistration> _registrations = new Dictionary<Type, UIViewRegistration>();

        public IReadOnlyCollection<UIViewRegistration> All => _registrations.Values;

        public bool TryGet(Type viewType, out UIViewRegistration registration)
        {
            if (viewType == null)
            {
                registration = default;
                return false;
            }
            return _registrations.TryGetValue(viewType, out registration);
        }

        public void Register<TView, TWindow>(string resourceKey, Func<IArchitecture, TView>? factory = null)
            where TView : AUIView
            where TWindow : class, IUIWindow
        {
            Register(typeof(TView), typeof(TWindow), resourceKey,
                factory == null ? (Func<IArchitecture, AUIView>?)null : architecture => factory(architecture));
        }

        public void Register(Type viewType, Type windowType, string resourceKey, Func<IArchitecture, AUIView>? factory = null)
        {
            if (viewType == null)
                throw new ArgumentNullException(nameof(viewType));
            if (windowType == null)
                throw new ArgumentNullException(nameof(windowType));
            if (!typeof(AUIView).IsAssignableFrom(viewType) || viewType.IsAbstract || viewType.IsInterface)
                throw new ArgumentException($"'{viewType.FullName}' must be a concrete AUIView type.", nameof(viewType));
            if (!typeof(IUIWindow).IsAssignableFrom(windowType))
                throw new ArgumentException($"'{windowType.FullName}' must implement IUIWindow.", nameof(windowType));
            if (_registrations.ContainsKey(viewType))
                throw new InvalidOperationException($"UI view '{viewType.FullName}' is already registered.");

            _registrations.Add(viewType, new UIViewRegistration(viewType, windowType, resourceKey, factory));
        }
    }
}
