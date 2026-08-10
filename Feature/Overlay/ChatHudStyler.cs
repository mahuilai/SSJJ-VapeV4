using Res.Assets.Scripts.ExText;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Vape.Feature.Overlay
{
    public sealed class ChatHudStyler : MonoBehaviour
    {
        private sealed class TextStyleState
        {
            public ExChatText Text;
            public Color Color;
            public Font Font;
            public int FontSize;
            public FontStyle FontStyle;
        }

        private static float _scanUntil;
        private readonly Dictionary<int, TextStyleState> _styled =
            new Dictionary<int, TextStyleState>(6);
        private readonly List<int> _restoreIds = new List<int>(6);
        private Font _hudFont;
        private float _nextScan;
        private float _nextGameInfoScan;
        private Text _gameInfoText;
        private Color _gameInfoColor;
        private Font _gameInfoFont;
        private int _gameInfoFontSize;
        private FontStyle _gameInfoFontStyle;
        private bool _gameInfoStyleSaved;

        public static void NotifyHitMessage()
        {
            _scanUntil = Mathf.Max(_scanUntil, Time.realtimeSinceStartup + 6.5f);
        }

        private void Update()
        {
            StyleGameInfoHud();

            if (Time.realtimeSinceStartup < _nextScan)
                return;
            if (Time.realtimeSinceStartup > _scanUntil && _styled.Count == 0)
                return;

            _nextScan = Time.realtimeSinceStartup + 0.12f;
            ExChatText[] chatTexts = FindObjectsOfType<ExChatText>();
            _restoreIds.Clear();
            foreach (KeyValuePair<int, TextStyleState> pair in _styled)
            {
                if (pair.Value.Text == null || !IsHitNotice(pair.Value.Text))
                    _restoreIds.Add(pair.Key);
            }
            for (int i = 0; i < _restoreIds.Count; i++)
                Restore(_restoreIds[i]);

            for (int i = 0; i < chatTexts.Length; i++)
            {
                ExChatText chatText = chatTexts[i];
                if (chatText == null || !IsHitNotice(chatText))
                    continue;

                int id = chatText.GetInstanceID();
                if (!_styled.ContainsKey(id))
                {
                    _styled.Add(id, new TextStyleState
                    {
                        Text = chatText,
                        Color = chatText.color,
                        Font = chatText.font,
                        FontSize = chatText.fontSize,
                        FontStyle = chatText.fontStyle
                    });
                }

                chatText.color = Vape.UI.Theme.VisualPink;
                Font font = GetHudFont();
                if (font != null)
                    chatText.font = font;
                chatText.fontSize = Mathf.Max(18, chatText.fontSize);
                chatText.fontStyle = FontStyle.Bold;
                chatText.SetVerticesDirty();
                chatText.SetLayoutDirty();
            }
        }

        private Font GetHudFont()
        {
            if (_hudFont != null)
                return _hudFont;
            try
            {
                _hudFont = Font.CreateDynamicFontFromOSFont(
                    new[] { "Comic Sans MS", "Microsoft YaHei UI", "Microsoft YaHei", "SimHei" },
                    20);
            }
            catch
            {
                _hudFont = null;
            }
            return _hudFont;
        }

        private void StyleGameInfoHud()
        {
            if (Time.realtimeSinceStartup < _nextGameInfoScan)
                return;
            _nextGameInfoScan = Time.realtimeSinceStartup + 0.5f;

            FpsDisplay display = FpsDisplay.GetInstance();
            Text text = display?._text;
            if (text == null)
                return;

            if (_gameInfoText != text || !_gameInfoStyleSaved)
            {
                RestoreGameInfoHud();
                _gameInfoText = text;
                _gameInfoColor = text.color;
                _gameInfoFont = text.font;
                _gameInfoFontSize = text.fontSize;
                _gameInfoFontStyle = text.fontStyle;
                _gameInfoStyleSaved = true;
            }

            text.color = Vape.UI.Theme.VisualPink;
            Font font = GetHudFont();
            if (font != null)
                text.font = font;
            text.fontSize = Mathf.Max(16, text.fontSize);
            text.fontStyle = FontStyle.Bold;
            text.SetVerticesDirty();
        }

        private void RestoreGameInfoHud()
        {
            if (!_gameInfoStyleSaved || _gameInfoText == null)
                return;
            _gameInfoText.color = _gameInfoColor;
            _gameInfoText.font = _gameInfoFont;
            _gameInfoText.fontSize = _gameInfoFontSize;
            _gameInfoText.fontStyle = _gameInfoFontStyle;
            _gameInfoText.SetVerticesDirty();
            _gameInfoStyleSaved = false;
        }

        private static bool IsHitNotice(ExChatText text)
        {
            return text != null && !string.IsNullOrEmpty(text.text) &&
                   text.text.IndexOf("命中目标", StringComparison.Ordinal) >= 0;
        }

        private void Restore(int id)
        {
            if (!_styled.TryGetValue(id, out TextStyleState state))
                return;
            if (state.Text != null)
            {
                state.Text.color = state.Color;
                state.Text.font = state.Font;
                state.Text.fontSize = state.FontSize;
                state.Text.fontStyle = state.FontStyle;
                state.Text.SetVerticesDirty();
                state.Text.SetLayoutDirty();
            }
            _styled.Remove(id);
        }

        private void OnDestroy()
        {
            RestoreGameInfoHud();
            _restoreIds.Clear();
            foreach (int id in _styled.Keys)
                _restoreIds.Add(id);
            for (int i = 0; i < _restoreIds.Count; i++)
                Restore(_restoreIds[i]);
        }
    }
}
