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
            if (Config.HistoryWindow <= 0)
                Config.HistoryWindow = 200;
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

        public static void SaveConfig(string configName)
        {
            try
            {
                EnsureDirectory();
                string configPath = GetConfigPath(configName);
                File.WriteAllText(configPath, ExportToString(), Encoding.UTF8);
#if Debug_Log
                global::System.Console.WriteLine($"[Vape.Cfg] saved: {configPath}");
#endif
            }
            catch (Exception ex)
            {
#if Debug_Log
                global::System.Console.WriteLine($"[Vape.Cfg] save fail: {ex.Message}");
#else
                _ = ex;
#endif
            }
        }

        public static void LoadConfig(string configName)
        {
            try
            {
                string configPath = GetConfigPath(configName);
                if (!File.Exists(configPath))
                {
#if Debug_Log
                    global::System.Console.WriteLine($"[Vape.Cfg] missing: {configName}");
#endif
                    return;
                }

                ImportFromString(File.ReadAllText(configPath, Encoding.UTF8));
#if Debug_Log
                global::System.Console.WriteLine($"[Vape.Cfg] loaded: {configName}");
#endif
            }
            catch (Exception ex)
            {
#if Debug_Log
                global::System.Console.WriteLine($"[Vape.Cfg] load fail: {ex.Message}");
#else
                _ = ex;
#endif
            }
        }

        public static void DeleteConfig(string configName)
        {
            try
            {
                string configPath = GetConfigPath(configName);
                if (File.Exists(configPath))
                    File.Delete(configPath);
            }
            catch (Exception ex)
            {
#if Debug_Log
                global::System.Console.WriteLine($"[Vape.Cfg] delete fail: {ex.Message}");
#else
                _ = ex;
#endif
            }
        }

        public static string[] GetAllConfigNames()
        {
            try
            {
                EnsureDirectory();
                string[] files = Directory.GetFiles(ConfigDirectory);
                var names = new string[files.Length];
                for (int i = 0; i < files.Length; i++)
                    names[i] = Path.GetFileName(files[i]);
                return names;
            }
            catch
            {
                return Array.Empty<string>();
            }
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
