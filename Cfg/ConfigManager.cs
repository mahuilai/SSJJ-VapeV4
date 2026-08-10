using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Vape.Cfg
{
    public static class ConfigManager
    {
        private static readonly string ConfigDirectory = Path.Combine(Application.persistentDataPath, "VapeConfigs");
        private static readonly FieldInfo[] Fields = typeof(Config).GetFields(BindingFlags.Public | BindingFlags.Static);
        private static readonly Dictionary<string, FieldInfo> FieldMap = BuildFieldMap();
        private static readonly StringBuilder SharedBuilder = new StringBuilder(2048);
        private static string _lastPersistedPayload = string.Empty;
        private static string _autoSaveProfile = string.Empty;
        private static float _nextAutoSaveCheck;

        public static string LastStatus { get; private set; } = "Ready";
        public static bool LastOperationSucceeded { get; private set; } = true;
        public static string ConfigDirectoryPath => ConfigDirectory;

        private static Dictionary<string, FieldInfo> BuildFieldMap()
        {
            var map = new Dictionary<string, FieldInfo>(Fields.Length, StringComparer.Ordinal);
            foreach (var f in Fields)
                map[f.Name] = f;
            return map;
        }

        private static string GetConfigPath(string configName)
        {
            return Path.Combine(ConfigDirectory, configName);
        }

        public static bool TryNormalizeConfigName(string configName, out string normalized)
        {
            normalized = (configName ?? string.Empty).Trim();
            if (normalized.Length == 0 || normalized.Length > 64 || normalized == "." || normalized == ".." ||
                normalized.EndsWith(".", StringComparison.Ordinal) ||
                !string.Equals(Path.GetFileName(normalized), normalized, StringComparison.Ordinal))
            {
                SetStatus(false, "Invalid profile name");
                return false;
            }

            char[] invalid = Path.GetInvalidFileNameChars();
            if (normalized.IndexOfAny(invalid) >= 0)
            {
                SetStatus(false, "Invalid profile name");
                return false;
            }

            string stem = normalized.Split('.')[0].ToUpperInvariant();
            string[] reserved = { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
            for (int i = 0; i < reserved.Length; i++)
            {
                if (stem == reserved[i])
                {
                    SetStatus(false, "Reserved profile name");
                    return false;
                }
            }
            return true;
        }

        public static void EnsureDirectory()
        {
            if (!Directory.Exists(ConfigDirectory))
                Directory.CreateDirectory(ConfigDirectory);
        }

        public static string ExportToString()
        {
            SharedBuilder.Length = 0;
            foreach (var field in Fields)
            {
                object value = field.GetValue(null);
                SharedBuilder.Append(field.Name).Append('=').Append(SerializeValue(value)).Append('\n');
            }
            return SharedBuilder.ToString();
        }

        public static void ImportFromString(string payload)
        {
            if (string.IsNullOrEmpty(payload)) return;
            string[] lines = payload.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
                ApplyLine(line);
        }

        public static void ApplyLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            int eq = line.IndexOf('=');
            if (eq <= 0) return;

            string fieldName = line.Substring(0, eq).Trim();
            string valueStr = line.Substring(eq + 1).Trim();
            if (!FieldMap.TryGetValue(fieldName, out FieldInfo field)) return;

            try
            {
                object value = DeserializeValue(valueStr, field.FieldType);
                field.SetValue(null, value);
            }
            catch (Exception ex)
            {
#if Debug_Log
                global::System.Console.WriteLine($"[Vape.Cfg] field {fieldName} parse fail: {ex.Message}");
#else
                _ = ex;
#endif
            }
        }

        public static bool SaveConfig(string configName)
        {
            if (!TryNormalizeConfigName(configName, out string normalized))
                return false;

            try
            {
                EnsureDirectory();
                string payload = ExportToString();
                WriteAtomically(GetConfigPath(normalized), payload);
                _lastPersistedPayload = payload;
                _autoSaveProfile = normalized;
                SetStatus(true, "Saved: " + normalized);
#if Debug_Log
                global::System.Console.WriteLine($"[Vape.Cfg] saved: {GetConfigPath(normalized)}");
#endif
                return true;
            }
            catch (Exception ex)
            {
                SetStatus(false, "Save failed: " + ex.Message);
#if Debug_Log
                global::System.Console.WriteLine($"[Vape.Cfg] save fail: {ex.Message}");
#endif
                return false;
            }
        }

        public static bool LoadConfig(string configName)
        {
            if (!TryNormalizeConfigName(configName, out string normalized))
                return false;

            try
            {
                string configPath = GetConfigPath(normalized);
                if (!File.Exists(configPath))
                {
                    SetStatus(false, "Profile not found: " + normalized);
#if Debug_Log
                    global::System.Console.WriteLine($"[Vape.Cfg] missing: {normalized}");
#endif
                    return false;
                }

                ImportFromString(File.ReadAllText(configPath, Encoding.UTF8));
                _lastPersistedPayload = ExportToString();
                _autoSaveProfile = normalized;
                SetStatus(true, "Loaded: " + normalized);
#if Debug_Log
                global::System.Console.WriteLine($"[Vape.Cfg] loaded: {normalized}");
#endif
                return true;
            }
            catch (Exception ex)
            {
                SetStatus(false, "Load failed: " + ex.Message);
#if Debug_Log
                global::System.Console.WriteLine($"[Vape.Cfg] load fail: {ex.Message}");
#endif
                return false;
            }
        }

        public static bool DeleteConfig(string configName)
        {
            if (!TryNormalizeConfigName(configName, out string normalized))
                return false;

            try
            {
                string configPath = GetConfigPath(normalized);
                if (File.Exists(configPath))
                    File.Delete(configPath);
                SetStatus(true, "Deleted: " + normalized);
                return true;
            }
            catch (Exception ex)
            {
                SetStatus(false, "Delete failed: " + ex.Message);
#if Debug_Log
                global::System.Console.WriteLine($"[Vape.Cfg] delete fail: {ex.Message}");
#endif
                return false;
            }
        }

        public static void UpdateAutoSave(string configName)
        {
            if (Time.unscaledTime < _nextAutoSaveCheck)
                return;
            _nextAutoSaveCheck = Time.unscaledTime + 0.8f;

            if (!TryNormalizeConfigName(configName, out string normalized))
                return;

            string payload = ExportToString();
            if (string.Equals(_autoSaveProfile, normalized, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(_lastPersistedPayload, payload, StringComparison.Ordinal))
                return;

            try
            {
                EnsureDirectory();
                WriteAtomically(GetConfigPath(normalized), payload);
                _autoSaveProfile = normalized;
                _lastPersistedPayload = payload;
                SetStatus(true, "Autosaved: " + normalized);
            }
            catch (Exception ex)
            {
                SetStatus(false, "Autosave failed: " + ex.Message);
            }
        }

        public static string[] GetAllConfigNames()
        {
            try
            {
                EnsureDirectory();
                string[] files = Directory.GetFiles(ConfigDirectory);
                var names = new List<string>(files.Length);
                for (int i = 0; i < files.Length; i++)
                {
                    string name = Path.GetFileName(files[i]);
                    if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
                        continue;
                    names.Add(name);
                }
                names.Sort(StringComparer.OrdinalIgnoreCase);
                return names.ToArray();
            }
            catch (Exception ex)
            {
                SetStatus(false, "Profile scan failed: " + ex.Message);
                return Array.Empty<string>();
            }
        }

        private static void WriteAtomically(string configPath, string payload)
        {
            string tempPath = configPath + ".tmp";
            try
            {
                File.WriteAllText(tempPath, payload, Encoding.UTF8);
                if (!File.Exists(configPath))
                {
                    File.Move(tempPath, configPath);
                    return;
                }

                try
                {
                    File.Replace(tempPath, configPath, null);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Copy(tempPath, configPath, true);
                }
                catch (IOException)
                {
                    File.Copy(tempPath, configPath, true);
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        private static void SetStatus(bool success, string status)
        {
            LastOperationSucceeded = success;
            LastStatus = status ?? string.Empty;
        }

        public static string SerializeValue(object value)
        {
            if (value == null) return string.Empty;
            if (value is bool b) return b ? "1" : "0";
            if (value is int i) return i.ToString(CultureInfo.InvariantCulture);
            if (value is float f) return f.ToString(CultureInfo.InvariantCulture);
            if (value is double d) return d.ToString(CultureInfo.InvariantCulture);
            if (value is KeyCode kc) return ((int)kc).ToString(CultureInfo.InvariantCulture);
            if (value is string s) return s;
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        public static object DeserializeValue(string valueStr, Type targetType)
        {
            if (valueStr == null) valueStr = string.Empty;
            if (valueStr == "null") return targetType == typeof(string) ? null : GetDefault(targetType);

            if (targetType == typeof(bool))
                return valueStr == "1" || valueStr.Equals("true", StringComparison.OrdinalIgnoreCase) || valueStr == "True";
            if (targetType == typeof(int))
                return int.TryParse(valueStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) ? i : 0;
            if (targetType == typeof(float))
                return float.TryParse(valueStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : 0f;
            if (targetType == typeof(double))
                return double.TryParse(valueStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 0d;
            if (targetType == typeof(string))
                return valueStr;
            if (targetType == typeof(KeyCode))
            {
                if (int.TryParse(valueStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ki))
                    return (KeyCode)ki;
                if (Enum.TryParse(valueStr, true, out KeyCode kc))
                    return kc;
                return KeyCode.None;
            }
            if (targetType.IsEnum)
                return Enum.Parse(targetType, valueStr, true);

            try
            {
                return Convert.ChangeType(valueStr, targetType, CultureInfo.InvariantCulture);
            }
            catch
            {
                return GetDefault(targetType);
            }
        }

        private static object GetDefault(Type t) => t.IsValueType ? Activator.CreateInstance(t) : null;
    }
}
