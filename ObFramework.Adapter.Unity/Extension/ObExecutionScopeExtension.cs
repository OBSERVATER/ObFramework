using System;
using System.Threading.Tasks;
using ObFramework.Core.Async;
using UnityEngine;

namespace ObFramework.Adapter.Unity.Extension
{
    public static class ObExecutionScopeExtension
    {
        public static void StopWhenDestroyed(this ObExecutionScope scope, MonoBehaviour owner)
        {
            if (scope == null)
                throw new ArgumentNullException(nameof(scope));

            if (!owner)
                throw new ArgumentNullException(nameof(owner));

            owner.destroyCancellationToken.Register(() => _ = StopAndObserveAsync(scope));
        }

        private static async Task StopAndObserveAsync(ObExecutionScope scope)
        {
            try
            {
                await scope.StopAsync();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }
    }
}