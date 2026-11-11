namespace ObFramework.Adapter.Unity.ViewBinding
{
    /// <summary>
    /// Synchronization direction of a <see cref="UIBindDataAttribute"/> binding.
    /// </summary>
    public enum UIBindMode
    {
        /// <summary>Business View state is pushed to the control; control input is ignored.</summary>
        OneWay = 0,

        /// <summary>
        /// Business View state and the control value are kept in sync. Supported input controls
        /// write the user value back to the View; display controls report a diagnostic.
        /// </summary>
        TwoWay = 1,
    }
}
