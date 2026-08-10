using NetData;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Vape.Cfg;
using Vape.Entity;
using Vape.UI;
using Vape.UI.Menu;

namespace Vape.Feature.Overlay
{
    public sealed class CsgoHud : MonoBehaviour
    {
        private sealed class KillNotice
        {
            public string Killer;
            public string Victim;
            public string Weapon;
            public int KillerTeam;
            public int VictimTeam;
            public int WeaponType;
            public bool Headshot;
            public bool Wallshot;
            public float CreatedAt;
        }

        private sealed class KillCardState
        {
            public string Victim;
            public bool Headshot;
            public int Combo;
            public float CreatedAt;
        }

        private sealed class HiddenHudRoot
        {
            public GameObject GameObject;
            public Canvas Canvas;
            public CanvasGroup Group;
            public float WasAlpha;
            public bool WasInteractable;
            public bool WasBlocksRaycasts;
            public bool WasIgnoreParentGroups;
        }

        private const float NoticeLifetime = 5f;
        private const float KillCardLifetime = 2.35f;
        private const float KillComboWindow = 3.4f;
        private const int MaxNotices = 5;
        private const float HudScanInterval = 1.5f;
        private static readonly string AssetRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "CSGO_HUD");
        private static readonly string AvatarPath = Path.Combine(AssetRoot, "profile_avatar.jpg");
        private static readonly string KillSoundPath = Path.Combine(AssetRoot, "kill_glass.wav");
        private static readonly string[] KillCardIconPaths =
        {
            AssetRoot + @"\kill_card_1_spade.png",
            AssetRoot + @"\kill_card_2_joker.png",
            AssetRoot + @"\kill_card_3_thunder.png",
            AssetRoot + @"\kill_card_4_death.png"
        };
        private static readonly string[] KillCardRanks = { "A", "J", "Q", "K" };
        private static readonly string[] KillCardTitles = { "KILL", "DOUBLE KILL", "TRIPLE KILL", "QUAD KILL" };
        private static readonly string[] PlayerAvatarPaths =
        {
            AssetRoot + @"\player_avatar_1.png",
            AssetRoot + @"\player_avatar_2.png",
            AssetRoot + @"\player_avatar_3.png"
        };

        // Values mirror the current CS2 Panorama HUD definitions.
        private static readonly Color CtBlue = Hex(0x6F9CE6, 1f);
        private static readonly Color TGold = Hex(0xEABE54, 1f);
        private static readonly Color TextMain = Color.white;
        private static readonly Color TextSoft = Hex(0xA0A0A0, 1f);
        private static readonly Color BlurPanel = new Color(0.015f, 0.02f, 0.024f, 0.90f);
        private static readonly Color Critical = Hex(0xDD0000, 1f);
        private static readonly Color CrosshairGreen = Hex(0x4CFF70, 1f);
        private static readonly Color HudGold = Hex(0xE4B94F, 1f);
        private static readonly Color HudGoldSoft = Hex(0xD7AE45, 0.72f);

        private readonly List<KillNotice> _notices = new List<KillNotice>(MaxNotices);
        private readonly HashSet<int> _seenKillObjects = new HashSet<int>();
        private readonly Dictionary<int, bool> _deadStates = new Dictionary<int, bool>(32);
        private readonly HashSet<int> _activePlayerIds = new HashSet<int>();
        private readonly List<int> _stalePlayerIds = new List<int>(32);
        private readonly Dictionary<int, GUIStyle> _styles = new Dictionary<int, GUIStyle>(32);
        private readonly Dictionary<int, HiddenHudRoot> _hiddenHudRoots = new Dictionary<int, HiddenHudRoot>(32);
        private readonly List<int> _restoreHudIds = new List<int>(4);
        private static readonly Dictionary<string, Texture2D> CircleRingTextures = new Dictionary<string, Texture2D>(8);
        private readonly List<PlayerInfo> _team1 = new List<PlayerInfo>(8);
        private readonly List<PlayerInfo> _team2 = new List<PlayerInfo>(8);
        private readonly HashSet<int> _playerIds = new HashSet<int>();

        private Font _hudFont;
        private Texture2D _radarMap;
        private Texture2D _avatarTexture;
        private readonly Texture2D[] _killCardIcons = new Texture2D[4];
        private readonly Texture2D[] _playerAvatars = new Texture2D[3];
        private Component _killAudioSource;
        private GameObject _killAudioObject;
        private UnityEngine.Object _killAudioClip;
        private MethodInfo _killAudioPlayOneShot;
        private MethodInfo _killAudioStop;
        private PropertyInfo _killAudioPitch;
        private KillCardState _killCard;
        private string _lastLocalKillVictim = string.Empty;
        private float _lastLocalKillAt = -10f;
        private int _localKillCombo;
        private bool _fontAttempted;
        private bool _avatarAttempted;
        private float _fontReadyAt;
        private float _avatarReadyAt;
        private float _smoothedDelta = 1f / 60f;
        private float _nextPingUpdate;
        private int _localPing;
        private bool _pauseOpen;
        private bool _settingsOpen;
        private bool _defaultFullscreen;
        private int _defaultVsync;
        private int _defaultQuality;
        private int _sessionPlayerId;
        private int _lastWeaponSlot = -1;
        private string _lastWeaponName = string.Empty;
        private float _weaponChangedAt = -10f;
        private float _nextHudScan;
        private float _killAssetsReadyAt;
        private bool _killAssetsAttempted;
        private bool _wasEnabled;
        private float _lastCustomKillSoundAt = -10f;
        private float _suppressNativeKillUntil = -10f;
        private bool _autoSoundReflectionReady;
        private PropertyInfo _autoSoundInstanceProperty;
        private FieldInfo _autoSoundCurrentItemField;
        private MethodInfo _autoSoundStopMethod;

        private void Awake()
        {
            _killAssetsReadyAt = Time.realtimeSinceStartup + 2f;
            _defaultFullscreen = Screen.fullScreen;
            _defaultVsync = QualitySettings.vSyncCount;
            _defaultQuality = QualitySettings.GetQualityLevel();
        }

        private void Update()
        {
            if (!Config.CsgoHud)
            {
                if (_wasEnabled)
                {
                    ClosePauseMenu();
                    RestoreOriginalHud();
                    ResetRuntime();
                }
                _wasEnabled = false;
                return;
            }

            PlayerInfo local = PlayerUpdate.LocalEntity;
            if (local == null || local._entity == null)
            {
                ClosePauseMenu();
                RestoreOriginalHud();
                _wasEnabled = false;
                return;
            }

            if (!_wasEnabled)
            {
                _fontReadyAt = Time.unscaledTime + 0.75f;
                _avatarReadyAt = Time.unscaledTime + 1f;
                _nextHudScan = Time.unscaledTime + 0.75f;
                _killAssetsReadyAt = Time.unscaledTime + 1.5f;
            }
            _wasEnabled = true;
            _smoothedDelta = Mathf.Lerp(_smoothedDelta, Mathf.Max(0.0001f, Time.unscaledDeltaTime), 0.08f);
            if (!_avatarAttempted && Time.unscaledTime >= _avatarReadyAt)
                LoadAvatarTexture();
            if (!_killAssetsAttempted && Time.unscaledTime >= _killAssetsReadyAt)
            {
                _killAssetsAttempted = true;
                LoadKillCardIcons();
                SetupKillAudio();
            }
            if (Time.unscaledTime >= _nextPingUpdate)
            {
                _nextPingUpdate = Time.unscaledTime + 0.5f;
                _localPing = GetLocalPing(local);
            }
            SuppressOriginalHud();

            if (Input.GetKeyDown(KeyCode.Escape))
                SetPauseOpen(!_pauseOpen);
            SyncCursorState();

            if (_sessionPlayerId != local.Id)
            {
                ResetRuntime();
                _sessionPlayerId = local.Id;
            }

            try
            {
                CaptureKillEvents();
                TrackDeaths(local);
                TrackWeapon(local);
                if (Time.unscaledTime < _suppressNativeKillUntil)
                    SuppressNativeKillSound();
            }
            catch
            {
            }

            float now = Time.unscaledTime;
            for (int i = _notices.Count - 1; i >= 0; i--)
            {
                if (now - _notices[i].CreatedAt >= NoticeLifetime)
                    _notices.RemoveAt(i);
            }
        }

        private void LateUpdate()
        {
            if (!Config.CsgoHud || PlayerUpdate.LocalEntity == null)
                return;
            foreach (HiddenHudRoot hidden in _hiddenHudRoots.Values)
                ApplyCanvasMask(hidden);
            SyncCursorState();
        }

        private void OnGUI()
        {
            if (!Config.CsgoHud)
                return;

            PlayerInfo local = PlayerUpdate.LocalEntity;
            if (local == null || local._entity == null)
                return;

            float scale = HudScale();
            if (_pauseOpen)
            {
                DrawPauseMenu(scale);
                return;
            }

            if (Event.current.type != EventType.Repaint)
                return;

            try
            {
                BuildTeamLists(local);
                DrawRadar(local, scale);
                DrawTopCounter(local, scale);
                DrawTelemetry(scale);
                DrawKillFeed(local, scale);
                DrawKillCard(scale);
                DrawMoney(scale);
                DrawBombStatus(scale);

                if (!local.IsDead)
                {
                    DrawHealthAmmo(local, scale);
                    DrawWeaponSelection(local, scale);
                    DrawCrosshair(local, scale);
                }

                if (Input.GetKey(KeyCode.Tab))
                    DrawScoreboard(local, scale);
            }
            catch
            {
            }
        }

        private void OnDisable()
        {
            RestoreOriginalHud();
        }

        private void OnDestroy()
        {
            ClosePauseMenu();
            RestoreOriginalHud();
            if (_radarMap != null)
                Destroy(_radarMap);
            if (_avatarTexture != null)
                Destroy(_avatarTexture);
            if (_killAudioClip != null)
                Destroy(_killAudioClip);
            if (_killAudioObject != null)
                Destroy(_killAudioObject);
            for (int i = 0; i < _killCardIcons.Length; i++)
            {
                if (_killCardIcons[i] != null)
                    Destroy(_killCardIcons[i]);
            }
            for (int i = 0; i < _playerAvatars.Length; i++)
            {
                if (_playerAvatars[i] != null)
                    Destroy(_playerAvatars[i]);
            }
        }

        private void SuppressOriginalHud()
        {
            _restoreHudIds.Clear();
            foreach (KeyValuePair<int, HiddenHudRoot> pair in _hiddenHudRoots)
            {
                HiddenHudRoot hidden = pair.Value;
                if (hidden.Canvas != null)
                {
                    if (ShouldPreserveGameInfoCanvas(hidden.Canvas))
                    {
                        RestoreHudRoot(hidden);
                        _restoreHudIds.Add(pair.Key);
                        continue;
                    }
                    if (_radarMap == null)
                        CaptureRadarMap(hidden.GameObject);
                    ApplyCanvasMask(hidden);
                }
            }
            for (int i = 0; i < _restoreHudIds.Count; i++)
                _hiddenHudRoots.Remove(_restoreHudIds[i]);

            if (Time.unscaledTime < _nextHudScan)
                return;
            _nextHudScan = Time.unscaledTime + HudScanInterval;

            Canvas[] canvases;
            try { canvases = Resources.FindObjectsOfTypeAll<Canvas>(); }
            catch { return; }

            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas canvas = canvases[i];
                if (canvas == null || canvas.gameObject == null || !canvas.gameObject.scene.IsValid() ||
                    !canvas.gameObject.activeInHierarchy)
                    continue;
                CaptureRadarMap(canvas.gameObject);
                if (ShouldPreserveGameInfoCanvas(canvas))
                    continue;
                HideCanvas(canvas);
            }

        }

        private void HideCanvas(Canvas canvas)
        {
            if (canvas == null || canvas.gameObject == null)
                return;
            GameObject gameObject = canvas.gameObject;
            int id = gameObject.GetInstanceID();
            if (!_hiddenHudRoots.ContainsKey(id))
            {
                CanvasGroup group = gameObject.GetComponent<CanvasGroup>();
                if (group == null)
                {
                    try { group = gameObject.AddComponent<CanvasGroup>(); }
                    catch { return; }
                }
                _hiddenHudRoots[id] = new HiddenHudRoot
                {
                    GameObject = gameObject,
                    Canvas = canvas,
                    Group = group,
                    WasAlpha = group.alpha,
                    WasInteractable = group.interactable,
                    WasBlocksRaycasts = group.blocksRaycasts,
                    WasIgnoreParentGroups = group.ignoreParentGroups
                };
            }
            ApplyCanvasMask(_hiddenHudRoots[id]);
        }

        private static void ApplyCanvasMask(HiddenHudRoot hidden)
        {
            if (hidden?.Group == null)
                return;
            hidden.Group.alpha = 0f;
            hidden.Group.interactable = false;
            hidden.Group.blocksRaycasts = false;
            hidden.Group.ignoreParentGroups = false;
        }

        private static bool ShouldPreserveGameInfoCanvas(Canvas canvas)
        {
            if (canvas == null)
                return false;
            try
            {
                Text infoText = FpsDisplay.GetInstance()?._text;
                return infoText != null && infoText.canvas == canvas;
            }
            catch
            {
                return false;
            }
        }

        private static void RestoreHudRoot(HiddenHudRoot hidden)
        {
            if (hidden?.Group == null)
                return;
            hidden.Group.alpha = hidden.WasAlpha;
            hidden.Group.interactable = hidden.WasInteractable;
            hidden.Group.blocksRaycasts = hidden.WasBlocksRaycasts;
            hidden.Group.ignoreParentGroups = hidden.WasIgnoreParentGroups;
        }

        private void RestoreOriginalHud()
        {
            foreach (HiddenHudRoot hidden in _hiddenHudRoots.Values)
                RestoreHudRoot(hidden);
            _hiddenHudRoots.Clear();
            _nextHudScan = 0f;
        }

        private void CaptureRadarMap(GameObject root)
        {
            if (_radarMap != null || root == null)
                return;

            try
            {
                Image[] images = root.GetComponentsInChildren<Image>(true);
                for (int i = 0; i < images.Length; i++)
                {
                    Image image = images[i];
                    if (image == null || image.sprite == null || !IsRadarGraphic(image))
                        continue;
                    Sprite sprite = image.sprite;
                    if (TryCaptureRadarTexture(sprite.texture, sprite.textureRect))
                        return;
                }

                RawImage[] rawImages = root.GetComponentsInChildren<RawImage>(true);
                for (int i = 0; i < rawImages.Length; i++)
                {
                    RawImage image = rawImages[i];
                    if (image == null || image.texture == null || !IsRadarGraphic(image))
                        continue;
                    Rect uv = image.uvRect;
                    Rect sourceRect = new Rect(uv.x * image.texture.width, uv.y * image.texture.height,
                        uv.width * image.texture.width, uv.height * image.texture.height);
                    if (TryCaptureRadarTexture(image.texture, sourceRect))
                        return;
                }
            }
            catch { }
        }

        private bool TryCaptureRadarTexture(Texture source, Rect sourceRect)
        {
            if (source == null || source.width < 128 || source.height < 128 ||
                sourceRect.width < 128f || sourceRect.height < 128f)
                return false;

            const int Size = 256;
            RenderTexture target = null;
            RenderTexture previous = RenderTexture.active;
            Texture2D masked = null;
            try
            {
                Vector2 uvScale = new Vector2(sourceRect.width / source.width, sourceRect.height / source.height);
                Vector2 uvOffset = new Vector2(sourceRect.x / source.width, sourceRect.y / source.height);
                target = RenderTexture.GetTemporary(Size, Size, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(source, target, uvScale, uvOffset);
                RenderTexture.active = target;

                masked = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                masked.ReadPixels(new Rect(0f, 0f, Size, Size), 0, 0, false);
                Color32[] pixels = masked.GetPixels32();
                float radius = Size * 0.49f;
                float feather = 3f;
                float center = (Size - 1) * 0.5f;
                for (int y = 0; y < Size; y++)
                {
                    float dy = y - center;
                    for (int x = 0; x < Size; x++)
                    {
                        int index = y * Size + x;
                        float dx = x - center;
                        float distance = Mathf.Sqrt(dx * dx + dy * dy);
                        float mask = Mathf.Clamp01((radius - distance) / feather);
                        Color32 pixel = pixels[index];
                        pixel.a = (byte)(pixel.a * mask * 0.88f);
                        pixels[index] = pixel;
                    }
                }
                masked.SetPixels32(pixels);
                masked.Apply(false, false);
                masked.wrapMode = TextureWrapMode.Clamp;
                masked.filterMode = FilterMode.Bilinear;
                masked.hideFlags = HideFlags.HideAndDontSave;
                _radarMap = masked;
                masked = null;
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                RenderTexture.active = previous;
                if (target != null)
                    RenderTexture.ReleaseTemporary(target);
                if (masked != null)
                    Destroy(masked);
            }
        }

        private static bool IsRadarTextureName(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;
            string name = value.ToLowerInvariant();
            return name == "map" || name.Contains("mapimage") || name.Contains("maptexture") ||
                   name.Contains("mapbg") || name.Contains("radarmap") || name.Contains("minimap");
        }

        private static bool IsRadarGraphic(Component component)
        {
            if (component == null)
                return false;
            Transform current = component.transform;
            for (int depth = 0; current != null && depth < 8; depth++, current = current.parent)
            {
                if (IsRadarTextureName(current.gameObject.name))
                    return true;
            }
            return false;
        }

        private void LoadAvatarTexture()
        {
            _avatarAttempted = true;
            _avatarTexture = CreateAvatarTexture(AvatarPath, 96, true);
            for (int i = 0; i < _playerAvatars.Length && i < PlayerAvatarPaths.Length; i++)
                _playerAvatars[i] = CreateAvatarTexture(PlayerAvatarPaths[i], 128, false);
        }

        private void LoadKillCardIcons()
        {
            for (int i = 0; i < _killCardIcons.Length && i < KillCardIconPaths.Length; i++)
                _killCardIcons[i] = CreateAvatarTexture(KillCardIconPaths[i], 256, false);
        }

        private void SetupKillAudio()
        {
            try
            {
                Type sourceType = Type.GetType("UnityEngine.AudioSource, UnityEngine.AudioModule", false);
                Type clipType = Type.GetType("UnityEngine.AudioClip, UnityEngine.AudioModule", false);
                if (sourceType == null || clipType == null)
                    return;

                _killAudioObject = new GameObject("Vape_CS2KillAudio");
                DontDestroyOnLoad(_killAudioObject);
                _killAudioSource = _killAudioObject.AddComponent(sourceType) as Component;
                if (_killAudioSource == null)
                    return;

                SetReflectedProperty(sourceType, _killAudioSource, "playOnAwake", false);
                SetReflectedProperty(sourceType, _killAudioSource, "loop", false);
                SetReflectedProperty(sourceType, _killAudioSource, "spatialBlend", 0f);
                SetReflectedProperty(sourceType, _killAudioSource, "volume", 1f);
                SetReflectedProperty(sourceType, _killAudioSource, "priority", 0);
                SetReflectedProperty(sourceType, _killAudioSource, "bypassEffects", true);
                SetReflectedProperty(sourceType, _killAudioSource, "bypassListenerEffects", true);
                SetReflectedProperty(sourceType, _killAudioSource, "bypassReverbZones", true);
                _killAudioPitch = sourceType.GetProperty("pitch", BindingFlags.Instance | BindingFlags.Public);
                _killAudioStop = sourceType.GetMethod("Stop", BindingFlags.Instance | BindingFlags.Public,
                    null, Type.EmptyTypes, null);
                _killAudioPlayOneShot = sourceType.GetMethod("PlayOneShot",
                    BindingFlags.Instance | BindingFlags.Public, null, new[] { clipType, typeof(float) }, null);

                if (File.Exists(KillSoundPath))
                    _killAudioClip = LoadPcmWav(KillSoundPath, clipType);
            }
            catch
            {
                _killAudioSource = null;
                _killAudioClip = null;
                _killAudioPlayOneShot = null;
                _killAudioStop = null;
                _killAudioPitch = null;
            }
        }

        private static void SetReflectedProperty(Type type, object target, string name, object value)
        {
            PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (property != null && property.CanWrite)
                property.SetValue(target, value, null);
        }

        private static UnityEngine.Object LoadPcmWav(string path, Type clipType)
        {
            ushort format = 0;
            ushort channels = 0;
            int sampleRate = 0;
            ushort bits = 0;
            byte[] pcm = null;

            using (FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                if (ReadFourCc(reader) != "RIFF")
                    throw new InvalidDataException("WAV RIFF header");
                reader.ReadUInt32();
                if (ReadFourCc(reader) != "WAVE")
                    throw new InvalidDataException("WAV WAVE header");

                while (stream.Position + 8 <= stream.Length)
                {
                    string chunk = ReadFourCc(reader);
                    int length = reader.ReadInt32();
                    if (length < 0 || stream.Position + length > stream.Length)
                        throw new InvalidDataException("WAV chunk size");
                    long chunkEnd = stream.Position + length;
                    if (chunk == "fmt " && length >= 16)
                    {
                        format = reader.ReadUInt16();
                        channels = reader.ReadUInt16();
                        sampleRate = reader.ReadInt32();
                        reader.ReadInt32();
                        reader.ReadUInt16();
                        bits = reader.ReadUInt16();
                    }
                    else if (chunk == "data")
                    {
                        pcm = reader.ReadBytes(length);
                    }
                    stream.Position = chunkEnd + (length & 1);
                }
            }

            if (channels == 0 || channels > 8 || sampleRate < 8000 || sampleRate > 192000 ||
                pcm == null || pcm.Length == 0)
                throw new InvalidDataException("WAV metadata");
            int bytesPerSample = bits / 8;
            if (bytesPerSample <= 0 || pcm.Length % bytesPerSample != 0)
                throw new InvalidDataException("WAV sample size");

            int sampleCount = pcm.Length / bytesPerSample;
            int frameCount = sampleCount / channels;
            float[] samples = new float[frameCount * channels];
            for (int i = 0; i < samples.Length; i++)
            {
                int offset = i * bytesPerSample;
                if (format == 1)
                {
                    switch (bits)
                    {
                        case 8:
                            samples[i] = (pcm[offset] - 128) / 128f;
                            break;
                        case 16:
                            samples[i] = BitConverter.ToInt16(pcm, offset) / 32768f;
                            break;
                        case 24:
                            int value24 = pcm[offset] | (pcm[offset + 1] << 8) | (pcm[offset + 2] << 16);
                            if ((value24 & 0x800000) != 0)
                                value24 |= unchecked((int)0xFF000000);
                            samples[i] = value24 / 8388608f;
                            break;
                        case 32:
                            samples[i] = BitConverter.ToInt32(pcm, offset) / 2147483648f;
                            break;
                        default:
                            throw new InvalidDataException("WAV PCM bits");
                    }
                }
                else if (format == 3 && bits == 32)
                {
                    samples[i] = BitConverter.ToSingle(pcm, offset);
                }
                else
                {
                    throw new InvalidDataException("WAV encoding");
                }
            }

            MethodInfo create = clipType.GetMethod("Create", BindingFlags.Static | BindingFlags.Public, null,
                new[] { typeof(string), typeof(int), typeof(int), typeof(int), typeof(bool) }, null);
            MethodInfo setData = clipType.GetMethod("SetData", BindingFlags.Instance | BindingFlags.Public, null,
                new[] { typeof(float[]), typeof(int) }, null);
            if (create == null || setData == null)
                throw new MissingMethodException("AudioClip API");

            UnityEngine.Object clip = create.Invoke(null,
                new object[] { "vape_kill_glass", frameCount, (int)channels, sampleRate, false }) as UnityEngine.Object;
            if (clip == null || !(bool)setData.Invoke(clip, new object[] { samples, 0 }))
                throw new InvalidOperationException("WAV AudioClip");
            clip.hideFlags = HideFlags.HideAndDontSave;
            return clip;
        }

        private static string ReadFourCc(BinaryReader reader)
        {
            return Encoding.ASCII.GetString(reader.ReadBytes(4));
        }

        private Texture2D CreateAvatarTexture(string path, int size, bool circular)
        {
            if (!System.IO.File.Exists(path))
                return null;
            Texture2D texture = null;
            try
            {
                Color32[] pixels = new Color32[size * size];
                using (var bitmap = new System.Drawing.Bitmap(path))
                {
                    int crop = Math.Min(bitmap.Width, bitmap.Height);
                    int cropX = (bitmap.Width - crop) / 2;
                    int cropY = (bitmap.Height - crop) / 2;
                    float center = (size - 1) * 0.5f;
                    float radius = size * 0.49f;
                    for (int y = 0; y < size; y++)
                    {
                        int sourceY = cropY + Math.Min(crop - 1, (int)((y + 0.5f) / size * crop));
                        float dy = y - center;
                        for (int x = 0; x < size; x++)
                        {
                            int sourceX = cropX + Math.Min(crop - 1, (int)((x + 0.5f) / size * crop));
                            System.Drawing.Color source = bitmap.GetPixel(sourceX, sourceY);
                            float dx = x - center;
                            float mask = circular
                                ? Mathf.Clamp01((radius - Mathf.Sqrt(dx * dx + dy * dy)) / 1.5f)
                                : 1f;
                            pixels[(size - 1 - y) * size + x] = new Color32(
                                source.R, source.G, source.B, (byte)(source.A * mask));
                        }
                    }
                }

                texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                texture.filterMode = FilterMode.Bilinear;
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.hideFlags = HideFlags.HideAndDontSave;
                Texture2D result = texture;
                texture = null;
                return result;
            }
            catch { }
            finally
            {
                if (texture != null)
                    Destroy(texture);
            }
            return null;
        }

        private void SetPauseOpen(bool open)
        {
            if (_pauseOpen == open)
                return;
            _pauseOpen = open;
            if (open)
            {
                _nextHudScan = 0f;
            }
            else
            {
                _settingsOpen = false;
                ClosePauseMenu();
            }
            SyncCursorState();
        }

        private void ClosePauseMenu()
        {
            _pauseOpen = false;
            _settingsOpen = false;
            SyncCursorState();
        }

        private void SyncCursorState()
        {
            bool menuOpen = _pauseOpen || Vape.Features.Menu.IsOpen;
            Cursor.visible = menuOpen;
            Cursor.lockState = menuOpen ? CursorLockMode.None : CursorLockMode.Locked;
        }

        private void TryInvokeOriginalResume()
        {
            string[] hints =
            {
                "resume", "continue", "returngame", "backgame",
                "\u7ee7\u7eed\u6e38\u620f", "\u7ee7\u7eed", "\u8fd4\u56de\u6e38\u620f"
            };

            foreach (HiddenHudRoot hidden in _hiddenHudRoots.Values)
            {
                if (hidden.GameObject == null)
                    continue;
                Button[] buttons;
                try { buttons = hidden.GameObject.GetComponentsInChildren<Button>(true); }
                catch { continue; }

                for (int i = 0; i < buttons.Length; i++)
                {
                    Button button = buttons[i];
                    if (button == null)
                        continue;
                    string identity = button.gameObject.name ?? string.Empty;
                    try
                    {
                        UnityEngine.UI.Text[] labels = button.GetComponentsInChildren<UnityEngine.UI.Text>(true);
                        for (int j = 0; j < labels.Length; j++)
                            identity += " " + labels[j].text;
                    }
                    catch { }

                    string normalized = identity.Replace(" ", string.Empty).ToLowerInvariant();
                    for (int j = 0; j < hints.Length; j++)
                    {
                        if (!normalized.Contains(hints[j]))
                            continue;
                        try { button.onClick.Invoke(); }
                        catch { }
                        return;
                    }
                }
            }
        }

        private void DrawRadar(PlayerInfo local, float scale)
        {
            float radius = 116f * scale;
            Vector2 center = new Vector2(132f * scale, 132f * scale);
            Rect outerRect = new Rect(center.x - radius - 3f * scale, center.y - radius - 3f * scale,
                (radius + 3f * scale) * 2f, (radius + 3f * scale) * 2f);
            Rect mapRect = new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f);
            CardDraw.RoundedRect(outerRect, new Color(0f, 0f, 0f, 0.72f), outerRect.width * 0.5f);
            CardDraw.RoundedRect(mapRect, new Color(0.025f, 0.035f, 0.04f, 0.86f), radius);

            if (_radarMap != null)
            {
                Matrix4x4 matrix = GUI.matrix;
                Color tint = GUI.color;
                GUIUtility.RotateAroundPivot(-local.ViewYaw, center);
                GUI.color = new Color(0.72f, 0.84f, 0.86f, 0.82f);
                GUI.DrawTexture(mapRect, _radarMap, ScaleMode.ScaleToFit, true);
                GUI.color = tint;
                GUI.matrix = matrix;
            }
            else
                DrawFallbackRadar(center, radius, scale);

            DrawGuiCircle(center, radius - scale, HudGold, Mathf.Max(1f, scale));
            DrawGuiCircle(center, radius - 8f * scale, Alpha(HudGold, 0.36f), Mathf.Max(1f, scale));

            Color grid = new Color(0.72f, 0.78f, 0.80f, 0.15f);
            CardDraw.RoundedRect(new Rect(center.x - radius * 0.88f, center.y,
                radius * 1.76f, Mathf.Max(1f, scale)), grid, 0f);
            CardDraw.RoundedRect(new Rect(center.x, center.y - radius * 0.88f,
                Mathf.Max(1f, scale), radius * 1.76f), grid, 0f);

            Vector3 localPosition = SSJJMath.VectorCoordConverter.SsjjToUnity(local.Position);
            float yaw = -local.ViewYaw * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(yaw);
            float sine = Mathf.Sin(yaw);
            DrawRadarPlayers(local, localPosition, center, radius, cosine, sine, scale);

            Text(new Rect(center.x - 12f * scale, center.y - 13f * scale,
                24f * scale, 24f * scale), "▲", FontSize(17f, scale), TextAnchor.MiddleCenter, Color.white, true);

            Text(new Rect(center.x - radius + 10f * scale, center.y - radius + 20f * scale,
                20f * scale, 20f * scale), "A", FontSize(16f, scale), TextAnchor.MiddleCenter, HudGold, true);
            Text(new Rect(center.x - radius + 10f * scale, center.y + radius - 46f * scale,
                20f * scale, 20f * scale), "B", FontSize(16f, scale), TextAnchor.MiddleCenter, HudGold, true);
            Text(new Rect(center.x - radius, center.y + radius + 8f * scale, radius * 2f, 22f * scale),
                "TACTICAL MAP", FontSize(12f, scale), TextAnchor.MiddleCenter, HudGold, true);
        }

        private static void DrawFallbackRadar(Vector2 center, float radius, float scale)
        {
            Color room = new Color(0.17f, 0.25f, 0.27f, 0.68f);
            Color wall = new Color(0.66f, 0.74f, 0.74f, 0.34f);
            Rect[] rooms =
            {
                new Rect(center.x - radius * 0.66f, center.y - radius * 0.55f, radius * 0.47f, radius * 0.34f),
                new Rect(center.x - radius * 0.12f, center.y - radius * 0.72f, radius * 0.49f, radius * 0.27f),
                new Rect(center.x + radius * 0.26f, center.y - radius * 0.36f, radius * 0.38f, radius * 0.50f),
                new Rect(center.x - radius * 0.45f, center.y - radius * 0.08f, radius * 0.47f, radius * 0.42f),
                new Rect(center.x - radius * 0.20f, center.y + radius * 0.38f, radius * 0.55f, radius * 0.28f)
            };
            for (int i = 0; i < rooms.Length; i++)
            {
                CardDraw.RoundedFrame(rooms[i], room, wall, 1f * scale, Mathf.Max(1f, scale));
            }
        }

        private static void DrawRadarPlayers(PlayerInfo local, Vector3 localPosition, Vector2 center,
            float radius, float cosine, float sine, float scale)
        {
            List<PlayerInfo> players = PlayerUpdate.EntityList;
            if (players == null)
                return;

            for (int i = 0; i < players.Count; i++)
            {
                PlayerInfo player = players[i];
                if (player == null || player._entity == null || player.IsDead || player.Id == local.Id)
                    continue;

                Vector3 world = SSJJMath.VectorCoordConverter.SsjjToUnity(player.Position);
                Vector3 delta = world - localPosition;
                float rx = delta.x * cosine - delta.z * sine;
                float ry = delta.x * sine + delta.z * cosine;
                Vector2 offset = Vector2.ClampMagnitude(new Vector2(rx, -ry) * (0.033f * scale), radius * 0.82f);
                Color marker = player.HasC4 ? TGold : TeamColor(player.Team);
                Vector2 point = center + offset;
                Rect outer = new Rect(point.x - 4.2f * scale, point.y - 4.2f * scale,
                    8.4f * scale, 8.4f * scale);
                Rect inner = new Rect(point.x - 2.8f * scale, point.y - 2.8f * scale,
                    5.6f * scale, 5.6f * scale);
                CardDraw.RoundedRect(outer, new Color(0f, 0f, 0f, 0.85f), outer.width * 0.5f);
                CardDraw.RoundedRect(inner, marker, inner.width * 0.5f);
            }
        }

        private void DrawTopCounter(PlayerInfo local, float scale)
        {
            scale *= 1.5f; // 放大顶部栏目
            const int Slots = 5;
            float slotW = 40f * scale;
            float slotH = 42f * scale;
            float centerW = 84f * scale;
            float totalW = Slots * slotW * 2f + centerW;
            float x = (Screen.width - totalW) * 0.5f;
            float y = 2f * scale;
            int avatarIndex = 0;

            // SSJJ team 2 is the blue side and team 1 is the red side.
            for (int i = 0; i < Slots; i++)
            {
                PlayerInfo player = GetSlot(_team2, Slots - 1 - i);
                DrawPlayerSlot(new Rect(x + i * slotW, y, slotW - scale, slotH), player, CtBlue,
                    PlayerAvatar(player, ref avatarIndex), scale);
            }

            Rect center = new Rect(x + Slots * slotW, y, centerW, 60f * scale);
            GetScore(out int team1Score, out int team2Score);
            GetRoundClock(out int minutes, out int seconds, out bool bombActive);
            Fill(new Rect(center.x, center.y, center.width, 47f * scale), new Color(0f, 0f, 0f, 0.918f));
            Text(new Rect(center.x, center.y, center.width, 27f * scale), $"{minutes}:{seconds:00}",
                FontSize(21f, scale), TextAnchor.MiddleCenter, bombActive ? TGold : TextMain, true);
            Fill(new Rect(center.x, center.y + 28f * scale, center.width * 0.5f, 19f * scale), Hex(0x7289A3, 0.86f));
            Fill(new Rect(center.center.x, center.y + 28f * scale, center.width * 0.5f, 19f * scale), Hex(0x916D41, 0.86f));
            Text(new Rect(center.x, center.y + 27f * scale, center.width * 0.5f, 20f * scale), team2Score.ToString(),
                FontSize(15f, scale), TextAnchor.MiddleCenter, Color.white, true);
            Text(new Rect(center.center.x, center.y + 27f * scale, center.width * 0.5f, 20f * scale), team1Score.ToString(),
                FontSize(15f, scale), TextAnchor.MiddleCenter, Color.white, true);

            for (int i = 0; i < Slots; i++)
            {
                PlayerInfo player = GetSlot(_team1, i);
                DrawPlayerSlot(new Rect(center.xMax + i * slotW, y, slotW - scale, slotH), player, TGold,
                    PlayerAvatar(player, ref avatarIndex), scale);
            }

            int team1Alive = Alive(_team2);
            int team2Alive = Alive(_team1);
            Text(new Rect(center.x - 55f * scale, center.y + 45f * scale, 52f * scale, 14f * scale),
                team1Alive.ToString(), FontSize(10f, scale), TextAnchor.MiddleRight, CtBlue, true);
            Text(new Rect(center.xMax + 3f * scale, center.y + 45f * scale, 52f * scale, 14f * scale),
                team2Alive.ToString(), FontSize(10f, scale), TextAnchor.MiddleLeft, TGold, true);
        }

        private Texture2D PlayerAvatar(PlayerInfo player, ref int avatarIndex)
        {
            if (player == null)
                return null;
            Texture2D texture = _playerAvatars[avatarIndex % _playerAvatars.Length];
            avatarIndex++;
            return texture;
        }

        private void DrawTelemetry(float scale)
        {
            float width = 372f * scale;
            float height = 43f * scale;
            float y = Screen.width < 900 ? 62f * scale : 8f * scale;
            Rect panel = new Rect(Screen.width - width - 12f * scale, y, width, height);

            // 亚克力模糊背景
            CardDraw.RoundedRect(new Rect(panel.x + 2f * scale, panel.y + 3f * scale, panel.width, panel.height),
                new Color(0.02f, 0.03f, 0.04f, 0.75f), 9f * scale);
            CardDraw.RoundedFrame(panel, new Color(0.025f, 0.045f, 0.055f, 0.82f),
                new Color(0.84f, 0.94f, 1f, 0.24f), 9f * scale, Mathf.Max(1f, scale));
            CardDraw.RoundedRect(new Rect(panel.x + 8f * scale, panel.y + 3f * scale,
                panel.width - 16f * scale, Mathf.Max(1f, scale)), new Color(1f, 1f, 1f, 0.34f), 0.5f);

            float x = panel.x + 12f * scale;
            float separatorY = panel.y + 10f * scale;
            float separatorH = panel.height - 20f * scale;
            int fps = Mathf.Clamp(Mathf.RoundToInt(1f / Mathf.Max(0.0001f, _smoothedDelta)), 0, 999);

            DrawSignalBars(new Rect(x, panel.y + 14f * scale, 17f * scale, 15f * scale), HudGold, scale);
            Text(new Rect(x + 22f * scale, panel.y, 62f * scale, panel.height), $"{fps} FPS",
                FontSize(11f, scale), TextAnchor.MiddleLeft, TextMain, true);
            x += 92f * scale;
            Fill(new Rect(x, separatorY, Mathf.Max(1f, scale), separatorH), new Color(1f, 1f, 1f, 0.16f));
            Text(new Rect(x + 10f * scale, panel.y, 87f * scale, panel.height), $"PING {_localPing} MS",
                FontSize(11f, scale), TextAnchor.MiddleLeft, _localPing > 100 ? Critical : TextMain, true);
            x += 105f * scale;
            Fill(new Rect(x, separatorY, Mathf.Max(1f, scale), separatorH), new Color(1f, 1f, 1f, 0.16f));
            Text(new Rect(x + 10f * scale, panel.y, 108f * scale, panel.height),
                "BJT " + DateTime.UtcNow.AddHours(8).ToString("HH:mm:ss"), FontSize(11f, scale),
                TextAnchor.MiddleLeft, HudGold, true);

            float avatarSize = 34f * scale;
            Rect avatar = new Rect(panel.xMax - avatarSize - 5f * scale,
                panel.y + (panel.height - avatarSize) * 0.5f, avatarSize, avatarSize);
            CardDraw.RoundedFrame(new Rect(avatar.x - 1f * scale, avatar.y - 1f * scale,
                    avatar.width + 2f * scale, avatar.height + 2f * scale),
                new Color(0.08f, 0.1f, 0.11f, 0.92f), HudGold, avatarSize * 0.5f, Mathf.Max(1f, scale));
            if (_avatarTexture != null)
                GUI.DrawTexture(avatar, _avatarTexture, ScaleMode.ScaleToFit, true);
            else
                Text(avatar, "?", FontSize(16f, scale), TextAnchor.MiddleCenter, HudGold, true);
        }

        private static void DrawSignalBars(Rect rect, Color color, float scale)
        {
            float width = Mathf.Max(2f * scale, rect.width * 0.16f);
            float gap = Mathf.Max(1f * scale, rect.width * 0.08f);
            for (int i = 0; i < 4; i++)
            {
                float height = rect.height * (0.35f + i * 0.2f);
                Fill(new Rect(rect.x + i * (width + gap), rect.yMax - height, width, height),
                    Alpha(color, 0.72f + i * 0.08f));
            }
        }

        private void DrawPlayerSlot(Rect rect, PlayerInfo player, Color teamColor, Texture2D avatar, float scale)
        {
            bool alive = player != null && !player.IsDead;
            Fill(rect, new Color(0f, 0f, 0f, alive ? 0.86f : 0.52f));
            if (player == null)
            {
                Stroke(rect, new Color(1f, 1f, 1f, 0.14f), Mathf.Max(1f, scale));
                return;
            }

            if (avatar != null)
            {
                Color previous = GUI.color;
                GUI.color = alive ? Color.white : new Color(0.48f, 0.48f, 0.48f, 0.72f);
                GUI.DrawTexture(new Rect(rect.x + 2f * scale, rect.y + 2f * scale,
                    rect.width - 4f * scale, rect.height - 4f * scale), avatar, ScaleMode.ScaleAndCrop, true);
                GUI.color = previous;
            }
            else
            {
                string initial = Initial(player.PlayerName);
                Text(new Rect(rect.x, rect.y + 2f * scale, rect.width, rect.height - 3f * scale), initial,
                    FontSize(18f, scale), TextAnchor.MiddleCenter, alive ? Color.white : TextSoft, true);
            }
            Stroke(rect, alive ? teamColor : new Color(0.4f, 0.4f, 0.4f, 0.7f), Mathf.Max(1f, scale));
            Fill(new Rect(rect.x, rect.yMax - 3f * scale, rect.width, 3f * scale),
                alive ? teamColor : new Color(0.4f, 0.4f, 0.4f, 0.7f));
            if (!alive)
                DrawSkull(new Vector2(rect.center.x, rect.yMax - 8f * scale), 5f * scale, TextSoft);
            else if (player.HasC4)
                Text(new Rect(rect.x, rect.yMax - 12f * scale, rect.width, 11f * scale), "B",
                    FontSize(8f, scale), TextAnchor.MiddleCenter, TGold, true);
        }

        private void DrawHealthAmmo(PlayerInfo local, float scale)
        {
            float y = Screen.height - 72f * scale;
            float centerX = Screen.width * 0.5f;
            float h = 53f * scale;
            Rect healthPlate = new Rect(centerX - 380f * scale, y, 220f * scale, h);
            Rect ammoPlate = new Rect(centerX + 160f * scale, y, 220f * scale, h);
            Fill(new Rect(healthPlate.xMax, y + 34f * scale,
                centerX - 37f * scale - healthPlate.xMax, Mathf.Max(1f, scale)), HudGoldSoft);
            Fill(new Rect(centerX + 37f * scale, y + 34f * scale,
                ammoPlate.x - centerX - 37f * scale, Mathf.Max(1f, scale)), HudGoldSoft);

            int health = Mathf.Max(0, Mathf.CeilToInt(local.Hp));
            bool helmet = false;
            bool armorBody = false;
            try
            {
                helmet = local._entity.basicInfo.Current.ArmorHead;
                armorBody = local._entity.basicInfo.Current.ArmorBody;
            }
            catch { }

            Color healthColor = health <= 25 ? Critical : HudGold;
            Rect armorIcon = new Rect(healthPlate.x + 4f * scale, healthPlate.y + 3f * scale,
                42f * scale, 46f * scale);
            Text(armorIcon, "◇", FontSize(39f, scale), TextAnchor.MiddleCenter, healthColor, true);
            Text(new Rect(healthPlate.x + 48f * scale, healthPlate.y - 2f * scale, 82f * scale, healthPlate.height),
                health.ToString(), FontSize(42f, scale), TextAnchor.MiddleLeft, healthColor, true);

            if (armorBody || helmet)
            {
                Text(new Rect(healthPlate.x + 12f * scale, healthPlate.y + 21f * scale,
                    27f * scale, 19f * scale), "100", FontSize(10f, scale), TextAnchor.MiddleCenter, HudGold, true);
            }

            GetAmmo(out int clip, out int reserve, out int maxClip, out bool hasAmmo);

            if (hasAmmo)
            {
                Color ammoColor = maxClip > 0 && clip <= Mathf.Max(3, Mathf.CeilToInt(maxClip * 0.2f)) ? Critical : HudGold;
                Text(new Rect(ammoPlate.x + 10f * scale, ammoPlate.y - 2f * scale, 78f * scale, ammoPlate.height),
                    clip.ToString(), FontSize(42f, scale), TextAnchor.MiddleRight, ammoColor, true);
                Text(new Rect(ammoPlate.x + 95f * scale, ammoPlate.y + 3f * scale, 16f * scale, ammoPlate.height),
                    "/", FontSize(24f, scale), TextAnchor.MiddleCenter, HudGoldSoft, false);
                Text(new Rect(ammoPlate.x + 114f * scale, ammoPlate.y + 5f * scale, 52f * scale, ammoPlate.height),
                    reserve.ToString(), FontSize(25f, scale), TextAnchor.MiddleLeft, HudGold, false);
            }

            Vector2 center = new Vector2(centerX, y + 26f * scale);
            Rect emblem = new Rect(center.x - 35f * scale, center.y - 35f * scale,
                70f * scale, 70f * scale);
            CardDraw.RoundedRect(emblem, new Color(0.02f, 0.025f, 0.028f, 0.90f), emblem.width * 0.5f);
            DrawGuiCircle(center, 33f * scale, HudGold, Mathf.Max(1f, scale));
            DrawGuiCircle(center, 27f * scale, Alpha(HudGold, 0.45f), Mathf.Max(1f, scale));
            Text(new Rect(center.x - 22f * scale, center.y - 23f * scale, 44f * scale, 44f * scale),
                "★", FontSize(30f, scale), TextAnchor.MiddleCenter, HudGold, true);
        }

        private void DrawMoney(float scale)
        {
            Text(new Rect(20f * scale, Screen.height - 112f * scale, 230f * scale, 48f * scale),
                "$ 13+78 = 91", FontSize(30f, scale), TextAnchor.MiddleLeft, HudGold, true);
            Fill(new Rect(20f * scale, Screen.height - 69f * scale, 125f * scale, 1f),
                Alpha(HudGold, 0.5f));
        }

        private void DrawWeaponSelection(PlayerInfo local, float scale)
        {
            int current = Mathf.Clamp(_lastWeaponSlot, 1, 5);
            float rowH = 52f * scale;
            float x = Screen.width - 188f * scale;
            float y = Screen.height - 338f * scale;
            int[] types = { 1, 0, 2, 3, 3 };
            string[] names = { "ak47", "deagle", "knife", "grenade", "c4" };
            for (int slot = 1; slot <= 5; slot++)
            {
                bool selected = slot == current;
                Rect row = new Rect(x, y + (slot - 1) * rowH, 176f * scale, rowH - 4f * scale);
                if (selected)
                    Fill(row, new Color(0f, 0f, 0f, 1.0f)); // 背包100%不透明
                Color color = selected ? HudGold : Alpha(HudGold, 0.72f);
                Text(new Rect(row.x, row.y, 26f * scale, row.height), slot.ToString(),
                    FontSize(14f, scale), TextAnchor.MiddleCenter, color, true);
                DrawWeaponSilhouette(new Rect(row.x + 36f * scale, row.y + 8f * scale,
                    120f * scale, 31f * scale), types[slot - 1], names[slot - 1], color);
                if (selected)
                    Fill(new Rect(row.x + 31f * scale, row.yMax - 2f * scale, 130f * scale, 2f * scale), color);
            }
        }

        private void DrawKillFeed(PlayerInfo local, float scale)
        {
            float y = (Screen.width < 900 ? 118f : 72f) * scale;
            float right = Screen.width - 10f * scale;
            float now = Time.unscaledTime;

            for (int i = _notices.Count - 1; i >= 0; i--)
            {
                KillNotice notice = _notices[i];
                float age = now - notice.CreatedAt;
                float alpha = age <= NoticeLifetime - 1f ? 1f : Mathf.Clamp01(NoticeLifetime - age);
                int nameSize = FontSize(16f, scale);
                float killerW = Mathf.Min(Measure(notice.Killer, nameSize, true), 160f * scale);
                float victimW = Mathf.Min(Measure(notice.Victim, nameSize, true), 160f * scale);
                float iconW = 66f * scale;
                float width = Mathf.Clamp(killerW + victimW + iconW + 42f * scale, 230f * scale, 470f * scale);
                Rect row = new Rect(right - width, y, width, 33f * scale);

                bool localKiller = NameEquals(notice.Killer, local.PlayerName);
                Fill(row, Alpha(BlurPanel, alpha));
                if (localKiller)
                    Stroke(row, Alpha(Hex(0xE10000, 1f), alpha), Mathf.Max(1f, 2f * scale));

                Text(new Rect(row.x + 10f * scale, row.y, killerW, row.height), notice.Killer,
                    nameSize, TextAnchor.MiddleLeft, Alpha(TeamColor(notice.KillerTeam), alpha), true);
                Rect weaponRect = new Rect(row.x + killerW + 18f * scale, row.y + 8f * scale,
                    iconW - 10f * scale, 18f * scale);
                DrawWeaponSilhouette(weaponRect, notice.WeaponType, notice.Weapon, Alpha(TextMain, alpha));

                float flagX = weaponRect.xMax + 1f * scale;
                if (notice.Wallshot)
                {
                    Text(new Rect(flagX, row.y, 16f * scale, row.height), "|", FontSize(13f, scale),
                        TextAnchor.MiddleCenter, Alpha(TextMain, alpha), true);
                    flagX += 12f * scale;
                }
                if (notice.Headshot)
                {
                    DrawHeadshot(new Vector2(flagX + 5f * scale, row.center.y), 5f * scale, Alpha(TextMain, alpha));
                    flagX += 14f * scale;
                }

                Text(new Rect(row.xMax - victimW - 10f * scale, row.y, victimW, row.height), notice.Victim,
                    nameSize, TextAnchor.MiddleRight, Alpha(TeamColor(notice.VictimTeam), alpha), true);
                y += 36f * scale;
            }
        }

        private void DrawKillCard(float scale)
        {
            if (_killCard == null)
                return;

            float age = Time.unscaledTime - _killCard.CreatedAt;
            if (age < 0f || age >= KillCardLifetime)
            {
                _killCard = null;
                return;
            }

            float enter = Mathf.Clamp01(age / 0.18f);
            float eased = 1f - Mathf.Pow(1f - enter, 3f);
            float fade = age <= 1.72f ? 1f : Mathf.Clamp01((KillCardLifetime - age) / 0.63f);
            int cardIndex = (_killCard.Combo - 1) % _killCardIcons.Length;
            Color accent = CardAccent(cardIndex, fade);
            Color gold = _killCard.Headshot ? Color.Lerp(accent, Color.white, 0.48f) : accent;
            float overshoot = 1f + Mathf.Sin(enter * Mathf.PI) * 0.09f;
            float cardScale = scale * Mathf.Lerp(0.72f, 1f, eased) * overshoot;
            float flipWidth = Mathf.Lerp(0.58f, 1f, Mathf.Sin(enter * Mathf.PI * 0.5f));
            float width = 104f * cardScale * flipWidth;
            float height = 138f * cardScale;
            float centerX = Screen.width * 0.5f;
            float centerY = Screen.height * 0.69f - (1f - eased) * 24f * scale;
            Rect card = new Rect(centerX - width * 0.5f, centerY - height * 0.5f, width, height);
            Color panel = new Color(0.018f, 0.024f, 0.027f, 0.94f * fade);

            float burst = Mathf.Clamp01(1f - age / (_killCard.Headshot ? 0.82f : 0.62f));
            float ringProgress = Mathf.Clamp01(age / 0.72f);
            float ringRadius = Mathf.Lerp(42f, _killCard.Headshot ? 116f : 94f, ringProgress) * scale;
            DrawGuiCircle(card.center, ringRadius, Alpha(gold, burst * 0.72f),
                Mathf.Max(1f, (_killCard.Headshot ? 3f : 2f) * scale * burst));
            for (int i = 0; i < 12; i++)
            {
                float angle = (i * 30f + age * 24f) * Mathf.Deg2Rad;
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                float inner = (51f + (i % 2) * 8f) * scale;
                float outer = inner + (24f + (i % 3) * 7f) * scale * burst;
                DrawGuiLine(card.center + direction * inner, card.center + direction * outer,
                    Alpha(gold, burst * 0.38f), Mathf.Max(1f, 1.5f * scale));
            }
            for (int i = 0; i < 10; i++)
            {
                float particleProgress = Mathf.Clamp01(age / (0.54f + (i % 3) * 0.08f));
                float angle = (i * 137.5f + cardIndex * 19f) * Mathf.Deg2Rad;
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                float distance = Mathf.Lerp(34f, 88f + (i % 4) * 7f, particleProgress) * scale;
                Vector2 point = card.center + direction * distance;
                float radius = Mathf.Lerp(3.2f, 0.7f, particleProgress) * scale;
                CardDraw.RoundedRect(new Rect(point.x - radius, point.y - radius,
                    radius * 2f, radius * 2f), Alpha(gold, (1f - particleProgress) * fade), radius);
            }

            Matrix4x4 previousMatrix = GUI.matrix;
            int stackCount = Mathf.Clamp(_killCard.Combo, 1, 4);
            float fanOpen = eased * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / 0.28f));
            for (int i = stackCount - 1; i >= 1; i--)
            {
                int slot = i == 1 ? -1 : i == 2 ? 1 : -2;
                float offsetX = slot * 42f * cardScale * fanOpen;
                float offsetY = Mathf.Abs(slot) * 6f * cardScale * fanOpen;
                Rect stacked = new Rect(card.x + offsetX, card.y + offsetY, card.width, card.height);
                int stackedIndex = ((_killCard.Combo - 1 - i) % _killCardIcons.Length + _killCardIcons.Length) %
                    _killCardIcons.Length;
                DrawFannedKillCard(stacked, stackedIndex, slot * 11f * fanOpen, fade, cardScale);
            }

            GUI.matrix = previousMatrix;
            GUIUtility.RotateAroundPivot(Mathf.Lerp(-5f, 0f, eased), card.center);
            CardDraw.RoundedRect(new Rect(card.x + 4f * cardScale, card.y + 7f * cardScale,
                card.width, card.height), new Color(0f, 0f, 0f, 0.42f * fade), 8f * cardScale);
            CardDraw.RoundedFrame(card, panel, gold, 8f * cardScale, Mathf.Max(1f, 1.8f * cardScale));
            Stroke(new Rect(card.x + 7f * cardScale, card.y + 7f * cardScale,
                card.width - 14f * cardScale, card.height - 14f * cardScale),
                Alpha(gold, 0.32f), Mathf.Max(1f, cardScale));

            Text(new Rect(card.x + 10f * cardScale, card.y + 7f * cardScale,
                22f * cardScale, 24f * cardScale), KillCardRanks[cardIndex], FontSize(17f, cardScale),
                TextAnchor.MiddleLeft, gold, true);
            if (_killCard.Combo > 1)
            {
                Text(new Rect(card.xMax - 43f * cardScale, card.y + 7f * cardScale,
                    32f * cardScale, 24f * cardScale), "x" + _killCard.Combo,
                    FontSize(14f, cardScale), TextAnchor.MiddleRight, gold, true);
            }

            Rect iconRect = new Rect(card.x + 21f * cardScale, card.y + 30f * cardScale,
                card.width - 42f * cardScale, card.width - 42f * cardScale);
            Texture2D icon = _killCardIcons[cardIndex];
            if (icon != null)
            {
                Color previous = GUI.color;
                GUI.color = gold;
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, true);
                GUI.color = previous;
            }
            else
            {
                DrawSkull(iconRect.center, iconRect.width * 0.34f, gold);
            }

            string title = _killCard.Headshot
                ? "HEADSHOT"
                : _killCard.Combo <= KillCardTitles.Length
                    ? KillCardTitles[_killCard.Combo - 1]
                    : "STREAK x" + _killCard.Combo;
            Text(new Rect(card.x + 7f * cardScale, card.yMax - 41f * cardScale,
                card.width - 14f * cardScale, 18f * cardScale), title,
                FontSize(12f, cardScale), TextAnchor.MiddleCenter, gold, true);
            Text(new Rect(card.x + 8f * cardScale, card.yMax - 24f * cardScale,
                card.width - 16f * cardScale, 14f * cardScale), Trim(_killCard.Victim, 13),
                FontSize(9f, cardScale), TextAnchor.MiddleCenter, Alpha(TextMain, fade * 0.78f), false);

            float sweep = Mathf.Clamp01((age - 0.10f) / 0.68f);
            if (sweep > 0f && sweep < 1f)
            {
                float sweepX = Mathf.Lerp(card.x - 16f * scale, card.xMax + 34f * scale, sweep);
                DrawGuiLine(new Vector2(sweepX, card.y + 8f * scale),
                    new Vector2(sweepX - 30f * scale, card.yMax - 8f * scale),
                    new Color(1f, 1f, 1f, Mathf.Sin(sweep * Mathf.PI) * 0.42f * fade),
                    Mathf.Max(2f, 5f * scale));
            }
            GUI.matrix = previousMatrix;
        }

        private void DrawFannedKillCard(Rect card, int cardIndex, float angle, float fade, float scale)
        {
            Matrix4x4 previous = GUI.matrix;
            Vector2 pivot = new Vector2(card.center.x, card.yMax - 8f * scale);
            GUIUtility.RotateAroundPivot(angle, pivot);

            Color accent = CardAccent(cardIndex, fade);
            CardDraw.RoundedRect(new Rect(card.x + 4f * scale, card.y + 7f * scale,
                card.width, card.height), new Color(0f, 0f, 0f, 0.34f * fade), 8f * scale);
            CardDraw.RoundedFrame(card, new Color(0.012f, 0.017f, 0.02f, 0.92f * fade),
                Alpha(accent, 0.9f), 8f * scale, Mathf.Max(1f, 1.5f * scale));
            Stroke(new Rect(card.x + 7f * scale, card.y + 7f * scale,
                card.width - 14f * scale, card.height - 14f * scale),
                Alpha(accent, 0.26f), Mathf.Max(1f, scale));
            Text(new Rect(card.x + 10f * scale, card.y + 7f * scale, 22f * scale, 24f * scale),
                KillCardRanks[cardIndex], FontSize(17f, scale), TextAnchor.MiddleLeft, accent, true);

            Rect iconRect = new Rect(card.x + 21f * scale, card.y + 31f * scale,
                card.width - 42f * scale, card.width - 42f * scale);
            Texture2D icon = _killCardIcons[cardIndex];
            if (icon != null)
            {
                Color previousColor = GUI.color;
                GUI.color = Alpha(accent, 0.82f);
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, true);
                GUI.color = previousColor;
            }
            else
            {
                DrawSkull(iconRect.center, iconRect.width * 0.34f, Alpha(accent, 0.82f));
            }

            Text(new Rect(card.x + 7f * scale, card.yMax - 35f * scale,
                card.width - 14f * scale, 17f * scale), KillCardTitles[cardIndex],
                FontSize(10f, scale), TextAnchor.MiddleCenter, accent, true);
            GUI.matrix = previous;
        }

        private void DrawBombStatus(float scale)
        {
            GetRoundClock(out _, out _, out bool bombActive);
            if (!bombActive)
                return;

            float pulse = 0.75f + Mathf.PingPong(Time.unscaledTime * 1.6f, 0.25f);
            Rect panel = new Rect(Screen.width * 0.5f - 55f * scale, 55f * scale, 110f * scale, 24f * scale);
            Fill(panel, new Color(0.15f, 0.015f, 0.01f, 0.82f));
            Text(panel, "BOMB", FontSize(13f, scale), TextAnchor.MiddleCenter,
                new Color(TGold.r, TGold.g, TGold.b, pulse), true);
        }

        private void DrawScoreboard(PlayerInfo local, float scale)
        {
            float width = Mathf.Min(Screen.width - 64f * scale, 980f * scale);
            float height = Mathf.Min(Screen.height - 70f * scale, 780f * scale);
            Rect board = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.34f));
            Fill(board, new Color(0.018f, 0.027f, 0.032f, 0.96f));
            Stroke(board, new Color(1f, 1f, 1f, 0.13f), Mathf.Max(1f, scale));

            GetScore(out int team1Score, out int team2Score);
            GetRoundClock(out int minutes, out int seconds, out _);
            Text(new Rect(board.x + 24f * scale, board.y + 13f * scale, board.width - 48f * scale, 30f * scale),
                "COMPETITIVE", FontSize(20f, scale), TextAnchor.MiddleLeft, Color.white, true);
            Text(new Rect(board.x, board.y + 10f * scale, board.width, 34f * scale),
                $"{minutes}:{seconds:00}", FontSize(22f, scale), TextAnchor.MiddleCenter, HudGold, true);
            Text(new Rect(board.x + 24f * scale, board.y + 13f * scale, board.width - 48f * scale, 30f * scale),
                "TAB", FontSize(12f, scale), TextAnchor.MiddleRight, TextSoft, false);

            float gap = 10f * scale;
            float tableY = board.y + 58f * scale;
            float tableH = (board.height - 76f * scale - gap) * 0.5f;
            Rect ct = new Rect(board.x + 16f * scale, tableY, board.width - 32f * scale, tableH);
            Rect terrorist = new Rect(ct.x, ct.yMax + gap, ct.width, tableH);
            DrawScoreTeam(ct, 2, "COUNTER-TERRORISTS", team2Score, CtBlue, local, scale);
            DrawScoreTeam(terrorist, 1, "TERRORISTS", team1Score, TGold, local, scale);
        }

        private void DrawScoreTeam(Rect rect, int team, string title, int score, Color color,
            PlayerInfo local, float scale)
        {
            Fill(new Rect(rect.x, rect.y, rect.width, 39f * scale), Alpha(color, 0.34f));
            Fill(new Rect(rect.x, rect.y, 4f * scale, 39f * scale), color);
            Text(new Rect(rect.x + 14f * scale, rect.y, rect.width - 70f * scale, 39f * scale), title,
                FontSize(15f, scale), TextAnchor.MiddleLeft, color, true);
            Text(new Rect(rect.x, rect.y, rect.width - 14f * scale, 39f * scale), score.ToString(),
                FontSize(24f, scale), TextAnchor.MiddleRight, Color.white, true);

            float headerY = rect.y + 42f * scale;
            Text(new Rect(rect.x + 14f * scale, headerY, rect.width - 325f * scale, 24f * scale), "PLAYER",
                FontSize(10f, scale), TextAnchor.MiddleLeft, TextSoft, true);
            Text(new Rect(rect.xMax - 300f * scale, headerY, 62f * scale, 24f * scale), "SCORE",
                FontSize(9f, scale), TextAnchor.MiddleCenter, TextSoft, true);
            Text(new Rect(rect.xMax - 235f * scale, headerY, 42f * scale, 24f * scale), "K",
                FontSize(10f, scale), TextAnchor.MiddleCenter, TextSoft, true);
            Text(new Rect(rect.xMax - 190f * scale, headerY, 42f * scale, 24f * scale), "A",
                FontSize(10f, scale), TextAnchor.MiddleCenter, TextSoft, true);
            Text(new Rect(rect.xMax - 145f * scale, headerY, 42f * scale, 24f * scale), "D",
                FontSize(10f, scale), TextAnchor.MiddleCenter, TextSoft, true);
            Text(new Rect(rect.xMax - 88f * scale, headerY, 70f * scale, 24f * scale), "PING",
                FontSize(9f, scale), TextAnchor.MiddleCenter, TextSoft, true);

            List<OneKillInfoData> all = null;
            try { all = Contexts.sharedInstance?.battleRoom?.playerInfo?.All; }
            catch { }
            if (all == null)
                return;

            int rowIndex = 0;
            float rowHeight = Mathf.Clamp((rect.height - 70f * scale) / 8f, 25f * scale, 38f * scale);
            for (int i = 0; i < all.Count && rowIndex < 8; i++)
            {
                OneKillInfoData data = all[i];
                if (data == null || data.Team != team)
                    continue;
                float y = headerY + 25f * scale + rowIndex * (rowHeight + 2f * scale);
                Rect row = new Rect(rect.x, y, rect.width, rowHeight);
                bool isLocal = data.Id == local.Id;
                Fill(row, isLocal ? Alpha(color, 0.25f) : new Color(1f, 1f, 1f, rowIndex % 2 == 0 ? 0.055f : 0.028f));
                float playerDot = Mathf.Min(10f * scale, row.height * 0.34f);
                CardDraw.RoundedRect(new Rect(row.x + 20f * scale - playerDot, row.center.y - playerDot,
                    playerDot * 2f, playerDot * 2f), isLocal ? color : new Color(0.25f, 0.29f, 0.31f, 1f), playerDot);
                Text(new Rect(row.x + 35f * scale, row.y, row.width - 350f * scale, row.height),
                    Trim(CleanName(data.PlayerName, "PLAYER"), 32), FontSize(13f, scale), TextAnchor.MiddleLeft,
                    isLocal ? Color.white : new Color(0.88f, 0.9f, 0.91f, 1f), isLocal);
                Text(new Rect(row.xMax - 300f * scale, row.y, 62f * scale, row.height), data.Score.ToString(),
                    FontSize(13f, scale), TextAnchor.MiddleCenter, isLocal ? color : Color.white, true);
                Text(new Rect(row.xMax - 235f * scale, row.y, 42f * scale, row.height), data.KillNum.ToString(),
                    FontSize(13f, scale), TextAnchor.MiddleCenter, Color.white, true);
                Text(new Rect(row.xMax - 190f * scale, row.y, 42f * scale, row.height), data.AssistsNum.ToString(),
                    FontSize(13f, scale), TextAnchor.MiddleCenter, TextSoft, false);
                Text(new Rect(row.xMax - 145f * scale, row.y, 42f * scale, row.height), data.BeKillNum.ToString(),
                    FontSize(13f, scale), TextAnchor.MiddleCenter, TextSoft, false);
                Text(new Rect(row.xMax - 88f * scale, row.y, 70f * scale, row.height), data.Ping.ToString(),
                    FontSize(11f, scale), TextAnchor.MiddleCenter, data.Ping > 100 ? Critical : TextSoft, false);
                rowIndex++;
            }
        }

        private void DrawPauseMenu(float scale)
        {
            Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0.01f, 0.016f, 0.02f, 0.84f));
            float panelW = Mathf.Clamp(360f * scale, 300f, Mathf.Max(300f, Screen.width * 0.34f));
            Rect panel = new Rect(0f, 0f, panelW, Screen.height);
            Fill(panel, new Color(0.018f, 0.027f, 0.032f, 0.99f));
            Fill(new Rect(panel.xMax - 3f * scale, 0f, 3f * scale, Screen.height), Alpha(CtBlue, 0.72f));

            Text(new Rect(34f * scale, 40f * scale, panelW - 100f * scale, 42f * scale), "反恐精英 2",
                FontSize(25f, scale), TextAnchor.MiddleLeft, Color.white, true);
            Text(new Rect(34f * scale, 82f * scale, panelW - 68f * scale, 24f * scale), "暂停菜单",
                FontSize(12f, scale), TextAnchor.MiddleLeft, CtBlue, true);
            if (PauseIconButton(new Rect(panelW - 62f * scale, 38f * scale, 34f * scale, 34f * scale), "×", scale))
                SetPauseOpen(false);

            float y = 148f * scale;
            if (PauseButton(new Rect(34f * scale, y, panelW - 68f * scale, 52f * scale), "继续游戏", false, scale))
            {
                TryInvokeOriginalResume();
                SetPauseOpen(false);
            }
            y += 64f * scale;
            if (PauseButton(new Rect(34f * scale, y, panelW - 68f * scale, 52f * scale), "游戏设置", _settingsOpen, scale))
                _settingsOpen = !_settingsOpen;

            if (_settingsOpen)
            {
                float gap = 26f * scale;
                float rightX = panelW + gap;
                float rightW = Mathf.Min(780f * scale, Screen.width - rightX - 26f * scale);
                Rect settingsRect;
                if (rightW < 490f * scale)
                {
                    float margin = 16f * scale;
                    settingsRect = new Rect(margin, 24f * scale,
                        Screen.width - margin * 2f, Screen.height - 48f * scale);
                }
                else
                {
                    settingsRect = new Rect(rightX, 34f * scale, rightW, Screen.height - 68f * scale);
                }
                DrawSettingsPanel(settingsRect, scale);
            }
        }

        private void DrawSettingsPanel(Rect rect, float scale)
        {
            Color panelColor = new Color(0.025f, 0.035f, 0.041f, 0.985f);
            Fill(rect, panelColor);
            Stroke(rect, new Color(1f, 1f, 1f, 0.11f), Mathf.Max(1f, scale));
            Fill(new Rect(rect.x, rect.y, rect.width, 3f * scale), CtBlue);

            float pad = 28f * scale;
            Text(new Rect(rect.x + pad, rect.y + 20f * scale, rect.width - 110f * scale, 34f * scale),
                "游戏设置", FontSize(23f, scale), TextAnchor.MiddleLeft, Color.white, true);
            Text(new Rect(rect.x + pad, rect.y + 54f * scale, rect.width - 56f * scale, 22f * scale),
                "显示与画面", FontSize(11f, scale), TextAnchor.MiddleLeft, CtBlue, true);
            if (PauseIconButton(new Rect(rect.xMax - 62f * scale, rect.y + 20f * scale, 34f * scale, 34f * scale), "×", scale))
            {
                _settingsOpen = false;
                SyncCursorState();
            }

            float y = rect.y + 94f * scale;
            DrawSettingsStatus(rect, ref y, scale);

            DrawSettingsSectionHeader(rect, ref y, "显示", scale);
            SettingsCycleRow(rect, ref y, "显示模式", Screen.fullScreen ? "全屏" : "窗口化", scale,
                delegate { Screen.fullScreen = false; }, delegate { Screen.fullScreen = true; });

            DrawSettingsSectionHeader(rect, ref y, "画面", scale);
            string[] qualityNames = QualitySettings.names;
            int quality = Mathf.Clamp(QualitySettings.GetQualityLevel(), 0, Mathf.Max(0, qualityNames.Length - 1));
            string qualityName = qualityNames.Length > 0 ? LocalizedQualityName(qualityNames[quality], quality, qualityNames.Length) : "默认";
            SettingsCycleRow(rect, ref y, "画质预设", qualityName, scale,
                delegate
                {
                    if (qualityNames.Length > 0)
                        QualitySettings.SetQualityLevel((quality - 1 + qualityNames.Length) % qualityNames.Length, true);
                },
                delegate
                {
                    if (qualityNames.Length > 0)
                        QualitySettings.SetQualityLevel((quality + 1) % qualityNames.Length, true);
                });
            SettingsToggleRow(rect, ref y, "垂直同步", QualitySettings.vSyncCount > 0, scale,
                delegate { QualitySettings.vSyncCount = QualitySettings.vSyncCount > 0 ? 0 : 1; });

            float footerY = rect.yMax - 58f * scale;
            if (SettingsButton(new Rect(rect.x + pad, footerY, 150f * scale, 36f * scale), "恢复默认", false, scale))
                RestoreDefaultVideoSettings();
            if (SettingsButton(new Rect(rect.xMax - pad - 126f * scale, footerY, 126f * scale, 36f * scale), "关闭", true, scale))
            {
                _settingsOpen = false;
                SyncCursorState();
            }
        }

        private void DrawSettingsStatus(Rect panel, ref float y, float scale)
        {
            Rect status = new Rect(panel.x + 28f * scale, y, panel.width - 56f * scale, 44f * scale);
            Fill(status, new Color(CtBlue.r, CtBlue.g, CtBlue.b, 0.10f));
            Fill(new Rect(status.x, status.y, 3f * scale, status.height), CtBlue);
            string mode = Screen.fullScreen ? "全屏" : "窗口化";
            string refresh = Screen.currentResolution.refreshRate > 0 ? Screen.currentResolution.refreshRate + " Hz" : "刷新率未知";
            Text(new Rect(status.x + 16f * scale, status.y, status.width * 0.42f, status.height),
                Screen.width + " × " + Screen.height, FontSize(12f, scale), TextAnchor.MiddleLeft, Color.white, true);
            Text(new Rect(status.x + status.width * 0.43f, status.y, status.width * 0.27f, status.height),
                refresh, FontSize(11f, scale), TextAnchor.MiddleCenter, TextSoft, false);
            Text(new Rect(status.xMax - status.width * 0.27f, status.y, status.width * 0.24f, status.height),
                mode, FontSize(11f, scale), TextAnchor.MiddleRight, CtBlue, true);
            y += status.height + 18f * scale;
        }

        private void DrawSettingsSectionHeader(Rect panel, ref float y, string title, float scale)
        {
            float pad = 28f * scale;
            Text(new Rect(panel.x + pad, y, panel.width - pad * 2f, 22f * scale), title,
                FontSize(12f, scale), TextAnchor.MiddleLeft, TextSoft, true);
            Fill(new Rect(panel.x + pad + 52f * scale, y + 11f * scale,
                Mathf.Max(20f * scale, panel.width - pad * 2f - 52f * scale), Mathf.Max(1f, scale)),
                new Color(1f, 1f, 1f, 0.10f));
            y += 30f * scale;
        }

        private void SettingsCycleRow(Rect panel, ref float y, string label, string value, float scale,
            Action previous, Action next)
        {
            float pad = 28f * scale;
            float rowH = 56f * scale;
            Rect row = new Rect(panel.x + pad, y, panel.width - pad * 2f, rowH);
            bool hovered = row.Contains(Event.current.mousePosition);
            Fill(row, hovered ? new Color(1f, 1f, 1f, 0.07f) : new Color(1f, 1f, 1f, 0.045f));
            Text(new Rect(row.x + 16f * scale, row.y, row.width * 0.42f, row.height), label,
                FontSize(13f, scale), TextAnchor.MiddleLeft, Color.white, true);

            float controlW = Mathf.Min(228f * scale, row.width * 0.48f);
            float controlX = row.xMax - controlW - 12f * scale;
            float controlY = row.y + 9f * scale;
            float controlH = row.height - 18f * scale;
            float arrowW = Mathf.Min(34f * scale, controlW * 0.2f);
            Rect previousRect = new Rect(controlX, controlY, arrowW, controlH);
            Rect valueRect = new Rect(previousRect.xMax + 4f * scale, controlY,
                controlW - arrowW * 2f - 8f * scale, controlH);
            Rect nextRect = new Rect(valueRect.xMax + 4f * scale, controlY, arrowW, controlH);
            Fill(previousRect, new Color(0f, 0f, 0f, 0.32f));
            Fill(valueRect, new Color(0f, 0f, 0f, 0.42f));
            Fill(nextRect, new Color(0f, 0f, 0f, 0.32f));
            Stroke(valueRect, Alpha(CtBlue, 0.62f), Mathf.Max(1f, scale));
            Text(previousRect, "‹", FontSize(20f, scale), TextAnchor.MiddleCenter, TextSoft, true);
            Text(valueRect, value, FontSize(11f, scale), TextAnchor.MiddleCenter, CtBlue, true);
            Text(nextRect, "›", FontSize(20f, scale), TextAnchor.MiddleCenter, TextSoft, true);
            if (GUI.Button(previousRect, GUIContent.none, GUIStyle.none))
                previous?.Invoke();
            if (GUI.Button(nextRect, GUIContent.none, GUIStyle.none))
                next?.Invoke();
            y += rowH + 8f * scale;
        }

        private void SettingsToggleRow(Rect panel, ref float y, string label, bool value, float scale, Action action)
        {
            float pad = 28f * scale;
            float rowH = 56f * scale;
            Rect row = new Rect(panel.x + pad, y, panel.width - pad * 2f, rowH);
            bool hovered = row.Contains(Event.current.mousePosition);
            Fill(row, hovered ? new Color(1f, 1f, 1f, 0.07f) : new Color(1f, 1f, 1f, 0.045f));
            Text(new Rect(row.x + 16f * scale, row.y, row.width * 0.5f, row.height), label,
                FontSize(13f, scale), TextAnchor.MiddleLeft, Color.white, true);
            Text(new Rect(row.xMax - 190f * scale, row.y, 86f * scale, row.height), value ? "开启" : "关闭",
                FontSize(11f, scale), TextAnchor.MiddleRight, value ? CtBlue : TextSoft, true);

            Rect toggle = new Rect(row.xMax - 72f * scale, row.y + 17f * scale, 44f * scale, 22f * scale);
            Color track = value ? Alpha(CtBlue, 0.85f) : new Color(1f, 1f, 1f, 0.18f);
            CardDraw.RoundedRect(toggle, track, 11f * scale);
            float knobX = value ? toggle.xMax - 11f * scale : toggle.x + 11f * scale;
            CardDraw.RoundedRect(new Rect(knobX - 7f * scale, toggle.center.y - 7f * scale,
                14f * scale, 14f * scale), Color.white, 7f * scale);
            if (GUI.Button(row, GUIContent.none, GUIStyle.none))
                action?.Invoke();
            y += rowH + 8f * scale;
        }

        private bool SettingsButton(Rect rect, string label, bool accent, float scale)
        {
            bool hovered = rect.Contains(Event.current.mousePosition);
            bool clicked = GUI.Button(rect, GUIContent.none, GUIStyle.none);
            Color fill = accent ? (hovered ? Alpha(CtBlue, 0.92f) : Alpha(CtBlue, 0.72f))
                : (hovered ? new Color(1f, 1f, 1f, 0.10f) : new Color(1f, 1f, 1f, 0.055f));
            Fill(rect, fill);
            Stroke(rect, accent ? Alpha(CtBlue, 0.95f) : new Color(1f, 1f, 1f, 0.15f), Mathf.Max(1f, scale));
            Text(rect, label, FontSize(11f, scale), TextAnchor.MiddleCenter, accent ? Color.white : TextSoft, true);
            return clicked;
        }

        private bool PauseIconButton(Rect rect, string icon, float scale)
        {
            bool hovered = rect.Contains(Event.current.mousePosition);
            bool clicked = GUI.Button(rect, GUIContent.none, GUIStyle.none);
            Fill(rect, hovered ? new Color(1f, 1f, 1f, 0.12f) : new Color(1f, 1f, 1f, 0.055f));
            Stroke(rect, new Color(1f, 1f, 1f, 0.14f), Mathf.Max(1f, scale));
            Text(rect, icon, FontSize(20f, scale), TextAnchor.MiddleCenter, Color.white, false);
            return clicked;
        }

        private void RestoreDefaultVideoSettings()
        {
            Screen.fullScreen = _defaultFullscreen;
            QualitySettings.vSyncCount = _defaultVsync;
            string[] qualityNames = QualitySettings.names;
            if (qualityNames != null && qualityNames.Length > 0)
                QualitySettings.SetQualityLevel(Mathf.Clamp(_defaultQuality, 0, qualityNames.Length - 1), true);
        }

        private static string LocalizedQualityName(string raw, int index, int count)
        {
            string normalized = (raw ?? string.Empty).Replace(" ", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
            if (normalized.Contains("verylow") || normalized == "fast") return "极低";
            if (normalized == "low" || normalized == "simple") return "低";
            if (normalized == "medium" || normalized == "good") return "中";
            if (normalized == "high" || normalized == "beautiful") return "高";
            if (normalized.Contains("veryhigh") || normalized == "ultra" || normalized == "fantastic") return "极高";
            if (count <= 1) return "默认";
            if (index == 0) return "极低";
            if (index == count - 1) return "极高";
            return "预设 " + (index + 1);
        }

        private bool PauseButton(Rect rect, string label, bool active, float scale)
        {
            bool clicked = GUI.Button(rect, GUIContent.none, GUIStyle.none);
            bool hovered = rect.Contains(Event.current.mousePosition);
            Fill(rect, active ? Alpha(CtBlue, 0.22f) : hovered ? new Color(1f, 1f, 1f, 0.075f) : new Color(1f, 1f, 1f, 0.045f));
            Fill(new Rect(rect.x, rect.y, 3f * scale, rect.height), active ? CtBlue : Alpha(CtBlue, 0.45f));
            Text(new Rect(rect.x + 18f * scale, rect.y, rect.width - 36f * scale, rect.height), label,
                FontSize(15f, scale), TextAnchor.MiddleLeft, active ? CtBlue : Color.white, true);
            return clicked;
        }

        private static void DrawCrosshair(PlayerInfo local, float scale)
        {
            try
            {
                WeaponEntity weapon = Contexts.sharedInstance?.weapon?.currentWeaponEntity;
                if (weapon != null && weapon.basicInfo.Info.WeaponType == 5 && local.Fov.IsZoom())
                    return;
            }
            catch { }

            Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            float gap = 4f * scale;
            float length = 8f * scale;
            float outline = Mathf.Max(2f, 3f * scale);
            float line = Mathf.Max(1f, 1.5f * scale);
            Color dark = new Color(0f, 0f, 0f, 0.9f);

            Fill(new Rect(center.x - gap - length, center.y - outline * 0.5f, length, outline), dark);
            Fill(new Rect(center.x + gap, center.y - outline * 0.5f, length, outline), dark);
            Fill(new Rect(center.x - outline * 0.5f, center.y - gap - length, outline, length), dark);
            Fill(new Rect(center.x - outline * 0.5f, center.y + gap, outline, length), dark);
            Fill(new Rect(center.x - gap - length, center.y - line * 0.5f, length, line), CrosshairGreen);
            Fill(new Rect(center.x + gap, center.y - line * 0.5f, length, line), CrosshairGreen);
            Fill(new Rect(center.x - line * 0.5f, center.y - gap - length, line, length), CrosshairGreen);
            Fill(new Rect(center.x - line * 0.5f, center.y + gap, line, length), CrosshairGreen);
        }

        private void BuildTeamLists(PlayerInfo local)
        {
            _team1.Clear();
            _team2.Clear();
            _playerIds.Clear();
            AddTeamPlayer(local);
            List<PlayerInfo> players = PlayerUpdate.EntityList;
            if (players != null)
            {
                for (int i = 0; i < players.Count; i++)
                    AddTeamPlayer(players[i]);
            }
            _team1.Sort(ComparePlayers);
            _team2.Sort(ComparePlayers);
        }

        private void AddTeamPlayer(PlayerInfo player)
        {
            if (player == null || player._entity == null || !_playerIds.Add(player.Id))
                return;
            if (player.Team == 1)
                _team1.Add(player);
            else if (player.Team == 2)
                _team2.Add(player);
        }

        private static int ComparePlayers(PlayerInfo left, PlayerInfo right)
        {
            int alive = left.IsDead.CompareTo(right.IsDead);
            return alive != 0 ? alive : left.Id.CompareTo(right.Id);
        }

        private static PlayerInfo GetSlot(List<PlayerInfo> players, int index)
        {
            return index >= 0 && index < players.Count ? players[index] : null;
        }

        private static int Alive(List<PlayerInfo> players)
        {
            int alive = 0;
            for (int i = 0; i < players.Count; i++)
            {
                if (!players[i].IsDead)
                    alive++;
            }
            return alive;
        }

        private void CaptureKillEvents()
        {
            GameRuleContext rules = Contexts.sharedInstance?.gameRule;
            if (rules == null || !rules.hasKillPlayerInfo || rules.killPlayerInfo?.InfoList == null)
                return;

            foreach (KillPlayerInfoData data in rules.killPlayerInfo.InfoList)
            {
                if (data == null)
                    continue;

                int identity = RuntimeHelpers.GetHashCode(data);
                if (!_seenKillObjects.Add(identity))
                    continue;

                AddNotice(new KillNotice
                {
                    Killer = CleanName(data.KillPlayerName, "WORLD"),
                    Victim = CleanName(data.BeKillPlayerName, "UNKNOWN"),
                    Weapon = CleanWeapon(data.WeaponName),
                    KillerTeam = data.KillTeam,
                    VictimTeam = data.BekillTeam,
                    WeaponType = data.WeaponType,
                    Headshot = data.Headshot,
                    Wallshot = data.Wallshot,
                    CreatedAt = Time.unscaledTime
                });
            }

            if (_seenKillObjects.Count > 512)
                _seenKillObjects.Clear();
        }

        private void TrackDeaths(PlayerInfo local)
        {
            _activePlayerIds.Clear();
            ObserveDeath(local, local);
            List<PlayerInfo> players = PlayerUpdate.EntityList;
            if (players != null)
            {
                for (int i = 0; i < players.Count; i++)
                    ObserveDeath(players[i], local);
            }

            _stalePlayerIds.Clear();
            foreach (KeyValuePair<int, bool> pair in _deadStates)
            {
                if (!_activePlayerIds.Contains(pair.Key))
                    _stalePlayerIds.Add(pair.Key);
            }
            for (int i = 0; i < _stalePlayerIds.Count; i++)
                _deadStates.Remove(_stalePlayerIds[i]);
        }

        private void ObserveDeath(PlayerInfo player, PlayerInfo local)
        {
            if (player == null || player._entity == null || player.Id <= 0)
                return;

            _activePlayerIds.Add(player.Id);
            bool isDead = player.IsDead;
            if (_deadStates.TryGetValue(player.Id, out bool wasDead) && !wasDead && isDead &&
                !HasRecentVictim(player.PlayerName))
            {
                string killer = "WORLD";
                try
                {
                    if (player._entity.hasKiller)
                        killer = CleanName(player._entity.killer.Killer, "WORLD");
                }
                catch { }

                PlayerInfo killerInfo = FindPlayer(killer, local);
                AddNotice(new KillNotice
                {
                    Killer = killer,
                    Victim = CleanName(player.PlayerName, "UNKNOWN"),
                    Weapon = killerInfo != null ? CleanWeapon(killerInfo.CurrentWeaponName) : "weapon",
                    KillerTeam = killerInfo?.Team ?? 0,
                    VictimTeam = player.Team,
                    WeaponType = killerInfo?.WeaponDetailType ?? -1,
                    CreatedAt = Time.unscaledTime
                });
            }
            _deadStates[player.Id] = isDead;
        }

        private static PlayerInfo FindPlayer(string name, PlayerInfo local)
        {
            if (NameEquals(local?.PlayerName, name))
                return local;
            List<PlayerInfo> players = PlayerUpdate.EntityList;
            if (players == null)
                return null;
            for (int i = 0; i < players.Count; i++)
            {
                if (NameEquals(players[i]?.PlayerName, name))
                    return players[i];
            }
            return null;
        }

        private bool HasRecentVictim(string victim)
        {
            float now = Time.unscaledTime;
            for (int i = _notices.Count - 1; i >= 0; i--)
            {
                if (now - _notices[i].CreatedAt <= 0.75f && NameEquals(_notices[i].Victim, victim))
                    return true;
            }
            return false;
        }

        private void AddNotice(KillNotice notice)
        {
            if (notice == null)
                return;

            float now = Time.unscaledTime;
            for (int i = _notices.Count - 1; i >= 0; i--)
            {
                KillNotice existing = _notices[i];
                if (now - existing.CreatedAt <= 0.35f && NameEquals(existing.Killer, notice.Killer) &&
                    NameEquals(existing.Victim, notice.Victim))
                {
                    _notices[i] = notice;
                    TriggerLocalKill(notice);
                    return;
                }
            }

            _notices.Add(notice);
            while (_notices.Count > MaxNotices)
                _notices.RemoveAt(0);
            TriggerLocalKill(notice);
        }

        private void TriggerLocalKill(KillNotice notice)
        {
            PlayerInfo local = PlayerUpdate.LocalEntity;
            if (local == null || !NameEquals(notice.Killer, local.PlayerName) ||
                NameEquals(notice.Victim, local.PlayerName))
                return;

            float now = Time.unscaledTime;
            if (now - _lastLocalKillAt <= 0.55f && NameEquals(_lastLocalKillVictim, notice.Victim))
                return;

            _localKillCombo = now - _lastLocalKillAt <= KillComboWindow
                ? Mathf.Min(_localKillCombo + 1, 9)
                : 1;
            _lastLocalKillAt = now;
            _lastLocalKillVictim = notice.Victim;
            _killCard = new KillCardState
            {
                Victim = notice.Victim,
                Headshot = notice.Headshot,
                Combo = _localKillCombo,
                CreatedAt = now
            };

            _suppressNativeKillUntil = now + 0.24f;
            SuppressNativeKillSound();
            if (now - _lastCustomKillSoundAt > 0.45f)
                TryPlayCustomKillSound(notice.Headshot, _localKillCombo, false);
        }

        private void SuppressNativeKillSound()
        {
            try
            {
                if (!_autoSoundReflectionReady)
                {
                    _autoSoundReflectionReady = true;
                    Type managerType = Type.GetType(
                        "Assets.Scripts.Utility.Sound.AutoSoundManager, Assembly-CSharp", false);
                    if (managerType == null)
                        return;

                    _autoSoundInstanceProperty = managerType.GetProperty("Instance",
                        BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                    _autoSoundCurrentItemField = managerType.GetField("_nowItemConfig",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                }

                object manager = _autoSoundInstanceProperty?.GetValue(null, null);
                object currentItem = manager == null ? null : _autoSoundCurrentItemField?.GetValue(manager);
                if (currentItem == null)
                    return;

                if (_autoSoundStopMethod == null ||
                    !_autoSoundStopMethod.DeclaringType.IsInstanceOfType(currentItem))
                {
                    _autoSoundStopMethod = currentItem.GetType().GetMethod("Stop",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                        null, Type.EmptyTypes, null);
                }
                _autoSoundStopMethod?.Invoke(currentItem, null);
            }
            catch
            {
            }
        }

        private bool TryPlayCustomKillSound(bool headShot, int killNum, bool firstBlood)
        {
            if (_killAudioSource == null || _killAudioClip == null || _killAudioPlayOneShot == null)
                return false;

            try
            {
                _killAudioStop?.Invoke(_killAudioSource, null);
                float pitch = firstBlood
                    ? 1.1f
                    : headShot
                        ? 1.08f
                        : Mathf.Min(1.08f, 1f + Mathf.Max(0, killNum - 1) * 0.02f);
                _killAudioPitch?.SetValue(_killAudioSource, pitch, null);
                _killAudioPlayOneShot.Invoke(_killAudioSource,
                    new object[] { _killAudioClip, headShot ? 2.35f : 2.15f });
                _lastCustomKillSoundAt = Time.unscaledTime;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void TrackWeapon(PlayerInfo local)
        {
            int slot = local.CurrentWeaponId;
            string name = CleanWeapon(local.CurrentWeaponName);
            if (_lastWeaponSlot == slot && string.Equals(_lastWeaponName, name, StringComparison.Ordinal))
                return;
            _lastWeaponSlot = slot;
            _lastWeaponName = name;
            _weaponChangedAt = Time.unscaledTime;
        }

        private static void GetAmmo(out int clip, out int reserve, out int maxClip, out bool hasAmmo)
        {
            clip = 0;
            reserve = 0;
            maxClip = 0;
            hasAmmo = false;
            try
            {
                WeaponEntity weapon = Contexts.sharedInstance?.weapon?.currentWeaponEntity;
                if (weapon != null && weapon.hasClip)
                {
                    clip = Mathf.Max(0, weapon.clip.Clip);
                    reserve = Mathf.Max(0, weapon.clip.CarryClip);
                    maxClip = Mathf.Max(0, weapon.clip.MaxClip);
                    hasAmmo = maxClip > 0 || clip > 0 || reserve > 0;
                }
            }
            catch { }
        }

        private static void GetScore(out int team1, out int team2)
        {
            team1 = 0;
            team2 = 0;
            try
            {
                GameRuleContext rules = Contexts.sharedInstance?.gameRule;
                if (rules != null && rules.hasBroadcastScore)
                {
                    team1 = Mathf.RoundToInt(rules.broadcastScore.Team1Score);
                    team2 = Mathf.RoundToInt(rules.broadcastScore.Team2Score);
                }
            }
            catch { }
        }

        private static void GetRoundClock(out int minutes, out int seconds, out bool bombActive)
        {
            minutes = 0;
            seconds = 0;
            bombActive = false;
            try
            {
                GameRuleContext rules = Contexts.sharedInstance?.gameRule;
                if (rules != null && rules.hasC4State && rules.c4State.Active)
                {
                    bombActive = true;
                    int remaining = Mathf.Max(0, 35000 - rules.c4State.Time);
                    minutes = remaining / 60000;
                    seconds = remaining / 1000 % 60;
                    return;
                }

                BattleRoomContext room = Contexts.sharedInstance?.battleRoom;
                if (room == null || !room.hasSectionTime)
                    return;
                var time = room.sectionTime;
                minutes = Mathf.Max(0, time.Minutes);
                seconds = Mathf.Clamp(time.Seconds, 0, 59);
                if (minutes == 0 && seconds == 0 && time.SectionTime > 0)
                {
                    int raw = Mathf.Max(0, time.SectionTime - time.Passed);
                    int totalSeconds = raw > 1000 ? raw / 1000 : raw;
                    minutes = totalSeconds / 60;
                    seconds = totalSeconds % 60;
                }
            }
            catch { }
        }

        private static int GetMoney(PlayerInfo local)
        {
            try
            {
                BattleRoomContext room = Contexts.sharedInstance?.battleRoom;
                if (room != null && room.hasPlayerInfo &&
                    room.playerInfo.PlayerInfoDatas.TryGetValue(local._entity.basicInfo.Cid, out PlayerInfoData data))
                    return Mathf.Max(0, data.Money);
            }
            catch { }
            return 0;
        }

        private static int GetLocalPing(PlayerInfo local)
        {
            try
            {
                BattleRoomContext room = Contexts.sharedInstance?.battleRoom;
                List<OneKillInfoData> players = room != null && room.hasPlayerInfo
                    ? room.playerInfo.All
                    : null;
                if (players == null)
                    return 0;
                for (int i = 0; i < players.Count; i++)
                {
                    OneKillInfoData player = players[i];
                    if (player != null && (player.Id == local.Id || NameEquals(player.PlayerName, local.PlayerName)))
                        return Mathf.Max(0, player.Ping);
                }
            }
            catch { }
            return 0;
        }

        private static void DrawWeaponSilhouette(Rect rect, int weaponType, string name, Color color)
        {
            string lower = name?.ToLowerInvariant() ?? string.Empty;
            bool knife = weaponType == 2 || lower.Contains("knife") || lower.Contains("sword");
            bool grenade = weaponType == 3 || lower.Contains("grenade") || lower.Contains("bomb");
            bool pistol = weaponType == 0 || lower.Contains("pistol");
            float line = Mathf.Max(1.5f, rect.height * 0.10f);

            if (knife)
            {
                DrawGuiLine(new Vector2(rect.x + rect.width * 0.15f, rect.yMax - rect.height * 0.12f),
                    new Vector2(rect.xMax - rect.width * 0.08f, rect.y + rect.height * 0.12f), color, line);
                DrawGuiLine(new Vector2(rect.x + rect.width * 0.08f, rect.yMax - rect.height * 0.22f),
                    new Vector2(rect.x + rect.width * 0.3f, rect.yMax - rect.height * 0.07f), color, line);
                return;
            }
            if (grenade)
            {
                float radius = rect.height * 0.28f;
                Vector2 center = rect.center + new Vector2(0f, rect.height * 0.08f);
                CardDraw.RoundedRect(new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f),
                    color, radius);
                Stroke(new Rect(rect.center.x - rect.width * 0.07f, rect.y + rect.height * 0.05f,
                    rect.width * 0.14f, rect.height * 0.28f), color, line * 0.55f);
                return;
            }
            if (pistol)
            {
                Fill(new Rect(rect.x + rect.width * 0.2f, rect.y + rect.height * 0.25f,
                    rect.width * 0.64f, rect.height * 0.22f), color);
                Fill(new Rect(rect.x + rect.width * 0.52f, rect.y + rect.height * 0.44f,
                    rect.width * 0.18f, rect.height * 0.43f), color);
                return;
            }

            Fill(new Rect(rect.x + rect.width * 0.17f, rect.y + rect.height * 0.29f,
                rect.width * 0.68f, rect.height * 0.19f), color);
            Fill(new Rect(rect.x, rect.y + rect.height * 0.34f, rect.width * 0.25f, rect.height * 0.10f), color);
            Fill(new Rect(rect.x + rect.width * 0.58f, rect.y + rect.height * 0.45f,
                rect.width * 0.12f, rect.height * 0.37f), color);
            Fill(new Rect(rect.x + rect.width * 0.37f, rect.y + rect.height * 0.45f,
                rect.width * 0.10f, rect.height * 0.29f), color);
        }

        private static void DrawHeadshot(Vector2 center, float radius, Color color)
        {
            float head = radius * 0.58f;
            Vector2 headCenter = center + new Vector2(0f, -radius * 0.35f);
            CardDraw.RoundedRect(new Rect(headCenter.x - head, headCenter.y - head, head * 2f, head * 2f), color, head);
            Fill(new Rect(center.x - radius * 0.8f, center.y + radius * 0.2f,
                radius * 1.6f, radius * 0.72f), color);
        }

        private static void DrawSkull(Vector2 center, float radius, Color color)
        {
            CardDraw.RoundedRect(new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f), color, radius);
            Fill(new Rect(center.x - radius * 0.55f, center.y + radius * 0.2f,
                radius * 1.1f, radius * 0.8f), color);
            float eye = radius * 0.16f;
            Vector2 left = center + new Vector2(-radius * 0.35f, -radius * 0.08f);
            Vector2 right = center + new Vector2(radius * 0.35f, -radius * 0.08f);
            CardDraw.RoundedRect(new Rect(left.x - eye, left.y - eye, eye * 2f, eye * 2f), Color.black, eye);
            CardDraw.RoundedRect(new Rect(right.x - eye, right.y - eye, eye * 2f, eye * 2f), Color.black, eye);
        }

        private static void DrawGuiCircle(Vector2 center, float radius, Color color, float thickness)
        {
            int size = Mathf.Max(8, Mathf.CeilToInt((radius + thickness) * 2f));
            int stroke = Mathf.Max(1, Mathf.CeilToInt(thickness));
            string key = size + ":" + stroke;
            if (!CircleRingTextures.TryGetValue(key, out Texture2D texture) || texture == null)
            {
                texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                texture.hideFlags = HideFlags.HideAndDontSave;
                Color32[] pixels = new Color32[size * size];
                float pixelCenter = (size - 1) * 0.5f;
                float halfStroke = stroke * 0.5f;
                for (int y = 0; y < size; y++)
                {
                    float dy = y - pixelCenter;
                    for (int x = 0; x < size; x++)
                    {
                        float dx = x - pixelCenter;
                        float distance = Mathf.Sqrt(dx * dx + dy * dy);
                        float alpha = Mathf.Clamp01(halfStroke + 0.75f - Mathf.Abs(distance - radius));
                        pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                    }
                }
                texture.SetPixels32(pixels);
                texture.Apply(false, true);
                texture.filterMode = FilterMode.Bilinear;
                CircleRingTextures[key] = texture;
            }

            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size), texture);
            GUI.color = previous;
        }

        private static void DrawGuiLine(Vector2 start, Vector2 end, Color color, float thickness)
        {
            Vector2 delta = end - start;
            float length = delta.magnitude;
            if (length <= 0.01f)
                return;
            Matrix4x4 previous = GUI.matrix;
            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            GUIUtility.RotateAroundPivot(angle, start);
            CardDraw.RoundedRect(new Rect(start.x, start.y - thickness * 0.5f, length, thickness),
                color, thickness * 0.5f);
            GUI.matrix = previous;
        }

        private static void Stroke(Rect rect, Color color, float thickness)
        {
            Fill(new Rect(rect.x, rect.y, rect.width, thickness), color);
            Fill(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            Fill(new Rect(rect.x, rect.y, thickness, rect.height), color);
            Fill(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        private static void Fill(Rect rect, Color color)
        {
            if (rect.width <= 0f || rect.height <= 0f || color.a <= 0f)
                return;
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, UiDraw.White);
            GUI.color = previous;
        }

        private void Text(Rect rect, string value, int fontSize, TextAnchor anchor, Color color, bool bold)
        {
            if (string.IsNullOrEmpty(value))
                return;
            GUIStyle style = Style(fontSize, anchor, bold);
            Color previous = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, color.a * 0.8f);
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), value, style);
            style.normal.textColor = color;
            GUI.Label(rect, value, style);
            style.normal.textColor = previous;
        }

        private GUIStyle Style(int fontSize, TextAnchor anchor, bool bold)
        {
            EnsureHudFont();
            int key = fontSize * 64 + (int)anchor * 2 + (bold ? 1 : 0);
            if (_styles.TryGetValue(key, out GUIStyle style) && style != null)
                return style;

            style = new GUIStyle(GUI.skin.label)
            {
                font = _hudFont,
                fontSize = fontSize,
                fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
                alignment = anchor,
                clipping = TextClipping.Clip,
                richText = false,
                padding = new RectOffset(0, 0, 0, 0)
            };
            _styles[key] = style;
            return style;
        }

        private void EnsureHudFont()
        {
            if (_hudFont != null || _fontAttempted || Time.unscaledTime < _fontReadyAt)
                return;

            _fontAttempted = true;
            try
            {
                _hudFont = Font.CreateDynamicFontFromOSFont(
                    new[] { "Bahnschrift", "Microsoft YaHei UI", "Arial Unicode MS" }, 18);
                if (_hudFont != null)
                {
                    _hudFont.hideFlags = HideFlags.HideAndDontSave;
                    _styles.Clear();
                }
            }
            catch
            {
                _hudFont = null;
            }
        }

        private float Measure(string value, int fontSize, bool bold)
        {
            if (string.IsNullOrEmpty(value))
                return 0f;
            return Style(fontSize, TextAnchor.MiddleLeft, bold).CalcSize(new GUIContent(value)).x;
        }

        private static Color TeamColor(int team)
        {
            return team == 1 ? TGold : team == 2 ? CtBlue : TextMain;
        }

        private static Color CardAccent(int index, float alpha)
        {
            Color color;
            switch (index)
            {
                case 1:
                    color = Hex(0xF09AC4, 1f);
                    break;
                case 2:
                    color = Hex(0x75D9E8, 1f);
                    break;
                case 3:
                    color = Hex(0xED695F, 1f);
                    break;
                default:
                    color = HudGold;
                    break;
            }
            color.a = Mathf.Clamp01(alpha);
            return color;
        }

        private static Color Alpha(Color color, float alpha)
        {
            color.a *= Mathf.Clamp01(alpha);
            return color;
        }

        private static Color Hex(int rgb, float alpha)
        {
            return new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f,
                (rgb & 255) / 255f, alpha);
        }

        private static float HudScale()
        {
            float heightScale = Screen.height / 1080f;
            float widthScale = Screen.width / 1920f;
            return Mathf.Clamp(Mathf.Min(heightScale, widthScale), 0.67f, 1.35f);
        }

        private static int FontSize(float baseSize, float scale)
        {
            return Mathf.Max(8, Mathf.RoundToInt(baseSize * scale));
        }

        private static string Initial(string value)
        {
            string clean = CleanName(value, "?");
            return clean.Length > 0 ? clean.Substring(0, 1).ToUpperInvariant() : "?";
        }

        private static string CleanName(string value, string fallback)
        {
            string clean = value?.Trim().TrimEnd('\0');
            return string.IsNullOrEmpty(clean) ? fallback : clean;
        }

        private static string CleanWeapon(string value)
        {
            string clean = value?.Trim().TrimEnd('\0');
            if (string.IsNullOrEmpty(clean))
                return "weapon";
            if (clean.StartsWith("weapon_", StringComparison.OrdinalIgnoreCase))
                clean = clean.Substring(7);
            return clean.Replace('_', ' ');
        }

        private static string Trim(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
                return value ?? string.Empty;
            return value.Substring(0, Mathf.Max(1, maxLength - 1)) + ".";
        }

        private static bool NameEquals(string left, string right)
        {
            return string.Equals(left?.TrimEnd('\0'), right?.TrimEnd('\0'), StringComparison.OrdinalIgnoreCase);
        }

        private void ResetRuntime()
        {
            _notices.Clear();
            _seenKillObjects.Clear();
            _deadStates.Clear();
            _activePlayerIds.Clear();
            _stalePlayerIds.Clear();
            _team1.Clear();
            _team2.Clear();
            _playerIds.Clear();
            _sessionPlayerId = 0;
            _lastWeaponSlot = -1;
            _lastWeaponName = string.Empty;
            _weaponChangedAt = -10f;
            _killCard = null;
            _lastLocalKillVictim = string.Empty;
            _lastLocalKillAt = -10f;
            _localKillCombo = 0;
        }
    }
}
