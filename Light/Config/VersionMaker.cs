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
            var orig = new { Light = LightPlugin.Version,LightInDark = LIDPlugin.Version };
            string json = JsonSerializer.Serialize(orig,new JsonSerializerOptions { WriteIndented = true});
            string path = Path.Combine(Paths.PluginPath, "version.json");
            File.WriteAllText(path, json);
            return true;
        }
        catch(Exception ex)
        {
            LightLogger.LogError($"写入version.json时异常：{ex.Message}");
            return false;
        }
    }
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
