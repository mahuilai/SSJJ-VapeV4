using Vape.Cfg;
using Vape.Features;
using System;
using System.IO.MemoryMappedFiles;
using System.Text;
using UnityEngine;

namespace Vape.UI
{
    /// <summary>
    /// Publishes config to external DX11/ImGui overlay and applies inbound writes.
    /// </summary>
    public class OverlayHost : MonoBehaviour
    {
        private MemoryMappedFile _mmf;
        private int _gameSeq;
        private int _lastOverlaySeq = -1;
        private float _accum;
        private bool _overlaySeen;
        private float _lastHeartbeat = -999f;
        private string _lastPayload = string.Empty;
        private readonly StringBuilder _header = new StringBuilder(128);

        public static bool ExternalUiActive { get; private set; }

        private void Start()
        {
            OverlaySync.TryOpen(out _mmf);
        }

        private void Update()
        {
            if (_mmf == null) return;

            _accum += Time.unscaledDeltaTime;
            if (_accum < 0.05f) return;
            _accum = 0f;

            if (OverlaySync.ReadBlock(_mmf, OverlaySync.OverlayBlockOffset, out int oSeq, out int oFlags, out string pending))
            {
                if ((oFlags & OverlaySync.FlagHeartbeat) != 0)
                {
                    _lastHeartbeat = Time.unscaledTime;
                    _overlaySeen = true;
                }

                if (oSeq != _lastOverlaySeq)
                {
                    _lastOverlaySeq = oSeq;
                    ApplyOverlayCommands(pending);
                }
            }

            ExternalUiActive = _overlaySeen && (Time.unscaledTime - _lastHeartbeat) < 1.5f;

            _gameSeq++;
            int flags = OverlaySync.FlagHeartbeat;
            if (Menu.IsOpen) flags |= OverlaySync.FlagMenuOpen;
            if (!ExternalUiActive) flags |= OverlaySync.FlagWantInternalUi;

            string body = ConfigManager.ExportToString();
            _header.Length = 0;
            _header.Append("MenuOpen=").Append(Menu.IsOpen ? "1" : "0").Append('\n');
            _header.Append("CurrentConfig=").Append(Configs.Current ?? "default").Append('\n');
            string payload = _header.ToString() + body;

            // skip identical writes to reduce MMF traffic
            if (payload != _lastPayload || (_gameSeq & 7) == 0)
            {
                _lastPayload = payload;
                OverlaySync.WriteBlock(_mmf, OverlaySync.GameBlockOffset, _gameSeq, flags, payload);
            }
            else
            {
                // still refresh heartbeat/flags with tiny payload occasionally
                OverlaySync.WriteBlock(_mmf, OverlaySync.GameBlockOffset, _gameSeq, flags,
                    "MenuOpen=" + (Menu.IsOpen ? "1" : "0") + "\n");
            }
        }

        private void OnDestroy()
        {
            try { _mmf?.Dispose(); } catch { /* ignore */ }
            _mmf = null;
            ExternalUiActive = false;
        }

        private static void ApplyOverlayCommands(string payload)
        {
            if (string.IsNullOrEmpty(payload)) return;

            string[] lines = payload.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string raw in lines)
            {
                int eq = raw.IndexOf('=');
                if (eq <= 0) continue;
                string key = raw.Substring(0, eq).Trim();
                string value = raw.Substring(eq + 1);

                switch (key)
                {
                    case "MenuOpen":
                        Menu.IsOpen = value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "CmdSave":
                        if (!string.IsNullOrEmpty(value)) Configs.Save(value);
                        break;
                    case "CmdLoad":
                        if (!string.IsNullOrEmpty(value)) Configs.Load(value);
                        break;
                    case "CmdDelete":
                        if (!string.IsNullOrEmpty(value)) Configs.Delete(value);
                        break;
                    default:
                        ConfigManager.ApplyLine(raw);
                        break;
                }
            }
        }
    }
}
