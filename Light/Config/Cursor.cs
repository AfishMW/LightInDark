using System;
using System.IO;
using LightInDark.Core;
using System.Text.Json;
using UnityEngine;
using Light.Utilities;

namespace Light.Config;

public static class Cursor
{
    public static int Index { get; private set; }
    private static string ConfigPath => Path.Combine(LightPlugin.LightUserDataPath, "Cursor.json");
    private const int DefaultCursorIndex = 1;
    private const int CursorSize = 32;
    private static readonly Texture2D? Cur_1 = LoadCursorTexture("Cursor/1.png");
    private static readonly Texture2D? Cur_2 = LoadCursorTexture("Cursor/2.png");

    public static void Initialize()
    {
        try
        {
            SetCursorIndex(DefaultCursorIndex);
            
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[Cursor.Initialize]", ex);
        }
    }
    public static bool? ChangeCursorFromIndex(int index)
    {
        return SetCursorIndex(index);
    }

    public static bool? SetCursorIndex(int index)
    {
        try
        {
            Texture2D? texture = index switch
            {
                1 => Cur_1,
                2 => Cur_2,
                _ => null
            };

            if (index < 0 || index > 2)
            {
                LightLogger.Log($"[Cursor] 未知索引： {index}");
                return false;
            }

            if (index > 0 && texture == null)
            {
                LightLogger.LogWarning($"[Cursor] 光标资源加载失败，索引：{index}");
                return null;
            }

            LightLogger.Log($"[DEBUG] SetCursor 前 index={index}, texture={(texture == null ? "null" : $"{texture.width}x{texture.height}, format={texture.format}, readable={texture.isReadable}")}, mode={CursorMode.Auto}");
            UnityEngine.Cursor.SetCursor(texture, Vector2.zero, CursorMode.Auto);
            LightLogger.Log($"[DEBUG] SetCursor 后 visible={UnityEngine.Cursor.visible}, lockState={UnityEngine.Cursor.lockState}");
            Index = index;
            SaveIndex(index);
            LightLogger.Log($"[Cursor] index {index}");
            return true;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[Cursor] 更换异常", ex);
            return null;
        }
    }

    private static Texture2D? LoadCursorTexture(string path)
    {
        Texture2D? source = ResourceHelper.LoadTexture(path);
        if (source == null)
            return null;

        if (source.width <= CursorSize && source.height <= CursorSize)
            return source;

        var resized = new Texture2D(CursorSize, CursorSize, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        for (int y = 0; y < CursorSize; y++)
        {
            float sourceY = (y + 0.5f) / CursorSize;
            for (int x = 0; x < CursorSize; x++)
            {
                float sourceX = (x + 0.5f) / CursorSize;
                resized.SetPixel(x, y, source.GetPixelBilinear(sourceX, sourceY));
            }
        }

        resized.Apply(false, false);
        LightLogger.Log($"[Cursor] 已完整缩放光标纹理 {path}: {source.width}x{source.height} -> {CursorSize}x{CursorSize}");
        return resized;
    }

    static int LoadIndexFromJson()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                var defaultData = new { index = DefaultCursorIndex };
                string json = JsonSerializer.Serialize(defaultData, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
                return DefaultCursorIndex;
            }

            string content = File.ReadAllText(ConfigPath);
            using var doc = JsonDocument.Parse(content);
            return doc.RootElement.TryGetProperty("index", out var el) ? el.GetInt32() : DefaultCursorIndex;
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[Cursor] 读取配置失败: {ex.Message}");
            return DefaultCursorIndex;
        }
    }

    /// <summary>
    /// 重新读取光标配置并应用（供 <see cref="LightSettings.ReloadConfig"/> 调用）。
    /// 注意：LoadIndexFromJson 之前**没有任何调用点**，所以光标索引一直是启动时的默认值；
    /// 这里把它接上，重载配置时光标就能按配置文件恢复。
    /// </summary>
    public static void Reload()
    {
        try
        {
            SetCursorIndex(LoadIndexFromJson());
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[Cursor.Reload] {ex.Message}");
        }
    }

    public static void SaveIndex(int index)
    {
        try
        {
            FileUtil.EnsureDirectoryExists(ConfigPath);
            var data = new { index };
            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[Cursor] 保存配置失败: {ex.Message}");
        }
    }

}