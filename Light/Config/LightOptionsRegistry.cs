using Light.Patches;
using LightInDark.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Light.Config;

internal class LightOptionsRegistry
{
    static bool _reg;
    static LightOptionButton? _unlockAll;
    static LightOptionButton? _dontShow;
    static LightOptionButton? _reloadConfig;
    static LightOptionButton? _showTaskPanelInMeeting;
    static LightOptionButton? _cursorIdx;
    public static void Register()
    {
        if (_reg) return;
        _reg = true;

        var s = LightPlugin.LightSettingsData ??= new LightSettings.LightSettingsData();

        _unlockAll = SettingsTabPatch.AddToggleButton(
            "解锁所有装扮", s.UnlockAllCosmic, on =>
            {
                var cur = LightPlugin.LightSettingsData ??= new LightSettings.LightSettingsData();
                cur.UnlockAllCosmic = on;
                LightSettings.Save(cur);
                LightSettings.ReloadConfig();
            }
            );

        _dontShow = SettingsTabPatch.AddToggleButton(
            "装扮简洁模式", s.DontShowCosmic, on =>
            {
                var cur = LightPlugin.LightSettingsData ??= new LightSettings.LightSettingsData();
                cur.DontShowCosmic = on;
                LightSettings.Save(cur);
                LightSettings.ReloadConfig();
            }, "不显示任何人的装扮"
            );
        _reloadConfig = SettingsTabPatch.AddActionButton("重载配置",()=>{ LightSettings.ReloadConfig(); },"手动重载配置");
        _showTaskPanelInMeeting = SettingsTabPatch.AddToggleButton("在会议中显示任务面板",s.ShowTaskPanelInMeeting, 
            on => 
            { 
                var cur = LightPlugin.LightSettingsData ??= new LightSettings.LightSettingsData();
                cur.ShowTaskPanelInMeeting = on;
                LightSettings.Save(cur);
                LightSettings.ReloadConfig();
            });
        _cursorIdx = SettingsTabPatch.AddSelectorButton("鼠标样式", ["不使用MOD光标", "全家福", "全家福2"], Cursor.Index,
         idx => 
         {
             var result = Cursor.ChangeCursorFromIndex(idx);
             if (result == true) return;

             LightLogger.LogWarning($"[LightOptions] 切换鼠标样式失败（idx={idx}，结果={(result == null ? "null：贴图未加载" : "false：索引非法")}），把显示回滚成实际值");
             SyncFromSettings();
         });

        SettingsTabPatch.LightTabOpened += SyncFromSettings;
    }

    /// <summary>
    /// 配置被重新加载（<see cref="LightSettings.ReloadConfig"/>）之后，
    /// 把 Light 设置页签上显示的值同步成最新配置（不通过行点击改的值也能同步）。
    /// </summary>
    public static void SyncFromSettings()
    {
        var s = LightPlugin.LightSettingsData;
        if (s == null) return;

        SyncToggle(_unlockAll, s.UnlockAllCosmic);
        SyncToggle(_dontShow, s.DontShowCosmic);
        SyncToggle(_showTaskPanelInMeeting, s.ShowTaskPanelInMeeting);

        SyncSelector(_cursorIdx, Cursor.Index);

        SettingsTabPatch.Refresh();
    }

    private static void SyncToggle(LightOptionButton? btn, bool value)
    {
        if (btn == null) return;
        btn.IsOn = value;
        btn.ValueText = value ? "启用" : "禁用";
    }

    /// <summary>把选择器显示同步成实际索引（同时修正 SelectorIndex，避免下次点击循环时错位）。</summary>
    private static void SyncSelector(LightOptionButton? btn, int index)
    {
        if (btn == null) return;

        var options = btn.SelectorOptions;
        if (options == null || options.Length == 0) return;

        if (index < 0 || index >= options.Length) index = 0;

        btn.SelectorIndex = index;
        btn.ValueText = options[index];
    }
}
