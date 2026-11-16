using System;

namespace ObFramework.Core
{
    /// <summary>Reads current business data. Queries must not refresh or mutate their data sources.</summary>
    public interface IQuery<out TResult> : ICanGetPlugin, IBelongToArchitecture, ICanSetArchitecture, ICanGetSystem, ICanGetModel, ICanQuery
    {
        TResult Q();
    }

    /// <summary>
    /// A read operation whose parameters may be supplied by its constructor.
    /// Results are not cached or copied; the query determines the returned value's shape.
    /// </summary>
    public abstract class AQuery<TResult> : IQuery<TResult>
    {
        private IArchitecture? _architecture;

        public void SetArchitecture(IArchitecture architecture) =>
            _architecture = architecture ?? throw new ArgumentNullException(nameof(architecture));

        public IArchitecture GetArchitecture() => _architecture
            ?? throw new InvalidOperationException("Query is not attached to an architecture.");

        public abstract TResult Q();
    }
}
