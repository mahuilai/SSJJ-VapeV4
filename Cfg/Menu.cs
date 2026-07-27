using Vape.Cfg;
using Vape.UI;
using System;
using System.Collections.Generic;
using UnityEngine;

public static class Configs
{
    public static string Current = "default";
    public static List<string> Names = new List<string>();

    public static void Save(string name)
    {
        if (string.IsNullOrEmpty(name)) return;
        ConfigManager.SaveConfig(name);
        if (!Names.Contains(name)) Names.Add(name);
        Current = name;
    }

    public static void Load(string name)
    {
        if (string.IsNullOrEmpty(name)) return;
        ConfigManager.LoadConfig(name);
        Current = name;
    }

    public static void Delete(string name)
    {
        if (name == "default" || !Names.Contains(name)) return;
        ConfigManager.DeleteConfig(name);
        Names.Remove(name);
        if (Current == name) Current = "default";
    }

    public static void Init()
    {
        string[] saved = ConfigManager.GetAllConfigNames();
        Names = new List<string>(saved);
        if (!Names.Contains("default"))
        {
            Names.Add("default");
            ConfigManager.SaveConfig("default");
        }
        ConfigManager.LoadConfig("default");
    }
}

namespace Vape.Features
{
    public class Menu : MonoBehaviour
    {
        public static bool forceThirdPerson;
        public static bool IsOpen = false;

        private enum PanelId { Offense, Vision, Motion, Utility, Cosmetic, Profiles, Count }

        private static readonly string[] PanelNames = { "Offense", "Vision", "Motion", "Utility", "Cosmetic", "Profiles" };
        private static readonly string[] PanelChips = { "ATK", "VIS", "MOV", "UTIL", "COS", "CFG" };

        private bool[] _open = new bool[(int)PanelId.Count];
        private Rect[] _rect = new Rect[(int)PanelId.Count];
        private Vector2[] _scroll = new Vector2[(int)PanelId.Count];
        private Rect _nav = new Rect(36, 28, 560, 52);

        private string _bindId;
        private bool _bindWait;
        private string _newCfg = "";
        private string _delCfg = "";
        private bool _confirmDel;
        private Rect _delRect;
        private string _toast;
        private float _toastAt;
        private bool _toastOn;
        private string _pitchBuf = "";
        private string _yawBuf = "";
        private Vector2 _skinScroll;
        private Vector2 _cfgScroll;
        private readonly Dictionary<string, DropState> _drops = new Dictionary<string, DropState>();
        private readonly Dictionary<string, Widgets.ComboState> _combos = new Dictionary<string, Widgets.ComboState>();

        private class DropState
        {
            public bool Open;
            public Rect Rect;
            public Vector2 Scroll;
            public int Index;
        }

        private static readonly string[] Bones =
        {
            "HeadCore","HeadTop","Neck","Gut","LClav","RClav","LUpper","RUpper","LFore","RFore",
            "LHand","RHand","LFinger","RFinger","Pelvis","LThigh","RThigh","LKnee","RKnee","LFoot","RFoot","LToe","RToe"
        };

        private void Start()
        {
            Configs.Init();
            useGUILayout = false;
            Vape.Feature.SkinChanger.Initialize();
            for (int i = 0; i < (int)PanelId.Count; i++)
            {
                _open[i] = i < 2;
                _rect[i] = new Rect(36 + (i % 3) * 300f, 100 + (i / 3) * 36f, 290f, 440f);
            }
            _drops["aim"] = new DropState();
            _drops["backAccessory"] = new DropState();
            _drops["character"] = new DropState();
            _drops["weapon"] = new DropState();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F12))
            {
                IsOpen = !IsOpen;
                useGUILayout = IsOpen;
            }
            if (Input.GetKeyDown(Config.OrbitKey)) forceThirdPerson = !forceThirdPerson;
            if (Input.GetKeyDown(Config.AirPathKey)) Config.AirPath = !Config.AirPath;
            if (_toastOn && Time.time - _toastAt > 2.2f) _toastOn = false;
            if (_bindWait) PollBind();
        }

        private void PollBind()
        {
            foreach (KeyCode kc in Enum.GetValues(typeof(KeyCode)))
            {
                if (!Input.GetKeyDown(kc)) continue;
                if (kc == KeyCode.Escape) SetBind(_bindId, KeyCode.None);
                else if (kc != KeyCode.F12) SetBind(_bindId, kc);
                else continue;
                _bindWait = false; _bindId = null; break;
            }
            for (int i = 0; i < 3; i++)
            {
                if (!Input.GetMouseButtonDown(i)) continue;
                SetBind(_bindId, i == 0 ? KeyCode.Mouse0 : i == 1 ? KeyCode.Mouse1 : KeyCode.Mouse2);
                _bindWait = false; _bindId = null; break;
            }
        }

        private void SetBind(string id, KeyCode k)
        {
            switch (id)
            {
                case "Soft": Config.SoftAimKey = k; break;
                case "Hard": Config.HardAimKey = k; break;
                case "Fix": Config.AngleFixKey = k; break;
                case "Orbit": Config.OrbitKey = k; break;
                case "Air": Config.AirPathKey = k; break;
            }
        }

        private void OnGUI()
        {
            if (Vape.UI.OverlayHost.ExternalUiActive) return;
            if (!IsOpen) return;
            Widgets.BeginFrame();
            UiDraw.Ensure();

            if (Event.current.type == EventType.MouseDown)
            {
                bool hit = false;
                foreach (var d in _drops.Values)
                    if (d.Open && d.Rect.Contains(Event.current.mousePosition)) { hit = true; break; }
                if (!hit) foreach (var d in _drops.Values) d.Open = false;
            }

            GUI.backgroundColor = Theme.BgDeep;
            _nav = GUI.Window(9000, _nav, DrawNav, "", UiDraw.Window);

            for (int i = 0; i < (int)PanelId.Count; i++)
            {
                if (!_open[i]) continue;
                GUI.backgroundColor = Theme.BgPanel;
                int capture = i;
                _rect[i] = GUI.Window(9100 + i, _rect[i], id => DrawPanel(id), "", UiDraw.Window);
            }

            if (_confirmDel)
            {
                _delRect = new Rect(Screen.width * 0.5f - 140f, Screen.height * 0.5f - 58f, 280f, 116f);
                GUI.backgroundColor = Theme.BgDeep;
                GUI.Window(9200, _delRect, DrawDelete, "", UiDraw.Window);
                GUI.BringWindowToFront(9200);
            }

            if (_toastOn)
            {
                var r = new Rect(Screen.width * 0.5f - 120f, 18f, 240f, 30f);
                UiDraw.Frame(r, Theme.BgCard, Theme.Accent);
                GUI.Label(r, _toast, new GUIStyle(UiDraw.Label) { alignment = TextAnchor.MiddleCenter });
            }
            GUI.backgroundColor = Color.white;
        }

        private void DrawNav(int id)
        {
            var r = new Rect(0, 0, _nav.width, _nav.height);
            Widgets.NavBar(r, "VAPE", "CLIENT");
            float x = 160f;
            for (int i = 0; i < (int)PanelId.Count; i++)
            {
                var chip = new Rect(x, 12, 56, 28);
                if (Widgets.ChipTab(chip, _open[i], PanelChips[i])) _open[i] = !_open[i];
                x += 60f;
            }
            GUI.Label(new Rect(_nav.width - 96, 16, 90, 22), "[F12]", new GUIStyle(GUI.skin.label) {
                alignment = TextAnchor.MiddleRight, fontSize = 11, normal = { textColor = Theme.TextMuted }
            });
            GUI.DragWindow(new Rect(0, 0, 10000, 52));
        }

        private void DrawPanel(int id)
        {
            int idx = id - 9100;
            var rect = _rect[idx];
            Widgets.PanelBackground(new Rect(0, 0, rect.width, rect.height));
            GUI.Label(new Rect(12, 5, 180, 20), PanelNames[idx].ToUpperInvariant(), new GUIStyle(GUI.skin.label) {
                fontSize = 13, fontStyle = FontStyle.Bold, normal = { textColor = Theme.TextPrimary }
            });
            var closeR = new Rect(rect.width - 28, 4, 22, 20);
            bool closeHov = closeR.Contains(Event.current.mousePosition);
            Widgets.RoundRect(closeR, closeHov ? Theme.Danger : Theme.BgHover, 4f);
            GUI.Label(closeR, "\u00d7", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 14, normal = { textColor = Color.white } });
            if (GUI.Button(closeR, GUIContent.none, GUIStyle.none)) { _open[idx] = false; return; }

            GUILayout.BeginArea(new Rect(8, 34, rect.width - 16, rect.height - 42));
            _scroll[idx] = GUILayout.BeginScrollView(_scroll[idx]);
            switch ((PanelId)idx)
            {
                case PanelId.Offense: DrawOffense(); break;
                case PanelId.Vision: DrawVision(); break;
                case PanelId.Motion: DrawMotion(); break;
                case PanelId.Utility: DrawUtility(); break;
                case PanelId.Cosmetic: DrawCosmetic(); break;
                case PanelId.Profiles: DrawProfiles(); break;
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUI.DragWindow(new Rect(0, 0, 10000, 28));
        }

        private void DrawDelete(int id)
        {
            var rr = new Rect(0, 0, _delRect.width, _delRect.height);
            Widgets.Shadow(rr, 6f);
            Widgets.Rect(rr, Theme.BgDeep, Theme.Danger, 1f);
            GUI.Label(new Rect(0, 16, _delRect.width, 36), $"Remove profile [{_delCfg}]?", new GUIStyle(GUI.skin.label) {
                alignment = TextAnchor.MiddleCenter, fontSize = 13, normal = { textColor = Theme.TextPrimary }
            });
            var cancel = new Rect(28, 70, 100, 28);
            var remove = new Rect(152, 70, 100, 28);
            Widgets.RoundRect(cancel, Theme.BgHover, 4f);
            Widgets.RoundRect(remove, Theme.Danger, 4f);
            GUI.Label(cancel, "Cancel", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Theme.TextPrimary } });
            GUI.Label(remove, "Remove", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } });
            if (GUI.Button(cancel, GUIContent.none, GUIStyle.none)) { _confirmDel = false; _delCfg = ""; }
            if (GUI.Button(remove, GUIContent.none, GUIStyle.none))
            {
                Configs.Delete(_delCfg); _confirmDel = false; _delCfg = ""; Toast("Profile removed");
            }
            GUI.DragWindow(new Rect(0, 0, 1000, 20));
        }

        private void Section(string t) => Widgets.Section(t);

        private void Card(Action body)
        {
            Widgets.BeginCard();
            body();
            Widgets.EndCard();
        }

        private void Toggle(ref bool v, string text) => Widgets.Toggle(ref v, text);

        private void SliderI(string text, ref int v, int min, int max, string s = "")
        {
            if (string.IsNullOrEmpty(s))
            {
                float f = v;
                Widgets.SliderFloat(text, ref f, min, max, "F0");
                v = Mathf.RoundToInt(f);
            }
            else Widgets.SliderIntS(text, ref v, min, max, s);
        }

        private void SliderF(string text, ref float v, float min, float max, string fmt = "F1")
            => Widgets.SliderFloat(text, ref v, min, max, fmt);

        private void Bind(string id, string text, KeyCode key)
            => Widgets.KeyBind(id, text, key, ref _bindId, ref _bindWait);

        private void Drop(string key, string label, List<string> items, Action<string> onSelect = null)
        {
            Widgets.Combo(key, label, items, _combos, onSelect);
        }

        private void Toast(string m) { _toastOn = true; _toast = m; _toastAt = Time.time; }

        private void DrawOffense()
        {
            Section("Soft Aim");
            Card(() =>
            {
                Toggle(ref Config.SoftAim, "Soft Aim");
                Toggle(ref Config.SoftAimFovDraw, "Draw FOV Ring");
                Toggle(ref Config.SoftAimVisCheck, "Visibility Gate");
                Toggle(ref Config.SoftAimLine, "Lock Beam");
                Bind("Soft", "Activation", Config.SoftAimKey);
                if (Config.SoftAim) Toggle(ref Config.HistoryAutoShoot, "History Auto Shot");
            });
            if (Config.SoftAim)
            {
                Section("Soft Tuning");
                Card(() =>
                {
                    SliderI("Field", ref Config.SoftAimFov, 0, 180, "°");
                    Toggle(ref Config.SoftAimSmoothOn, "Smoothing");
                    if (Config.SoftAimSmoothOn) SliderF("Smooth", ref Config.SoftAimSmooth, 1f, 30f);
                    Drop("aim", "Target Bone", new List<string>(Bones), n => Config.SoftAimBone = Array.IndexOf(Bones, n));
                });
            }

            Section("Hard Aim");
            Card(() =>
            {
                Toggle(ref Config.HardAim, "Hard Aim");
                if (Config.HardAim)
                {
                    Toggle(ref Config.HardAimOnKey, "Key Gate");
                    if (Config.HardAimOnKey) Bind("Hard", "Hard Key", Config.HardAimKey);
                }
            });

            Section("History Hit");
            Card(() =>
            {
                Toggle(ref Config.HistoryHit, "History Hit");
                if (!Config.HistoryHit) return;
                SliderI("Window", ref Config.HistoryWindow, 0, 5000, "ms");
                Toggle(ref Config.HistoryPreferLive, "Prefer Live Body");
                Toggle(ref Config.HistoryNoWall, "Skip Wall Ghost");
                Toggle(ref Config.HistoryTrail, "Trail Ghosts");
            });

            Section("Angle / Desync");
            Card(() =>
            {
                Toggle(ref Config.AngleFix, "Angle Fix");
                Toggle(ref Config.AngleFixRandom, "Randomize");
                Bind("Fix", "Force Fix", Config.AngleFixKey);
                Toggle(ref Config.Desync, "Desync");
                Toggle(ref Config.PacketHold, "Packet Hold");
                if (Config.PacketHold) SliderI("Ticks", ref Config.PacketHoldTicks, 0, 100);
            });

            if (Config.Desync)
            {
                Section("Desync Modes");
                Card(() =>
                {
                    Config.DesyncMode = Widgets.Segment("desync_mode", Config.DesyncMode, new[] { "Static", "Spin", "Jitter" });
                    if (Config.DesyncMode == 0)
                    {
                        SliderF("Pitch", ref Config.DesyncPitch, -360, 360, "F0");
                        GUILayout.BeginHorizontal();
                        GUILayout.Label("Pitch In", UiDraw.Label, GUILayout.Width(108));
                        _pitchBuf = GUILayout.TextField(Config.DesyncPitch.ToString());
                        float.TryParse(_pitchBuf, out Config.DesyncPitch);
                        GUILayout.EndHorizontal();
                        SliderF("Yaw", ref Config.DesyncYaw, -180, 180, "F0");
                        GUILayout.BeginHorizontal();
                        GUILayout.Label("Yaw In", UiDraw.Label, GUILayout.Width(108));
                        _yawBuf = GUILayout.TextField(Config.DesyncYaw.ToString());
                        float.TryParse(_yawBuf, out Config.DesyncYaw);
                        GUILayout.EndHorizontal();
                    }
                    else if (Config.DesyncMode == 1)
                    {
                        SliderF("Pitch", ref Config.DesyncPitch, -360, 360, "F0");
                        SliderI("Spin", ref Config.DesyncSpin, 0, 100);
                    }
                    else
                    {
                        SliderF("Pitch", ref Config.DesyncPitch, -360, 360, "F0");
                        SliderF("Yaw", ref Config.DesyncYaw, -180, 180, "F0");
                        SliderF("Jitter Min", ref Config.DesyncJitterMin, -180, 180, "F0");
                        SliderF("Jitter Max", ref Config.DesyncJitterMax, -180, 180, "F0");
                    }
                });
            }

            Section("Auto Fire");
            Card(() =>
            {
                Toggle(ref Config.AutoFire, "Auto Fire");
                Toggle(ref Config.AutoFireNoScope, "Skip Scopes");
                Toggle(ref Config.AutoFireDelay, "Armed Delay");
                if (Config.AutoFireDelay) SliderF("Hold", ref Config.AutoFireHold, 0f, 10f);
            });
        }

        private void DrawVision()
        {
            Section("Player Overlay");
            Card(() =>
            {
                Toggle(ref Config.EspMaster, "ESP Master");
                Toggle(ref Config.EspBox, "Bound Box");
                Toggle(ref Config.EspBones, "Bone Map");
                Toggle(ref Config.EspHealthBar, "Vital Bar");
                Toggle(ref Config.EspHealth, "Vital Text");
                Toggle(ref Config.EspName, "Identity");
                Toggle(ref Config.EspDist, "Range");
                Toggle(ref Config.EspWeapon, "Loadout");
                Toggle(ref Config.EspBomb, "Bomb Tag");
                Toggle(ref Config.EspSnap, "Snap Beam");
                Toggle(ref Config.EspYaw, "Yaw Read");
                Toggle(ref Config.EspPitch, "Pitch Read");
                Toggle(ref Config.EspCube, "Cube Mesh");
            });

            Section("Box Style");
            Card(() =>
            {
                Config.EspBoxStyle = Widgets.Segment("box_style", Config.EspBoxStyle, new[] { "Full", "Corners" });
            });

            Section("World Sense");
            Card(() =>
            {
                Toggle(ref Config.ModelGlow, "Model Glow");
                Toggle(ref Config.AntiFlash, "Anti Flash");
                Toggle(ref Config.ShotPath, "Shot Path");
                Toggle(ref Config.ProjectileTags, "Projectile Tags");
                Toggle(ref Config.FieldTags, "Field Tags");
                Toggle(ref Config.LootTags, "Loot Tags");
                Toggle(ref Config.LootGlow, "Loot Glow");
            });

            Section("Interface");
            Card(() =>
            {
                Toggle(ref Config.ObserverPanel, "Observers");
                Toggle(ref Config.MiniMap, "Radar");
                Toggle(ref Config.VelocityRing, "Velocity Ring");
                Toggle(ref Config.StateStrip, "State Strip");
            });
        }

        private void DrawMotion()
        {
            Section("Camera");
            Card(() =>
            {
                Toggle(ref Config.OrbitCam, "Orbit Cam");
                Bind("Orbit", "Orbit Key", Config.OrbitKey);
                Toggle(ref Config.LensCustom, "Custom Lens");
                SliderI("Orbit FOV", ref Config.OrbitFov, 0, 150);
                SliderF("Lens FOV", ref Config.LensFov, 0, 150, "F0");
            });

            Section("Traversal");
            Card(() =>
            {
                Toggle(ref Config.AutoHop, "Auto Hop");
                Toggle(ref Config.AirPath, "Air Path");
                Toggle(ref Config.Airglide, "Airglide");
                Bind("Air", "Air Key", Config.AirPathKey);
                Toggle(ref Config.GhostStep, "Ghost Step");
            });
        }

        private void DrawUtility()
        {
            Section("Recoil");
            Card(() =>
            {
                Toggle(ref Config.RecoilStrip, "Recoil Strip");
                if (Config.RecoilStrip) Toggle(ref Config.RecoilSmooth, "Strip Smooth");
                Toggle(ref Config.BlockSecondary, "Block Secondary");
                Toggle(ref Config.ConePredict, "Cone Predict");
            });

            Section("Assist");
            Card(() =>
            {
                Toggle(ref Config.PathAssist, "Path Assist");
                Toggle(ref Config.AutoSpam, "Auto Spam");
                Config.SpamText = Widgets.TextField("", Config.SpamText ?? "");
            });

            Section("World");
            Card(() =>
            {
                if (Widgets.Button("Potato Quality"))
                    Vape.Feature.WorldSettings.SetLowestQuality();
                if (Widgets.Button("Unlock Framerate"))
                    Vape.Feature.WorldSettings.UnlockFrameRate();
            });
        }

        private void DrawCosmetic()
        {
            Section("Avatar");
            _skinScroll = GUILayout.BeginScrollView(_skinScroll);
            var local = Vape.Entity.PlayerUpdate.LocalEntity;
            if (local?._entity != null && local._entity.hasBasicInfo)
            {
                var info = local._entity.basicInfo.Current;
                Card(() =>
                {
                    float s = info.Scale;
                    Widgets.SliderFloat("Scale", ref s, -5f, 5f, "F2");
                    if (Math.Abs(s - info.Scale) > 0.01f) Vape.Feature.SkinChanger.ChangeScale(s);

                    float h = info.HeadEnlarge;
                    Widgets.SliderFloat("Head", ref h, -5f, 5f, "F2");
                    if (Math.Abs(h - info.HeadEnlarge) > 0.01f) Vape.Feature.SkinChanger.ChangeHeadEnlarge(h);

                    int t = info.Team;
                    Widgets.SliderIntS("Team", ref t, 0, 13, "");
                    if (t != info.Team) Vape.Feature.SkinChanger.ChangeTeam(t);

                    int a = info.Alpha;
                    Widgets.SliderIntS("Alpha", ref a, 0, 100, "");
                    if (a != info.Alpha) Vape.Feature.SkinChanger.ChangeAlpha(a);

                    int sa = info.SelfAlpha;
                    Widgets.SliderIntS("Self", ref sa, 0, 100, "");
                    if (sa != info.SelfAlpha) Vape.Feature.SkinChanger.ChangeSelfAlpha(sa);
                });
            }
            else GUILayout.Label("Awaiting local avatar...", UiDraw.Muted);

            if (Vape.Feature.SkinChanger.BackAccessoryNames.Count > 0)
            {
                Section("Backpiece");
                Card(() => Drop("backAccessory", "Back", Vape.Feature.SkinChanger.BackAccessoryNames,
                    n => Vape.Feature.SkinChanger.ChangeBackAccessory(n)));
            }
            if (Vape.Feature.SkinChanger.CharacterNames.Count > 0)
            {
                Section("Character");
                Card(() => Drop("character", "Model", Vape.Feature.SkinChanger.CharacterNames,
                    n => Vape.Feature.SkinChanger.ChangeCharacter(n)));
            }
            if (Vape.Feature.SkinChanger.WeaponNames.Count > 0)
            {
                Section("Weapon Skin");
                Card(() => Drop("weapon", "Skin", Vape.Feature.SkinChanger.WeaponNames,
                    n => Vape.Feature.SkinChanger.ChangeWeapon(n)));
            }
            GUILayout.EndScrollView();
        }

        private void DrawProfiles()
        {
            Section($"Active · {Configs.Current}");
            Card(() =>
            {
                GUILayout.BeginHorizontal();
                _newCfg = Widgets.TextField("Name", _newCfg);
                if (Widgets.Button("Save Profile") && !string.IsNullOrEmpty(_newCfg))
                {
                    Configs.Save(_newCfg); Toast("Saved " + _newCfg); _newCfg = "";
                }
                GUILayout.EndHorizontal();
            });

            Section("Library");
            Card(() =>
            {
                _cfgScroll = GUILayout.BeginScrollView(_cfgScroll, GUILayout.Height(240));
                foreach (var c in Configs.Names)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(c, UiDraw.Label, GUILayout.Width(90));
                    if (Widgets.Button("Load"))
                    { Configs.Load(c); Toast("Loaded " + c); }
                    if (c != "default" && Widgets.Button("Delete"))
                    { _delCfg = c; _confirmDel = true; }
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndScrollView();
            });
        }
    }
}
