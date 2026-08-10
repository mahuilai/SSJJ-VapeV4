// =============================================================
// InstantSniper — 移植自 Aura AutoInstantSniper
// 功能: 按下快捷键后自动执行"切副武器→切主武器"键序列,
//       利用游戏动画帧差使狙击枪快速完成瞄准 (瞬间开镜)
// =============================================================
using Vape.Cfg;
using UnityEngine;

namespace Vape.Feature
{
    public class InstantSniper : MonoBehaviour
    {
        private enum SniperState { Idle, SwitchToSecondary, SwitchToPrimary }

        private SniperState _state     = SniperState.Idle;
        private float       _stateTimer = 0f;

        // 两段切枪之间的延迟 (毫秒), 参考 Aura: Alpha3 → Alpha1
        // 约 2-3 帧 @60fps = 33~50ms
        private const float SwitchDelay = 0.035f;

        private void Update()
        {
            if (!Config.InstantSniper)
            {
                _state = SniperState.Idle;
                return;
            }

            bool triggered = Input.GetKeyDown(Config.InstantSniperKey);

            switch (_state)
            {
                case SniperState.Idle:
                    if (triggered)
                    {
                        _state = SniperState.SwitchToSecondary;
                        _stateTimer = 0f;
                        // 切副武器 (槽位 Alpha3 = 副武器)
                        SimulateWeaponSlot(3);
                    }
                    break;

                case SniperState.SwitchToSecondary:
                    _stateTimer += Time.deltaTime;
                    if (_stateTimer >= SwitchDelay)
                    {
                        _stateTimer = 0f;
                        _state = SniperState.SwitchToPrimary;
                        // 切回主武器 (槽位 Alpha1 = 主武器)
                        SimulateWeaponSlot(1);
                    }
                    break;

                case SniperState.SwitchToPrimary:
                    _stateTimer += Time.deltaTime;
                    if (_stateTimer >= SwitchDelay)
                    {
                        _state = SniperState.Idle;
                        _stateTimer = 0f;
                    }
                    break;
            }
        }

        // 向 SSJJ 的 UserCommandSystem 注入武器槽切换指令
        // Aura 方式: 直接操作游戏输入层的 weaponSlot
        private static void SimulateWeaponSlot(int slot)
        {
            try
            {
                // 尝试通过 Contexts 设置武器槽请求
                var player = Contexts.sharedInstance?.player?.myPlayerEntity;
                if (player == null) return;

                // 映射槽位号到 KeyCode (Alpha1~Alpha3)
                KeyCode key = slot switch
                {
                    1 => KeyCode.Alpha1,
                    2 => KeyCode.Alpha2,
                    3 => KeyCode.Alpha3,
                    4 => KeyCode.Alpha4,
                    _ => KeyCode.Alpha1
                };

                // 通过 MouseSimulator.ForceKey 注入一次性按键脉冲
                Vape.Engine.MouseSimulator.ForceKey(key, Vape.Engine.MouseSimulator.InputState.TrueOnce);
            }
            catch { }
        }
    }
}
