namespace ObFramework.Core
{
    public interface ISystem : ICanGetPlugin, ICanGetModel, ICanGetSystem, ICanInit, ICanSetArchitecture, ICanRegisterService, ICanRegisterEvent, ICanSendEvent, ICanQuery
    {
    }
    public abstract class ASystem : ISystem
    {
        public bool Initialized { get; set; }
        private IArchitecture _architecture;
        public IPluginContext Plugins => (_architecture ?? throw new System.InvalidOperationException("Architecture has not been assigned.")).Plugins;

        void ICanInit.Init() => OnInit();
        void ICanInit.Deinit() => OnDeinit();

        protected abstract void OnInit();
        protected virtual void OnDeinit() { }

        public void SetArchitecture(IArchitecture architecture) => _architecture = architecture;
        public IArchitecture GetArchitecture() => _architecture;
    }
}