using UnrealSharp.Attributes;
using UnrealSharp.UnrealSharpCore;

namespace ObFramework.Adapter.UnrealSharp.Core;

[UClass]
public partial class UObFrameworkSubsystem : UCSGameInstanceSubsystem
{
    public override void Initialize(FSubsystemCollectionBaseRef collection)
    {
        base.Initialize(collection);
    }
    public override void Deinitialize()
    {
        base.Deinitialize();
    }
}