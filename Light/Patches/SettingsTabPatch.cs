using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Light.UI.HudUI;
using Light.UI.Window;
using LightInDark;
using LightInDark.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UColor = UnityEngine.Color;
using Object = UnityEngine.Object;

namespace Light.Patches;

/// <summary>
/// 原版 <see cref="ToggleButtonBehaviour.ResetText"/> 会把文字重置成"翻译键 + 开/关"，
/// 我们克隆出来的自定义行 BaseText = 0（没有翻译键），必须跳过重置，
/// 否则一换语言 / 重新打开菜单，行文字就会被清成空字符串。
/// </summary>
[HarmonyPatch]
internal static class LightRowResetTextSkipPatch
{
    [HarmonyPatch(typeof(ToggleButtonBehaviour), nameof(ToggleButtonBehaviour.ResetText))]
    [HarmonyPrefix]
    public static bool ResetTextPrefix(ToggleButtonBehaviour __instance)
    {
        try { return __instance.BaseText != 0; }
        catch { return false; }
    }
}

/// <summary>
/// 在设置菜单里添加 "Light" 页签，并提供设置行的公开 API。
/// 做法与 Nebula 的 OptionMenuPatch 一致（原版设置行的模板是 ToggleButtonBehaviour）。
///
/// ── 怎么加一个设置项 ──────────────────────────────────────────────
/// 在插件加载后调用一次即可（推荐放在 LightPlugin.Load 里，或单独的注册方法里）：
///
///     // 1) 开关：点击在 启用/禁用 之间切换，行文字自动显示 "标签: 值"
///     SettingsTabPatch.AddToggleButton("显示队友信息", myData.ShowTeammateInfo,
///         on => { myData.ShowTeammateInfo = on; SaveSettings(); });
///
///     // 2) 多选：点击循环切换选项
///     SettingsTabPatch.AddSelectorButton("提示语言", new[] { "中文", "English", "跟随游戏" }, 0,
///         idx => { myData.Language = idx; SaveSettings(); });
///
///     // 3) 纯动作按钮
///     SettingsTabPatch.AddActionButton("重置所有设置", () => ResetAllSettings());
///
///     // 4) 打开模组自己的按钮窗口（HudUI）
///     SettingsTabPatch.AddHudWindowButton("调试工具", list =>
///     {
///         list.Add(new HudUI.ButtonOption("打印状态", () => DumpState()));
///         list.Add(new HudUI.ButtonOption("重载配置", () => ReloadConfig()));
///     });
///
///     // 5) 占位行（以后接功能）
///     SettingsTabPatch.AddEmptyButton("待实现：xxx");
///
/// ── 注意事项 ─────────────────────────────────────────────────────
///  * **只注册一次**：设置项数据存在静态列表里，界面每次打开/重建菜单时会自动按数据重建行；
///    重复注册会导致行数翻倍。
///  * 行按 2 列网格排布（顺序 = 注册顺序）。
///  * 开关的"开启"底色使用模组主色（原版绿 → 模组色）。
///  * 若在别处改了值（不是通过行点击），调用 <see cref="SettingsTabPatch.Refresh"/> 刷新显示。
///
/// ── 修复记录（2026-10-01）──────────────────────────────────────
///  1) 设置行原来是手搓裸 GameObject + TextMeshPro（没图、没命中区）→ 一行都看不见。
///     改为克隆原版设置行 ToggleButtonBehaviour。
///  2) 页签原来"替换 Tabs 最后一项"，被替换项不在数组里 → 原版 OpenTabGroup 永远不 Close 它
///     → 内容面板一直亮着（层层叠在一起）。改为只占它的**槽位**并把它的按钮与内容一起收起来。
///  3) 页签位置是 AspectPosition 按屏幕比例算的（实测两个菜单坐标不同）→ 手动设的位置会被算回去，
///     Light 落回 Graphics 上 → 重叠。现在重排前先禁用页签上的 AspectPosition。
/// </summary>
[HarmonyPatch(typeof(OptionsMenuBehaviour))]
public static class SettingsTabPatch
{
    // 行布局（数值取自 Nebula 的可用实现）
    private const float RowSpacingX = 1.3f;
    private const float RowTopY = 1.6f;
    private const float RowSpacingY = 0.5f;

    private static GameObject? _lightTabContent;
    private static GameObject? _rowTemplate;
    private static OptionsMenuBehaviour? _menu;
    private static readonly List<LightOptionButton> _buttons = new();
    private static readonly List<ToggleButtonBehaviour> _rows = new();
    private static int _lightTabIndex = -1;

    /// <summary>
    /// Light 页签被打开时触发（菜单刚建好 / 每次点击 Light 页签都会触发）。
    /// 供 <c>LightOptionsRegistry</c> 之类同步显示值用 —— 注册表依赖本类，所以这里只提供事件，避免反向依赖。
    /// </summary>
    public static event Action? LightTabOpened;

    [HarmonyPatch(nameof(OptionsMenuBehaviour.Start))]
    [HarmonyPostfix]
    public static void Postfix(OptionsMenuBehaviour __instance)
    {
        try
        {
            _menu = __instance;
            DetailPopup.Reset();      // 新菜单：清掉上一次的 detail 状态（含 Tab 固定）

            var tabs = new List<TabGroup>(__instance.Tabs.ToArray());
            if (tabs.Count == 0)
            {
                LightLogger.LogWarning("[SettingsTabPatch] 原版设置没有页签，跳过 Light 页签");
                return;
            }

            // ① 行模板：原版设置里已有的 ToggleButtonBehaviour
            _rowTemplate = FindRowTemplate(tabs);
            if (_rowTemplate == null)
            {
                LightLogger.LogWarning("[SettingsTabPatch] 找不到原版设置行模板(ToggleButtonBehaviour)，跳过 Light 页签（宁可不做，也不建一堆看不见的裸物体）");
                return;
            }

            // 顺手把原版页签打一份日志（位置对不上时按这个调）
            for (int i = 0; i < tabs.Count; i++)
            {
                var t = tabs[i];
                if (t == null) continue;
                var tp = t.transform.localPosition;
                LightLogger.Log($"[SettingsTabPatch] 原版页签[{i}] {t.gameObject.name} pos=({tp.x:F2},{tp.y:F2},{tp.z:F2}) content={(t.Content != null ? t.Content.name : "null")}");
            }

            // ② 行引用清零（容器按菜单实例复用，见下）
            _rows.Clear();

            // ③ 内容容器：**每个设置菜单各一个**。场景里可能同时存在"设置菜单"和"大厅设置菜单"，
            //    两者都会走这个 Postfix —— 所以在各自菜单下按名字查找/复用，绝不 Destroy 别人的容器。
            var existingContent = __instance.transform.FindChild("LightTabContent");
            if (existingContent != null)
            {
                _lightTabContent = existingContent.gameObject;
            }
            else
            {
                _lightTabContent = new GameObject("LightTabContent");
                _lightTabContent.transform.SetParent(__instance.transform);
                _lightTabContent.transform.localPosition = Vector3.zero;
                _lightTabContent.transform.localScale = Vector3.one;
                _lightTabContent.SetActive(false);
            }

            // ④ 页签按钮：**复用最后一个页签槽位**（它的位置一定在页签行内，不会跑到面板外）。
            // 注意：被替换掉的那一项虽然 GameObject 还在，但已经不在 Tabs 数组里，
            // 原版 OpenTabGroup 再也不会 Close() 它 —— 必须手动把它的内容面板关掉，
            // 否则就是用户看到的"很多界面叠在一起"。
            int slot = tabs.Count - 1;
            var replaced = tabs[slot];
            if (replaced != null)
            {
                // 内容面板要关（它已不在 Tabs 里，原版再也不会 Close 它）
                if (replaced.Content != null)
                {
                    replaced.Content.SetActive(false);
                    LightLogger.Log($"[SettingsTabPatch] 已关掉被替换页签 {replaced.gameObject.name} 的内容面板 {replaced.Content.name}");
                }

                // 按钮本体也要关：否则它和我们的 Light 页签会重叠在同一格上
                replaced.gameObject.SetActive(false);
                LightLogger.Log($"[SettingsTabPatch] 已隐藏被替换页签按钮 {replaced.gameObject.name}（让出第 {slot} 槽位给 Light）");
            }

            var slotParent = replaced != null ? replaced.transform.parent : tabs[0].transform.parent;
            var tabButton = Object.Instantiate(tabs[0].gameObject, slotParent);
            tabButton.name = "LightTabButton";
            tabButton.transform.localScale = Vector3.one;

            // ⚠️ 关键：原版页签位置是 AspectPosition 按屏幕比例算出来的
            //（日志实证：两个设置菜单里同排页签的坐标完全不同）。
            // 不禁用它，我们设的位置下一帧就被拉回"克隆来源"的锚点 → Light 落到 Graphics 上面 → 重叠。
            DisableAspectPosition(tabButton.transform);

            if (replaced != null)
            {
                tabButton.transform.localPosition = replaced.transform.localPosition;   // 先用被替换槽位的位置，稍后统一重排
            }
            SetTabLabel(tabButton, "Light");

            var lightTab = tabButton.GetComponent<TabGroup>();
            if (lightTab == null)
            {
                LightLogger.LogWarning("[SettingsTabPatch] 克隆出来的页签没有 TabGroup，跳过");
                Object.Destroy(tabButton);
                return;
            }
            lightTab.Content = _lightTabContent;

            // 替换数组里的这一项（其余原版页签位置/索引都不动）
            tabs[slot] = lightTab;
            __instance.Tabs = new Il2CppReferenceArray<TabGroup>(tabs.ToArray());
            _lightTabIndex = slot;

            // 页签行重排：照 Nebula 的做法（间距 1.7 / ≥5 项时 1.62），但要**把我们的页签一起排**，
            // 否则新页签会停在克隆来的坐标上、和原版页签重叠（上一版就是这个毛病）。
            LayoutTabRow(tabs, tabButton.transform);

            var pb = tabButton.GetComponent<PassiveButton>();
            if (pb != null)
            {
                pb.OnClick = new Button.ButtonClickedEvent();
                pb.OnClick.AddListener((UnityAction)(() =>
                {
                    __instance.OpenTabGroup(_lightTabIndex);
                    RaiseLightTabOpened();
                }));
            }

            LightLogger.Log($"[SettingsTabPatch] Light 页签占用第 {_lightTabIndex} 槽位（共 {tabs.Count} 项），位置 {tabButton.transform.localPosition}，行模板={_rowTemplate.name}");

            // ⑤ 按已注册的设置项建行（真正的设置项由外部调用 AddXxxButton 注册，见文件顶部说明）
            RebuildButtons();

            // ⑥ 通知订阅者同步显示值（例如 LightOptionsRegistry 把"启动较早、还没初始化"的值补齐，
            //    比如光标索引 —— 它在 LightPlugin.Load 里注册时还没 Initialize）
            RaiseLightTabOpened();
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[SettingsTabPatch.Postfix]", ex);
        }
    }

    /// <summary>触发 <see cref="LightTabOpened"/>，回调异常不影响主流程。</summary>
    private static void RaiseLightTabOpened()
    {
        try
        {
            LightTabOpened?.Invoke();
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[SettingsTabPatch] LightTabOpened 回调异常：{ex.Message}");
        }
    }

    // ════════════════════════════════════════════
    //  公共方法（给以后真正的设置项用）
    // ════════════════════════════════════════════

    public static LightOptionButton AddToggleButton(string label, bool initialValue, Action<bool> onToggle, string? detail = null)
    {
        var btn = new LightOptionButton(label, initialValue ? "启用" : "禁用") { IsOn = initialValue, Detail = detail };
        btn.OnClick = () =>
        {
            btn.IsOn = !btn.IsOn;
            btn.ValueText = btn.IsOn ? "启用" : "禁用";
            RebuildButtons();          // 先刷新显示，再回调（回调里可能再改值）
            onToggle?.Invoke(btn.IsOn);
        };
        _buttons.Add(btn);
        RebuildIfVisible();
        return btn;
    }

    public static LightOptionButton AddSelectorButton(string label, string[] options, int initialIndex, Action<int> onSelect, string? detail = null)
    {
        // 防御：选项为空 / 初值越界时不要直接 options[initialIndex]（会抛异常）
        if (options == null || options.Length == 0)
        {
            LightLogger.LogWarning($"[SettingsTabPatch.AddSelectorButton]「{label}」没有选项，已忽略该设置项");
            return new LightOptionButton(label, "");
        }
        if (initialIndex < 0 || initialIndex >= options.Length) initialIndex = 0;

        var btn = new LightOptionButton(label, options[initialIndex]) { SelectorOptions = options, SelectorIndex = initialIndex, Detail = detail };
        btn.OnClick = () =>
        {
            btn.SelectorIndex = (btn.SelectorIndex + 1) % options.Length;
            btn.ValueText = options[btn.SelectorIndex];
            RebuildButtons();
            onSelect?.Invoke(btn.SelectorIndex);
        };
        _buttons.Add(btn);
        RebuildIfVisible();
        return btn;
    }

    public static LightOptionButton AddActionButton(string label, Action onClick, string? detail = null)
    {
        var btn = new LightOptionButton(label, "") { Detail = detail };
        btn.OnClick = () => onClick?.Invoke();
        _buttons.Add(btn);
        RebuildIfVisible();
        return btn;
    }

    public static LightOptionButton AddEmptyButton(string label, string? detail = null)
    {
        var btn = new LightOptionButton(label, "") { Detail = detail };
        btn.OnClick = () => { };
        _buttons.Add(btn);
        RebuildIfVisible();
        return btn;
    }

    public static LightOptionButton AddHudWindowButton(string label, Action<List<HudUI.ButtonOption>> windowBuilder, string? detail = null)
    {
        var btn = new LightOptionButton(label, "") { Detail = detail };
        btn.OnClick = () =>
        {
            try
            {
                var options = new List<HudUI.ButtonOption>();
                windowBuilder?.Invoke(options);
                HudUI.OpenButtonWindow(label, options.ToArray());
            }
            catch (Exception ex) { LightLogger.LogError("[SettingsTabPatch.AddHudWindowButton]", ex); }
        };
        _buttons.Add(btn);
        RebuildIfVisible();
        return btn;
    }

    /// <summary>
    /// 按当前设置项数据重建 Light 页签里的所有行。
    /// 用在"别处改了值、需要刷新显示"的场合（例如重载配置、恢复默认）。
    /// 平时点击行自己就会刷新，不需要调用。
    /// </summary>
    public static void Refresh() => RebuildButtons();

    private static void RebuildIfVisible()
    {
        if (_lightTabContent != null && _lightTabContent.activeSelf) RebuildButtons();
    }

    // ════════════════════════════════════════════
    //  页签 / 设置行 构建
    // ════════════════════════════════════════════

    /// <summary>
    /// 把 Light 页签排进"行内等距的那一组"，避免重叠。
    ///
    /// 依据实测数据（两个设置菜单的日志）：
    ///   设置菜单    : General -1.70 / Graphics 0.00 / Data 1.70 / Help 2.39  → 间距 1.70,1.70,0.69
    ///   大厅设置菜单: General -1.00 / Graphics 1.00 / Help 1.66              → 间距 2.00,0.66
    /// 可见原版有一组"等距页签"，另有一个靠右的独立按钮（Help），间距明显更小。
    /// 所以这里取"最大相邻间距"为基准，只把间距接近基准的项算作同一组（Help 被排除、不参与重排），
    /// 然后把「组内页签 + Light」围绕组中心等距排布；总宽最多比原组宽一个间距（超了就压紧）。
    /// </summary>
    private static void LayoutTabRow(List<TabGroup> tabs, Transform newTab)
    {
        try
        {
            var first = tabs[0].transform.localPosition;
            float rowY = first.y, rowZ = first.z;

            // 同一排、可见、不是自己的项
            var sameRow = new List<Transform>();
            foreach (var t in tabs)
            {
                if (t == null) continue;
                var tr = t.transform;
                if (tr == newTab) continue;
                if (!t.gameObject.activeSelf) continue;
                if (Mathf.Abs(tr.localPosition.y - rowY) > 0.01f) continue;      // 不同排的（如 Done）不动
                sameRow.Add(tr);
            }
            sameRow.Sort((a, b) => a.localPosition.x.CompareTo(b.localPosition.x));
            if (sameRow.Count == 0) return;

            // 基准间距 = 最大相邻间距
            float baseSpacing = 0f;
            for (int i = 1; i < sameRow.Count; i++)
            {
                float gap = sameRow[i].localPosition.x - sameRow[i - 1].localPosition.x;
                if (gap > baseSpacing) baseSpacing = gap;
            }
            if (baseSpacing <= 0.01f) baseSpacing = 1.7f;

            // 等距组：间距接近基准的连续项（靠右的独立按钮会被自然排除）
            var group = new List<Transform> { sameRow[0] };
            for (int i = 1; i < sameRow.Count; i++)
            {
                float gap = sameRow[i].localPosition.x - sameRow[i - 1].localPosition.x;
                if (Mathf.Abs(gap - baseSpacing) <= baseSpacing * 0.25f) group.Add(sameRow[i]);
            }

            float minX = float.MaxValue, maxX = float.MinValue;
            foreach (var tr in group)
            {
                float x = tr.localPosition.x;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
            }
            if (minX > maxX) { minX = 0f; maxX = 0f; }

            int n = group.Count + 1;                       // 组内 + Light
            float center = (minX + maxX) * 0.5f;
            float spacing = baseSpacing;
            float maxWidth = (maxX - minX) + spacing;      // 最多比原组宽一个间距，避免顶出面板
            if (spacing * (n - 1) > maxWidth) spacing = maxWidth / (n - 1);

            float totalWidth = spacing * (n - 1);
            float startX = center - totalWidth * 0.5f;

            var ordered = new List<Transform>(group) { newTab };
            for (int i = 0; i < ordered.Count; i++)
            {
                DisableAspectPosition(ordered[i]);         // 必须先禁用，否则位置会被算回去
                ordered[i].localPosition = new Vector3(startX + spacing * i, rowY, rowZ);
            }

            LightLogger.Log($"[SettingsTabPatch] 页签重排：等距组 {group.Count} 项 + Light，基准间距 {baseSpacing:F2} → 实排 {spacing:F2}，x {startX:F2}..{startX + totalWidth:F2}（组原范围 {minX:F2}..{maxX:F2}，同排共 {sameRow.Count} 项）");
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[SettingsTabPatch] 页签重排失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 禁用该物体及最近几层祖先上的 <see cref="AspectPosition"/>。
    /// 页签位置就是它按屏幕比例算的（两个菜单坐标不同即证据），不禁用则手动位置会被覆盖。
    /// 只往上查 3 层：再往上就是整个菜单的锚点，动它会破坏菜单自身的自适应。
    /// </summary>
    private static void DisableAspectPosition(Transform t)
    {
        try
        {
            var cur = t;
            for (int depth = 0; cur != null && depth < 3; depth++, cur = cur.parent)
            {
                var ap = cur.GetComponent<AspectPosition>();
                if (ap == null || !ap.enabled) continue;

                ap.enabled = false;
                LightLogger.Log($"[SettingsTabPatch] 已禁用 {cur.name} 上的 AspectPosition（否则页签位置会被改回去）");
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[SettingsTabPatch] 禁用 AspectPosition 失败：{ex.Message}");
        }
    }

    private static void SetTabLabel(GameObject tabButton, string text)
    {
        try
        {
            var tmp = tabButton.GetComponentInChildren<TextMeshPro>(true);
            if (tmp == null) return;

            var tr = tmp.GetComponent<TextTranslatorTMP>();
            if (tr != null) tr.enabled = false;      // 别让翻译器把 "Light" 换掉

            tmp.text = text;
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[SettingsTabPatch] 设置页签文字失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 找原版设置行的模板：优先 Nebula 用的 MiscGroup/StreamerModeButton，
    /// 找不到就在所有页签内容里取第一个 ToggleButtonBehaviour。
    /// </summary>
    private static GameObject? FindRowTemplate(List<TabGroup> tabs)
    {
        try
        {
            foreach (var tab in tabs)
            {
                var content = tab?.Content;
                if (content == null) continue;

                var misc = content.transform.FindChild("MiscGroup");
                if (misc == null) continue;

                var streamer = misc.FindChild("StreamerModeButton");
                if (streamer != null) return streamer.gameObject;
            }
        }
        catch { }

        try
        {
            foreach (var tab in tabs)
            {
                var content = tab?.Content;
                if (content == null) continue;

                foreach (var tbb in content.GetComponentsInChildren<ToggleButtonBehaviour>(true))
                {
                    if (tbb == null) continue;
                    if (tbb.gameObject.name.StartsWith("LightOption")) continue;   // 别拿自己建的当模板
                    return tbb.gameObject;
                }
            }
        }
        catch { }

        return null;
    }

    /// <summary>按 2 列网格重建所有设置行。</summary>
    private static void RebuildButtons()
    {
        try
        {
            if (_lightTabContent == null || _rowTemplate == null) return;

            foreach (var row in _rows)
            {
                if (row != null) Object.Destroy(row.gameObject);
            }
            _rows.Clear();

            for (int i = 0; i < _buttons.Count; i++)
            {
                var row = CreateRow(new Vector2(i % 2, i / 2), _buttons[i]);
                if (row != null) _rows.Add(row);
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[SettingsTabPatch.RebuildButtons]", ex);
        }
    }

    private static ToggleButtonBehaviour? CreateRow(Vector2 grid, LightOptionButton info)
    {
        try
        {
            var go = Object.Instantiate(_rowTemplate!, null);
            go.name = $"LightOption_{info.Label}";
            go.transform.SetParent(_lightTabContent!.transform);
            go.transform.localScale = Vector3.one;
            go.transform.localPosition = new Vector3(
                RowSpacingX * (grid.x * 2f - 1f),
                RowTopY - RowSpacingY * grid.y,
                0f);

            var tbb = go.GetComponent<ToggleButtonBehaviour>();
            if (tbb != null) tbb.BaseText = 0;      // 关键：配合 ResetTextSkip 补丁，保住自定义文字

            var pb = go.GetComponent<PassiveButton>();
            if (pb != null)
            {
                // ⚠️ 必须**整体替换** OnClick，不能用 AddListener 追加：
                // 行是从原版设置的 ToggleButtonBehaviour（StreamerModeButton / 直播主模式）克隆来的，
                // 它自带原版的点击逻辑 —— 追加的话点我们的开关会同时把"直播主模式"打开
                //（主菜单里那个逻辑没有可见效果，进大厅后就会真的生效）。
                pb.OnClick = new Button.ButtonClickedEvent();
                pb.OnClick.AddListener((UnityAction)(() => info.OnClick?.Invoke()));

                // 悬浮显示 detail：只有传了非空文本才挂监听（null/空白 → 连提示框都不会出现）
                if (!string.IsNullOrWhiteSpace(info.Detail))
                {
                    var detailText = info.Detail;
                    pb.OnMouseOver ??= new Button.ButtonClickedEvent();
                    pb.OnMouseOut ??= new Button.ButtonClickedEvent();
                    // 注意：canPinByTab 在 options 注册接口里是**硬编码 true**，注册方只能传文本
                    pb.OnMouseOver.AddListener((UnityAction)(() => DetailPopup.Show(detailText, true, go.transform)));
                    pb.OnMouseOut.AddListener((UnityAction)(() => DetailPopup.Hide()));
                }
            }

            ApplyRowText(tbb, info);
            go.SetActive(true);
            return tbb;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[SettingsTabPatch.CreateRow]", ex);
            return null;
        }
    }

    private static void ApplyRowText(ToggleButtonBehaviour? tbb, LightOptionButton info)
    {
        if (tbb == null) return;
        try
        {
            string text = string.IsNullOrEmpty(info.ValueText) ? info.Label : $"{info.Label}: {info.ValueText}";
            if (tbb.Text != null) tbb.Text.text = text;

            // 开启色 = 模组主色（原来是照 Nebula 抄的硬编码绿，和"MOD 颜色改成金色"不一致）
            var onColor = UColor.white;
            if (info.IsOn)
            {
                try
                {
                    var cd = LightPlugin.ColorData;
                    onColor = (cd != null && !cd.IsVanillaMode) ? cd.modMainColor.ToUnityColor() : UColor.white;
                }
                catch { onColor = UColor.white; }
            }

            var color = info.IsOn ? onColor : UColor.white;
            if (tbb.Background != null) tbb.Background.color = color;
            if (tbb.Rollover != null) tbb.Rollover.ChangeOutColor(color);

            tbb.onState = info.IsOn;
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[SettingsTabPatch] 刷新设置行文字失败：{ex.Message}");
        }
    }
}

/// <summary>
/// 设置菜单开着时每帧驱动 <see cref="DetailPopup"/> —— 只做一件事：检测 Tab 键，切换"固定介绍"状态。
/// </summary>
[HarmonyPatch]
internal static class DetailPopupDriverPatch
{
    [HarmonyPatch(typeof(OptionsMenuBehaviour), nameof(OptionsMenuBehaviour.Update))]
    [HarmonyPostfix]
    public static void UpdatePostfix()
    {
        DetailPopup.Update();
    }
}

/// <summary>Light 设置选项按钮数据。</summary>
public class LightOptionButton
{
    public string Label { get; set; }
    public string ValueText { get; set; }
    public bool IsOn { get; set; }
    public string[]? SelectorOptions { get; set; }
    public int SelectorIndex { get; set; }
    public Action? OnClick { get; set; }

    /// <summary>鼠标悬浮时显示的说明。null / 空白 → 不显示提示框。</summary>
    public string? Detail { get; set; }

    public LightOptionButton(string label, string valueText)
    {
        Label = label;
        ValueText = valueText;
    }
}
