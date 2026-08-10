using Assets.Scripts.Input;
using Vape.Cfg;
using Vape.Engine;
using Vape.Entity;
using Vape.Feature;
using Vape.Feature.Precision;
using Vape.Feature.Overlay;
using Vape.Feature.Automation;
using Vape.Features;
using System.Collections.Generic;
using UnityEngine;

namespace Vape
{
    public class Main : MonoBehaviour
    {
        private const string RuntimeRootName = "vp_runtime_root";
        private static GameObject _runtimeRoot;
        private readonly List<GameObject> _modules = new List<GameObject>();
        private PhysxModelDisplay _physxModelDisplay;

        private void Awake()
        {
            AntiCheatBypass.Initialize();
            Init();
        }

        private void Init()
        {
            RemoveLegacyMovementModules();
            ReplaceVisualModule<EspMaster>("vp_esp");
            if (_runtimeRoot != null)
            {
                Attach<BlinkMovement>("vp_blink");
                Attach<KeyHud>("vp_keys");
                Attach<CsgoHud>("vp_csgo_hud");
                Attach<ChatHudStyler>("vp_chat_style");
                EnsureCardMenu();
                return;
            }

            var existing = GameObject.Find(RuntimeRootName);
            if (existing != null)
            {
                _runtimeRoot = existing;
                Attach<BlinkMovement>("vp_blink");
                Attach<KeyHud>("vp_keys");
                Attach<CsgoHud>("vp_csgo_hud");
                Attach<ChatHudStyler>("vp_chat_style");
                EnsureCardMenu();
                return;
            }

            _runtimeRoot = new GameObject(RuntimeRootName);
            Attach<PlayerUpdate>("vp_entity");
            Attach<ObserverPanel>("vp_spec");
            Attach<DesyncIndicator>("vp_aa_ind");
            Attach<Trace>("vp_trace");
            Attach<ModelGlow>("vp_chams");
            Attach<MiniMap>("vp_radar");
            Attach<C4Timer>("vp_c4");
            Attach<Crosshair>("vp_cross");
            Attach<SoftAim>("vp_aim");
            Attach<AutoFire>("vp_trigger");
            Attach<RecoilStrip>("vp_recoil");
            Attach<AngleFix>("vp_resolver");
            Attach<AutoSpam>("vp_say");
            Attach<BlinkMovement>("vp_blink");
            EnsureCardMenu();
            Attach<BoundingBox3D>("vp_box3d");
            Attach<StateStrip>("vp_hud");
            Attach<ConsoleManager>("vp_log");
            Attach<MikadukiSwordDkl>("vp_auto_dkl");
            Attach<WindSpiritRecall>("vp_wind");
            Attach<NonStopDanceAuto>("vp_dance");
            Attach<DamageDisplay>("vp_dmg");
            Attach<ItemESP>("vp_item");
            Attach<ItemOutline>("vp_item_glow");
            Attach<MoveEntityESP>("vp_move_esp");
            Attach<SceneBuffESP>("vp_buff_esp");
            Attach<VelocityRing>("vp_speed");
            Attach<KeyHud>("vp_keys");
            Attach<CsgoHud>("vp_csgo_hud");
            Attach<ChatHudStyler>("vp_chat_style");
            Attach<CrouchAssist>("vp_crouch_assist");
            Attach<InstantSniper>("vp_instant_sniper");

            InputCollector.Instance.SetDeviceInput(new MouseSimulator());
            HookManager.StartHook();
        }

        private void Update()
        {
            HookManager.UpdateBhopInput();
            SpeedBoost.UpdateHotkey();
            ProtocolProbe.Update();

            // Loader invokes Awake during injection. PhysX touches Unity rendering and
            // physics APIs, so create it lazily from the first main-thread Update.
            if (Config.PhysxModel && _physxModelDisplay == null)
                _physxModelDisplay = Attach<PhysxModelDisplay>("vp_physx_model");
        }

        private static void RemoveLegacyMovementModules()
        {
            string[] names = { "vp_air_lock", "vp_fakelag", "vp_fakelag_speed_sync", "vp_bieber_model" };
            for (int i = 0; i < names.Length; i++)
            {
                GameObject module = GameObject.Find(names[i]);
                if (module != null)
                    Destroy(module);
            }
        }

        // A previous injected build can leave the persistent runtime root alive.
        // Always repair the menu component instead of treating that root as fully initialized.
        private void EnsureCardMenu()
        {
            var menu = FindObjectOfType<Vape.UI.Menu.CardClickGui>();
            if (menu != null)
            {
                menu.enabled = true;
                return;
            }

            var go = new GameObject("vp_clickgui");
            go.AddComponent<Vape.UI.Menu.CardClickGui>();
            DontDestroyOnLoad(go);
            _modules.Add(go);
        }

        private T Attach<T>(string objectName) where T : Component
        {
            T existing = FindObjectOfType<T>();
            if (existing != null)
                return existing;

            var go = new GameObject(objectName);
            T component = go.AddComponent<T>();
            DontDestroyOnLoad(go);
            _modules.Add(go);
            return component;
        }

        private void ReplaceVisualModule<T>(string objectName) where T : Component
        {
            GameObject existing = GameObject.Find(objectName);
            if (existing != null)
                existing.SetActive(false);

            GameObject gameObject = new GameObject(objectName);
            gameObject.AddComponent<T>();
            DontDestroyOnLoad(gameObject);
            _modules.Add(gameObject);
        }

        private void Destroy()
        {
            foreach (var m in _modules)
            {
                if (m != null) Destroy(m);
            }
            if (_runtimeRoot != null) Destroy(_runtimeRoot);
        }
    }
}
