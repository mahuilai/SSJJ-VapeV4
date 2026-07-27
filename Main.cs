using Assets.Scripts.Input;
using Vape.Cfg;
using Vape.Engine;
using Vape.Entity;
using Vape.Feature;
using Vape.Feature.Legit;
using Vape.Feature.Visuals;
using Vape.Feature.AutoTrigger;
using Vape.Feature.Backtrack;
using Vape.Features;
using Vape.UI;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using ThreadPriority = System.Threading.ThreadPriority;

namespace Vape
{
    public class Main : MonoBehaviour
    {
        private const string RuntimeRootName = "vp_runtime_root";
        private static GameObject _runtimeRoot;
        private readonly List<GameObject> _modules = new List<GameObject>();

        private void Awake()
        {
            AntiCheatBypass.Initialize();
            BacktrackManager.Initialize();

            var thread = new Thread(Init)
            {
                Priority = ThreadPriority.Highest
            };
            thread.Start();
        }

        private void Init()
        {
            if (_runtimeRoot != null) return;

            var existing = GameObject.Find(RuntimeRootName);
            if (existing != null)
            {
                _runtimeRoot = existing;
                return;
            }

            _runtimeRoot = new GameObject(RuntimeRootName);
            Attach<PlayerUpdate>("vp_entity");
            Attach<EspMaster>("vp_esp");
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
            Attach<Menu>("vp_clickgui");
            Attach<OverlayHost>("vp_overlay_host");
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

            InputCollector.Instance.SetDeviceInput(new MouseSimulator());
            HookManager.StartHook();
        }

        private void Attach<T>(string objectName) where T : Component
        {
            if (FindObjectOfType<T>() != null) return;

            var go = new GameObject(objectName);
            go.AddComponent<T>();
            DontDestroyOnLoad(go);
            _modules.Add(go);
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
