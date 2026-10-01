using System;
using LightInDark.Core;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Light.UI.Window;

/// <summary>
/// 主界面风格文本模板（可复用）。
/// 目的：模组自建界面里的文字统一用「主界面"本地/在线"卡片那套字体」+「辉光白」，
/// 避免每处各写一份字体/颜色。
/// 用法：
///     var tmp = MenuTextTemplate.Create(parent, localPos, "文本", 1.1f);
///     var tmp2 = MenuTextTemplate.Create(parent, localPos, "红字", 1.1f, Color.red);
/// </summary>
public static class MenuTextTemplate
{
    /// <summary>模组统一的"辉光白"（主界面按钮文字与本模板默认文字色共用这一个来源）。</summary>
    public static readonly Color GlowWhite = new Color(1f, 0.95f, 0.85f, 1f);

    private static TMP_FontAsset? _menuFont;
    private static bool _fontResolved;

    /// <summary>
    /// 主界面"本地/在线"卡片文字所用字体（懒加载）。
    /// 首次取不到会留待下次重试（例如主界面还没建好时）。
    /// </summary>
    public static TMP_FontAsset? MenuFont
    {
        get
        {
            if (_menuFont == null && !_fontResolved)
            {
                _menuFont = ResolveMenuFont();
                _fontResolved = _menuFont != null;
            }
            return _menuFont;
        }
    }

    /// <summary>
    /// 建一段"主界面风格"文字：原版标准文本预制体 + 主界面字体 + 辉光白 + 居中不换行。
    /// 预制体不可用时返回 null（不抛异常）。
    /// </summary>
    public static TextMeshPro? Create(Transform parent, Vector3 localPos, string text, float fontSize, Color? color = null)
    {
        try
        {
            var prefab = VanillaAsset.GetStandardTextPrefab();
            if (prefab == null)
            {
                LightLogger.LogWarning("[MenuTextTemplate] 原版标准文本预制体不可用，跳过建字");
                return null;
            }

            var tmp = Object.Instantiate(prefab, parent);
            tmp.transform.localPosition = localPos;

            var font = MenuFont;
            if (font != null) tmp.font = font;        // 关键：与主界面"本地/在线"同字体

            tmp.fontSize = fontSize;
            tmp.color = color ?? GlowWhite;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            tmp.raycastTarget = false;
            tmp.text = text;
            tmp.ForceMeshUpdate();
            return tmp;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[MenuTextTemplate.Create]", ex);
            return null;
        }
    }

    /// <summary>
    /// 解析主界面字体：优先取主界面里卡片上的 ModeText（"本地/在线"那两行字），
    /// 取不到再退到主界面任意文字，最后退到原版资产里的字体。
    /// </summary>
    private static TMP_FontAsset? ResolveMenuFont()
    {
        try
        {
            var mainUI = GameObject.Find("MainUI");
            if (mainUI != null)
            {
                var all = mainUI.GetComponentsInChildren<TextMeshPro>(true);

                foreach (var tmp in all)
                {
                    if (tmp == null || tmp.font == null) continue;
                    if (tmp.gameObject.name != "ModeText") continue;    // 卡片上的模式文字
                    LightLogger.Log($"[MenuTextTemplate] 采用主界面卡片字体：{tmp.font.name}（来源 ModeText）");
                    return tmp.font;
                }

                foreach (var tmp in all)
                {
                    if (tmp == null || tmp.font == null) continue;
                    LightLogger.Log($"[MenuTextTemplate] 未找到 ModeText，退用主界面文字字体：{tmp.font.name}（来源 {tmp.gameObject.name}）");
                    return tmp.font;
                }
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[MenuTextTemplate] 解析主界面字体失败：{ex.Message}");
        }

        try
        {
            var prefab = VanillaAsset.GetStandardTextPrefab();
            if (prefab != null && prefab.font != null)
            {
                LightLogger.Log($"[MenuTextTemplate] 退用原版标准字体：{prefab.font.name}");
                return prefab.font;
            }
        }
        catch { }

        try
        {
            var f = VanillaAsset.PreSpawnFont;
            if (f != null) LightLogger.Log($"[MenuTextTemplate] 退用 PreSpawnFont：{f.name}");
            return f;
        }
        catch { return null; }
    }
}
