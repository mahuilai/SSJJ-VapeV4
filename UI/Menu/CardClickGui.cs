using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using Vape.Cfg;
using Vape.Features;

namespace Vape.UI.Menu
{
    public class CardClickGui : MonoBehaviour
    {
        private const int CategoryCount = 6;
        private static readonly string[] Bones =
        {
            "HeadCore", "HeadTop", "Neck", "Gut", "LClav", "RClav", "LUpper", "RUpper", "LFore", "RFore",
            "LHand", "RHand", "LFinger", "RFinger", "Pelvis", "LThigh", "RThigh", "LKnee", "RKnee", "LFoot", "RFoot", "LToe", "RToe"
        };
        private readonly Rect[] _cards = new Rect[CategoryCount];
        private readonly bool[] _collapsed = new bool[CategoryCount];
        private readonly float[] _contentHeights = new float[CategoryCount];
        private readonly float[] _scrollOffsets = new float[CategoryCount];
        private readonly float[] _scrollTargets = new float[CategoryCount];
        private readonly float[] _maxScrolls = new float[CategoryCount];
        private readonly CardScrollBar[] _scrollBars = new CardScrollBar[CategoryCount];
        private readonly Dictionary<string, CardAnimFloat> _animations = new Dictionary<string, CardAnimFloat>();
        private GUIStyle _windowStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _rowStyle;
        private GUIStyle _smallStyle;
        private GUIStyle _inputStyle;
        private float _contentWidth;
        private string _bindingId;
        private string _newProfile = "profile";
        private bool _menuKeyHeld;
        private int _drawingCardIndex;
        private string _pickerId;
        private string _pickerTitle;
        private string _pickerSearch = string.Empty;
        private List<string> _pickerChoices;
        private Action<string> _pickerApply;
        private Rect _pickerRect;
        private int _pickerCardIndex = -1;
        private int _layoutScreenWidth;
        private int _layoutScreenHeight;
        private float _cardHeightLimit = 560f;

        private float CardHeight(int index)
        {
            float maximum = Mathf.Max(150f, _cardHeightLimit);
            if (_contentHeights[index] <= 0f)
                return maximum;

            return Mathf.Clamp(_contentHeights[index] + 84f, 150f, maximum);
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

        private void Start()
        {
            Configs.Init();
            Vape.Feature.SkinChanger.Initialize();
            useGUILayout = true;
            // Open once after a fresh attach so a failed hotkey cannot leave the menu unreachable.
            Vape.Features.Menu.IsOpen = true;
            for (int i = 0; i < CategoryCount; i++)
                _scrollBars[i] = new CardScrollBar();
            LayoutCards();
        }

        private void Update()
        {
            if (_bindingId != null)
                PollBinding();
            else
                PollMenuHotkey();

            if (_bindingId == null && Config.OrbitKey != KeyCode.None && Input.GetKeyDown(Config.OrbitKey)) Vape.Features.Menu.forceThirdPerson = !Vape.Features.Menu.forceThirdPerson;
            Vape.Feature.SpeedBoost.UpdateHotkey();
            Configs.UpdateAutoSave();
            foreach (var animation in _animations.Values) animation.Update(Time.unscaledDeltaTime);
            float scrollBlend = 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime);
            for (int i = 0; i < CategoryCount; i++)
            {
                _scrollTargets[i] = Mathf.Clamp(_scrollTargets[i], 0f, _maxScrolls[i]);
                _scrollOffsets[i] = _scrollBars[i] != null && _scrollBars[i].IsDragging
                    ? _scrollTargets[i]
                    : Mathf.Lerp(_scrollOffsets[i], _scrollTargets[i], scrollBlend);
            }
        }

        private void PollMenuHotkey()
        {
            bool held = Input.GetKey(KeyCode.F12) || Input.GetKey(KeyCode.Insert) || IsNativeMenuKeyHeld();
            if (held && !_menuKeyHeld) ToggleMenu();
            _menuKeyHeld = held;
        }

        private static bool IsNativeMenuKeyHeld()
        {
            if (Application.platform != RuntimePlatform.WindowsPlayer && Application.platform != RuntimePlatform.WindowsEditor)
                return false;

            const int F12 = 0x7B;
            const int Insert = 0x2D;
            return (GetAsyncKeyState(F12) & 0x8000) != 0 || (GetAsyncKeyState(Insert) & 0x8000) != 0;
        }

        private void PollBinding()
        {
            foreach (KeyCode key in Enum.GetValues(typeof(KeyCode)))
            {
                if (!Input.GetKeyDown(key)) continue;
                if (key == KeyCode.F12 || key == KeyCode.Insert) continue;
                SetBinding(_bindingId, key == KeyCode.Escape ? KeyCode.None : key);
                _bindingId = null;
                return;
            }
        }

        private void ToggleMenu()
        {
            Vape.Features.Menu.IsOpen = !Vape.Features.Menu.IsOpen;
        }

        private static void SetBinding(string id, KeyCode key)
        {
            switch (id)
            {
                case "soft": Config.SoftAimKey = key; break;
                case "hard": Config.HardAimKey = key; break;
                case "angle": Config.AngleFixKey = key; break;
                case "orbit": Config.OrbitKey = key; break;
                case "bhop8": Config.BhopKey = key; break;
                case "boost": Config.SpeedBoostKey = key; break;
                case "blink": Config.BlinkMoveKey = key; break;
                case "crouch": Config.CrouchAssistKey = key; break;
                case "sniper": Config.InstantSniperKey = key; break;
            }
        }

        private void LayoutCards()
        {
            const float gap = 12f;
            const float marginX = 18f;
            const float top = 50f;
            const float bottom = 18f;
            const float minimumWidth = 220f;

            _layoutScreenWidth = Screen.width;
            _layoutScreenHeight = Screen.height;
            float usableWidth = Mathf.Max(220f, Screen.width - marginX * 2f);
            int columns = Mathf.Clamp(
                Mathf.FloorToInt((usableWidth + gap) / (minimumWidth + gap)),
                1,
                CategoryCount);
            int rows = Mathf.CeilToInt(CategoryCount / (float)columns);
            float width = Mathf.Min(300f, (usableWidth - gap * (columns - 1)) / columns);
            float gridWidth = width * columns + gap * (columns - 1);
            float startX = (Screen.width - gridWidth) * 0.5f;
            float usableHeight = Mathf.Max(150f, Screen.height - top - bottom - gap * (rows - 1));
            _cardHeightLimit = Mathf.Max(150f, Mathf.Floor(usableHeight / rows));

            for (int i = 0; i < CategoryCount; i++)
            {
                int row = i / columns;
                int column = i % columns;
                _cards[i] = new Rect(
                    startX + column * (width + gap),
                    top + row * (_cardHeightLimit + gap),
                    width,
                    CardHeight(i));
            }
        }

        private void OnGUI()
        {
            if (!Vape.Features.Menu.IsOpen) return;
            EnsureStyles();
            if (_layoutScreenWidth != Screen.width || _layoutScreenHeight != Screen.height)
                LayoutCards();
            if (Event.current.type == EventType.MouseDown && _pickerChoices != null && !_pickerRect.Contains(Event.current.mousePosition))
                ClosePicker();

            for (int i = 0; i < CategoryCount; i++)
            {
                Rect card = _cards[i];
                card.height = _collapsed[i] ? 60f : CardHeight(i);
                _cards[i] = card;
                _cards[i] = GUI.Window(9100 + i, card, DrawCard, "", _windowStyle);
                Rect clamped = _cards[i];
                clamped.x = Mathf.Clamp(clamped.x, 6f, Mathf.Max(6f, Screen.width - clamped.width - 6f));
                clamped.y = Mathf.Clamp(clamped.y, 6f, Mathf.Max(6f, Screen.height - clamped.height - 6f));
                _cards[i] = clamped;
            }
            DrawPicker();
        }

        private void EnsureStyles()
        {
            if (_windowStyle != null) return;
            _windowStyle = new GUIStyle(GUI.skin.window)
            {
                padding = new RectOffset(0, 0, 0, 0),
                normal = { background = CardDraw.Transparent }, hover = { background = CardDraw.Transparent },
                active = { background = CardDraw.Transparent }, focused = { background = CardDraw.Transparent },
                onNormal = { background = CardDraw.Transparent }, onHover = { background = CardDraw.Transparent },
                onActive = { background = CardDraw.Transparent }, onFocused = { background = CardDraw.Transparent }
            };
            _titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = CardColors.TextPrimary } };
            _rowStyle = new GUIStyle(GUI.skin.label) { fontSize = 19, alignment = TextAnchor.MiddleLeft, normal = { textColor = CardColors.TextPrimary } };
            _smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleLeft, normal = { textColor = CardColors.TextSecondary } };
            _inputStyle = new GUIStyle(GUI.skin.textField) { fontSize = 16, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(12, 12, 7, 7), normal = { textColor = CardColors.TextPrimary, background = CardDraw.Transparent }, focused = { textColor = CardColors.TextPrimary, background = CardDraw.Transparent } };
        }

        private string T(string english, string chinese)
        {
            return Config.Language == 1 ? chinese : english;
        }

        private string CategoryName(int index)
        {
            switch (index)
            {
                case 0: return T("COMBAT", "战斗");
                case 1: return T("VISUALS", "视觉");
                case 2: return T("MOVEMENT", "移动");
                case 3: return T("UTILITY", "工具");
                case 4: return T("COSMETIC", "外观");
                default: return T("PROFILES", "配置");
            }
        }

        private void DrawCard(int id)
        {
            int index = id - 9100;
            _drawingCardIndex = index;
            Rect full = new Rect(0f, 0f, _cards[index].width, _cards[index].height);
            _contentWidth = full.width - 40f;
            CardDraw.RoundedFrame(full, CardColors.BgFrame, CardColors.Border, 28f, 2f);
            CardDraw.RoundedRect(new Rect(0f, 0f, full.width, 60f), CardColors.BgHover, 26f);
            GUI.Label(new Rect(18f, 10f, full.width - 78f, 40f), CategoryName(index), _titleStyle);
            Rect collapse = new Rect(full.width - 56f, 13f, 36f, 32f);
            CardDraw.RoundedFrame(collapse, CardColors.BgFrame, CardColors.Border, 16f, 1f);
            GUI.Label(collapse, _collapsed[index] ? "+" : "-", new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter, normal = { textColor = CardColors.TextPrimary } });
            if (Event.current.type == EventType.MouseDown && collapse.Contains(Event.current.mousePosition))
            {
                _collapsed[index] = !_collapsed[index];
                Event.current.Use();
            }
            if (_collapsed[index]) { GUI.DragWindow(new Rect(0f, 0f, full.width, 60f)); return; }

            Rect viewport = new Rect(10f, 66f, full.width - 20f, Mathf.Max(1f, full.height - 76f));
            float contentHeight = Mathf.Max(viewport.height, _contentHeights[index]);
            _maxScrolls[index] = Mathf.Max(0f, contentHeight - viewport.height);
            _scrollTargets[index] = Mathf.Clamp(_scrollTargets[index], 0f, _maxScrolls[index]);
            _scrollOffsets[index] = Mathf.Clamp(_scrollOffsets[index], 0f, _maxScrolls[index]);
            bool hasOverflow = _maxScrolls[index] > 0.5f;
            _contentWidth = viewport.width - (hasOverflow ? 16f : 0f);

            GUI.BeginGroup(viewport);
            GUILayout.BeginArea(new Rect(0f, -_scrollOffsets[index], _contentWidth, Mathf.Max(contentHeight, viewport.height)));
            switch (index)
            {
                case 0: DrawCombat(); break;
                case 1: DrawVisuals(); break;
                case 2: DrawMovement(); break;
                case 3: DrawUtility(); break;
                case 4: DrawCosmetic(); break;
                case 5: DrawProfiles(); break;
            }
            if (Event.current.type == EventType.Repaint)
            {
                Rect lastControl = GUILayoutUtility.GetLastRect();
                _contentHeights[index] = Mathf.Max(0f, lastControl.yMax + 12f);
            }
            GUILayout.EndArea();
            GUI.EndGroup();

            float previousTarget = _scrollTargets[index];
            _scrollTargets[index] = _scrollBars[index].Draw(
                viewport,
                _scrollOffsets[index],
                _scrollTargets[index],
                contentHeight);
            if (_pickerCardIndex == index && Mathf.Abs(previousTarget - _scrollTargets[index]) > 0.1f)
                ClosePicker();
            GUI.DragWindow(new Rect(0f, 0f, full.width, 60f));
        }

        private CardAnimFloat Animation(string key, bool value)
        {
            if (!_animations.TryGetValue(key, out CardAnimFloat animation))
            {
                animation = new CardAnimFloat(value);
                _animations[key] = animation;
            }
            return animation;
        }

        private void Row(string key, string english, string chinese, ref bool value)
        {
            Rect row = GUILayoutUtility.GetRect(_contentWidth, 48f);
            var animation = Animation(key, value);
            CardDraw.RoundedRect(row, Color.Lerp(CardColors.BgFrame, CardColors.BgEnabled, animation.Value), 16f);
            GUI.Label(new Rect(row.x + 16f, row.y + 6f, row.width - 70f, 34f), T(english, chinese), _rowStyle);
            CardDraw.Toggle(new Rect(row.xMax - 48f, row.y + 15f, 32f, 18f), value, animation);
            if (GUI.Button(row, GUIContent.none, GUIStyle.none)) value = !value;
        }

        private void SliderInt(string english, string chinese, ref int value, int min, int max, string suffix = "")
        {
            value = Mathf.RoundToInt(CardDraw.Slider(GUILayoutUtility.GetRect(_contentWidth, 54f), T(english, chinese), value, min, max, "F0", suffix));
        }

        private void SliderFloat(string english, string chinese, ref float value, float min, float max, string format = "F1", string suffix = "")
        {
            value = CardDraw.Slider(GUILayoutUtility.GetRect(_contentWidth, 54f), T(english, chinese), value, min, max, format, suffix);
        }

        private void BindRow(string id, string english, string chinese, KeyCode key)
        {
            Rect row = GUILayoutUtility.GetRect(_contentWidth, 48f);
            CardDraw.RoundedRect(row, CardColors.BgHover, 16f);
            GUI.Label(new Rect(row.x + 16f, row.y + 6f, row.width - 118f, 34f), T(english, chinese), _rowStyle);
            Rect button = new Rect(row.xMax - 100f, row.y + 8f, 84f, 32f);
            bool waiting = _bindingId == id;
            CardDraw.RoundedFrame(button, waiting ? CardColors.AccentDark : CardColors.BgFrame, waiting ? CardColors.Accent : CardColors.Border, 13f, 1f);
            GUI.Label(button, waiting ? T("PRESS KEY", "请按键") : (key == KeyCode.None ? T("NONE", "无") : key.ToString()), new GUIStyle(_smallStyle) { alignment = TextAnchor.MiddleCenter, normal = { textColor = waiting ? Color.white : CardColors.TextPrimary } });
            if (GUI.Button(button, GUIContent.none, GUIStyle.none)) _bindingId = waiting ? null : id;
        }

        private void Segment(string english, string chinese, ref int value, string[] englishOptions, string[] chineseOptions)
        {
            Rect row = GUILayoutUtility.GetRect(_contentWidth, 54f);
            GUI.Label(new Rect(row.x + 4f, row.y, row.width, 20f), T(english, chinese), _smallStyle);
            float width = (row.width - 8f) / englishOptions.Length;
            for (int i = 0; i < englishOptions.Length; i++)
            {
                Rect option = new Rect(row.x + 4f + i * width, row.y + 24f, width - 4f, 25f);
                bool selected = value == i;
                CardDraw.RoundedFrame(option, selected ? CardColors.AccentDark : CardColors.BgHover, selected ? CardColors.Accent : CardColors.Border, 10f, 1f);
                GUI.Label(option, T(englishOptions[i], chineseOptions[i]), new GUIStyle(_smallStyle) { alignment = TextAnchor.MiddleCenter, normal = { textColor = selected ? Color.white : CardColors.TextSecondary } });
                if (GUI.Button(option, GUIContent.none, GUIStyle.none)) value = i;
            }
        }

        private void TextRow(string english, string chinese, ref string value)
        {
            Rect row = GUILayoutUtility.GetRect(_contentWidth, 62f);
            GUI.Label(new Rect(row.x + 4f, row.y, row.width, 20f), T(english, chinese), _smallStyle);
            Rect input = new Rect(row.x + 4f, row.y + 24f, row.width - 8f, 32f);
            CardDraw.RoundedFrame(input, CardColors.BgHover, CardColors.Border, 11f, 1f);
            value = GUI.TextField(input, value ?? string.Empty, 120, _inputStyle);
        }

        private bool ButtonRow(string english, string chinese)
        {
            Rect row = GUILayoutUtility.GetRect(_contentWidth, 44f);
            CardDraw.RoundedFrame(row, CardColors.AccentDark, CardColors.Accent, 14f, 1f);
            GUI.Label(row, T(english, chinese), new GUIStyle(_smallStyle) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } });
            return GUI.Button(row, GUIContent.none, GUIStyle.none);
        }

        private void ChoiceRow(string id, string english, string chinese, List<string> choices, string current, Action<string> apply)
        {
            if (choices == null || choices.Count == 0) return;
            Rect row = GUILayoutUtility.GetRect(_contentWidth, 48f);
            CardDraw.RoundedRect(row, CardColors.BgHover, 16f);
            GUI.Label(new Rect(row.x + 14f, row.y + 6f, 72f, 34f), T(english, chinese), _smallStyle);
            Rect select = new Rect(row.x + 84f, row.y + 7f, row.width - 100f, 34f);
            CardDraw.RoundedFrame(select, CardColors.BgFrame, CardColors.Border, 11f, 1f);
            GUI.Label(new Rect(select.x + 10f, select.y, select.width - 38f, select.height), ShortName(current), new GUIStyle(_smallStyle) { alignment = TextAnchor.MiddleLeft, normal = { textColor = CardColors.TextPrimary } });
            GUI.Label(new Rect(select.xMax - 28f, select.y, 22f, select.height), "v", new GUIStyle(_rowStyle) { alignment = TextAnchor.MiddleCenter, normal = { textColor = CardColors.Accent } });
            if (GUI.Button(select, GUIContent.none, GUIStyle.none))
            {
                if (_pickerId == id) ClosePicker();
                else OpenPicker(id, T(english, chinese), choices, apply, row);
            }
        }

        private void OpenPicker(string id, string title, List<string> choices, Action<string> apply, Rect row)
        {
            _pickerId = id;
            _pickerTitle = title;
            _pickerChoices = choices;
            _pickerApply = apply;
            _pickerSearch = string.Empty;
            _pickerCardIndex = _drawingCardIndex;
            Rect card = _cards[_drawingCardIndex];
            _pickerRect = new Rect(card.x + 10f, card.y + 66f + row.yMax - _scrollOffsets[_drawingCardIndex] + 6f, card.width - 20f, 100f);
        }

        private void ClosePicker()
        {
            _pickerId = null;
            _pickerChoices = null;
            _pickerApply = null;
            _pickerCardIndex = -1;
        }

        private void DrawPicker()
        {
            if (_pickerChoices == null) return;

            var matches = new List<string>();
            foreach (string choice in _pickerChoices)
            {
                if (string.IsNullOrEmpty(_pickerSearch) || choice.IndexOf(_pickerSearch, StringComparison.OrdinalIgnoreCase) >= 0)
                    matches.Add(choice);
                if (matches.Count == 7) break;
            }

            float height = 82f + Mathf.Max(1, matches.Count) * 38f;
            _pickerRect.height = height;
            if (_pickerRect.yMax > Screen.height - 18f)
                _pickerRect.y = Mathf.Max(18f, Screen.height - 18f - height);

            CardDraw.RoundedFrame(_pickerRect, CardColors.BgFrame, CardColors.Accent, 16f, 1f);
            GUI.Label(new Rect(_pickerRect.x + 12f, _pickerRect.y + 8f, _pickerRect.width - 24f, 22f), _pickerTitle, _smallStyle);
            Rect search = new Rect(_pickerRect.x + 10f, _pickerRect.y + 32f, _pickerRect.width - 20f, 32f);
            CardDraw.RoundedFrame(search, CardColors.BgHover, CardColors.Border, 10f, 1f);
            _pickerSearch = GUI.TextField(search, _pickerSearch, 80, _inputStyle);

            if (matches.Count == 0)
            {
                GUI.Label(new Rect(_pickerRect.x + 12f, _pickerRect.y + 72f, _pickerRect.width - 24f, 30f), T("No matches", "没有匹配项"), new GUIStyle(_smallStyle) { alignment = TextAnchor.MiddleCenter });
                return;
            }

            for (int i = 0; i < matches.Count; i++)
            {
                Rect option = new Rect(_pickerRect.x + 10f, _pickerRect.y + 72f + i * 38f, _pickerRect.width - 20f, 32f);
                CardDraw.RoundedRect(option, CardColors.BgHover, 10f);
                GUI.Label(new Rect(option.x + 10f, option.y, option.width - 20f, option.height), matches[i], new GUIStyle(_smallStyle) { alignment = TextAnchor.MiddleLeft, normal = { textColor = CardColors.TextPrimary } });
                if (GUI.Button(option, GUIContent.none, GUIStyle.none))
                {
                    _pickerApply(matches[i]);
                    ClosePicker();
                    Event.current.Use();
                    return;
                }
            }
        }

        private void IndexChoiceRow(string english, string chinese, string[] choices, ref int index)
        {
            if (choices == null || choices.Length == 0) return;
            index = Mathf.Clamp(index, 0, choices.Length - 1);
            Rect row = GUILayoutUtility.GetRect(_contentWidth, 48f);
            CardDraw.RoundedRect(row, CardColors.BgHover, 16f);
            GUI.Label(new Rect(row.x + 14f, row.y + 6f, 68f, 34f), T(english, chinese), _smallStyle);
            Rect previous = new Rect(row.x + 78f, row.y + 9f, 28f, 30f);
            Rect next = new Rect(row.xMax - 44f, row.y + 9f, 28f, 30f);
            CardDraw.RoundedFrame(previous, CardColors.BgFrame, CardColors.Border, 10f, 1f);
            CardDraw.RoundedFrame(next, CardColors.BgFrame, CardColors.Border, 10f, 1f);
            GUI.Label(previous, "<", new GUIStyle(_rowStyle) { alignment = TextAnchor.MiddleCenter });
            GUI.Label(next, ">", new GUIStyle(_rowStyle) { alignment = TextAnchor.MiddleCenter });
            GUI.Label(new Rect(row.x + 110f, row.y + 7f, row.width - 160f, 32f), ShortName(choices[index]), new GUIStyle(_smallStyle) { alignment = TextAnchor.MiddleCenter, normal = { textColor = CardColors.TextPrimary } });
            if (GUI.Button(previous, GUIContent.none, GUIStyle.none)) index = (index - 1 + choices.Length) % choices.Length;
            if (GUI.Button(next, GUIContent.none, GUIStyle.none)) index = (index + 1) % choices.Length;
        }

        private static string ShortName(string value)
        {
            if (string.IsNullOrEmpty(value)) return "-";
            return value.Length > 14 ? value.Substring(0, 13) + "..." : value;
        }

        private void DrawCombat()
        {
            Row("soft", "Soft Aim", "柔和瞄准", ref Config.SoftAim);
            if (Config.SoftAim)
            {
                Row("soft_fov", "Draw FOV Ring", "显示范围圈", ref Config.SoftAimFovDraw);
                Row("soft_vis", "Visibility Check", "可见性检测", ref Config.SoftAimVisCheck);
                Row("soft_line", "Lock Beam", "锁定指示线", ref Config.SoftAimLine);
                Row("soft_smooth", "Smoothing", "平滑瞄准", ref Config.SoftAimSmoothOn);
                BindRow("soft", "Activation Key", "激活按键", Config.SoftAimKey);
                SliderInt("Aim FOV", "瞄准范围", ref Config.SoftAimFov, 0, 180, " deg");
                if (Config.SoftAimSmoothOn) SliderFloat("Smooth", "平滑度", ref Config.SoftAimSmooth, 1f, 30f);
                IndexChoiceRow("Target Bone", "目标骨骼", Bones, ref Config.SoftAimBone);
            }
            Row("hard", "Hard Aim", "强制瞄准", ref Config.HardAim);
            if (Config.HardAim)
            {
                Row("hard_key", "Key Gate", "按键限制", ref Config.HardAimOnKey);
                if (Config.HardAimOnKey) BindRow("hard", "Hard Aim Key", "强制瞄准按键", Config.HardAimKey);
                SliderFloat("Accuracy", "精度", ref Config.Accurary, 0f, 100f, "F0", "%");
            }
            Row("angle", "Angle Fix", "角度修复", ref Config.AngleFix);
            if (Config.AngleFix)
            {
                Row("angle_random", "Randomize", "随机化", ref Config.AngleFixRandom);
                BindRow("angle", "Angle Fix Key", "角度修复按键", Config.AngleFixKey);
            }
            Row("desync", "Desync", "不同步", ref Config.Desync);
            if (Config.Desync)
            {
                Segment("Desync Mode", "不同步模式", ref Config.DesyncMode, new[] { "Static", "Spin", "Jitter" }, new[] { "固定", "旋转", "抖动" });
                SliderFloat("Pitch", "俯仰", ref Config.DesyncPitch, -360f, 360f, "F0");
                if (Config.DesyncMode == 0 || Config.DesyncMode == 2) SliderFloat("Yaw", "偏航", ref Config.DesyncYaw, -180f, 180f, "F0");
                if (Config.DesyncMode == 1) SliderInt("Spin Speed", "旋转速度", ref Config.DesyncSpin, 0, 100);
                if (Config.DesyncMode == 2)
                {
                    SliderFloat("Jitter Min", "抖动最小值", ref Config.DesyncJitterMin, -180f, 180f, "F0");
                    SliderFloat("Jitter Max", "抖动最大值", ref Config.DesyncJitterMax, -180f, 180f, "F0");
                }
            }
            Row("autofire", "Auto Fire", "自动开火", ref Config.AutoFire);
            if (Config.AutoFire)
            {
                Row("autofire_scope", "Skip Scopes", "跳过开镜", ref Config.AutoFireNoScope);
                Row("autofire_delay", "Armed Delay", "延迟触发", ref Config.AutoFireDelay);
                if (Config.AutoFireDelay) SliderFloat("Hold Time", "保持时间", ref Config.AutoFireHold, 0f, 10f, "F1", " s");
            }
        }

        private void DrawVisuals()
        {
            Row("esp", "ESP Master", "ESP 主开关", ref Config.EspMaster); Row("box", "Box", "方框", ref Config.EspBox);
            if (Config.EspBox) Segment("Box Style", "方框样式", ref Config.EspBoxStyle, new[] { "Full", "Corners" }, new[] { "完整", "角落" });
            Row("bones", "Bones", "骨骼", ref Config.EspBones); Row("health_bar", "Health Bar", "血条", ref Config.EspHealthBar);
            Row("health_text", "Health Text", "血量文字", ref Config.EspHealth); Row("name", "Name", "名称", ref Config.EspName);
            Row("distance", "Distance", "距离", ref Config.EspDist); Row("weapon", "Weapon", "武器", ref Config.EspWeapon);
            Row("bomb", "Bomb Tag", "炸弹标识", ref Config.EspBomb); Row("snap", "Snap Beam", "连线", ref Config.EspSnap);
            Row("yaw", "Yaw Read", "偏航读数", ref Config.EspYaw); Row("pitch", "Pitch Read", "俯仰读数", ref Config.EspPitch);
            Row("cube", "Cube Mesh", "立方体模型", ref Config.EspCube); Row("glow", "Model Glow", "模型发光", ref Config.ModelGlow);
            Row("flash", "Anti Flash", "防闪光", ref Config.AntiFlash); Row("numbers", "Hit Numbers", "伤害数字", ref Config.HitNumbers);
            Row("trace", "Shot Path", "弹道轨迹", ref Config.ShotPath); Row("projectile", "Projectile Tags", "投掷物标识", ref Config.ProjectileTags);
            Row("field", "Field Tags", "场景物品标识", ref Config.FieldTags); Row("loot_tags", "Loot Tags", "战利品标识", ref Config.LootTags);
            Row("loot_glow", "Loot Glow", "战利品发光", ref Config.LootGlow); Row("bomb_clock", "Bomb Clock", "炸弹计时", ref Config.BombClock);
            Row("physx_model", "Unity PhysX Model", "Unity PhysX 模型", ref Config.PhysxModel);
            if (Config.PhysxModel)
            {
                Row("physx_black_map", "Black Map + No Skybox", "黑色地图 + 关闭天空盒", ref Config.PhysxBlackMap);
                SliderFloat("PhysX Range", "PhysX 距离", ref Config.PhysxModelDistance, 30f, 250f, "F0", " m");
                GUILayout.Label("PhysX: " + Vape.Feature.Overlay.PhysxModelDisplay.LastStatus, _smallStyle);
            }
            Row("observer", "Observers", "观战列表", ref Config.ObserverPanel); Row("radar", "Radar", "雷达", ref Config.MiniMap);
            Row("reticle", "Reticle", "准星", ref Config.Reticle); Row("speed", "Velocity Display", "速度显示", ref Config.VelocityRing);
            Row("key_hud", "Input HUD", "按键显示", ref Config.KeyHud);
            Row("csgo_hud", "CS2 HUD", "CS2 HUD", ref Config.CsgoHud);
            Row("strip", "State Strip", "状态栏", ref Config.StateStrip);
        }

        private void DrawMovement()
        {
            Row("orbit", "Third Person", "第三人称", ref Config.OrbitCam);
            BindRow("orbit", "Third Person Key", "第三人称按键", Config.OrbitKey);
            SliderInt("Orbit FOV", "第三人称视野", ref Config.OrbitFov, 30, 150);
            Row("lens", "Custom Lens", "自定义视野", ref Config.LensCustom);
            if (Config.LensCustom) SliderFloat("Lens FOV", "镜头视野", ref Config.LensFov, 30f, 150f, "F0");
            Row("bhop8", "8-Dir Bhop", "八向连跳", ref Config.Bhop8Dir);
            if (Config.Bhop8Dir)
            {
                Segment("Activation", "触发模式", ref Config.BhopActivationMode,
                    new[] { "Hold", "Toggle" }, new[] { "长按", "单按" });
                BindRow("bhop8", "Bhop Key", "连跳按键", Config.BhopKey);
            }
            Row("glide", "Air Glide", "空中滑翔", ref Config.Airglide);
            Row("ghost", "Ghost Step", "幽灵步", ref Config.GhostStep);
            Row("blink", "Freecam", "自由视角", ref Config.BlinkMove);
            if (Config.BlinkMove)
            {
                BindRow("blink", "Freecam Key", "自由视角按键", Config.BlinkMoveKey);
                SliderFloat("Freecam Speed", "自由视角速度", ref Config.BlinkSpeedMultiplier, 1f, 4f, "F1", "x");
                GUILayout.Label($"Freecam: {Vape.Feature.BlinkMovement.LastStatus}", _smallStyle);
            }
            Row("boost", "Speed Boost", "加速", ref Config.SpeedBoost);
            if (Config.SpeedBoost)
            {
                BindRow("boost", "Boost Key", "加速按键", Config.SpeedBoostKey);
                SliderFloat("Multiplier", "加速倍率", ref Config.SpeedBoostMultiplier, 1f, 30f, "F1", "x");
                GUILayout.Label($"Boost: {Vape.Feature.SpeedBoost.LastStatus}", _smallStyle);
            }
        }

        private void DrawUtility()
        {
            Row("recoil", "No Recoil", "无后坐", ref Config.RecoilStrip);
            if (Config.RecoilStrip) Row("recoil_smooth", "Smooth Recoil", "平滑后坐", ref Config.RecoilSmooth);
            Row("block", "Block Secondary", "屏蔽副攻击", ref Config.BlockSecondary);
            Row("cone", "Cone Predict", "弹道预测", ref Config.ConePredict); Row("spam", "Auto Spam", "自动发送", ref Config.AutoSpam);
            if (Config.AutoSpam) TextRow("Spam Text", "发送文本", ref Config.SpamText);
            Row("path", "Path Assist", "路径辅助", ref Config.PathAssist);
            Row("crouch", "Crouch Assist", "蹲跳辅助", ref Config.CrouchAssist);
            if (Config.CrouchAssist)
            {
                BindRow("crouch", "Crouch Key", "蹲跳按键", Config.CrouchAssistKey);
            }
            Row("sniper", "Instant Sniper", "瞬间开镜", ref Config.InstantSniper);
            if (Config.InstantSniper)
            {
                BindRow("sniper", "Sniper Key", "开镜按键", Config.InstantSniperKey);
            }
            if (ButtonRow("LOWEST QUALITY", "最低画质")) Vape.Feature.WorldSettings.SetLowestQuality();
            if (ButtonRow("UNLOCK FRAMERATE", "解除帧率限制")) Vape.Feature.WorldSettings.UnlockFrameRate();
        }

        private void DrawCosmetic()
        {
            var local = Vape.Entity.PlayerUpdate.LocalEntity;
            if (local != null && local._entity != null && local._entity.hasBasicInfo)
            {
                var info = local._entity.basicInfo.Current;
                float scale = info.Scale;
                SliderFloat("Scale", "角色大小", ref scale, -5f, 5f, "F2");
                if (Math.Abs(scale - info.Scale) > 0.01f) Vape.Feature.SkinChanger.ChangeScale(scale);
                float head = info.HeadEnlarge;
                SliderFloat("Head Size", "头部大小", ref head, -5f, 5f, "F2");
                if (Math.Abs(head - info.HeadEnlarge) > 0.01f) Vape.Feature.SkinChanger.ChangeHeadEnlarge(head);
                int team = info.Team;
                SliderInt("Team", "队伍", ref team, 0, 13);
                if (team != info.Team) Vape.Feature.SkinChanger.ChangeTeam(team);
                int alpha = info.Alpha;
                SliderInt("Alpha", "透明度", ref alpha, 0, 100);
                if (alpha != info.Alpha) Vape.Feature.SkinChanger.ChangeAlpha(alpha);
                int selfAlpha = info.SelfAlpha;
                SliderInt("Self Alpha", "自身透明度", ref selfAlpha, 0, 100);
                if (selfAlpha != info.SelfAlpha) Vape.Feature.SkinChanger.ChangeSelfAlpha(selfAlpha);
                ChoiceRow("backpiece", "Backpiece", "背部装饰", Vape.Feature.SkinChanger.BackAccessoryNames, info.BackAccessory, Vape.Feature.SkinChanger.ChangeBackAccessory);
                ChoiceRow("character", "Character", "角色", Vape.Feature.SkinChanger.CharacterNames, info.Career, Vape.Feature.SkinChanger.ChangeCharacter);
                ChoiceRow("weapon", "Weapon", "武器", Vape.Feature.SkinChanger.WeaponNames, info.CurrentWeaponName, Vape.Feature.SkinChanger.ChangeWeapon);
            }
            else GUILayout.Label(T("Waiting for player...", "等待玩家数据..."), new GUIStyle(GUI.skin.label) { fontSize = 18, normal = { textColor = CardColors.TextMuted } });
        }

        private void DrawProfiles()
        {
            Rect language = GUILayoutUtility.GetRect(_contentWidth, 52f);
            CardDraw.RoundedFrame(language, CardColors.BgHover, CardColors.Border, 16f, 1f);
            GUI.Label(new Rect(language.x + 12f, language.y, language.width - 116f, language.height), T("LANGUAGE", "语言"), _rowStyle);
            Rect languageButton = new Rect(language.xMax - 98f, language.y + 10f, 84f, 32f);
            CardDraw.RoundedFrame(languageButton, CardColors.AccentDark, CardColors.Accent, 13f, 1f);
            GUI.Label(languageButton, Config.Language == 1 ? "中文" : "English", new GUIStyle(_smallStyle) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } });
            if (GUI.Button(languageButton, GUIContent.none, GUIStyle.none)) Config.Language = Config.Language == 1 ? 0 : 1;

            TextRow("New Profile", "新配置名称", ref _newProfile);
            if (ButtonRow("SAVE NEW PROFILE", "保存新配置"))
            {
                string profile = string.IsNullOrEmpty(_newProfile) ? "profile" : _newProfile.Trim();
                Configs.Save(profile);
            }
            if (ButtonRow("SAVE CURRENT PROFILE", "保存当前配置")) Configs.Save(Configs.Current);
            GUILayout.Label(Configs.LastStatus, new GUIStyle(_smallStyle)
            {
                normal = { textColor = Configs.LastOperationSucceeded ? CardColors.TextMuted : new Color(1f, 0.42f, 0.5f, 1f) }
            });
            foreach (string name in Configs.Names)
            {
                Rect row = GUILayoutUtility.GetRect(_contentWidth, 46f);
                CardDraw.RoundedRect(row, CardColors.BgHover, 14f);
                GUI.Label(new Rect(row.x + 14f, row.y + 5f, row.width - 104f, 34f), name, _rowStyle);
                Rect load = new Rect(row.xMax - 86f, row.y + 7f, 70f, 32f);
                CardDraw.RoundedFrame(load, CardColors.AccentDark, CardColors.Accent, 12f, 1f);
                GUI.Label(load, T("LOAD", "载入"), new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } });
                if (GUI.Button(load, GUIContent.none, GUIStyle.none)) Configs.Load(name);
            }
        }
    }
}
