using BepInEx;
using LightInDark;
using LightInDark.Core;
using LightInDark.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Light.Config;

public class VersionMaker
{
    public static bool MakeVersion()
    {
        try
        {
            string path = Path.Combine(Paths.PluginPath, "version.json");

            #region 新增：写入前检测版本变化（更新后用于自动弹新闻）
            JustUpdated = false;
            _updatedConsumed = false;
            try
            {
                if (File.Exists(path))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(path));
                    if (doc.RootElement.TryGetProperty("Light", out var oldProp))
                    {
                        string? oldVer = oldProp.GetString();
                        if (!string.IsNullOrEmpty(oldVer) && IsNewerVersion(LightPlugin.Version, oldVer!))
                        {
                            JustUpdated = true;
                            LightLogger.Log($"[Version] 检测到版本更新：{oldVer} -> {LightPlugin.Version}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[Version] 读取旧 version.json 失败：{ex.Message}");
            }
            #endregion

            var orig = new { Light = LightPlugin.Version,LightInDark = LIDPlugin.Version };
            string json = JsonSerializer.Serialize(orig,new JsonSerializerOptions { WriteIndented = true});
            File.WriteAllText(path, json);
            return true;
        }
        catch(Exception ex)
        {
            LightLogger.LogError($"写入version.json时异常：{ex.Message}");
            return false;
        }
    }

    #region 新增：版本变化标记（供"更新后自动弹新闻"使用）
    /// <summary>本次启动是否检测到版本更新（由 MakeVersion 在写入前比较得出）</summary>
    public static bool JustUpdated { get; private set; }

    private static bool _updatedConsumed = true;

    /// <summary>
    /// 取用一次"刚更新"标记：同一次启动只会返回一次 true，
    /// 避免每次进出主菜单都反复弹新闻。
    /// </summary>
    public static bool ConsumeJustUpdated()
    {
        if (JustUpdated && !_updatedConsumed)
        {
            _updatedConsumed = true;
            return true;
        }
        return false;
    }

    /// <summary>
    /// 当前版本是否比旧版本新。版本号形如 1.0.0（带 v 前缀请先去掉），
    /// 解析不了时退化成"不相等即视为更新"。
    /// </summary>
    private static bool IsNewerVersion(string current, string previous)
    {
        if (System.Version.TryParse(current, out var c) && System.Version.TryParse(previous, out var p))
            return c > p;
        return !string.Equals(current, previous, StringComparison.OrdinalIgnoreCase);
    }
    #endregion

    public static readonly string UpdaterExeName = "LightInDarkUpdater.exe";

    public static string CheckForUpdate()
    {
        try
        {
            string path = Path.Combine(Paths.GameRootPath, UpdaterExeName);
            if (!File.Exists(path))
            {
                LightLogger.LogError($"未找到 {UpdaterExeName}，请确保它位于游戏根目录。");
                return "path error";
            }
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8
            };
            try
            {
                using (Process process = Process.Start(startInfo)!)
                {
                    string output = process!.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit();
                    return output;
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogError($"更新检查失败: {ex.Message}");
                return "check error";
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[VersionMaker.CheckForUpdate]", ex); return default!;
        }
    }
    public static void StartUpdateProcess()
    {
        try
        {
            string exePath = Path.Combine(Paths.GameRootPath, UpdaterExeName);
            if (!File.Exists(exePath)) return;

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = "--listen",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal
            };

            try
            {
                Process.Start(startInfo);
                LightUtils.ShowCustomDisconnectWindow("更新器已在后台启动，等待游戏退出后自动更新。");
            }
            catch (Exception ex)
            {
                LightUtils.ShowCustomDisconnectWindow($"启动更新器失败。\n请将游戏目录下的Light.log发送给开发者或者QQ群中。\n不要直接将此界面截图/拍照给其他人。");
                LightLogger.LogError($"启动更新器失败：{ex.Message}");
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[VersionMaker.StartUpdateProcess]", ex);
        }
    }
}
