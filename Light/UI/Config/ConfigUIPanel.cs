using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using Light.Config;
using Light.Patches;
using Light.UI.Window;
using LightInDark.Configuration;
using LightInDark.Core;
using LightInDark.Language;
using LightInDark.UI.Window;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using UColor = UnityEngine.Color;

namespace Light.UI.Config
{
    /// <summary>
    /// 配置项 UI —— 在现有「MOD 设置」页签里，用**原版控件**渲染配置块。
    ///
    /// 结构（每个块）：
    ///   金色分类头（克隆原版 <see cref="CategoryHeaderMasked"/>，可调色）
    ///     └ 若干配置行
    ///        · Bool  → 克隆原版 ToggleOption（勾选框）
    ///        · Int/Float → 克隆原版 NumberOption（- 值 +）
    ///        · Value/Filter → 克隆原版 StringOption（循环切换）
    ///
    /// ⚠️ 关键坑（必须遵守，否则行会被原版逻辑覆盖）：
    ///   1. 原版 NumberOption/ToggleOption/StringOption 都是**每帧/每 FixedUpdate**
    ///      从 <c>data.GetValueString(Value)</c>、<c>data.GetValue()</c> 回写显示，
    ///      而自定义配置项没有 <c>BaseGameSetting</c> → 必须
    ///      <c>option.enabled = false</c> 停掉它们的 Update/FixedUpdate，
    ///      再由我们自己的 <see cref="ConfigRowDriver"/> 驱动显示。
    ///   2. 克隆出来的 <c>PassiveButton.OnClick</c> **自带原版点击逻辑**，
    ///      必须 <c>new ButtonClickedEvent()</c> 整体替换（不是 AddListener 追加）。
    ///   3. <c>z = -2f</c>（与原版行一致），分类头 <c>localScale = 0.63</c>。
    /// </summary>
    public static class ConfigUIPanel
    {
        // ---- 版面对齐原版 GameOptionsMenu.CreateSettings 的常量 ----
        private const float StartY = 0.713f;
        private const float RowX = 0.952f;
        private const float HeaderX = -0.903f;
        private const float HeaderHeight = 0.63f;
        private const float SpacingY = 0.45f;
        private const int MaskLayer = 20;
        private const float RowZ = -2f;

        /// <summary>本页承载的容器（挂在 MOD 设置页下）。</summary>
        private static GameObject _page;
        private static Transform _container;
        private static readonly List<GameObject> _spawned = new();
        private static readonly Dictionary<ConfigItem, ConfigRowDriver> _drivers = new();

        public static bool Built => _page != null;

        /// <summary>在给定父级下构建配置面板（幂等：已建则只刷新值）。</summary>
        public static void Build(Transform parent)
        {
            try
            {
                if (_page != null) { Refresh(); return; }

                _page = NewUIObject("LightConfigPage", parent, new Vector3(0f, 0f, RowZ));
                _container = _page.transform;

                // 只渲染调试块（本轮范围）：金色分类头 + 两项配置
                BuildBlock(DebugConfig.Block);

                LightLogger.Log($"[ConfigUIPanel] 已构建配置面板，行数 {_spawned.Count}");
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[ConfigUIPanel.Build]", ex);
            }
        }

        /// <summary>清空重建（值变化导致可见性变化时调用）。</summary>
        public static void Rebuild(Transform parent)
        {
            Clear();
            Build(parent);
        }

        public static void Clear()
        {
            foreach (var go in _spawned)
                if (go != null) Object.Destroy(go);
            _spawned.Clear();
            _drivers.Clear();
            if (_page != null) { Object.Destroy(_page); _page = null; _container = null; }
        }

        // =====================================================================
        //  构建
        // =====================================================================

        private static void BuildBlock(ConfigBlock block)
        {
            if (block == null) return;

            float y = StartY;
            y = AddCategoryHeader(block, y);

            foreach (var item in block.Items)
            {
                if (!item.IsVisible) continue;         // 依赖项未满足 → 不建行
                y = AddConfigRow(item, y);
            }
        }

        /// <summary>金色分类头：克隆原版 CategoryHeaderMasked。</summary>
        private static float AddCategoryHeader(ConfigBlock block, float y)
        {
            try
            {
                var origin = FindHeaderTemplate();
                if (origin == null)
                {
                    LightLogger.LogWarning("[ConfigUIPanel] 找不到原版分类头模板(CategoryHeaderMasked)，跳过分类头");
                    return y;
                }

                var header = Object.Instantiate(origin, Vector3.zero, Quaternion.identity, _container);
                header.name = $"LightConfigHeader_{block.Key}";
                header.transform.localScale = Vector3.one * HeaderHeight;
                header.transform.localPosition = new Vector3(HeaderX, y, RowZ);
                header.gameObject.SetActive(true);
                _spawned.Add(header.gameObject);

                // 头文字：SetHeader 走 StringNames，我们用翻译槽位塞自定义文本
                SetHeaderText(header, block);

                y -= HeaderHeight;
                return y;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[ConfigUIPanel.AddCategoryHeader]", ex);
                return y;
            }
        }

        /// <summary>
        /// 设置分类头文字与颜色。
        /// 原版 SetHeader 只接受 StringNames，所以我们直接写它的 Title 文本
        /// （比挪用翻译槽位更直接，且不会影响别处）。
        /// </summary>
        private static void SetHeaderText(CategoryHeaderMasked header, ConfigBlock block)
        {
            try
            {
                header.SetHeader(StringNames.None, MaskLayer);   // 先走一遍原版：设 mask/stencil

                // 再覆盖文字
                var tmp = header.GetComponentInChildren<TextMeshPro>(true);
                if (tmp != null)
                {
                    var tr = tmp.GetComponent<TextTranslatorTMP>();
                    if (tr != null) tr.enabled = false;          // 别被翻译器改回去
                    tmp.text = block.DisplayName;
                }

                // 调色：分类头的背景/分隔线是 SpriteRenderer
                if (block.HeaderColor.HasValue)
                {
                    var color = block.HeaderColor.Value;
                    foreach (var sr in header.GetComponentsInChildren<SpriteRenderer>(true))
                    {
                        if (sr == null) continue;
                        sr.color = color;
                    }
                    if (tmp != null) tmp.color = color;
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[ConfigUIPanel.SetHeaderText] {ex.Message}");
            }
        }

        /// <summary>建一行配置项，返回下一个 y。</summary>
        private static float AddConfigRow(ConfigItem item, float y)
        {
            var row = NewUIObject($"LightConfigRow_{item.Key}", _container, new Vector3(RowX, y, RowZ));
            _spawned.Add(row);

            switch (item.Type)
            {
                case ConfigType.Bool:
                    BuildToggleRow(row, item);
                    break;
                case ConfigType.Value:
                case ConfigType.Filter:
                    BuildStringRow(row, item);
                    break;
                default:
                    BuildNumberRow(row, item);
                    break;
            }

            return y - SpacingY;
        }

        /// <summary>Bool：原版勾选框行（克隆 ToggleOption 的 CheckMark + TitleText）。</summary>
        private static void BuildToggleRow(GameObject row, ConfigItem item)
        {
            var toggle = CloneRowComponent<ToggleOption>(row, item, out var template);
            if (toggle == null) return;

            // 停掉原版每帧回写（它读的是 data，不是我们的值）
            toggle.enabled = false;

            var driver = row.AddComponent<ConfigRowDriver>();
            driver.Bind(item, toggle, null);
            _drivers[item] = driver;

            WireHover(row, item);
            driver.RefreshVisual();
        }

        /// <summary>Int/Float：原版数值行（- / 值 / +）。</summary>
        private static void BuildNumberRow(GameObject row, ConfigItem item)
        {
            var number = CloneRowComponent<NumberOption>(row, item, out var template);
            if (number == null) return;

            number.enabled = false;                 // 停掉 FixedUpdate 的 data 回写

            // 原版 +/- 按钮各自带原版逻辑，必须整体替换 OnClick
            var driver = row.AddComponent<ConfigRowDriver>();
            driver.Bind(item, null, number);
            _drivers[item] = driver;

            HookPlusMinus(row, driver);
            WireHover(row, item);
            driver.RefreshVisual();
        }

        /// <summary>Value/Filter：原版字符串行（循环切换）。</summary>
        private static void BuildStringRow(GameObject row, ConfigItem item)
        {
            var str = CloneRowComponent<StringOption>(row, item, out var template);
            if (str == null)
            {
                // 没有 StringOption 模板时退化成数值行，至少能改
                BuildNumberRow(row, item);
                return;
            }

            str.enabled = false;

            var driver = row.AddComponent<ConfigRowDriver>();
            driver.Bind(item, null, null, str);
            _drivers[item] = driver;

            WireHover(row, item);
            driver.RefreshVisual();
        }

        /// <summary>
        /// 克隆原版行控件到我们自己的 row 下。
        /// 返回克隆出的组件；模板缺失时返回 null。
        /// </summary>
        private static T? CloneRowComponent<T>(GameObject row, ConfigItem item, out T? template) where T : OptionBehaviour
        {
            template = null;
            try
            {
                template = FindRowTemplate<T>();
                if (template == null)
                {
                    LightLogger.LogWarning($"[ConfigUIPanel] 找不到原版行模板 {typeof(T).Name}，跳过 {item.Key}");
                    return null;
                }

                var clone = Object.Instantiate(template, row.transform);
                clone.name = $"Row_{item.Key}";
                clone.transform.localPosition = Vector3.zero;
                clone.transform.localScale = Vector3.one;
                clone.gameObject.SetActive(true);

                // 清掉原版的点击逻辑（必须整体替换）
                var pb = clone.GetComponent<PassiveButton>();
                if (pb != null) pb.OnClick = new UnityEngine.UI.Button.ButtonClickedEvent();

                return clone;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[ConfigUIPanel.CloneRowComponent]", ex);
                return null;
            }
        }

        /// <summary>把 +/- 按钮接到我们自己的增减逻辑。</summary>
        private static void HookPlusMinus(GameObject row, ConfigRowDriver driver)
        {
            try
            {
                foreach (var pb in row.GetComponentsInChildren<PassiveButton>(true))
                {
                    if (pb == null) continue;
                    string n = pb.gameObject.name.ToLowerInvariant();
                    bool isPlus = n.Contains("plus");
                    bool isMinus = n.Contains("minus");
                    if (!isPlus && !isMinus) continue;

                    pb.OnClick = new UnityEngine.UI.Button.ButtonClickedEvent();
                    bool plus = isPlus;
                    pb.OnClick.AddListener((UnityAction)(() => driver.Step(plus ? +1 : -1)));
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[ConfigUIPanel.HookPlusMinus] {ex.Message}");
            }
        }

        /// <summary>悬浮显示详情（走本工程已有的 DetailPopup）。</summary>
        private static void WireHover(GameObject row, ConfigItem item)
        {
            if (string.IsNullOrWhiteSpace(item.Detail)) return;

            try
            {
                var col = row.AddComponent<BoxCollider2D>();
                col.isTrigger = true;
                col.size = new Vector2(6.2f, 0.42f);
                col.offset = new Vector2(0f, 0f);

                var pb = row.GetComponent<PassiveButton>() ?? row.AddComponent<PassiveButton>();
                pb.OnMouseOver ??= new Button.ButtonClickedEvent();
                pb.OnMouseOut ??= new Button.ButtonClickedEvent();

                var text = item.Detail;
                pb.OnMouseOver.AddListener((UnityAction)(() => DetailPopup.Show(text, true, row.transform)));
                pb.OnMouseOut.AddListener((UnityAction)(() => DetailPopup.Hide()));
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[ConfigUIPanel.WireHover] {ex.Message}");
            }
        }

        // =====================================================================
        //  刷新
        // =====================================================================

        /// <summary>刷新所有行的显示（可见性变化时重建）。</summary>
        public static void Refresh()
        {
            try
            {
                bool visibilityChanged = false;
                foreach (var kv in _drivers)
                {
                    if (kv.Key.IsVisible != kv.Value.WasVisible) { visibilityChanged = true; break; }
                }

                if (visibilityChanged && _container != null)
                {
                    var parent = _container.parent;
                    Rebuild(parent);
                    return;
                }

                foreach (var kv in _drivers) kv.Value.RefreshVisual();
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[ConfigUIPanel.Refresh]", ex);
            }
        }

        // =====================================================================
        //  模板查找
        // =====================================================================

        private static CategoryHeaderMasked FindHeaderTemplate()
        {
            try
            {
                // 原版规则页里现成的分类头
                var menu = GameSettingMenu.Instance;
                if (menu != null)
                {
                    var h = menu.GetComponentInChildren<CategoryHeaderMasked>(true);
                    if (h != null) return h;
                }
                return Object.FindObjectOfType<CategoryHeaderMasked>(true);
            }
            catch { return null; }
        }

        private static T? FindRowTemplate<T>() where T : OptionBehaviour
        {
            try
            {
                var menu = GameSettingMenu.Instance;
                if (menu != null)
                {
                    var t = menu.GetComponentInChildren<T>(true);
                    if (t != null) return t;
                }
                return Object.FindObjectOfType<T>(true);
            }
            catch { return null; }
        }

        private static GameObject NewUIObject(string name, Transform parent, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.layer = LayerExpansion.GetUILayer();
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one;
            return go;
        }
    }

    /// <summary>
    /// 一行配置项的驱动器：把配置值写进原版控件的显示部件。
    /// 因为原版控件的 Update/FixedUpdate 已被 <c>enabled = false</c> 停掉，
    /// 显示完全由这里负责。
    /// </summary>
    public class ConfigRowDriver : MonoBehaviour
    {
        private ConfigItem _item;
        private ToggleOption _toggle;
        private NumberOption _number;
        private StringOption _string;

        /// <summary>上一次的可见性（用于检测是否需要重建面板）。</summary>
        public bool WasVisible { get; private set; }

        public void Bind(ConfigItem item, ToggleOption toggle, NumberOption number, StringOption str = null)
        {
            _item = item;
            _toggle = toggle;
            _number = number;
            _string = str;
            WasVisible = item.IsVisible;
        }

        /// <summary>按当前值刷新所有显示部件。</summary>
        public void RefreshVisual()
        {
            try
            {
                if (_item == null) return;

                // 标题：我们的显示名（原版行文字走翻译键，这里直接写）
                var title = GetTitleText();
                if (title != null)
                {
                    var tr = title.GetComponent<TextTranslatorTMP>();
                    if (tr != null) tr.enabled = false;
                    title.text = _item.DisplayName ?? _item.Key;
                    if (_item.NameColor.HasValue) title.color = _item.NameColor.Value;
                }

                // Bool：勾选状态
                if (_toggle != null && _toggle.CheckMark != null)
                    _toggle.CheckMark.enabled = _item.GetBool();

                // Int/Float/Value：值文本
                var valueText = GetValueText();
                if (valueText != null)
                    valueText.text = _item.GetValueText();
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[ConfigRowDriver.RefreshVisual]", ex);
            }
        }

        /// <summary>增减一步并同步。</summary>
        public void Step(int dir)
        {
            try
            {
                if (_item == null) return;

                if (_item.Type == ConfigType.Bool) _item.Toggle();
                else if (dir > 0) _item.Increase();
                else _item.Decrease();

                RefreshVisual();
                ConfigSync.RaiseAndSync(_item);

                // 值变了可能影响别的项的可见性（如"启用调试模式"控制数量项）
                ConfigUIPanel.Refresh();
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[ConfigRowDriver.Step]", ex);
            }
        }

        private TextMeshPro GetTitleText()
        {
            if (_toggle != null) return _toggle.TitleText;
            if (_number != null) return _number.TitleText;
            if (_string != null) return _string.TitleText;
            return GetComponentInChildren<TextMeshPro>(true);
        }

        private TextMeshPro GetValueText()
        {
            if (_number != null) return _number.ValueText;
            if (_string != null) return _string.ValueText;
            return null;
        }
    }
}
