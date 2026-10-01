using System;
using System.Collections.Generic;
using System.Globalization;
using LightInDark.Core;

namespace LightInDark.Configuration
{
    /// <summary>
    /// 配置项类型。用于 UI 分派建行，也是"这条配置项该怎么被编辑"的声明。
    /// </summary>
    public enum ConfigType
    {
        /// <summary>勾选框（原版 ToggleOption）。</summary>
        Bool,
        /// <summary>整数（原版 NumberOption，ValueText 显示整数）。</summary>
        Int,
        /// <summary>浮点（原版 NumberOption，按 Step 决定小数位）。</summary>
        Float,
        /// <summary>枚举/预设值：在固定候选表里循环切换（原版 StringOption 语义）。</summary>
        Value,
        /// <summary>过滤器：从游戏已有池子里选（职业/阵营/地图等）。本轮仅声明，UI 走 Value 同款。</summary>
        Filter,
    }

    /// <summary>
    /// Float 数值后缀装饰器。渲染时追加在数值后面。
    /// 需要自定义文本时用 <see cref="ConfigItem.SetSuffix(string)"/> 直接给字符串。
    /// </summary>
    public enum ConfigSuffix
    {
        /// <summary>不追加任何东西。</summary>
        None = 0,
        /// <summary>秒 → "s"</summary>
        Second,
        /// <summary>百分比 → "%"</summary>
        Percent,
        /// <summary>次 → "次"</summary>
        Times,
        /// <summary>倍 → "x"</summary>
        Multiplier,
    }

    /// <summary>
    /// 配置块：一组配置项的注册容器。
    /// 用法（在静态构造或初始化方法里）：
    /// <code>
    /// var block = new ConfigBlock("debug", "调试设置", ConfigCategory.Debug);
    /// var on = block.AddConfiguration&lt;bool&gt;("debug.enabled", false);
    /// var n  = block.AddConfiguration&lt;int&gt;("debug.dummies", 0, 0, 14, 1);
    /// </code>
    /// </summary>
    public class ConfigBlock
    {
        /// <summary>块内部名（唯一）。</summary>
        public string Key { get; }
        /// <summary>块显示名（分类头文字）。</summary>
        public string DisplayName { get; }
        /// <summary>分类归属（决定分到哪个页签/分类下）。</summary>
        public ConfigCategory Category { get; }
        /// <summary>分类头颜色；null = 用原版分类头默认色。</summary>
        public UnityEngine.Color? HeaderColor { get; set; }

        /// <summary>本块已注册的配置项。</summary>
        public List<ConfigItem> Items { get; } = new();

        public ConfigBlock(string key, string displayName, ConfigCategory category)
        {
            Key = key;
            DisplayName = displayName;
            Category = category;
        }

        /// <summary>设置分类头颜色（金色等）。</summary>
        public ConfigBlock SetHeaderColor(UnityEngine.Color color)
        {
            HeaderColor = color;
            return this;
        }

        // -----------------------------------------------------------------
        //  AddConfiguration<T> —— 具名工厂，避免 bool/int 重载靠隐式转换区分
        // -----------------------------------------------------------------

        /// <summary>注册一个勾选框配置项。</summary>
        public ConfigItem AddConfiguration(string key, bool defaultValue, string displayName = null, string detail = null)
            => Add(ConfigItem.CreateBool(key, defaultValue, displayName, detail, this));

        /// <summary>注册一个整数配置项（含范围与步进，值会被 clamp）。</summary>
        public ConfigItem AddConfiguration(string key, int defaultValue, int min, int max, int step = 1,
            string displayName = null, string detail = null)
            => Add(ConfigItem.CreateInt(key, defaultValue, min, max, step, displayName, detail, this));

        /// <summary>注册一个浮点配置项（含范围与步进，值会被 clamp）。</summary>
        public ConfigItem AddConfiguration(string key, float defaultValue, float min, float max, float step,
            string displayName = null, string detail = null)
            => Add(ConfigItem.CreateFloat(key, defaultValue, min, max, step, displayName, detail, this));

        /// <summary>注册一个枚举/预设值配置项。候选表首项为默认值。</summary>
        public ConfigItem AddConfiguration(string key, string[] selections, string displayName = null, string detail = null)
            => Add(ConfigItem.CreateValue(key, selections, displayName, detail, this));

        /// <summary>注册一个 Filter 配置项（从游戏已有池子取值，本轮与 Value 同实现）。</summary>
        public ConfigItem AddFilter(string key, IReadOnlyList<ConfigFilterOption> pool,
            string displayName = null, string detail = null)
            => Add(ConfigItem.CreateFilter(key, pool, displayName, detail, this));

        private ConfigItem Add(ConfigItem item)
        {
            Items.Add(item);
            ConfigRegistry.Register(item);
            return item;
        }
    }

    /// <summary>Filter 候选项：一个稳定 id + 一个名字。</summary>
    public readonly struct ConfigFilterOption
    {
        public readonly int Id;
        public readonly string Name;
        public ConfigFilterOption(int id, string name) { Id = id; Name = name; }
    }

    /// <summary>
    /// 分类归属。将来 6 个彩边标签(CREWS/IMP/NEU/MODI/GHOST/MOD)各对应一项。
    /// </summary>
    public enum ConfigCategory
    {
        Mod = 0,
        Crewmate,
        Impostor,
        Neutral,
        Modifier,
        Ghost,
        /// <summary>调试设置（金色分类）。</summary>
        Debug,
    }

    /// <summary>
    /// 一条配置项。值统一存为 <c>float</c> + 可选的候选表索引，
    /// 由 <see cref="Type"/> 决定编辑方式与显示格式。
    /// 设计上只保留 min/max/step 元数据（Nebula 把它们丢掉后面无法 clamp）。
    /// </summary>
    public class ConfigItem
    {
        // ---- 身份 ----
        /// <summary>稳定字符串键（唯一，兼作落盘/同步键）。</summary>
        public string Key { get; internal set; }
        /// <summary>显示名；null 时回退 <see cref="Key"/>。</summary>
        public string DisplayName { get; internal set; }
        /// <summary>悬停详情；null 表示不挂提示。</summary>
        public string Detail { get; internal set; }
        /// <summary>所属块。</summary>
        public ConfigBlock Block { get; internal set; }

        // ---- 类型与取值 ----
        public ConfigType Type { get; internal set; }
        public float Min { get; internal set; }
        public float Max { get; internal set; }
        public float Step { get; internal set; } = 1f;
        public float DefaultValue { get; internal set; }
        private float _value;
        /// <summary>当前值（写入时按 Min/Max clamp）。</summary>
        public float Value
        {
            get => _value;
            set => _value = Clamp(value);
        }
        /// <summary>候选表（Value/Filter 用）。</summary>
        public string[] Selections { get; internal set; }
        /// <summary>候选索引（Value/Filter 的有效值就是它）。</summary>
        public int Selection
        {
            get => (int)Math.Round(_value);
            set => _value = Clamp(value);
        }

        // ---- 显示装饰 ----
        /// <summary>数值后缀字符串；null = 无。</summary>
        public string Suffix { get; internal set; }
        /// <summary>数值后缀装饰器枚举（Suffix 为 null 时生效）。</summary>
        public ConfigSuffix SuffixKind { get; internal set; } = ConfigSuffix.None;
        /// <summary>名称颜色（用于把配置项名字染色）。</summary>
        public UnityEngine.Color? NameColor { get; internal set; }

        // ---- 可见性 ----
        /// <summary>依赖项：本项只在依赖项为 true 时显示。</summary>
        public ConfigItem DependsOn { get; internal set; }
        /// <summary>额外自定义可见性条件。</summary>
        public Func<bool> VisibleWhen { get; internal set; }

        /// <summary>本项当前是否应显示。</summary>
        public bool IsVisible
        {
            get
            {
                if (DependsOn != null && !DependsOn.GetBool()) return false;
                if (VisibleWhen != null && !VisibleWhen()) return false;
                return true;
            }
        }

        /// <summary>扰动回调：值变化后由 UI 调用（用于"开启调试模式后让数量项出现"这类联动）。</summary>
        public event Action<ConfigItem> OnChanged;

        internal ConfigItem() { }

        // -----------------------------------------------------------------
        //  工厂
        // -----------------------------------------------------------------

        internal static ConfigItem CreateBool(string key, bool def, string displayName, string detail, ConfigBlock block)
        {
            var it = new ConfigItem
            {
                Key = key, DisplayName = displayName ?? key, Detail = detail, Block = block,
                Type = ConfigType.Bool, Min = 0f, Max = 1f, Step = 1f,
                DefaultValue = def ? 1f : 0f,
            };
            it._value = it.DefaultValue;
            return it;
        }

        internal static ConfigItem CreateInt(string key, int def, int min, int max, int step,
            string displayName, string detail, ConfigBlock block)
        {
            var it = new ConfigItem
            {
                Key = key, DisplayName = displayName ?? key, Detail = detail, Block = block,
                Type = ConfigType.Int, Min = min, Max = max,
                Step = step <= 0 ? 1f : step,
                DefaultValue = def,
            };
            it._value = it.Clamp(def);
            return it;
        }

        internal static ConfigItem CreateFloat(string key, float def, float min, float max, float step,
            string displayName, string detail, ConfigBlock block)
        {
            var it = new ConfigItem
            {
                Key = key, DisplayName = displayName ?? key, Detail = detail, Block = block,
                Type = ConfigType.Float, Min = min, Max = max,
                Step = step <= 0f ? 1f : step,
                DefaultValue = def,
            };
            it._value = it.Clamp(def);
            return it;
        }

        internal static ConfigItem CreateValue(string key, string[] selections, string displayName, string detail, ConfigBlock block)
        {
            if (selections == null || selections.Length == 0)
                throw new ArgumentException($"配置项 {key} 的候选表不能为空", nameof(selections));

            var it = new ConfigItem
            {
                Key = key, DisplayName = displayName ?? key, Detail = detail, Block = block,
                Type = ConfigType.Value, Selections = selections,
                Min = 0f, Max = selections.Length - 1, Step = 1f, DefaultValue = 0f,
            };
            it._value = 0f;
            return it;
        }

        internal static ConfigItem CreateFilter(string key, IReadOnlyList<ConfigFilterOption> pool,
            string displayName, string detail, ConfigBlock block)
        {
            if (pool == null || pool.Count == 0)
                throw new ArgumentException($"Filter 配置项 {key} 的候选池不能为空", nameof(pool));

            var names = new string[pool.Count];
            for (int i = 0; i < pool.Count; i++) names[i] = pool[i].Name;

            var it = new ConfigItem
            {
                Key = key, DisplayName = displayName ?? key, Detail = detail, Block = block,
                Type = ConfigType.Filter, Selections = names,
                Min = 0f, Max = names.Length - 1, Step = 1f, DefaultValue = 0f,
            };
            it._value = 0f;
            return it;
        }

        // -----------------------------------------------------------------
        //  链式配置
        // -----------------------------------------------------------------

        /// <summary>数值后缀装饰器。</summary>
        public ConfigItem WithSuffix(ConfigSuffix kind) { SuffixKind = kind; Suffix = null; return this; }

        /// <summary>自定义后缀字符串（追加在数值后）。</summary>
        public ConfigItem SetSuffix(string suffix) { Suffix = suffix; SuffixKind = ConfigSuffix.None; return this; }

        /// <summary>名称着色。</summary>
        public ConfigItem WithColor(UnityEngine.Color color) { NameColor = color; return this; }

        /// <summary>依赖另一项：它开启时本项才显示。</summary>
        public ConfigItem SetDependsOn(ConfigItem other) { DependsOn = other; return this; }

        /// <summary>自定义可见性条件。</summary>
        public ConfigItem SetVisibleWhen(Func<bool> condition) { VisibleWhen = condition; return this; }

        // -----------------------------------------------------------------
        //  取值
        // -----------------------------------------------------------------

        public bool GetBool() => _value > 0.5f;
        public int GetInt() => (int)Math.Round(_value);
        public float GetFloat() => _value;
        /// <summary>Value/Filter 型的候选名字；其它类型返回数值文本。</summary>
        public string GetSelectionName()
            => Selections != null && Selections.Length > 0 ? Selections[Mathf_ClampIndex(Selection)] : GetValueText();

        /// <summary>UI 上显示的数值文本（含后缀）。</summary>
        public string GetValueText()
        {
            string text;
            switch (Type)
            {
                case ConfigType.Bool:
                    // 勾选框行不显示数值，这里只作为兜底
                    return GetBool() ? "开" : "关";

                case ConfigType.Value:
                case ConfigType.Filter:
                    text = GetSelectionName();
                    break;

                case ConfigType.Int:
                    text = GetInt().ToString(CultureInfo.InvariantCulture);
                    break;

                default:
                    // 小数位数由 Step 决定，避免 0.30000001 这种显示
                    int decimals = Step >= 1f ? 0 : Math.Min(4, DecimalsOf(Step));
                    text = GetFloat().ToString("F" + decimals, CultureInfo.InvariantCulture);
                    break;
            }

            return text + SuffixText();
        }

        /// <summary>后缀文本（枚举 → 符号）。</summary>
        public string SuffixText() => Suffix ?? SuffixKind switch
        {
            ConfigSuffix.Second => "s",
            ConfigSuffix.Percent => "%",
            ConfigSuffix.Times => "次",
            ConfigSuffix.Multiplier => "x",
            _ => "",
        };

        /// <summary>增减步进（Bool 型为翻转，Value/Filter 型为循环切换）。</summary>
        public void Increase() => Change(+1);
        public void Decrease() => Change(-1);
        /// <summary>Bool 型专用：直接翻转。</summary>
        public void Toggle() { if (Type == ConfigType.Bool) { Value = GetBool() ? 0f : 1f; Raise(); } }

        private void Change(int dir)
        {
            switch (Type)
            {
                case ConfigType.Bool:
                    Value = GetBool() ? 0f : 1f;
                    break;

                case ConfigType.Value:
                case ConfigType.Filter:
                {
                    if (Selections == null || Selections.Length == 0) break;
                    int n = Selections.Length;
                    int idx = (Selection + dir) % n;
                    if (idx < 0) idx += n;
                    Value = idx;
                    break;
                }

                default:
                    Value = _value + dir * Step;
                    break;
            }
            Raise();
        }

        /// <summary>不触发通知地写入值（同步/加载用）。</summary>
        public void SetValueSilently(float v) => _value = Clamp(v);

        /// <summary>把值重置为默认值。</summary>
        public void ResetToDefault() { Value = DefaultValue; Raise(); }

        /// <summary>触发变更通知（UI 改完值后调用）。</summary>
        public void Raise() => OnChanged?.Invoke(this);

        private float Clamp(float v)
        {
            if (Type == ConfigType.Bool) return v > 0.5f ? 1f : 0f;
            if (Max > Min) { if (v < Min) v = Min; else if (v > Max) v = Max; }
            // 量化到 Step 的整数倍（基准为 Min），保证增减结果稳定、可编码
            if (Step > 0f)
            {
                float steps = (float)Math.Round((v - Min) / Step, MidpointRounding.AwayFromZero);
                v = Min + steps * Step;
                if (v < Min) v = Min; else if (v > Max) v = Max;
            }
            return v;
        }

        /// <summary>本项在其候选取值域内的归一化步数（同步编码用）。</summary>
        internal int StepCount()
        {
            if (Type == ConfigType.Bool) return 1;
            if (Step <= 0f) return 0;
            return (int)Math.Round((Max - Min) / Step);
        }

        private int Mathf_ClampIndex(int i)
            => Selections == null || Selections.Length == 0 ? 0 : (i < 0 ? 0 : (i >= Selections.Length ? Selections.Length - 1 : i));

        private static int DecimalsOf(float step)
        {
            // 0.25 → 2 位；0.5 → 1 位；1 → 0 位
            for (int d = 0; d <= 4; d++)
            {
                double scaled = step * Math.Pow(10, d);
                if (Math.Abs(scaled - Math.Round(scaled)) < 1e-6) return d;
            }
            return 4;
        }

        public override string ToString() => $"{Key}={GetValueText()}";
    }

    /// <summary>全局配置注册表：按 Key 索引，供同步/落盘/UI 枚举。</summary>
    public static class ConfigRegistry
    {
        private static readonly Dictionary<string, ConfigItem> _byKey = new();
        private static readonly List<ConfigItem> _all = new();
        private static readonly List<ConfigBlock> _blocks = new();

        /// <summary>所有已注册配置项（按注册顺序 = 稳定顺序，同步编码依赖它）。</summary>
        public static IReadOnlyList<ConfigItem> All => _all;
        /// <summary>所有已注册配置块。</summary>
        public static IReadOnlyList<ConfigBlock> Blocks => _blocks;

        internal static void RegisterBlock(ConfigBlock block)
        {
            if (block == null || _blocks.Contains(block)) return;
            _blocks.Add(block);
        }

        internal static void Register(ConfigItem item)
        {
            if (item == null) return;
            if (_byKey.ContainsKey(item.Key))
            {
                LightLogger.LogWarning($"[ConfigRegistry] 配置键重复：{item.Key}（后注册的将被忽略）");
                return;
            }
            _byKey[item.Key] = item;
            _all.Add(item);
            RegisterBlock(item.Block);
        }

        /// <summary>按 Key 查找（找不到返回 null）。</summary>
        public static ConfigItem Get(string key)
            => key != null && _byKey.TryGetValue(key, out var it) ? it : null;

        /// <summary>按 Key 取布尔值（找不到返回 false）。</summary>
        public static bool GetBool(string key) => Get(key)?.GetBool() ?? false;
        /// <summary>按 Key 取整数（找不到返回 0）。</summary>
        public static int GetInt(string key) => Get(key)?.GetInt() ?? 0;
        /// <summary>按 Key 取浮点（找不到返回 0）。</summary>
        public static float GetFloat(string key) => Get(key)?.GetFloat() ?? 0f;

        /// <summary>取某分类下的所有配置项。</summary>
        public static List<ConfigItem> ByCategory(ConfigCategory category)
        {
            var list = new List<ConfigItem>();
            foreach (var it in _all)
                if (it.Block != null && it.Block.Category == category) list.Add(it);
            return list;
        }
    }
}
