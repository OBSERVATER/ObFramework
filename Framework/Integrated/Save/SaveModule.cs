using ObFramework.Core;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using ZLinq;

namespace ObFramework.Integrated.Save
{
    public class SaveModule
    {
        public static ISaveManager SaveManager { get; private set; }
        public static void Init([NotNull]ISaveStore store,[NotNull]IArchitecture a)
        {
            if (SaveManager != null)
            {
                return;
            }
            if (a is not IModelSource source)
            {
                throw new InvalidOperationException("cannot auto collect icansave model");
            }
            var models = source.GetModelInsts();
            var kvp = models.AsValueEnumerable().Where(x => x is ICanSave)
                                                .ToDictionary(key => ((ICanSave)key).Key.ToString(), val => (ICanSave)val);
            SaveManager = new SaveManager(store,kvp);
        }

        public static Task LoadAsync(string slotId) => SaveManager.LoadAsync(slotId);
        public static Task SaveAsync(string slotId) => SaveManager.SaveAsync(slotId);
    }
}
