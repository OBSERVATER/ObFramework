using System;
using System.ComponentModel;
using ObFramework.Core;

namespace ObFramework.Integrated.ViewBinding
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    public interface IGeneratedViewBinding<out TView> where TView : class,IController
    {
        Type ControllerType { get; }
        TView CreateView(IArchitecture architecture);
    }
}
