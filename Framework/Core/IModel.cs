using System.Collections.Generic;

namespace ObFramework.Core
{
    public interface IModel : ICanGetModel, ICanGetSystem, ICanInit, ICanSetArchitecture, ICanRegisterEvent, ICanSendEvent, ICanQuery
    {
    }
    internal interface IModelSource
    {
        IEnumerable<IModel> GetModelInsts();
    }
    public abstract class AModel : IModel
    {
        public bool Initialized { get; set; }
        private IArchitecture _architecture;

        void ICanInit.Init() => OnInit();
        void ICanInit.Deinit() => OnDeinit();

        protected abstract void OnInit();
        protected virtual void OnDeinit() { }

        public void SetArchitecture(IArchitecture architecture) => _architecture = architecture;
        public IArchitecture GetArchitecture() => _architecture;
    }
}