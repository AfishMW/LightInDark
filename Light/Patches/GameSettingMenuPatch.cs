using System;
using HarmonyLib;
using LightInDark.Core;
using LightInDark.Language;
using LightInDark.UI.Window;
using Light.UI.Window;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;
using UColor = UnityEngine.Color;

namespace Light.Patches;

/// <summary>
/// 原版规则编辑界面（GameSettingMenu）改造 —— 框架版（本轮只搭界面，功能下一轮）：
///  - 保留原版三个主按钮（样式/位置不动），仅把第三个按钮文本改为「MOD 设置」；
///  - 「游戏设置」页签保留原版内容不动；
///  - 「预设」页签替换为 4 按钮框架（各 1 张常态图 + 1 张高光图 + 下方文字 + 点击暂无效果）；
///  - 「MOD 设置」页签替换为 6 个彩色边框标签（不显示文字，边框中间放该标签的常态/悬停图），
///    点击标签在下方显示「XX页签暂未实现。」。
/// 美术资源按约定路径从嵌入资源加载（见 LoadTabAndPresetAssets），文件缺失时保持占位、不崩。
/// </summary>
[HarmonyPatch]
public static class GameSettingMenuPatch
{
    /// <summary>MOD 设置页 6 个分类标签：彩色边框，不显示文字，中间留空放图标。</summary>
    private static readonly (string Key, string Cn, UColor BorderColor)[] ModTabs =
    {
        ("MOD",   "MOD",  new UColor(0.55f, 0.55f, 0.55f, 1f)), // 灰
        ("CREWS", "船员",  new UColor(0.20f, 0.55f, 1.00f, 1f)), // 蓝
        ("IMP",   "内鬼",  new UColor(1.00f, 0.25f, 0.20f, 1f)), // 红
        ("NEU",   "中立",  new UColor(0.55f, 0.55f, 0.55f, 1f)), // 灰
        ("MODI",  "附加",  new UColor(1.00f, 0.85f, 0.20f, 1f)), // 黄
        ("GHOST", "幽灵",  new UColor(0.90f, 0.90f, 0.90f, 1f)), // 白
    };

    private static readonly string[] PresetLabels =
    {
        "加载预设", "保存预设", "导出预设为TXT文件", "导入预设",
    };

    // =====================================================================
    //  美术资源槽位
    //  预设页 4 按钮：各 1 张常态 + 1 张高光 = 8 张
    //  MOD 页 6 标签：各 1 张常态 + 1 张悬停 = 12 张
    //  约定路径（相对 Light.Resources，即 Light\Resources\ 下）：
    //    预设 → GUI\Preset\<名字>Normal.png / <名字>Hover.png
    //    标签 → GUI\RoleTab\<Key>Normal.png / <Key>Hover.png
    //  文件缺失时该项保持 null → 走原有暗色占位 / 空图标槽，不崩。
    // =====================================================================

    /// <summary>预设按钮 4 张常态图（顺序同 <see cref="PresetLabels"/>）。</summary>
    private static readonly Sprite?[] _presetNormal = new Sprite?[4];
    /// <summary>预设按钮 4 张高光图（顺序同上）。</summary>
    private static readonly Sprite?[] _presetHover = new Sprite?[4];
    /// <summary>MOD 标签 6 张常态图（顺序同 <see cref="ModTabs"/>）。</summary>
    private static readonly Sprite?[] _tabNormal = new Sprite?[6];
    /// <summary>MOD 标签 6 张悬停图（顺序同上）。</summary>
    private static readonly Sprite?[] _tabHover = new Sprite?[6];

    private static bool _assetsLoaded;

    /// <summary>预设按钮图片文件名（不带 Normal/Hover 后缀）。</summary>
    private static readonly string[] PresetFileNames =
    {
        "LoadPreset", "SavePreset", "ExportPreset", "ImportPreset",
    };

    /// <summary>
    /// 载入全部槽位图片。每个文件独立 try/catch 且缺失即留 null，
    /// 因此"只画好了其中几张"也能正常工作。
    /// </summary>
    private static void LoadTabAndPresetAssets()
    {
        if (_assetsLoaded) return;
        _assetsLoaded = true;

        for (int i = 0; i < PresetFileNames.Length && i < _presetNormal.Length; i++)
        {
            _presetNormal[i] = TryLoad($"GUI/Preset/{PresetFileNames[i]}Normal.png");
            _presetHover[i] = TryLoad($"GUI/Preset/{PresetFileNames[i]}Hover.png");
        }

        for (int i = 0; i < ModTabs.Length && i < _tabNormal.Length; i++)
        {
            _tabNormal[i] = TryLoad($"GUI/RoleTab/{ModTabs[i].Key}Normal.png");
            _tabHover[i] = TryLoad($"GUI/RoleTab/{ModTabs[i].Key}Hover.png");
        }

        int ok = 0;
        foreach (var s in _presetNormal) if (s != null) ok++;
        foreach (var s in _presetHover) if (s != null) ok++;
        foreach (var s in _tabNormal) if (s != null) ok++;
        foreach (var s in _tabHover) if (s != null) ok++;
        LightLogger.Log($"[GameSettingMenuPatch] 槽位图片载入完成：{ok}/20 张（缺失项保持占位）");
    }

    /// <summary>从嵌入资源按相对路径取 Sprite；不存在时静默返回 null（不打错误日志）。</summary>
    private static Sprite? TryLoad(string relativePath)
    {
        try
        {
            var asm = typeof(LightPlugin).Assembly;
            string resName = "Light.Resources." + relativePath.Replace('/', '.').Replace('\\', '.');
            using var stream = asm.GetManifestResourceStream(resName);
            if (stream == null) return null;      // 美术未提供：正常情况，不报错

            byte[] bytes = new byte[stream.Length];
            stream.Read(bytes, 0, bytes.Length);

            var tex = new Texture2D(2, 2, TextureFormat.ARGB32, false);
            if (!ImageConversion.LoadImage(tex, bytes, false)) return null;
            tex.wrapMode = TextureWrapMode.Clamp;

            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f), 100f);
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[GameSettingMenuPatch.TryLoad] {relativePath} 载入失败：{ex.Message}");
            return null;
        }
    }

    // ---- 尺寸 ----
    private const float TabWidth = 0.8f;
    private const float TabHeight = 0.8f;
    private const float TabSpacing = 1.05f;
    private const float TabBorderThickness = 0.06f;
    private const float IconSize = 0.45f;

    private const float PresetWidth = 1.5f;
    private const float PresetHeight = 0.75f;
    private const float PresetSpacingX = 1.7f;
    private const float PresetSpacingY = 1.0f;

    // ---- 运行时引用 ----
    private static GameObject? _presetsPage;
    private static GameObject? _modPage;
    private static TextMeshPro? _modPlaceholderText;

    // =====================================================================
    //  Harmony Patches
    // =====================================================================

    [HarmonyPatch(typeof(GameSettingMenu), nameof(GameSettingMenu.Start))]
    [HarmonyPostfix]
    public static void StartPostfix(GameSettingMenu __instance)
    {
        try
        {
            RenameThirdTabButton(__instance);
            BuildPages(__instance);

            // 构建完成后立刻应用一次，确保刚打开的页签不会同时露出原版内容
            ApplyPresetsVisibility();
            ApplyModVisibility();
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[GameSettingMenuPatch.StartPostfix]", ex);
        }
    }

    [HarmonyPatch(typeof(GameSettingMenu), nameof(GameSettingMenu.Close))]
    [HarmonyPostfix]
    public static void ClosePostfix()
    {
        // 菜单销毁：清空引用，下次打开重新构建
        _presetsPage = null;
        _modPage = null;
        _modPlaceholderText = null;
    }

    /// <summary>预设页启用时：只显示我们的框架页，隐藏原版预设内容。</summary>
    [HarmonyPatch(typeof(GamePresetsTab), nameof(GamePresetsTab.OnEnable))]
    [HarmonyPostfix]
    public static void PresetsOnEnablePostfix()
    {
        ApplyPresetsVisibility();
    }

    /// <summary>MOD 设置页（原版职业设置页）启用时：只显示我们的框架页。</summary>
    [HarmonyPatch(typeof(RolesSettingsMenu), nameof(RolesSettingsMenu.OnEnable))]
    [HarmonyPostfix]
    public static void RolesOnEnablePostfix()
    {
        ApplyModVisibility();
    }

    /// <summary>
    /// 原版 OpenMenu/ChangeTab 会走 OpenChancesTab，把 RoleChancesSettings 重新 SetActive(true)，
    /// 因此在其之后再压一次显示状态。
    /// </summary>
    [HarmonyPatch(typeof(RolesSettingsMenu), nameof(RolesSettingsMenu.OpenChancesTab),
        new Type[] { typeof(bool) })]
    [HarmonyPostfix]
    public static void RolesOpenChancesTabPostfix()
    {
        ApplyModVisibility();
    }

    // =====================================================================
    //  显示状态
    // =====================================================================

    private static void ApplyPresetsVisibility()
    {
        try
        {
            if (_presetsPage == null) return;
            var parent = _presetsPage.transform.parent;
            if (parent == null) return;
            HideChildrenExcept(parent, _presetsPage);
            _presetsPage.SetActive(true);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[GameSettingMenuPatch.ApplyPresetsVisibility]", ex);
        }
    }

    private static void ApplyModVisibility()
    {
        try
        {
            if (_modPage == null) return;
            var parent = _modPage.transform.parent;
            if (parent == null) return;
            HideChildrenExcept(parent, _modPage);
            _modPage.SetActive(true);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[GameSettingMenuPatch.ApplyModVisibility]", ex);
        }
    }

    /// <summary>隐藏 parent 的所有直接子对象，except 例外（保留显示）。</summary>
    private static void HideChildrenExcept(Transform parent, GameObject? except)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (child == null) continue;
            if (except != null && child == except.transform) continue;
            child.gameObject.SetActive(false);
        }
    }

    // =====================================================================
    //  第三个主按钮改名
    // =====================================================================

    private static void RenameThirdTabButton(GameSettingMenu menu)
    {
        try
        {
            // 按文本找（中文 / 英文），找不到再按对象名兜底
            var rolesBtn = FindButtonByText(menu, "角色设置")
                ?? FindButtonByText(menu, "Role Setting");
            if (rolesBtn == null)
            {
                var byName = FindChildRecursive(menu.transform, "RoleSettingsButton")
                    ?? FindChildRecursive(menu.transform, "RolesButton")
                    ?? FindChildRecursive(menu.transform, "RoleSettings");
                if (byName != null) rolesBtn = byName.GetComponent<PassiveButton>();
            }

            if (rolesBtn == null)
            {
                LightLogger.LogWarning("[GameSettingMenuPatch] 未找到第三个主按钮，跳过改名");
                return;
            }

            SetButtonText(rolesBtn, Language.Translate("gss.tab.mod", "MOD 设置"));
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[GameSettingMenuPatch.RenameThirdTabButton]", ex);
        }
    }

    // =====================================================================
    //  框架页构建
    // =====================================================================

    private static void BuildPages(GameSettingMenu menu)
    {
        try
        {
            if (_presetsPage != null || _modPage != null) return;

            // 预设页容器（原版预设 Tab）
            var presetsTab = menu.transform.Find("PresetsTab")
                ?? FindChildRecursive(menu.transform, "PresetsTab");
            if (presetsTab == null)
            {
                var gpt = menu.GetComponentInChildren<GamePresetsTab>(true);
                if (gpt != null) presetsTab = gpt.transform;
            }
            if (presetsTab != null)
                _presetsPage = BuildPresetsPage(presetsTab);
            else
                LightLogger.LogWarning("[GameSettingMenuPatch] 未找到预设页容器");

            // MOD 设置页容器（原版职业设置 Tab）
            var rolesTab = menu.transform.Find("RoleSettingsTab")
                ?? FindChildRecursive(menu.transform, "RoleSettingsTab");
            if (rolesTab == null)
            {
                var rsm = menu.GetComponentInChildren<RolesSettingsMenu>(true);
                if (rsm != null) rolesTab = rsm.transform;
            }
            if (rolesTab != null)
                _modPage = BuildModPage(rolesTab);
            else
                LightLogger.LogWarning("[GameSettingMenuPatch] 未找到 MOD 设置页容器");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[GameSettingMenuPatch.BuildPages]", ex);
        }
    }

    // ---------------------------------------------------------------------
    //  预设页：4 按钮框架
    // ---------------------------------------------------------------------

    private static GameObject BuildPresetsPage(Transform parent)
    {
        LoadTabAndPresetAssets();

        var page = NewUIObject("LightPresetsPage", parent, new Vector3(0f, 0.1f, -2.5f));

        for (int i = 0; i < PresetLabels.Length; i++)
        {
            int row = i / 2;
            int col = i % 2;
            var pos = new Vector2((col - 0.5f) * PresetSpacingX, (0.5f - row) * PresetSpacingY);
            CreatePresetButton(page.transform, PresetLabels[i], pos, i);
        }

        return page;
    }

    /// <summary>
    /// 预设页按钮：每个按钮用自己的常态图 + 高光图（4 组共 8 张），
    /// 资源缺失时退回暗色底 + 下方文字；点击本轮无效果。
    /// </summary>
    private static void CreatePresetButton(Transform parent, string label, Vector2 pos, int index)
    {
        var normal = index < _presetNormal.Length ? _presetNormal[index] : null;
        var hover = index < _presetHover.Length ? _presetHover[index] : null;

        var go = NewUIObject($"LightPresetButton_{label}", parent, new Vector3(pos.x, pos.y, 0f));

        // 图片槽位（空图时给一块暗色底，便于看到按钮范围）
        var img = NewUIObject("Image", go.transform, new Vector3(0f, 0f, 0f));
        var imgSr = img.AddComponent<SpriteRenderer>();
        imgSr.sprite = normal;                                   // 资源未提供 → null
        imgSr.drawMode = SpriteDrawMode.Sliced;
        imgSr.size = new Vector2(PresetWidth, PresetHeight);
        imgSr.color = normal != null
            ? UColor.white
            : new UColor(0.15f, 0.15f, 0.15f, 0.8f);

        // 下方文字：走统一模板（与主界面"本地/在线"卡片同字体 + 辉光白）
        // 有图时文字压在图片下缘内，省出纵向空间；无图时维持原留白。
        float textY = normal != null ? -PresetHeight * 0.5f + 0.18f : -PresetHeight * 0.5f - 0.2f;
        MenuTextTemplate.Create(go.transform, new Vector3(0f, textY, -0.1f), label, 1.1f);

        // 点击区域 + PassiveButton
        AddButtonArea(go, PresetWidth, PresetHeight);

        var pb = go.SetUpButton(true, null, null, null, false);
        pb.OnClick.AddListener((UnityAction)(() => { /* 本轮无效果 */ }));
        pb.OnMouseOver.AddListener((UnityAction)(() =>
        {
            if (hover != null) imgSr.sprite = hover;
        }));
        pb.OnMouseOut.AddListener((UnityAction)(() =>
        {
            if (normal != null) imgSr.sprite = normal;
        }));
    }

    // ---------------------------------------------------------------------
    //  MOD 设置页：6 彩色边框标签框架
    // ---------------------------------------------------------------------

    private static GameObject BuildModPage(Transform parent)
    {
        LoadTabAndPresetAssets();

        var page = NewUIObject("LightModSettingsPage", parent, new Vector3(0f, 1.2f, -2.5f));

        for (int i = 0; i < ModTabs.Length; i++)
        {
            int idx = i;
            CreateTabButton(page.transform, ModTabs[i].Key, ModTabs[i].BorderColor,
                new Vector2((i - 2.5f) * TabSpacing, 0.8f),
                (UnityAction)(() => OnModTabClicked(idx)), i);
        }

        // 占位提示（点击标签后显示「XX页签暂未实现。」）
        _modPlaceholderText = CloneText(page.transform, new Vector3(0f, -0.8f, -0.1f), "", 1.5f);

        return page;
    }

    /// <summary>
    /// 彩色边框标签：边框 = 4 条代码色块（白 sprite tint 成边框色），
    /// 中间放该标签自己的常态图 + 悬停图（6 组共 12 张，资源缺失时中间留空）。
    /// 标签上不显示文字。
    /// </summary>
    private static void CreateTabButton(Transform parent, string key, UColor borderColor,
        Vector2 pos, UnityAction onClick, int index)
    {
        var normal = index < _tabNormal.Length ? _tabNormal[index] : null;
        var hover = index < _tabHover.Length ? _tabHover[index] : null;

        var go = NewUIObject($"LightModTab_{key}", parent, new Vector3(pos.x, pos.y, 0f));

        float halfW = TabWidth * 0.5f;
        float halfH = TabHeight * 0.5f;
        float t = TabBorderThickness;

        // 上下左右四条边框
        MakeRect(go.transform, "Top", TabWidth, t, 0f, halfH - t * 0.5f, borderColor);
        MakeRect(go.transform, "Bottom", TabWidth, t, 0f, -halfH + t * 0.5f, borderColor);
        MakeRect(go.transform, "Left", t, TabHeight - t * 2f, -halfW + t * 0.5f, 0f, borderColor);
        MakeRect(go.transform, "Right", t, TabHeight - t * 2f, halfW - t * 0.5f, 0f, borderColor);

        // 中间图标槽位（图片未提供时为空，只有边框可见）
        var icon = NewUIObject("IconSlot", go.transform, Vector3.zero);
        var iconSr = icon.AddComponent<SpriteRenderer>();
        iconSr.sprite = normal;                                  // 资源未提供 → null
        iconSr.drawMode = SpriteDrawMode.Sliced;
        iconSr.size = new Vector2(IconSize, IconSize);

        // 点击区域 + PassiveButton
        AddButtonArea(go, TabWidth, TabHeight);

        var pb = go.SetUpButton(true, null, null, null, false);
        pb.OnClick.AddListener(onClick);
        pb.OnMouseOver.AddListener((UnityAction)(() =>
        {
            if (hover != null) iconSr.sprite = hover;
        }));
        pb.OnMouseOut.AddListener((UnityAction)(() =>
        {
            if (normal != null) iconSr.sprite = normal;
        }));
    }

    private static void OnModTabClicked(int index)
    {
        try
        {
            if (_modPlaceholderText == null) return;
            _modPlaceholderText.text = $"{ModTabs[index].Cn}页签暂未实现。";
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[GameSettingMenuPatch.OnModTabClicked]", ex);
        }
    }

    // =====================================================================
    //  UI 工具
    // =====================================================================

    private static GameObject NewUIObject(string name, Transform parent, Vector3 localPos)
    {
        var go = new GameObject(name);
        go.layer = LayerExpansion.GetUILayer();
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = Vector3.one;
        return go;
    }

    /// <summary>用白 sprite（Sliced）画一块纯色矩形（边框用）。</summary>
    private static void MakeRect(Transform parent, string name, float width, float height,
        float x, float y, UColor color)
    {
        var go = NewUIObject(name, parent, new Vector3(x, y, 0f));
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = VanillaAsset.WhiteSprite;                    // null 安全
        sr.drawMode = SpriteDrawMode.Sliced;
        sr.tileMode = SpriteTileMode.Continuous;
        sr.size = new Vector2(width, height);
        sr.color = color;
    }

    /// <summary>给按钮对象加点击/悬浮检测用的碰撞体（PassiveButton 依赖 Collider2D）。</summary>
    private static void AddButtonArea(GameObject go, float width, float height)
    {
        var col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.offset = Vector2.zero;
        col.size = new Vector2(width, height);
    }

    /// <summary>克隆原版标准文本预制体（带字体）；预制体不可用时返回 null，不崩。</summary>
    private static TextMeshPro? CloneText(Transform parent, Vector3 pos, string text, float fontSize)
    {
        var prefab = VanillaAsset.GetStandardTextPrefab();
        if (prefab == null) return null;

        var tmp = Object.Instantiate(prefab, parent);
        tmp.transform.localPosition = pos;
        tmp.fontSize = fontSize;
        tmp.color = UColor.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;
        tmp.raycastTarget = false;
        tmp.text = text;
        tmp.ForceMeshUpdate();
        return tmp;
    }

    // =====================================================================
    //  原版对象查找/文本工具
    // =====================================================================

    private static Transform? FindChildRecursive(Transform parent, string name)
    {
        try
        {
            if (parent == null) return null;
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name == name) return child;
                var r = FindChildRecursive(child, name);
                if (r != null) return r;
            }
            return null;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[GameSettingMenuPatch.FindChildRecursive]", ex);
            return null;
        }
    }

    private static PassiveButton? FindButtonByText(GameSettingMenu menu, string keyword)
    {
        try
        {
            var btns = menu.GetComponentsInChildren<PassiveButton>(true);
            foreach (var pb in btns)
            {
                if (pb == null) continue;
                var text = GetButtonText(pb);
                if (!string.IsNullOrEmpty(text) && text.Contains(keyword)) return pb;
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[GameSettingMenuPatch.FindButtonByText]", ex);
        }
        return null;
    }

    private static string GetButtonText(PassiveButton btn)
    {
        try
        {
            TextMeshPro? tmp = null;
            var fp = btn.transform.FindChild("FontPlacer");
            if (fp != null && fp.childCount > 0)
                tmp = fp.GetChild(0).GetComponent<TextMeshPro>();
            if (tmp == null)
                tmp = btn.GetComponentInChildren<TextMeshPro>(true);
            return tmp != null ? tmp.text : "";
        }
        catch { return ""; }
    }

    private static void SetButtonText(PassiveButton? btn, string text)
    {
        try
        {
            if (btn == null) return;
            TextMeshPro? tmp = null;
            var fp = btn.transform.FindChild("FontPlacer");
            if (fp != null && fp.childCount > 0)
                tmp = fp.GetChild(0).GetComponent<TextMeshPro>();
            if (tmp == null)
                tmp = btn.GetComponentInChildren<TextMeshPro>(true);
            if (tmp == null) return;

            tmp.text = text;
            // 关掉原版翻译组件，否则会被本地化文本覆盖
            var translators = btn.GetComponentsInChildren<TextTranslatorTMP>(true);
            foreach (var tr in translators)
                if (tr != null) tr.enabled = false;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[GameSettingMenuPatch.SetButtonText]", ex);
        }
    }
}
