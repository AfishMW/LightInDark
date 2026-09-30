using Color = LightInDark.Color;
using System.Text.Json;
using LightInDark.Core;
using System.Text.Json.Serialization;
using LightInDark;

using UnityEngine;

namespace Light.Config;

public static class MainColor
{
    static string jsonPath => Path.Combine(LightPlugin.LightUserDataPath, "ChatSettings.json");
    static JsonSerializerOptions _options = new() { WriteIndented = true };
    [Serializable]
    public class ModColorData
    {
        public string ChatColorARGB { get; set; } = "FFE566CC";
        public string ModMainColorARGB { get; set; } = "FFE566CC";
        public bool IsVanillaMode { get; set; }
        public string ChatText { get; set; } = "说点啥...";
        public bool ChatFollowPlayerColor { get; set; } = true;
        public string CustomChatColorOverride { get; set; } = "FFE566CC";
        
        public bool AlphaChatBackGround { get; set; } = true;
        [JsonIgnore]
        public Color chatColor => HexToColor(ChatColorARGB);
        [JsonIgnore]
        public Color modMainColor => HexToColor(ModMainColorARGB);
        [JsonIgnore]
        public Color? CustomChatFiledColor => TryParseHex(CustomChatColorOverride);

        private static Color? TryParseHex(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return null;
            if (ColorUtility.TryParseHtmlString("#" + hex, out UnityEngine.Color uc))
                return uc.ToLIDColor();
            return null;
        }

        private static Color HexToColor(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return Color.ModLightGoldenL;
            if (ColorUtility.TryParseHtmlString("#" + hex, out UnityEngine.Color uc))
                return uc.ToLIDColor();
            return Color.ModLightGoldenL;
        }
    }

    public static ModColorData LoadChatColor()
    {
        try
        {
            if (!File.Exists(jsonPath))
            {
                LightLogger.Log("未发现LID_ModColor.json，开始创建。");
                ModColorData data = new();
                Save(data);
                LightLogger.Log("创建完毕。");
                return data;
            }
            string json = File.ReadAllText(jsonPath);
            var result = JsonSerializer.Deserialize<ModColorData>(json, _options);
            if (result == null)
            {
                LightLogger.LogWarning("LID_ModColor.json 无效，使用默认值。");
                result = new ModColorData();
                Save(result);
            }
            return result;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[MainColor.LoadChatColor]", ex);
            return new ModColorData();
        }
    }

    public static void Save(ModColorData data)
    {
        try
        {
            FileUtil.EnsureDirectoryExists(jsonPath);
            string json = JsonSerializer.Serialize(data, _options);
            File.WriteAllText(jsonPath, json);
            LightLogger.Log("颜色配置已保存。");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[MainColor.Save]", ex);
        }
    }
}
