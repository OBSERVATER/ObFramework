using System;
using MessagePipe;
using UnityEngine;

namespace ObFramework.Adapter.Unity.Extension
{
    public static class CoreExtension
    {
        public static IDisposable DisposeWhenDestroyed(this IDisposable subscription, MonoBehaviour owner)
        {
            // TODO 1: 检查参数
            if (!owner)
            {
                throw new InvalidOperationException("owner cannot be null");
            }
            // TODO 2: 获取 owner.destroyCancellationToken
            var ct = owner.destroyCancellationToken;
            // TODO 3: 注册取消回调，调用 subscription.Dispose()
            ct.Register(subscription.Dispose);
            // TODO 4: 返回订阅对象
            return subscription;
        }
    }
}