namespace ObFramework.Core
{
    public interface IBelongToArchitecture
    {
        IArchitecture GetArchitecture();
    }

    public interface ICanSetArchitecture
    {
        void SetArchitecture(IArchitecture architecture);
    }

    public interface ICanGetModel : IBelongToArchitecture
    {
    }

    public interface ICanGetSystem : IBelongToArchitecture
    {
    }

    public interface ICanRegisterEvent : IBelongToArchitecture
    {
    }

    public interface ICanSendCommand : IBelongToArchitecture
    {
    }

    public interface ICanSendEvent : IBelongToArchitecture
    {
    }
    public interface ICanRegisterService : IBelongToArchitecture
    {
    }
    public interface ICanQuery : IBelongToArchitecture
    {
    }
    public interface ICanGetPlugin : IBelongToArchitecture
    {
    }
    public interface ICanInit
    {
        bool Initialized { get; set; }
        void Init();
        void Deinit();
    }
}