global using HarmonyLib;
global using Light.Utilities;
global using System.Collections;
global using UnityEngine;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Light.ChatCommands;
using Light.Config;
using Light.News;
using Light.Patches;
using Light.Roles.Crewmates;
using Light.Roles.Impostors;
using Light.Roles.Vanilla;
using LightInDark.Core;
using LightInDark.Events;
using LightInDark.Language;
using LightInDark.Roles;
using LightInDark.RPCs;
using System;
using System.Text.Json;
using UnityEngine.SceneManagement;

namespace Light;

[BepInPlugin(Id, Name, Version)]
[BepInProcess("Among Us.exe")]
[BepInDependency("com.moonscar.lightapi",BepInDependency.DependencyFlags.HardDependency)]
public partial class LightPlugin : BasePlugin
{
    public const string Id = "com.moonscar.lightindark";
    public const string Name = "LightInTheDark";
    public const string Version = "1.0.0";

    public const string VisualVersion = "v1.0.0";

    public const string RichVersion = "<color=#4FD1C5>ver</color> <color=#38B2AC>1.0.0</color>";
    public static string AUVersion;
    public static string CursurDataPath = Application.persistentDataPath;
    public static MainColor.ModColorData ColorData;
    public static LightSettings.LightSettingsData LightSettingsData;
    public Harmony Harmony { get; } = new(Id);
    public static string LightUserDataPath => Path.Combine(Application.persistentDataPath, "LightInDark");
    public const string ModGuid = "1c3cba75-359d-4504-a4dc-b0916e3f3010"; // GUID，这辈子不能改。
    internal static ManualLogSource StaticLog { get; private set; } = null!;

    public override void Load()
    {
        try
        {
            //FirstChanceExceptionLogger.Initialize();
            StaticLog = Log;
            Harmony.PatchAll();
            CurrentModRegistration.ModRegistrationGuidString = ModGuid; // 感谢树懒18.0+提供的牛逼Modded方法。
            Log.LogInfo($"Mod Guid {CurrentModRegistration.ModRegistrationGuidString},解析{CurrentModRegistration.TryGetModRegistrationGuid(out _)},协议版本{Constants.GetBroadcastVersion()}");
            LightSettingsData = LightSettings.LoadSettingData();
            if (!VersionMaker.MakeVersion())
                Log.LogError($"VM json 加载失败。具体异常请查看Light.log。");
            LoadCommand();
            LightOptionsRegistry.Register();
            DebugConfig.Register();          // 配置块：调试设置（金色分类）
            EventSystem.RegisterAssembly(typeof(LightPlugin).Assembly);
            ExtractLanguageFiles();
            Language.Load();
            LidRpcRegistry.ScanAndPatch(Harmony);
            ColorData = MainColor.LoadChatColor();
            PaletteColorOverride.Apply();
            LoadRole();
            RoleConfigRegistrar.Register();   // 职业配置块：数量/概率 + 职业专属项
            Dispatcher.Initialize();
#if !DEBUG
            LightLogger.ClearLog();
#endif
            RpcDefinitions.OnFreeChatStateChanged += show => ShowChatPatch.NeedShowFreeChat = show;
            AddCursorComponent();
            RegisterShowModStampOnMainMenu();
            ChatHistoryLogUtils.Init();
            // [已禁用-握手系统] 先确保模组可玩性，握手验证暂停（2026-09-26）。
            // 恢复时取消注释下一行，并同步服务器 official.json 的 hash。
            // Handshake.HandshakeManager.Initialize();
            NewsManager.LoadNews();
            Log.LogInfo($"模组 {Name} v{Version} 已加载！");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[LightPlugin.Load]", ex);
        }
    }
    private static void RegisterShowModStampOnMainMenu()
    {
        try
        {
            SceneManager.add_sceneLoaded((Action<Scene, LoadSceneMode>)((scene, mode) =>
            {
                try
                {
                    if (scene.name == "MainMenu")
                        ModManager.Instance.ShowModStamp();
                }
                catch (Exception ex)
                {
                    LightLogger.LogError("[LightPlugin.ShowModStampOnMainMenu]", ex);
                }
            }));
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[LightPlugin.RegisterShowModStampOnMainMenu]", ex);
        }
    }

    private static void AddCursorComponent()
    {
        try
        {
            Light.Config.Cursor.Initialize();
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[LightPlugin.AddCursorComponent]", ex);
        }
    }

    /// <summary>
    /// 解压语言文件到游戏目录 Language 文件夹。
    /// 文件不存在 → 直接写出；已存在 → 只补充缺失的词条（不覆盖用户已有的翻译）。
    /// 这样新增职业的本地化词条会随版本更新自动合并进旧语言文件。
    /// </summary>
    private static void ExtractLanguageFiles()
    {
        try
        {
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Language");
            Directory.CreateDirectory(folder);
            var asm = typeof(LightPlugin).Assembly;
            foreach (var res in new[] { "Light.Resources.Language.SChinese.json", "Light.Resources.Language.English.json" })
            {
                string fileName = res.Substring(res.LastIndexOf('.', res.LastIndexOf('.') - 1) + 1);
                string path = Path.Combine(folder, fileName);

                Dictionary<string, string> embedded;
                using (var stream = asm.GetManifestResourceStream(res))
                {
                    if (stream == null) continue;
                    using var reader = new StreamReader(stream);
                    embedded = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd());
                }
                if (embedded == null || embedded.Count == 0) continue;

                if (!File.Exists(path))
                {
                    File.WriteAllText(path, JsonSerializer.Serialize(embedded, new JsonSerializerOptions { WriteIndented = true }), System.Text.Encoding.UTF8);
                    continue;
                }

                // 已存在：合并缺失词条
                var disk = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path, System.Text.Encoding.UTF8));
                if (disk == null) { disk = new Dictionary<string, string>(); }
                int added = 0;
                foreach (var kv in embedded)
                {
                    if (!disk.ContainsKey(kv.Key)) { disk[kv.Key] = kv.Value; added++; }
                }
                if (added > 0)
                {
                    File.WriteAllText(path, JsonSerializer.Serialize(disk, new JsonSerializerOptions { WriteIndented = true }), System.Text.Encoding.UTF8);
                    StaticLog.LogInfo($"语言文件 {fileName} 合并了 {added} 条新词条");
                }
            }
        }
        catch (Exception ex)
        {
            StaticLog.LogWarning($"解压语言文件失败：{ex.Message}");
        }
    }

    private static void LoadCommand()
    {
        try
        {
            var harmony = new Harmony("Light.cmd.harmony");
            var orig = AccessTools.Method(typeof(ChatController), "SendChat");
            if (orig == null) return;
            var prefixMethod = AccessTools.Method(typeof(PatchManager), nameof(PatchManager.OnSendChat));
            if (prefixMethod == null) return;
            harmony.Patch(orig, new HarmonyMethod(prefixMethod));
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[LightPlugin.LoadCommand]", ex);
        }
    }
    private void LoadRole()
    {
        try
        {
            RoleRegistry.Register<Caller>();
            RoleRegistry.Register<BloodThirstyKiller>();
            RoleRegistry.Register(VanillaImpostor.Instance);
            RoleRegistry.Register(VanillaCrewmate.Instance);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[LightPlugin.LoadRole]", ex);
        }
    }
    public static class FirstChanceExceptionLogger
    {
        static bool _init = false;
        static readonly object _lock = new();
        public static void Initialize()
        {
            if (_init) return;
            lock (_lock)
            {
                if (_init) return;
                AppDomain.CurrentDomain.FirstChanceException += (sender, e) =>
                {
                    try
                    {
                        string dir = @"D:\log";
                        if(!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                        string name = $"FIRST_CHANCE{DateTime.Now:yyyyMMdd_HHmmss_fff}.txt";
                        string pathF = Path.Combine(dir, name);
                        File.WriteAllText(pathF,e.Exception.ToString());
                    }
                    catch(Exception ex)
                    {
                        LightLogger.LogError($"FirstChance捕获失败",ex);
                    }
                };
                _init = true;
            }
        }
    }
}

/// <summary>
/// 当颜色配置无效时抛出。
/// </summary>
public class InvalidColorTypeException : Exception
{
    public InvalidColorTypeException(string message) : base(message) { }
    public InvalidColorTypeException(string message, Exception innerException) : base(message, innerException) { }
}

