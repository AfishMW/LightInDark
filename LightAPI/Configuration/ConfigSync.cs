using System;
using System.Collections.Generic;
using AmongUs.GameOptions;
using HarmonyLib;
using InnerNet;
using LightInDark.Core;
using UnityEngine;

namespace LightInDark.Configuration
{
    /// <summary>
    /// 配置项同步器：借用原版 <c>Int32OptionNames.Tag</c> 这一个自由范围的整数字段做通道。
    ///
    /// 为什么用 Tag：
    ///  - 原版 <c>IGameOptions.SetInt/SetFloat/SetBool</c> 都是**枚举白名单 switch**，
    ///    自定义键会静默失败（只写日志，不抛异常）→ 无法新增原版选项键。
    ///  - <c>NormalGameOptionsV12.Serialize</c> 是**24 个字段的硬编码表**
    ///    （其中 <c>writer.Write((byte)gameOptions.Tag)</c>）→ 走 GameOptions 的值会自动随
    ///    <c>RpcSyncSettings</c> 广播给所有客户端，也不需要自建 RPC。
    ///  - Tag 在序列化里是**1 字节**，所以只有 0~255。
    ///
    /// 编码方案：把配置项按 <see cref="ConfigRegistry.All"/> 的稳定注册顺序切成 8 个区段，
    /// 每一区段的值打包进 Tag 的 8 个 bit 之一（`Tag = Σ (bit_i &lt;&lt; i)`）。
    /// 这样一个"屏"最多同步 8 项；需要更多项时用 <see cref="PageCount"/> 分屏，
    /// 每次改动只写当前页。当前配置数量（调试块 2 项）远小于 8。
    /// </summary>
    public static class ConfigSync
    {
        /// <summary>Tag 是 1 字节，天然 8 个 bit。</summary>
        public const int BitsPerPage = 8;

        private static bool _applyingRemote;

        /// <summary>总页数。</summary>
        public static int PageCount
            => Mathf.Max(1, Mathf.CeilToInt(ConfigRegistry.All.Count / (float)BitsPerPage));

        /// <summary>当前页（由 UI 打开的分类决定；默认 0）。</summary>
        public static int CurrentPage { get; set; }

        private static IGameOptions Options => GameOptionsManager.Instance?.CurrentGameOptions;

        // =====================================================================
        //  读取：把远程/本地的 Tag 解回配置项
        // =====================================================================

        /// <summary>
        /// 按当前 Tag 值刷新本页所有配置项的值。
        /// 由 Harmony 在反序列化完成后调用（见 <see cref="ConfigSyncPatches"/>）。
        /// </summary>
        public static void ApplyFromTag()
        {
            try
            {
                var opts = Options;
                if (opts == null) return;

                int tag;
                if (!opts.TryGetInt(Int32OptionNames.Tag, out tag)) return;

                var all = ConfigRegistry.All;
                if (all.Count == 0) return;

                int start = CurrentPage * BitsPerPage;
                _applyingRemote = true;
                for (int i = 0; i < BitsPerPage; i++)
                {
                    int index = start + i;
                    if (index >= all.Count) break;

                    int packed = (tag >> i) & 1;
                    var item = all[index];
                    float v = Unpack(item, packed);
                    if (Math.Abs(v - item.GetFloat()) > 1e-4f)
                    {
                        item.SetValueSilently(v);
                        LightLogger.Log($"[ConfigSync] 应用远端值 {item.Key} → {v}");
                    }
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[ConfigSync.ApplyFromTag]", ex);
            }
            finally
            {
                _applyingRemote = false;
            }
        }

        // =====================================================================
        //  写入：把配置项打包进 Tag 并广播
        // =====================================================================

        /// <summary>
        /// 某项的值变化后调用：重算本页 Tag、写入原版 GameOptions、同步给所有人、弹原版提示。
        /// 只有房主生效（与 Nebula 一致：客户端改不动房主的规则）。
        /// </summary>
        public static void NotifyChanged(ConfigItem changed)
        {
            if (_applyingRemote) return;                 // 正在应用远端值，别回环广播
            if (changed == null) return;

            try
            {
                var client = AmongUsClient.Instance;
                if (client == null) return;

                if (!client.AmHost)
                {
                    LightLogger.Log($"[ConfigSync] 非房主，忽略对 {changed.Key} 的修改");
                    return;
                }

                WriteTagAndBroadcast(changed);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[ConfigSync.NotifyChanged]", ex);
            }
        }

        /// <summary>把当前页所有配置项打包写进 Tag 并广播 + 提示。</summary>
        public static void WriteTagAndBroadcast(ConfigItem changed = null)
        {
            try
            {
                var opts = Options;
                if (opts == null) return;

                var all = ConfigRegistry.All;
                int start = CurrentPage * BitsPerPage;
                int tag = 0;
                for (int i = 0; i < BitsPerPage; i++)
                {
                    int index = start + i;
                    if (index >= all.Count) break;
                    if (Pack(all[index]) != 0) tag |= 1 << i;
                }

                opts.SetInt(Int32OptionNames.Tag, tag);
                LightLogger.Log($"[ConfigSync] 写入 Tag=0b{Convert.ToString(tag, 2).PadLeft(8, '0')}（页 {CurrentPage}，{changed?.Key ?? "-"}）");

                BroadcastSettings();
                NotifyVanilla(changed);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[ConfigSync.WriteTagAndBroadcast]", ex);
            }
        }

        /// <summary>调用原版同步（房主 → 所有客户端）并弹原版右下角设置变更提示。</summary>
        private static void BroadcastSettings()
        {
            try
            {
                var client = AmongUsClient.Instance;
                // SyncOptions 内部会 PlayerControl.LocalPlayer.RpcSyncSettings(...)，
                // 不在房间里时 LocalPlayer 为 null → 必须先确认在游戏中且是房主。
                if (client == null || !client.AmHost) return;
                if (client.GameState != InnerNetClient.GameStates.Started) return;
                if (PlayerControl.LocalPlayer == null) return;

                // 照抄原版设置菜单的写法（GameOptionsMenu.ValueChanged）：
                // GameHostOptions = CurrentGameOptions → LogicOptions.SyncOptions()
                var mgr = GameOptionsManager.Instance;
                if (mgr == null) return;
                mgr.GameHostOptions = mgr.CurrentGameOptions;

                GameManager.Instance?.LogicOptions?.SyncOptions();
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[ConfigSync.BroadcastSettings] 同步失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 弹原版设置变更提示。
        /// ⚠️ 原版 <c>AddSettingsChangeMessage</c> 的第一个参数是 <see cref="StringNames"/> 翻译键，
        /// 模组无法传自定义文本 → 我们用 <see cref="ConfigTranslationPatch"/> 把一个空闲的
        /// StringNames 值映射到我们的自定义文本上。
        /// </summary>
        private static void NotifyVanilla(ConfigItem changed)
        {
            try
            {
                var hud = HudManager.Instance;
                if (hud?.Notifier == null) return;
                if (changed == null) return;

                // LobbyChangeSettingNotification 会拼成 "已将设置改为 X:Y" 之类，
                // key 用我们占用的 StringNames 槽位 → GetString 被 patch 成显示配置项名。
                hud.Notifier.AddSettingsChangeMessage(
                    ConfigTranslationPatch.SlotFor(changed),
                    changed.GetValueText(),
                    false,                       // 不播声音（避免刷屏）
                    RoleTypes.Crewmate);
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[ConfigSync.NotifyVanilla] {ex.Message}");
            }
        }

        // =====================================================================
        //  打包 / 解包
        // =====================================================================

        /// <summary>把一项配置压成 0/1（bit）。</summary>
        private static int Pack(ConfigItem item)
        {
            switch (item.Type)
            {
                case ConfigType.Bool:
                    return item.GetBool() ? 1 : 0;

                case ConfigType.Value:
                case ConfigType.Filter:
                    // 候选表超过 2 项时，只有"当前是否与默认不同"能塞进 1 bit。
                    // 需要完整候选同步时应扩到多 bit / 多页（当前调试块用不到）。
                    return item.Selection == 0 ? 0 : 1;

                default:
                    // Int/Float：StepCount 为 1 时（如 0~1）可直接表示；
                    // 否则按"是否非默认"降级为 1 bit，完整值靠本地 JSON 兜底。
                    return item.StepCount() <= 1
                        ? (item.GetFloat() > item.Min + item.Step * 0.5f ? 1 : 0)
                        : (Math.Abs(item.GetFloat() - item.DefaultValue) > 1e-4f ? 1 : 0);
            }
        }

        /// <summary>把 0/1 还原成该项的合法取值。</summary>
        private static float Unpack(ConfigItem item, int bit)
        {
            switch (item.Type)
            {
                case ConfigType.Bool:
                    return bit != 0 ? 1f : 0f;

                case ConfigType.Value:
                case ConfigType.Filter:
                    if (bit == 0) return item.Min;
                    // 候选表只有 2 项时 bit=1 就是第 2 项，否则退化为默认（本地值优先）
                    return item.Selections != null && item.Selections.Length == 2 ? 1f : item.GetFloat();

                default:
                    if (item.StepCount() <= 1) return bit != 0 ? item.Max : item.Min;
                    // 多步进项：bit 只表达"非默认"，真正数值由本地 JSON 决定，这里不覆盖
                    return item.GetFloat();
            }
        }

        /// <summary>把某个配置项的值广播出去（供 UI 调用）。</summary>
        public static void RaiseAndSync(ConfigItem item)
        {
            if (item == null) return;
            item.Raise();
            NotifyChanged(item);
        }
    }

    /// <summary>
    /// 让原版反序列化完成后自动把 Tag 解回配置项。
    /// <c>NormalGameOptionsV12.Deserialize</c> 是 static 方法，返回构造好的实例。
    /// </summary>
    [HarmonyPatch]
    public static class ConfigSyncPatches
    {
        [HarmonyPatch(typeof(NormalGameOptionsV12), nameof(NormalGameOptionsV12.Deserialize))]
        [HarmonyPostfix]
        public static void NormalDeserializePostfix()
        {
            ConfigSync.ApplyFromTag();
        }

        [HarmonyPatch(typeof(HideNSeekGameOptionsV12), nameof(HideNSeekGameOptionsV12.Deserialize))]
        [HarmonyPostfix]
        public static void HnsDeserializePostfix()
        {
            ConfigSync.ApplyFromTag();
        }
    }
}
