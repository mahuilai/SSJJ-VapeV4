// =============================================================
// CrouchAssist — 移植自 Aura BunnyHopCommandController / CrouchAssist
// 功能: 按键时自动执行"双蹲"动作序列 (蹲→站→蹲), 实现快速蹲起辅助
// 支持 7 种触发键: LeftCtrl, Mouse4, Mouse5, LeftAlt, MidClick, Space, C
// =============================================================
using Vape.Cfg;
using UnityEngine;

namespace Vape.Feature
{
    public class CrouchAssist : MonoBehaviour
    {
        // 双蹲序列状态机
        private enum CrouchState { Idle, CrouchDown, StandUp, CrouchAgain }

        private CrouchState _state    = CrouchState.Idle;
        private float       _stateTimer = 0f;

        // 每个阶段持续时间 (秒)
        private const float CrouchDownDuration  = 0.06f;
        private const float StandUpDuration     = 0.04f;
        private const float CrouchAgainDuration = 0.08f;

        // 是否正在执行序列 (供 Bhop 等模块查询, 避免 Space 冲突)
        public static bool IsActive { get; private set; }

        private void Update()
        {
            if (!Config.CrouchAssist)
            {
                IsActive = false;
                _state = CrouchState.Idle;
                return;
            }

            bool triggerKey = GetTriggerKeyDown();

            switch (_state)
            {
                case CrouchState.Idle:
                    if (triggerKey)
                    {
                        _state = CrouchState.CrouchDown;
                        _stateTimer = 0f;
                        IsActive = true;
                        SimulateKeyDown(KeyCode.LeftControl);
                    }
                    break;

                case CrouchState.CrouchDown:
                    _stateTimer += Time.deltaTime;
                    if (_stateTimer >= CrouchDownDuration)
                    {
                        _stateTimer = 0f;
                        _state = CrouchState.StandUp;
                        SimulateKeyUp(KeyCode.LeftControl);
                    }
                    break;

                case CrouchState.StandUp:
                    _stateTimer += Time.deltaTime;
                    if (_stateTimer >= StandUpDuration)
                    {
                        _stateTimer = 0f;
                        _state = CrouchState.CrouchAgain;
                        SimulateKeyDown(KeyCode.LeftControl);
                    }
                    break;

                case CrouchState.CrouchAgain:
                    _stateTimer += Time.deltaTime;
                    if (_stateTimer >= CrouchAgainDuration)
                    {
                        _stateTimer = 0f;
                        _state = CrouchState.Idle;
                        SimulateKeyUp(KeyCode.LeftControl);
                        IsActive = false;
                    }
                    break;
            }
        }

        // 读取配置中的触发键
        private static bool GetTriggerKeyDown()
        {
            KeyCode k = Config.CrouchAssistKey;
            return Input.GetKeyDown(k);
        }

        // 通过 MouseSimulator.ForceKey 注入按键状态到 SSJJ 输入管线
        private static void SimulateKeyDown(KeyCode key)
        {
            Vape.Engine.MouseSimulator.ForceKey(key, Vape.Engine.MouseSimulator.InputState.TrueKeep);
        }

        private static void SimulateKeyUp(KeyCode key)
        {
            Vape.Engine.MouseSimulator.ForceKey(key, Vape.Engine.MouseSimulator.InputState.None);
        }
    }
}
