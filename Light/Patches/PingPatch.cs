using AmongUs.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TMPro;

namespace Light.Patches;

[HarmonyPatch(typeof(PingTracker), nameof(PingTracker.Update))]
public static class BetterPingTrackerPatch
{
    private static int _lastFps = -1;
    private static float _lastFpsUpdateTime = -1f;

    public static bool Prefix(PingTracker __instance)
    {
        try
        {
            // 完全接管原版 PingTracker.Update（return false 跳过 vanilla）：
            // 原版会在练习模式(FreePlay)把 PingTracker 本体 SetActive(false)，
            // 失活后 Update 与补丁都不再执行，且没有任何代码重新激活它 → 文字"消失后回不来"。
            // 接管后 PingTracker 永不失活，从根上消除该问题。
            if (__instance.text == null)
                return false;

            // PING 数值：直接从 AmongUsClient.Ping 读取（不再解析文字）
            int value = AmongUsClient.Instance?.Ping ?? 0;

            Color color = GetValueColor(value, 0, 1000);
            string hex = ColorUtility.ToHtmlStringRGB(color);
            string pingText = $"<color=#{hex}>PING:{value}ms</color>";

            int fps = (int)(1f / Time.smoothDeltaTime);
            float now = Time.time;

            if (_lastFps < 0)
            {
                _lastFps = fps;
                _lastFpsUpdateTime = now;
            }
            else if (Mathf.Abs(fps - _lastFps) > 1)
            {
                _lastFps = fps;
                _lastFpsUpdateTime = now;
            }
            else
            {
                float interval = Mathf.Lerp(3f, 1f, Mathf.Clamp01((float)_lastFps / 60f));

                if (now - _lastFpsUpdateTime >= interval)
                {
                    _lastFps = fps;
                    _lastFpsUpdateTime = now;
                }
            }

            fps = _lastFps;

            Color fpsColor = GetInverseValueColor(fps, 0, 60);
            string hexFps = ColorUtility.ToHtmlStringRGB(fpsColor);
            string fpsText = $"<color=#{hexFps}>FPS - {fps}</color>";

            __instance.text.text = $"<size=110%><color=#F5D48A>Light In Dark {LightPlugin.VisualVersion}</color></size>\n<size=75%> by Moon-Scar 制作组</size>\n<color=red>模组社群:1101385982</color>\n{pingText} | {fpsText}";
            __instance.text.alignment = TextAlignmentOptions.TopRight;
            var pos = __instance.GetComponent<AspectPosition>();
            if (pos == null) pos = __instance.gameObject.AddComponent<AspectPosition>();
            pos.Alignment = AspectPosition.EdgeAlignments.RightTop;
            float offsetX = 2f;
            if (HudManager.InstanceExists && HudManager.Instance.Chat.chatButton.gameObject.active) offsetX = 2.8f;
            pos.DistanceFromEdge = new Vector3(offsetX, 0.2f, -800f);
            pos.updateAlways = true;
            bool shouldHide = false;

            // 会议中隐藏
            try
            {
                var mh = MeetingHud.Instance;
                shouldHide |= mh != null && mh.isActiveAndEnabled;
            }
            catch { } //不在会议

            try { shouldHide |= GameSettingMenu.Instance?.gameObject.active ?? false; }
            catch { }
            try { shouldHide |= FriendsListUI.Instance?.gameObject.active ?? false; }
            catch { }

            __instance.text.gameObject.SetActive(!shouldHide);

            return false;
        }
        catch
        {
            // 出错也跳过原版，避免 FreePlay 时原版把对象关掉导致"回不来"
            return false;
        }
    }

    private static Color GetValueColor(int val, int min = 0, int max = 1000)
    {
        Color green = new Color(0.2f, 1f, 0.1f);
        Color red = new Color(1f, 0.1f, 0.1f);

        if (val < min)
        {
            return green;
        }

        if (val > max)
        {
            return red;
        }

        if (min == max)
        {
            return green;
        }

        float t = (float)(val - min) / (max - min);
        return Color.Lerp(green, red, t);
    }

    private static Color GetInverseValueColor(int val, int min, int max)
    {
        Color green = new(0.2f, 1f, 0.1f);
        Color red = new(1f, 0.1f, 0.1f);

        if (val <= min)
        {
            return red;
        }

        if (val >= max)
        {
            return green;
        }

        float t = (float)(val - min) / (max - min);
        return Color.Lerp(red, green, t);
    }
}