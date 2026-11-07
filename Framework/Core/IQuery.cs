using System.Collections.Generic;

namespace ObFramework.Core
{
    public interface IQuery<TResult> : ICanGetPlugin, IBelongToArchitecture, ICanSetArchitecture, ICanGetSystem, ICanGetModel, ICanQuery where TResult : class, IRuntimeData
    {
        IRuntimeDataGroup<TResult> Q(bool refresh = false);
        void Refresh();
    }
    public abstract class AQuery<TResult> : IQuery<TResult> where TResult : class, IRuntimeData
    {
        private IArchitecture _arch;
        protected readonly Dictionary<string, TResult> _data = new Dictionary<string, TResult>();
        private readonly IRuntimeDataGroup<TResult> _group;
        private bool _initialized;

        protected AQuery()
        {
            _group = new DefaultRuntimeDataGroup<TResult>(_data);
        }
        public void SetArchitecture(IArchitecture architecture) => _arch = architecture;
        public IArchitecture GetArchitecture() => _arch;

        public IRuntimeDataGroup<TResult> Q(bool refresh = false)
        {
            if (refresh || !_initialized)
                Refresh();

            return _group;
        }

        public void Refresh()
        {
            OnRefresh();
            _initialized = true;
        }
        protected abstract void OnRefresh();
    }
}
