using System;
using NetData;

namespace Vape
{
    public static class GlobalEvents
    {
        // 定义击中事件
        public static event Action<GameServerSetupData> OnPlayerHit;

        // 触发事件
        public static void InvokePlayerHit(GameServerSetupData data)
        {
            OnPlayerHit?.Invoke(data);
        }
    }
}
