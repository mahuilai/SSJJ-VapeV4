using System.Globalization;
using System.IO.MemoryMappedFiles;
using System.Text;
using ClickableTransparentOverlay;
using ImGuiNET;
using System.Numerics;

namespace Vape.Overlay;

internal static class Program
{
    [STAThread]
    private static async Task Main()
    {
        using var gui = new VapeClickGui();
        await gui.Run();
    }
}

internal sealed class VapeClickGui : ClickableTransparentOverlay.Overlay
{
    private MemoryMappedFile? _mmf;
    private int _overlaySeq;
    private int _lastGameSeq = -1;
    private readonly Dictionary<string, string> _state = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _boolCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _intCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> _floatCache = new(StringComparer.Ordinal);
    private readonly StringBuilder _pending = new();
    private bool _menuOpen = true;
    private string _status = "waiting for Vape.dll...";
    private string _profile = "default";
    private string _newProfile = "";

    // window toggles
    private bool _winOffense = true;
    private bool _winVision = true;
    private bool _winMotion;
    private bool _winUtility;
    private bool _winProfiles = true;

    public VapeClickGui() : base("Vape Overlay")
    {
    }

    protected override Task PostInitialized()
    {
        try
        {
            _mmf = MemoryMappedFile.CreateOrOpen(Ipc.MapName, Ipc.MapSize, MemoryMappedFileAccess.ReadWrite);
            _status = "map ready";
        }
        catch (Exception ex)
        {
            _status = "map failed: " + ex.Message;
        }
        return Task.CompletedTask;
    }

    protected override void Render()
    {
        PumpIpc();

        ApplyStyle();

        // top bar
        ImGui.SetNextWindowPos(new Vector2(20, 20), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(560, 64), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("VAPE##nav", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar))
        {
            ImGui.TextColored(new Vector4(0.30f, 0.64f, 1f, 1f), "VAPE");
            ImGui.SameLine();
            ImGui.TextDisabled("DX11 / ImGui ClickGUI");
            ImGui.SameLine(360);
            ImGui.TextDisabled(_status);

            ChipToggle("ATK", ref _winOffense); ImGui.SameLine();
            ChipToggle("VIS", ref _winVision); ImGui.SameLine();
            ChipToggle("MOV", ref _winMotion); ImGui.SameLine();
            ChipToggle("UTIL", ref _winUtility); ImGui.SameLine();
            ChipToggle("CFG", ref _winProfiles); ImGui.SameLine();
            if (ChipToggle("MENU", ref _menuOpen))
                Queue("MenuOpen", _menuOpen ? "1" : "0");
        }
        ImGui.End();

        if (!_menuOpen)
        {
            FlushPending();
            return;
        }

        if (_winOffense) DrawOffense();
        if (_winVision) DrawVision();
        if (_winMotion) DrawMotion();
        if (_winUtility) DrawUtility();
        if (_winProfiles) DrawProfiles();

        FlushPending();
    }

    private void DrawOffense()
    {
        ImGui.SetNextWindowSize(new Vector2(320, 460), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Offense", ref _winOffense)) { ImGui.End(); return; }

        if (ImGui.CollapsingHeader("Soft Aim", ImGuiTreeNodeFlags.DefaultOpen))
        {
            Toggle("SoftAim", "Soft Aim");
            Toggle("SoftAimFovDraw", "Draw FOV");
            Toggle("SoftAimVisCheck", "Visibility Gate");
            Toggle("SoftAimLine", "Lock Beam");
            SliderInt("SoftAimFov", "Field", 0, 180);
            Toggle("SoftAimSmoothOn", "Smoothing");
            SliderFloat("SoftAimSmooth", "Smooth", 1f, 30f);
        }
        if (ImGui.CollapsingHeader("Hard Aim", ImGuiTreeNodeFlags.DefaultOpen))
        {
            Toggle("HardAim", "Hard Aim");
            Toggle("HardAimOnKey", "Key Gate");
        }
        if (ImGui.CollapsingHeader("History Hit"))
        {
            Toggle("HistoryHit", "History Hit");
            SliderInt("HistoryWindow", "Window ms", 0, 5000);
            Toggle("HistoryTrail", "Trail Ghosts");
            Toggle("HistoryPreferLive", "Prefer Live");
            Toggle("HistoryNoWall", "Skip Wall Ghost");
            Toggle("HistoryAutoShoot", "Auto Shot");
        }
        if (ImGui.CollapsingHeader("Desync / Hold"))
        {
            Toggle("Desync", "Desync");
            Toggle("PacketHold", "Packet Hold");
            SliderInt("PacketHoldTicks", "Ticks", 0, 100);
            Toggle("AngleFix", "Angle Fix");
            Toggle("AngleFixRandom", "Randomize");
        }
        if (ImGui.CollapsingHeader("Auto Fire"))
        {
            Toggle("AutoFire", "Auto Fire");
            Toggle("AutoFireNoScope", "Skip Scopes");
            Toggle("AutoFireDelay", "Armed Delay");
            SliderFloat("AutoFireHold", "Hold", 0f, 10f);
        }
        ImGui.End();
    }

    private void DrawVision()
    {
        ImGui.SetNextWindowSize(new Vector2(320, 460), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Vision", ref _winVision)) { ImGui.End(); return; }

        if (ImGui.CollapsingHeader("Player Overlay", ImGuiTreeNodeFlags.DefaultOpen))
        {
            Toggle("EspMaster", "ESP Master");
            Toggle("EspBox", "Bound Box");
            Toggle("EspBones", "Bone Map");
            Toggle("EspHealthBar", "Vital Bar");
            Toggle("EspHealth", "Vital Text");
            Toggle("EspName", "Identity");
            Toggle("EspDist", "Range");
            Toggle("EspWeapon", "Loadout");
            Toggle("EspBomb", "Bomb Tag");
            Toggle("EspSnap", "Snap Beam");
            Toggle("EspCube", "Cube Mesh");
            SliderInt("EspBoxStyle", "Box Style 0/1", 0, 1);
        }
        if (ImGui.CollapsingHeader("World Sense", ImGuiTreeNodeFlags.DefaultOpen))
        {
            Toggle("ModelGlow", "Model Glow");
            Toggle("AntiFlash", "Anti Flash");
            Toggle("ShotPath", "Shot Path");
            Toggle("LootTags", "Loot Tags");
            Toggle("LootGlow", "Loot Glow");
            Toggle("ProjectileTags", "Projectile Tags");
            Toggle("FieldTags", "Field Tags");
        }
        if (ImGui.CollapsingHeader("Interface"))
        {
            Toggle("ObserverPanel", "Observers");
            Toggle("MiniMap", "Radar");
            Toggle("VelocityRing", "Velocity Ring");
            Toggle("StateStrip", "State Strip");
        }
        ImGui.End();
    }

    private void DrawMotion()
    {
        ImGui.SetNextWindowSize(new Vector2(300, 280), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Motion", ref _winMotion)) { ImGui.End(); return; }
        Toggle("OrbitCam", "Orbit Cam");
        Toggle("LensCustom", "Custom Lens");
        SliderInt("OrbitFov", "Orbit FOV", 0, 150);
        SliderFloat("LensFov", "Lens FOV", 0f, 150f);
        Toggle("AutoHop", "Auto Hop");
        Toggle("AirPath", "Air Path");
        Toggle("Airglide", "Airglide");
        Toggle("GhostStep", "Ghost Step");
        ImGui.End();
    }

    private void DrawUtility()
    {
        ImGui.SetNextWindowSize(new Vector2(300, 260), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Utility", ref _winUtility)) { ImGui.End(); return; }
        Toggle("RecoilStrip", "Recoil Strip");
        Toggle("RecoilSmooth", "Strip Smooth");
        Toggle("BlockSecondary", "Block Secondary");
        Toggle("ConePredict", "Cone Predict");
        Toggle("PathAssist", "Path Assist");
        Toggle("AutoSpam", "Auto Spam");
        ImGui.End();
    }

    private void DrawProfiles()
    {
        ImGui.SetNextWindowSize(new Vector2(300, 220), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Profiles", ref _winProfiles)) { ImGui.End(); return; }
        ImGui.Text($"Active: {_profile}");
        ImGui.InputText("Name", ref _newProfile, 64);
        if (ImGui.Button("Save")) Queue("CmdSave", _newProfile);
        ImGui.SameLine();
        if (ImGui.Button("Load")) Queue("CmdLoad", string.IsNullOrWhiteSpace(_newProfile) ? _profile : _newProfile);
        ImGui.SameLine();
        if (ImGui.Button("Delete")) Queue("CmdDelete", _newProfile);
        ImGui.End();
    }

    private void Toggle(string key, string label)
    {
        bool v = GetBool(key);
        ImGui.PushID(key);
        // custom row: label left, switch-like checkbox right
        float full = ImGui.GetContentRegionAvail().X;
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        ImGui.SameLine(Math.Max(full - 34f, full * 0.7f));
        if (ImGui.Checkbox("##box", ref v))
        {
            _boolCache[key] = v;
            Queue(key, v ? "1" : "0");
        }
        ImGui.PopID();
    }

    private void SliderInt(string key, string label, int min, int max)
    {
        int v = GetInt(key);
        if (ImGui.SliderInt(label + "##" + key, ref v, min, max))
        {
            _intCache[key] = v;
            Queue(key, v.ToString(CultureInfo.InvariantCulture));
        }
    }

    private void SliderFloat(string key, string label, float min, float max)
    {
        float v = GetFloat(key);
        if (ImGui.SliderFloat(label + "##" + key, ref v, min, max))
        {
            _floatCache[key] = v;
            Queue(key, v.ToString(CultureInfo.InvariantCulture));
        }
    }

    private bool GetBool(string key)
    {
        if (_boolCache.TryGetValue(key, out var c)) return c;
        if (_state.TryGetValue(key, out var s))
            return s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase);
        return false;
    }

    private int GetInt(string key)
    {
        if (_intCache.TryGetValue(key, out var c)) return c;
        if (_state.TryGetValue(key, out var s) && int.TryParse(s, out var v)) return v;
        return 0;
    }

    private float GetFloat(string key)
    {
        if (_floatCache.TryGetValue(key, out var c)) return c;
        if (_state.TryGetValue(key, out var s) &&
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            return v;
        return 0f;
    }

    private void Queue(string key, string value)
    {
        _pending.Append(key).Append('=').Append(value).Append('\n');
        _state[key] = value;
    }

    private void FlushPending()
    {
        if (_mmf == null) return;
        _overlaySeq++;
        int flags = Ipc.FlagHeartbeat;
        if (_menuOpen) flags |= Ipc.FlagMenuOpen;
        string payload = _pending.ToString();
        _pending.Clear();
        Ipc.WriteBlock(_mmf, Ipc.OverlayBlockOffset, _overlaySeq, flags, payload);
    }

    private void PumpIpc()
    {
        if (_mmf == null) return;
        if (!Ipc.ReadBlock(_mmf, Ipc.GameBlockOffset, out int seq, out int flags, out string payload))
        {
            _status = "no game data";
            return;
        }

        _status = (flags & Ipc.FlagHeartbeat) != 0 ? "linked" : "stale";
        if (seq == _lastGameSeq) return;
        _lastGameSeq = seq;

        foreach (var line in payload.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string k = line.Substring(0, eq);
            string v = line.Substring(eq + 1);

            if (_state.TryGetValue(k, out var old) && old == v)
                continue;

            _state[k] = v;
            if (k == "MenuOpen")
                _menuOpen = v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
            else if (k == "CurrentConfig")
                _profile = v;

            // only invalidate typed cache when value actually changed
            _boolCache.Remove(k);
            _intCache.Remove(k);
            _floatCache.Remove(k);
        }
    }

    private static bool ChipToggle(string label, ref bool value)
    {
        var accent = new Vector4(0.30f, 0.64f, 1f, 1f);
        if (value)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, accent);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.45f, 0.78f, 1f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, accent);
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 1, 1, 1));
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.12f, 0.14f, 0.18f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.18f, 0.22f, 0.28f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.20f, 0.24f, 0.30f, 1f));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.70f, 0.73f, 0.78f, 1f));
        }
        bool pressed = ImGui.Button(label);
        ImGui.PopStyleColor(4);
        if (pressed) value = !value;
        return pressed;
    }

    private static void ApplyStyle()
    {
        // Premium dark ClickGUI style (ImGui), inspired by modern cheat UIs / ImThemes dark variants
        var style = ImGui.GetStyle();
        style.WindowPadding = new Vector2(12, 10);
        style.FramePadding = new Vector2(10, 6);
        style.ItemSpacing = new Vector2(10, 8);
        style.ItemInnerSpacing = new Vector2(8, 5);
        style.WindowRounding = 8f;
        style.ChildRounding = 6f;
        style.FrameRounding = 5f;
        style.PopupRounding = 6f;
        style.ScrollbarRounding = 8f;
        style.GrabRounding = 4f;
        style.TabRounding = 5f;
        style.WindowBorderSize = 1f;
        style.FrameBorderSize = 0f;
        style.PopupBorderSize = 1f;
        style.ScrollbarSize = 10f;
        style.GrabMinSize = 12f;
        style.WindowTitleAlign = new Vector2(0.02f, 0.5f);

        var c = style.Colors;
        Vector4 accent = new(0.30f, 0.64f, 1.00f, 1f);
        Vector4 accentH = new(0.45f, 0.78f, 1.00f, 1f);
        Vector4 bg = new(0.06f, 0.07f, 0.09f, 0.96f);
        Vector4 bg2 = new(0.09f, 0.10f, 0.13f, 1f);
        Vector4 bg3 = new(0.12f, 0.14f, 0.18f, 1f);
        Vector4 text = new(0.92f, 0.94f, 0.97f, 1f);
        Vector4 textD = new(0.55f, 0.58f, 0.64f, 1f);
        Vector4 border = new(0.18f, 0.20f, 0.25f, 1f);

        c[(int)ImGuiCol.Text] = text;
        c[(int)ImGuiCol.TextDisabled] = textD;
        c[(int)ImGuiCol.WindowBg] = bg;
        c[(int)ImGuiCol.ChildBg] = new Vector4(0.05f, 0.055f, 0.07f, 0.70f);
        c[(int)ImGuiCol.PopupBg] = new Vector4(0.07f, 0.08f, 0.10f, 0.98f);
        c[(int)ImGuiCol.Border] = border;
        c[(int)ImGuiCol.BorderShadow] = new Vector4(0, 0, 0, 0);
        c[(int)ImGuiCol.FrameBg] = bg3;
        c[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.18f, 0.22f, 0.30f, 1f);
        c[(int)ImGuiCol.FrameBgActive] = new Vector4(0.22f, 0.32f, 0.48f, 1f);
        c[(int)ImGuiCol.TitleBg] = bg2;
        c[(int)ImGuiCol.TitleBgActive] = new Vector4(0.10f, 0.14f, 0.22f, 1f);
        c[(int)ImGuiCol.TitleBgCollapsed] = bg2;
        c[(int)ImGuiCol.MenuBarBg] = bg2;
        c[(int)ImGuiCol.ScrollbarBg] = new Vector4(0.04f, 0.045f, 0.06f, 0.6f);
        c[(int)ImGuiCol.ScrollbarGrab] = new Vector4(0.25f, 0.28f, 0.34f, 1f);
        c[(int)ImGuiCol.ScrollbarGrabHovered] = accent;
        c[(int)ImGuiCol.ScrollbarGrabActive] = accentH;
        c[(int)ImGuiCol.CheckMark] = accentH;
        c[(int)ImGuiCol.SliderGrab] = accent;
        c[(int)ImGuiCol.SliderGrabActive] = accentH;
        c[(int)ImGuiCol.Button] = new Vector4(0.15f, 0.18f, 0.24f, 1f);
        c[(int)ImGuiCol.ButtonHovered] = new Vector4(0.22f, 0.36f, 0.55f, 1f);
        c[(int)ImGuiCol.ButtonActive] = accent;
        c[(int)ImGuiCol.Header] = new Vector4(0.14f, 0.20f, 0.30f, 1f);
        c[(int)ImGuiCol.HeaderHovered] = new Vector4(0.20f, 0.34f, 0.52f, 1f);
        c[(int)ImGuiCol.HeaderActive] = accent;
        c[(int)ImGuiCol.Separator] = border;
        c[(int)ImGuiCol.SeparatorHovered] = accent;
        c[(int)ImGuiCol.SeparatorActive] = accentH;
        c[(int)ImGuiCol.ResizeGrip] = new Vector4(accent.X, accent.Y, accent.Z, 0.25f);
        c[(int)ImGuiCol.ResizeGripHovered] = accent;
        c[(int)ImGuiCol.ResizeGripActive] = accentH;
        c[(int)ImGuiCol.Tab] = bg2;
        c[(int)ImGuiCol.TabHovered] = new Vector4(0.20f, 0.34f, 0.52f, 1f);
        c[(int)ImGuiCol.TabSelected] = new Vector4(0.14f, 0.24f, 0.40f, 1f);
        c[(int)ImGuiCol.PlotHistogram] = accent;
        // nav highlight omitted for imgui version compat
    }

    protected override void Dispose(bool disposing)
    {
        try { _mmf?.Dispose(); } catch { }
        _mmf = null;
        base.Dispose(disposing);
    }
}

/// <summary>Mirror of Vape.UI.OverlaySync constants/logic for the overlay process.</summary>
internal static class Ipc
{
    public const string MapName = "Local\\VapeOverlayIO_v1";
    public const int MapSize = 256 * 1024;
    public const int BlockSize = 128 * 1024;
    public const int HeaderSize = 20;
    public const int Version = 1;
    public static readonly int Magic = BitConverter.ToInt32(Encoding.ASCII.GetBytes("VAPE"), 0);
    public const int FlagMenuOpen = 1 << 0;
    public const int FlagHeartbeat = 1 << 1;
    public const int GameBlockOffset = 0;
    public const int OverlayBlockOffset = 128 * 1024;

    public static void WriteBlock(MemoryMappedFile mmf, int offset, int sequence, int flags, string payload)
    {
        byte[] data = Encoding.UTF8.GetBytes(payload ?? string.Empty);
        int max = BlockSize - HeaderSize;
        if (data.Length > max) Array.Resize(ref data, max);
        using var acc = mmf.CreateViewAccessor(offset, BlockSize, MemoryMappedFileAccess.Write);
        acc.Write(0, Magic);
        acc.Write(4, Version);
        acc.Write(8, sequence);
        acc.Write(12, flags);
        acc.Write(16, data.Length);
        if (data.Length > 0) acc.WriteArray(HeaderSize, data, 0, data.Length);
    }

    public static bool ReadBlock(MemoryMappedFile mmf, int offset, out int sequence, out int flags, out string payload)
    {
        sequence = 0; flags = 0; payload = string.Empty;
        using var acc = mmf.CreateViewAccessor(offset, BlockSize, MemoryMappedFileAccess.Read);
        if (acc.ReadInt32(0) != Magic) return false;
        if (acc.ReadInt32(4) != Version) return false;
        sequence = acc.ReadInt32(8);
        flags = acc.ReadInt32(12);
        int len = acc.ReadInt32(16);
        if (len < 0 || len > BlockSize - HeaderSize) return false;
        if (len == 0) return true;
        byte[] data = new byte[len];
        acc.ReadArray(HeaderSize, data, 0, len);
        payload = Encoding.UTF8.GetString(data);
        return true;
    }
}
