namespace ObFramework.Core
{
    public interface IController : ICanGetPlugin, IBelongToArchitecture, ICanSendCommand, ICanGetSystem, ICanGetModel, ICanRegisterEvent, ICanQuery, ICanSendEvent
    {
    }
}
