namespace ObFramework.Adapter.Unity.ViewBinding
{
    /// <summary>
    /// Implemented by generated Unity window controllers to refresh their generated display-cycle
    /// subscriptions and initial values. The adapter calls it after activating a window so an
    /// already-active Single window reopen also refreshes values, in addition to the generated
    /// OnEnable path used for normal activation.
    /// </summary>
    public interface IGeneratedUIBinding
    {
        void RefreshBindings();
    }
}
