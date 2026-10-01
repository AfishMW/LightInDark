using System.Collections.Generic;
using AmongUs.GameOptions;
using HarmonyLib;
using LightInDark.Core;

namespace LightInDark.Configuration
{
    /// <summary>
    /// 让原版只认 <c>StringNames</c> 翻译键的接口能显示我们的自定义文本。
    ///
    /// 背景：<c>NotificationPopper.AddSettingsChangeMessage(StringNames key, string value, ...)</c>
    /// 内部会 <c>TranslationController.GetString(key)</c>，所以配置项显示名如果直接传进去
    /// 只会得到 "<InvalidString>" 之类。这里 patch <c>GetString</c>：
    /// 先查我们的槽位表，命中就返回自定义文本，否则原样交给原版。
    ///
    /// 槽位：占用 StringNames 里一批**未被原版使用**的高位枚举值。
    /// 我们只挪用 <see cref="SlotCount"/> 个，且只在"确实被我们注册过"时才返回自定义文本，
    /// 因此对原版行为没有副作用。
    /// </summary>
    public static class ConfigTranslationPatch
    {
        /// <summary>占用多少个 StringNames 槽位（同时能显示的配置项数上限）。</summary>
        public const int SlotCount = 32;

        /// <summary>占用 StringNames 的最高位起始值（原版枚举远低于此）。</summary>
        public const int SlotBase = 9000;

        private static readonly Dictionary<int, ConfigItem> _slotToItem = new();
        private static readonly Dictionary<string, int> _keyToSlot = new();
        private static int _nextSlot;

        /// <summary>取（或分配）某个配置项占用的 StringNames 槽位。</summary>
        public static StringNames SlotFor(ConfigItem item)
        {
            if (item == null) return StringNames.None;

            if (_keyToSlot.TryGetValue(item.Key, out int existing))
                return (StringNames)existing;

            if (_nextSlot >= SlotCount)
            {
                LightLogger.LogWarning($"[ConfigTranslation] 槽位用尽，{item.Key} 的提示将显示为原版文本");
                return StringNames.None;
            }

            int slot = SlotBase + _nextSlot++;
            _keyToSlot[item.Key] = slot;
            _slotToItem[slot] = item;
            LightLogger.Log($"[ConfigTranslation] 配置项 {item.Key} → StringNames 槽位 {slot}");
            return (StringNames)slot;
        }

        /// <summary>查槽位对应的自定义文本；不是我们的槽位时返回 null。</summary>
        internal static string TryGetCustom(int stringNameValue)
        {
            if (_slotToItem.TryGetValue(stringNameValue, out var item))
                return item.DisplayName ?? item.Key;
            return null;
        }
    }

    /// <summary>
    /// patch 原版取字符串的入口，把我们的槽位翻译成配置项显示名。
    /// </summary>
    [HarmonyPatch]
    public static class ConfigTranslationStringPatch
    {
        /// <summary>
        /// 注意：<c>TranslationController.GetString</c> 有多个重载，
        /// 这里只拦最常用的 (StringNames, object[]) 那个。
        /// Prefix 返回 false 表示"我们接管了返回值"。
        /// </summary>
        [HarmonyPatch(typeof(TranslationController), nameof(TranslationController.GetString),
            new[] { typeof(StringNames), typeof(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppSystem.Object>) })]
        [HarmonyPrefix]
        public static bool GetStringPrefix(StringNames id, ref string __result)
        {
            var custom = ConfigTranslationPatch.TryGetCustom((int)id);
            if (custom == null) return true;      // 不是我们的槽位 → 原版处理

            __result = custom;
            return false;
        }
    }
}
