using System;
using HarmonyLib;
using LightInDark.Configuration;
using LightInDark.Core;
using LightInDark.Language;

namespace Light.Patches
{
    /// <summary>
    /// 调试模式。
    ///
    /// 本轮只做一件事：**开启后游戏无法正常结束**。
    /// 做法是拦掉 <see cref="GameManager.RpcEndGame"/> —— 原版所有结束路径
    /// （LogicGameFlowNormal / LogicGameFlowHnS / GameManager 内部的任务胜利等）
    /// 最终都汇总到这一个方法，所以一个 Prefix 就能全覆盖。
    ///
    /// 另外把 <c>ShouldCheckForGameEnd</c> 压住，避免原版逻辑每帧重复尝试结束。
    /// </summary>
    public static class DebugMode
    {
        /// <summary>配置键常量（供其它模块引用）。</summary>
        public const string KeyEnabled = "lid.debug.enabled";
        public const string KeyDummyCount = "lid.debug.dummyCount";

        /// <summary>调试模式是否开启。</summary>
        public static bool Enabled => ConfigRegistry.GetBool(KeyEnabled);

        /// <summary>要生成的假人数量。</summary>
        public static int DummyCount => ConfigRegistry.GetInt(KeyDummyCount);
    }

    /// <summary>
    /// 调试模式下禁止结束游戏。
    /// 返回 false 直接跳过原版 RpcEndGame 的方法体 → 不会 StartEndGame、不会 OnGameEnd。
    /// </summary>
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.RpcEndGame))]
    public static class DebugBlockGameEndPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(GameOverReason endReason)
        {
            try
            {
                if (!DebugMode.Enabled) return true;      // 未开调试 → 原版行为

                // 调试模式：压住"该检查结束了"的标志，并把结束请求吞掉
                var gm = GameManager.Instance;
                if (gm != null) gm.ShouldCheckForGameEnd = false;

                LightLogger.Log($"[DebugMode] 已阻止游戏结束（原因为 {endReason}）");
                return false;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[DebugBlockGameEndPatch.Prefix]", ex);
                return true;                              // 出错时放行，别把游戏卡死
            }
        }
    }

    /// <summary>
    /// 调试模式下持续压住结束检查。
    /// 原版 <c>LogicGameFlowNormal.Update</c> 每帧会重算胜负，
    /// 只拦 RpcEndGame 的话它仍会反复尝试；这里把标志位一直摁住。
    /// </summary>
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.FixedUpdate))]
    public static class DebugSuppressEndCheckPatch
    {
        [HarmonyPostfix]
        public static void Postfix(GameManager __instance)
        {
            try
            {
                if (!DebugMode.Enabled) return;
                __instance.ShouldCheckForGameEnd = false;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[DebugSuppressEndCheckPatch.Postfix]", ex);
            }
        }
    }

    /// <summary>
    /// 调试设置的配置块声明。
    /// 分类头用金色（<see cref="HeaderGold"/>），下面挂两个配置项：
    ///  ① 启用调试模式（勾选框，红字）
    ///  ② 生成假人的数量（0~14，仅当①开启时出现）
    /// </summary>
    public static class DebugConfig
    {
        /// <summary>分类头金色。</summary>
        public static readonly UnityEngine.Color HeaderGold = new(0.85f, 0.68f, 0.20f, 1f);
        /// <summary>"启用调试模式"的红字颜色。</summary>
        public static readonly UnityEngine.Color WarnRed = new(0.93f, 0.26f, 0.22f, 1f);

        /// <summary>调试设置块（金色分类）。</summary>
        public static ConfigBlock Block { get; private set; }

        /// <summary>启用调试模式。</summary>
        public static ConfigItem Enabled { get; private set; }

        /// <summary>生成假人的数量。</summary>
        public static ConfigItem DummyCount { get; private set; }

        private static bool _registered;

        /// <summary>注册调试配置块（幂等，插件启动时调用一次）。</summary>
        public static void Register()
        {
            if (_registered) return;
            _registered = true;

            try
            {
                Block = new ConfigBlock("lid.debug", Language.Translate("config.debug.title", "调试设置"), ConfigCategory.Debug)
                    .SetHeaderColor(HeaderGold);

                Enabled = Block.AddConfiguration(
                    DebugMode.KeyEnabled, false,
                    Language.Translate("config.debug.enabled", "启用调试模式"),
                    Language.Translate("config.debug.enabled.detail", "调试模式下游戏无法正常结束。"));

                DummyCount = Block.AddConfiguration(
                    DebugMode.KeyDummyCount, 0, 0, 14, 1,
                    Language.Translate("config.debug.dummyCount", "生成假人的数量"),
                    Language.Translate("config.debug.dummyCount.detail", "开局时生成的假人（AI）数量。"))
                    .WithSuffix(ConfigSuffix.None)
                    .SetDependsOn(Enabled);

                // 值变化后走同步（只有房主生效）并弹原版设置变更提示
                Enabled.OnChanged += item => ConfigSync.RaiseAndSync(item);
                DummyCount.OnChanged += item => ConfigSync.RaiseAndSync(item);

                // "启用调试模式"用红字显示
                Enabled.WithColor(WarnRed);

                LightLogger.Log($"[DebugConfig] 调试配置块已注册：{Block.Key}（{Block.Items.Count} 项）");
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[DebugConfig.Register]", ex);
            }
        }
    }
}
