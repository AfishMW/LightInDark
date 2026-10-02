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
        /// <summary>当前分类过滤（null = 全部显示）。</summary>
        private static ConfigCategory[]? _categoryFilter;
        /// <summary>单职业模式：只渲染这一个块（职业配置页）。</summary>
        private static ConfigBlock? _singleBlock;
        /// <summary>单职业模式：返回按钮的回调（回职业列表页）。</summary>
        private static Action? _onBack;
        private static readonly List<GameObject> _spawned = new();
        private static readonly Dictionary<ConfigItem, ConfigRowDriver> _drivers = new();

        public static bool Built => _page != null;

        /// <summary>按分类过滤显示配置块（切换 MOD 设置页的标签时调用）。</summary>
        public static void Show(ConfigCategory[] categories, Transform parent)
        {
            _categoryFilter = categories;
            _singleBlock = null;
            _onBack = null;
            Rebuild(parent);
        }

        /// <summary>单职业模式：只渲染一个职业块，顶部带返回按钮（点击回职业列表）。</summary>
        public static void ShowRole(ConfigBlock block, Transform parent, Action onBack)
        {
            Clear();
            _categoryFilter = null;
            _singleBlock = block;
            _onBack = onBack;
            Build(parent);
        }

        /// <summary>在给定父级下构建配置面板（幂等：已建则只刷新值）。</summary>
        public static void Build(Transform parent)
        {
            try
            {
                if (_page != null) { Refresh(); return; }

                _page = NewUIObject("LightConfigPage", parent, new Vector3(0f, 0f, RowZ));
                _container = _page.transform;

                if (_singleBlock != null)
                {
                    // 单职业模式（独立新页面，标签行已隐藏）：返回按钮在原标签行位置，仅渲染这一个块
                    float y = AddBackButton(0.8f);
                    BuildBlock(_singleBlock, y);
                }
                else
                {
                    // 渲染所有已注册配置块（按分类过滤：调试块/职业块均在 ConfigRegistry 中）
                    foreach (var block in ConfigRegistry.Blocks)
                    {
                        if (!MatchesFilter(block)) continue;
                        BuildBlock(block);
                    }
                }

                LightLogger.Log($"[ConfigUIPanel] 已构建配置面板，行数 {_spawned.Count}");
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[ConfigUIPanel.Build]", ex);
            }
        }

        /// <summary>块是否通过当前分类过滤。</summary>
        private static bool MatchesFilter(ConfigBlock block)
        {
            if (_categoryFilter == null || _categoryFilter.Length == 0) return true;
            foreach (var c in _categoryFilter)
                if (block.Category == c) return true;
            return false;
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

        private static void BuildBlock(ConfigBlock block, float startY = StartY)
        {
            if (block == null) return;

            float y = startY;
            y = AddCategoryHeader(block, y);

            foreach (var item in block.Items)
            {
                if (!item.IsVisible) continue;         // 依赖项未满足 → 不建行
                y = AddConfigRow(item, y);
            }
        }

        /// <summary>单职业模式顶部的返回按钮，返回下一个 y。</summary>
        private static float AddBackButton(float y)
        {
            var go = NewUIObject("LightConfigBack", _container, new Vector3(HeaderX + 0.35f, y, RowZ));
            _spawned.Add(go);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = GetRoundedSprite();
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.size = new Vector2(0.9f, 0.32f);
            sr.color = new UColor(0.2f, 0.2f, 0.2f, 0.9f);

            MenuTextTemplate.Create(go.transform, new Vector3(0f, 0f, -0.1f), "< 返回", 0.8f);

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(0.9f, 0.32f);

            var pb = go.SetUpButton(true, null, null, null, false);
            pb.OnClick.AddListener((UnityAction)(() => _onBack?.Invoke()));

            return y - 0.42f;
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

        // ---- 自绘行的布局常量（相对行锚点） ----
        private const float NameX = -1.3f;    // 名字文本中心
        private const float MinusX = 1.45f;   // [-] 按钮中心
        private const float ValueX = 2.05f;   // 值/开关中心
        private const float PlusX = 2.65f;    // [+] 按钮中心
        private const float CtrlH = 0.34f;    // 控件高度
        private static readonly UColor BtnColor = new(0.30f, 0.30f, 0.30f, 0.95f);
        private static readonly UColor ToggleOn = new(0.30f, 0.75f, 0.40f, 1f);
        private static readonly UColor ToggleOff = new(0.28f, 0.28f, 0.28f, 0.95f);

        /// <summary>建一行配置项（自绘控件，不依赖原版行模板），返回下一个 y。</summary>
        private static float AddConfigRow(ConfigItem item, float y)
        {
            var row = NewUIObject($"LightConfigRow_{item.Key}", _container, new Vector3(RowX, y, RowZ));
            _spawned.Add(row);

            var driver = row.AddComponent<ConfigRowDriver>();

            switch (item.Type)
            {
                case ConfigType.Bool:
                    BuildToggleRow(row, item, driver);
                    break;
                case ConfigType.Value:
                case ConfigType.Filter:
                    BuildStringRow(row, item, driver);
                    break;
                default:
                    BuildNumberRow(row, item, driver);
                    break;
            }

            _drivers[item] = driver;
            WireHover(row, item);
            driver.RefreshVisual();

            return y - SpacingY;
        }

        /// <summary>行左侧的名字文本（带可选配色）。</summary>
        private static void RowName(GameObject row, ConfigItem item)
        {
            var tmp = RowText(row.transform, new Vector3(NameX, 0f, -0.1f), item.DisplayName ?? item.Key, 1.0f);
            if (tmp != null && item.NameColor.HasValue) tmp.color = item.NameColor.Value;
        }

        /// <summary>Bool：名字 + 开/关按钮（点击切换，底色随状态变化）。</summary>
        private static void BuildToggleRow(GameObject row, ConfigItem item, ConfigRowDriver driver)
        {
            RowName(row, item);
            var label = RowButton(row.transform, "Toggle", new Vector3(ValueX, 0f, 0f), 1.0f, CtrlH,
                ToggleOff, "", () => driver.Step(1));
            driver.BindCustom(item, label, label.GetComponentInParent<SpriteRenderer>(), ToggleOn, ToggleOff);
        }

        /// <summary>Int/Float：名字 + [-] 值 [+]。</summary>
        private static void BuildNumberRow(GameObject row, ConfigItem item, ConfigRowDriver driver)
        {
            RowName(row, item);
            RowButton(row.transform, "Minus", new Vector3(MinusX, 0f, 0f), 0.4f, CtrlH, BtnColor, "-", () => driver.Step(-1));
            var value = RowButton(row.transform, "Value", new Vector3(ValueX, 0f, 0f), 1.0f, CtrlH, BtnColor, "", null);
            RowButton(row.transform, "Plus", new Vector3(PlusX, 0f, 0f), 0.4f, CtrlH, BtnColor, "+", () => driver.Step(1));
            driver.BindCustom(item, value, null, default, default);
        }

        /// <summary>Value/Filter：名字 + 值按钮（点击循环切换候选）。</summary>
        private static void BuildStringRow(GameObject row, ConfigItem item, ConfigRowDriver driver)
        {
            RowName(row, item);
            var value = RowButton(row.transform, "Value", new Vector3(ValueX, 0f, 0f), 1.3f, CtrlH, BtnColor, "", () => driver.Step(1));
            driver.BindCustom(item, value, null, default, default);
        }

        /// <summary>行内文本（走统一文字模板）。</summary>
        private static TextMeshPro RowText(Transform parent, Vector3 pos, string text, float fontSize)
        {
            var tmp = MenuTextTemplate.Create(parent, pos, text, fontSize);
            if (tmp != null) tmp.ForceMeshUpdate();
            return tmp;
        }

        /// <summary>
        /// 行内圆角小按钮（底 = 圆角 sprite，悬浮变亮），返回文字引用便于外部改写。
        /// onClick 为 null 时只显示不响应。
        /// </summary>
        private static TextMeshPro RowButton(Transform parent, string name, Vector3 pos,
            float w, float h, UColor color, string label, Action onClick)
        {
            var go = NewUIObject(name, parent, pos);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = GetRoundedSprite();
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.size = new Vector2(w, h);
            sr.color = color;

            var tmp = MenuTextTemplate.Create(go.transform, new Vector3(0f, 0f, -0.1f), label, 0.9f);

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(w, h);

            var pb = go.SetUpButton(true, null, null, null, false);
            if (onClick != null)
            {
                pb.OnClick.AddListener((UnityAction)(() => onClick()));
                pb.OnMouseOver.AddListener((UnityAction)(() => sr.color = UColor.Lerp(color, UColor.white, 0.35f)));
                pb.OnMouseOut.AddListener((UnityAction)(() => sr.color = color));
            }
            return tmp;
        }

        /// <summary>悬浮显示详情（走本工程已有的 DetailPopup）。</summary>
        private static void WireHover(GameObject row, ConfigItem item)
        {
            if (string.IsNullOrWhiteSpace(item.Detail)) return;

            try
            {
                // 碰撞区只盖住左侧名字区域，避免挡住右侧 +/- / 开关按钮
                var col = row.AddComponent<BoxCollider2D>();
                col.isTrigger = true;
                col.size = new Vector2(3.4f, 0.42f);
                col.offset = new Vector2(NameX, 0f);

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

        private static Sprite? _roundedSprite;

        /// <summary>圆角长方形 sprite（带 9 宫格边框，Sliced 任意拉伸；列表/返回按钮共用）。</summary>
        internal static Sprite GetRoundedSprite()
        {
            if (_roundedSprite != null) return _roundedSprite;

            const int w = 64, h = 32, r = 10;
            var tex = new Texture2D(w, h, TextureFormat.ARGB32, false);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    // 像素到圆角矩形边缘的距离 → 1px 抗锯齿
                    float dx = Mathf.Max(Mathf.Abs(x + 0.5f - w * 0.5f) - (w * 0.5f - r), 0f);
                    float dy = Mathf.Max(Mathf.Abs(y + 0.5f - h * 0.5f) - (h * 0.5f - r), 0f);
                    float a = Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy));
                    tex.SetPixel(x, y, new UColor(1f, 1f, 1f, a));
                }
            }
            tex.Apply();

            _roundedSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f,
                0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
            return _roundedSprite;
        }

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
    /// 一行配置项的驱动器：把配置值写进行内的自绘控件（值文本 + 可选开关底色）。
    /// </summary>
    public class ConfigRowDriver : MonoBehaviour
    {
        private ConfigItem _item;
        private TextMeshPro _valueLabel;   // 值/开关文字
        private SpriteRenderer _toggleBg;  // Bool 开关底色（可空）
        private UColor _onColor, _offColor;

        /// <summary>上一次的可见性（用于检测是否需要重建面板）。</summary>
        public bool WasVisible { get; private set; }

        /// <summary>绑定自绘行：valueLabel 显示取值文本；toggleBg 仅 Bool 行用来变色。</summary>
        public void BindCustom(ConfigItem item, TextMeshPro valueLabel, SpriteRenderer toggleBg,
            UColor onColor, UColor offColor)
        {
            _item = item;
            _valueLabel = valueLabel;
            _toggleBg = toggleBg;
            _onColor = onColor;
            _offColor = offColor;
            WasVisible = item.IsVisible;
        }

        /// <summary>按当前值刷新所有显示部件。</summary>
        public void RefreshVisual()
        {
            try
            {
                if (_item == null) return;

                if (_valueLabel != null)
                    _valueLabel.text = _item.GetValueText();

                if (_toggleBg != null)
                    _toggleBg.color = _item.GetBool() ? _onColor : _offColor;
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
    }
}
