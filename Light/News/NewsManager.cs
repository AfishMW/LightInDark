using AmongUs.Data;
using AmongUs.Data.Player;
using Assets.InnerNet;
using BepInEx.Unity.IL2CPP.Utils.Collections;   // 新增：WrapToIl2Cpp()（启动协程用）
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Light.Config;                 // 新增：VersionMaker（更新后自动弹新闻）
using Light.UI.HudUI;               // 新增：SpriteSheetLoader（分类按钮图片）
using Light.UI.Window;              // 新增：VanillaAsset（占位色块）
using LightInDark.Core;
using LightInDark.UI.Window;        // 新增：LayerExpansion
using System;
using TMPro;                        // 新增：TextMeshPro / TextTranslatorTMP
using UnityEngine.Events;          // 新增：UnityAction
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Light.News;

[HarmonyPatch]
public class NewsManager
{
    //公告编号
    public const int NumberSet = 1145141;
    const int Id = 100000;
    public static readonly List<LightNews> AllNews = new();
    static string? ReadNewsRes(SupportedLangs lang)
    {
        var asm = typeof(LightPlugin).Assembly;
        string resName = $"Light.Resources.News.News_{lang}.json";
        using var stream = asm.GetManifestResourceStream(resName);
        if(stream == null) return null;
        using var reader = new StreamReader(stream,Encoding.UTF8);
        return reader.ReadToEnd();
    }
    private static readonly JsonSerializerOptions _json = new()
    {
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
    public static void LoadNews()
    {
        try
        {
            var l = DataManager.Settings.Language.CurrentLanguage;
            string? json = ReadNewsRes(l) ?? (l!=SupportedLangs.English ? ReadNewsRes(SupportedLangs.English) : null);

            if (json == null) { LightLogger.LogWarning("[News] 没找到 News.json 资源"); return; }

            var list = JsonSerializer.Deserialize<List<LightNews>>(json,_json); 
            if (list == null || list.Count == 0) return;
            AllNews.Clear();
            foreach (var item in list) if (item != null) AllNews.Add(item);
            AllNews.Sort((a,b)=>string.Compare(b.date,a.date));
            LightLogger.Log($"载入 {AllNews.Count} 条公告");
        }
        catch(Exception ex)
        {
            LightLogger.LogError("[News.LoadNews]", ex);
        }
    }
    public static string PreprocessBody(string? detail)
    {
        var text = detail ?? "";
        if (text.Length == 0) return text;
        try
        {
            //去掉等号
            text = text.Replace("<color=#", "<#").Replace("<Color=#", "<#").Replace("<COLOR=#", "<#");
            // []后面自动补个空格
            var sb = new System.Text.StringBuilder(text.Length + 8);
            for (int i = 0; i < text.Length; i++)
            {
                sb.Append(text[i]);
                if (text[i] != ']' || i == 0 || text[i - 1] != '[') continue;
                bool atEnd = i + 1 >= text.Length;
                if (atEnd || text[i + 1] != ' ') sb.Append(' ');
            }
            return sb.ToString();
        }
        catch (Exception ex) { LightLogger.LogWarning(ex.ToString()); return detail ?? ""; }
    }
    #region Patch
    [HarmonyPatch(typeof(PlayerAnnouncementData), nameof(PlayerAnnouncementData.SetAnnouncements))]
    [HarmonyPrefix]
    public static void SetAnnouncementsPrefix(ref Il2CppReferenceArray<Announcement> aRange)
    {
        try
        {
            if (AllNews.Count == 0) return;
            var temp = new List<Announcement>(aRange);
            temp.RemoveAll(a => a.Number >= NumberSet && a.Number < NumberSet + Id);
            temp.AddRange(AllNews
                .Where(n => !n.debug)
                .Select(n => n.ToAnnouncement()));
            temp.Sort((a1, a2) => string.Compare(a2.Date, a1.Date));
            aRange = temp.ToArray();
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($" [SetAnnouncementsPrefix] {ex}");
        }
    }
    [HarmonyPatch(typeof(SelectableHyperLinkHelper), nameof(SelectableHyperLinkHelper.DecomposeAnnouncementText))]
    public static class AnnouncementFallback
    {
        //签名static string DecomposeAnnouncementText(TextMeshPro tmp, List<SelectableHyperLink> links, string menuName, string text)
        [HarmonyFinalizer]
        public static Exception? Finalizer(Exception? __exception, ref string __result, string text)
        {
            if (__exception == null) return null;
            __result = text ?? "";
            return null;
        }
    }
    #endregion
}
public class LightNews
{
    public int id = 0;             //序号
    public string title = "";      //大标题
    public string shortTitle = ""; //列表短标题,空则用title
    public string subTitle = "";   //大标题下小字
    public string date = "";       
    public string detail = "";     //正文
    public bool debug = false;     
    public Announcement ToAnnouncement()
    {
        var result = new Announcement
        {
            Id = "LightNews",
            Number = NewsManager.NumberSet + id,
            Title = title,
            SubTitle = subTitle,
            ShortTitle = string.IsNullOrEmpty(shortTitle) ? title : shortTitle,
            Text = NewsManager.PreprocessBody(detail),
            Date = date ?? "",
            Language = (uint)DataManager.Settings.Language.CurrentLanguage,
            PinState = false,

        };
        return result;
    }
}

#region 新增：新闻面板分类过滤（MOD / 原版）+ 更新后自动弹新闻

/// <summary>
/// 新闻列表的分类过滤（MOD / 原版）。
///
/// 做法：patch 原版 <see cref="AnnouncementPopUp.CreateAnnouncementList"/> 的 Postfix ——
/// 让原版照常把所有公告都建出来，然后把不属于当前分类的条目隐藏掉，
/// 再把剩下的条目按原版间距（0.8f）重排、重算滚动范围、把选中项挪到第一个可见条目。
///
/// 为什么不用"临时替换 AllAnnouncements"的写法：
///   PlayerAnnouncementData.AllAnnouncements 在这套 interop 程序集里是【只读属性】，
///   不能赋值；而面板级过滤只用到公开可读的 visibleAnnouncements / panelStartPos，
///   也不碰 DataManager 里的数据与已读状态，更安全。
/// </summary>
[HarmonyPatch(typeof(AnnouncementPopUp), nameof(AnnouncementPopUp.CreateAnnouncementList))]
public static class NewsFilterPatch
{
    /// <summary>0 = MOD 新闻（默认），1 = 原版新闻</summary>
    public const int FilterMod = 0;
    public const int FilterVanilla = 1;

    /// <summary>当前分类（静态保存，切场景后仍保留用户选择）</summary>
    public static int Current = FilterMod;

    /// <summary>
    /// MOD 新闻占用的编号区间长度。
    /// MOD 新闻编号 = NewsManager.NumberSet + LightNews.id，所以用区间判定即可。
    /// </summary>
    private const int ModNumberRange = 100000;

    /// <summary>原版每条公告的间距（AnnouncementPopUp.CreateAnnouncementList 里写死的 0.8f）</summary>
    private const float PanelStep = 0.8f;

    /// <summary>原版滚动范围公式里的常量（0.8f * (count + 1) - 2.512f）</summary>
    private const float ScrollBoundsOffset = 2.512f;

    [HarmonyPostfix]
    public static void Postfix(AnnouncementPopUp __instance)
    {
        try
        {
            ApplyFilter(__instance);
            NewsFilterButtons.Ensure(__instance);      // 建按钮（只建一次）
            DumpCandidateTexts(__instance);            // 一次性诊断：把疑似"标题"的文本抄进日志
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[News.NewsFilterPatch.Postfix]", ex);
        }
    }

    private static bool _dumped;

    /// <summary>
    /// 诊断用：第一次打开面板时，把含 "AMONG" 或 "公告" 的文本连同它所在对象 / 翻译键打进日志，
    /// 便于确认「《AMONG US》公告」到底是哪个对象在管（确认无误后可删掉这段）。
    /// </summary>
    private static void DumpCandidateTexts(AnnouncementPopUp popup)
    {
        if (_dumped) return;
        _dumped = true;
        try
        {
            foreach (var tmp in popup.GetComponentsInChildren<TextMeshPro>(true))
            {
                if (tmp == null) continue;
                string t = tmp.text ?? "";
                if (t.IndexOf("AMONG", StringComparison.OrdinalIgnoreCase) < 0
                    && t.IndexOf("公告", StringComparison.Ordinal) < 0) continue;

                var tr = tmp.GetComponent<TextTranslatorTMP>();
                LightLogger.Log($"[News][诊断] 文本=\"{t}\" | 对象={tmp.gameObject.name} | 翻译键={(tr != null ? tr.TargetText.ToString() : "无")}");
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[News] 诊断输出失败：{ex.Message}");
        }
    }

    /// <summary>编号是否属于 MOD 新闻</summary>
    public static bool IsModNews(int number)
        => number >= NewsManager.NumberSet && number < NewsManager.NumberSet + ModNumberRange;

    /// <summary>按当前分类显示 / 隐藏条目，并重排位置与滚动范围。</summary>
    private static void ApplyFilter(AnnouncementPopUp popup)
    {
        var panels = popup.visibleAnnouncements;
        if (panels == null) return;

        // 先数一下当前分类有几条：一条都没有就退回"全部显示"，免得列表全空
        int matchCount = 0;
        foreach (var p in panels)
        {
            if (p != null && IsModNews(p.AnnouncementNumber) == (Current == FilterMod)) matchCount++;
        }

        bool showAll = matchCount == 0;
        if (showAll)
            LightLogger.LogWarning($"[News] 分类 {Current} 下没有公告，本次不做过滤");

        float y = popup.panelStartPos.y;
        int visible = 0;
        AnnouncementPanel? firstVisible = null;
        var controller = ControllerManager.Instance;

        foreach (var p in panels)
        {
            if (p == null) continue;

            bool show = showAll || IsModNews(p.AnnouncementNumber) == (Current == FilterMod);
            if (p.gameObject.activeSelf != show) p.gameObject.SetActive(show);

            if (!show)
            {
                // 隐藏的条目不参与手柄导航
                if (controller != null)
                {
                    try { controller.RemoveSelectableUiElement(p.PassiveButton); } catch { }
                }
                continue;
            }

            var lp = p.transform.localPosition;
            p.transform.localPosition = new Vector3(lp.x, y, lp.z);
            y -= PanelStep;
            visible++;
            if (firstVisible == null) firstVisible = p;
        }

        // 选中项挪到第一个可见条目（否则右栏可能还停在被隐藏的那条上）
        if (firstVisible != null && popup.selectedPanel != firstVisible)
        {
            var old = popup.selectedPanel;
            if (old != null)
            {
                try { old.UnSelect(); } catch { }
            }
            popup.selectedPanel = firstVisible;
            try { firstVisible.Select(); } catch { }
        }

        // 手柄侧同步：原版建完列表会把选中项设成"第一条"，而那条可能刚被我们隐藏，
        // 于是 ControllerManager 后面会报 "Failed to highlight, selection is null"。
        // 这里把手柄选中项改指到第一个可见条目。
        if (firstVisible != null && controller != null)
        {
            try { controller.SetCurrentSelected(firstVisible.PassiveButton); } catch { }
        }

        // 按可见条数重算滚动范围（照搬原版公式）
        var scroller = popup.ListScroller;
        if (scroller != null)
        {
            try { scroller.SetBoundsMax(PanelStep * (visible + 1) - ScrollBoundsOffset, 0f); } catch { }
        }
    }
}

/// <summary>
/// 两个纯图片分类按钮（选中时换成高光图）。
///
/// 选中态原理：<see cref="PassiveButton"/> 用 6 组 Sprite GameObject 互斥 SetActive ——
///   inactiveSprites = 常态、activeSprites = 鼠标悬浮、selectedSprites = 选中高光。
/// 坑：原版 SetPassiveButtonHoverStateActive() 会【无条件】把 inactiveSprites 关掉，
///     所以只给 inactive 的话，鼠标一悬浮按钮就消失了 —— 三组都必须给。
///
/// 图片放在 Light\Resources\News\ 下（会自动嵌入）：
///   FilterMod.png / FilterMod_On.png / FilterVanilla.png / FilterVanilla_On.png
/// 美术未就绪时自动退化成代码色块，不会崩、也不会刷错误日志。
/// </summary>
public static class NewsFilterButtons
{
    private static readonly List<PassiveButton> _buttons = new();

    // ── 尺寸与位置：视觉微调改这几个常量即可 ──────────────────────
    private const float ButtonSize = 0.62f;
    private const float ButtonGap = 0.10f;
    /// <summary>相对"列表第一条公告"的偏移（x 往左、y 往上 = 面板左上角）</summary>
    private static readonly Vector3 AnchorOffset = new Vector3(-0.04f, 0.82f, -0.5f);

    // ── 占位色块颜色（图片缺失时用）──────────────────────────────
    private static readonly UnityEngine.Color ModNormal = new UnityEngine.Color(0.31f, 0.82f, 0.77f, 0.85f);
    private static readonly UnityEngine.Color ModSelect = new UnityEngine.Color(0.31f, 0.82f, 0.77f, 1f);
    private static readonly UnityEngine.Color VanNormal = new UnityEngine.Color(0.55f, 0.55f, 0.55f, 0.75f);
    private static readonly UnityEngine.Color VanSelect = new UnityEngine.Color(0.85f, 0.85f, 0.85f, 1f);

    // ── 图片资源名（不含 .png）───────────────────────────────────
    private const string ModRes = "Light.Resources.News.FilterMod";
    private const string ModResOn = "Light.Resources.News.FilterMod_On";
    private const string VanRes = "Light.Resources.News.FilterVanilla";
    private const string VanResOn = "Light.Resources.News.FilterVanilla_On";

    /// <summary>惰性创建按钮；列表每次重建时都会被调用，已建过就直接返回。</summary>
    public static void Ensure(AnnouncementPopUp popup)
    {
        try
        {
            // 场景重载后旧按钮已被销毁 → 清空重建
            if (_buttons.Count > 0)
            {
                if (_buttons[0] != null) return;
                _buttons.Clear();
            }

            if (popup == null || popup.AnnouncementListSlider == null) return;

            // 以"列表第一条公告"的位置作锚点，往左上挪一点 = 面板左上角
            var listT = popup.AnnouncementListSlider.transform;
            var parent = listT.parent != null ? listT.parent : popup.transform;
            Vector3 anchor = listT.localPosition + popup.panelStartPos + AnchorOffset;

            bool modSelected = NewsFilterPatch.Current == NewsFilterPatch.FilterMod;

            _buttons.Add(CreateButton(parent, "NewsFilterMod",
                new Vector3(anchor.x, anchor.y, anchor.z), NewsFilterPatch.FilterMod,
                ModRes, ModResOn, ModNormal, ModSelect, modSelected));

            _buttons.Add(CreateButton(parent, "NewsFilterVanilla",
                new Vector3(anchor.x + ButtonSize + ButtonGap, anchor.y, anchor.z), NewsFilterPatch.FilterVanilla,
                VanRes, VanResOn, VanNormal, VanSelect, !modSelected));

            LightLogger.Log($"[News] 分类按钮已创建，当前分类={NewsFilterPatch.Current}");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[News.NewsFilterButtons.Ensure]", ex);
        }
    }

    /// <summary>建一个纯图片按钮：常态图 / 悬浮图 / 选中高光图各一个 GameObject。</summary>
    private static PassiveButton CreateButton(Transform parent, string name, Vector3 pos, int index,
        string normalRes, string selectRes,
        UnityEngine.Color normalFallback, UnityEngine.Color selectFallback, bool selected)
    {
        var go = new GameObject(name);
        go.SetActive(false);          // 先失活：等字段全部配好再激活，避免 Awake/OnEnable 读到 null
        go.layer = LayerExpansion.GetUILayer();
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = Vector3.one;

        var normalGo = MakeSprite(go.transform, "Normal", TryLoadSprite(normalRes), normalFallback);
        var hoverGo = MakeSprite(go.transform, "Hover", TryLoadSprite(normalRes), normalFallback); // 悬浮暂复用常态图
        var selectGo = MakeSprite(go.transform, "Selected", TryLoadSprite(selectRes), selectFallback);

        // PassiveButton 必须有 Collider2D，否则收不到点击 / 悬浮
        var col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.offset = Vector2.zero;
        col.size = new Vector2(ButtonSize, ButtonSize);

        var pb = go.AddComponent<PassiveButton>();
        pb.inactiveSprites = normalGo;
        pb.activeSprites = hoverGo;
        pb.selectedSprites = selectGo;

        // ⚠️ 必须手动补这三个事件：UiElement.OnMouseOver / OnMouseOut 是【没有初始值】的字段，
        // 运行时 AddComponent 出来就是 null；而 UiElement.ReceiveMouseOver/ReceiveMouseOut()
        // 会直接 OnMouseOver/OnMouseOut.Invoke()，原版 PassiveButtonManager.Update() 每帧都会调，
        // 于是一旦按钮存在就每帧 NullReferenceException（原版 prefab 里这三个事件是序列化好的，
        // 所以克隆 prefab 不会踩到）。OnClick 自带字段初始化器，这里一并设置保持一致。
        pb.OnMouseOver = new UnityEvent();
        pb.OnMouseOut = new UnityEvent();
        pb.OnClick = new UnityEngine.UI.Button.ButtonClickedEvent();
        pb.OnClick.AddListener((UnityAction)(() => OnClicked(index)));

        go.SetActive(true);          // 激活后才触发 Awake/OnEnable，此时字段已全部就绪
        pb.SelectButton(selected);   // 设置初始选中态（选中 → selectedSprites 高光；未选中 → inactiveSprites 常态）

        return pb;
    }

    /// <summary>造一张纯图片；图片缺失时用白 Sprite 染色占位（size 直接定尺寸，与图片分辨率无关）。</summary>
    private static GameObject MakeSprite(Transform parent, string name, Sprite? sprite,
        UnityEngine.Color fallbackColor)
    {
        var go = new GameObject(name);
        go.layer = LayerExpansion.GetUILayer();
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localScale = Vector3.one;

        var sr = go.AddComponent<SpriteRenderer>();
        if (sprite != null)
        {
            sr.sprite = sprite;
            sr.color = UnityEngine.Color.white;
        }
        else
        {
            sr.sprite = VanillaAsset.WhiteSprite;    // 代码色块占位
            sr.color = fallbackColor;
        }
        sr.drawMode = SpriteDrawMode.Sliced;
        sr.size = new Vector2(ButtonSize, ButtonSize);
        return go;
    }

    /// <summary>先探测资源是否存在再加载，避免美术没画好时每次都刷错误日志。</summary>
    private static Sprite? TryLoadSprite(string resBase)
    {
        try
        {
            string resName = resBase + ".png";
            var asm = typeof(LightPlugin).Assembly;
            using var probe = asm.GetManifestResourceStream(resName);
            if (probe == null) return null;          // 美术未就绪 → 走占位色块
            return SpriteSheetLoader.Load(resName);
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[News] 加载图片 {resBase}.png 失败：{ex.Message}");
            return null;
        }
    }

    private static void OnClicked(int index)
    {
        try
        {
            if (NewsFilterPatch.Current == index) return;   // 已经是当前分类，不重复重建

            NewsFilterPatch.Current = index;

            for (int i = 0; i < _buttons.Count; i++)
            {
                var b = _buttons[i];
                if (b != null) b.SelectButton(i == index);   // 高光切换
            }

            Rebuild();
            NewsHeaderRenamePatch.Refresh();   // 标题跟着分类走：MOD → 模组名，原版 → AMONG US
            LightLogger.Log($"[News] 切换分类：{(index == NewsFilterPatch.FilterMod ? "MOD" : "原版")}");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[News.NewsFilterButtons.OnClicked]", ex);
        }
    }

    /// <summary>让原版按新分类重建一次列表。</summary>
    private static void Rebuild()
    {
        var popup = UnityEngine.Object.FindObjectOfType<AnnouncementPopUp>();
        if (popup == null) return;

        popup.CreateAnnouncementList();     // 重建（我们的 Prefix 会按新分类过滤）

        // 顺手刷新右侧预览文本；随后把两个状态 HUD 关掉 ——
        // 否则原版 Update() 检测到 HUD 处于激活状态，会自动切进"阅读模式"
        if (!popup.readingAnnouncement && popup.selectedPanel != null)
        {
            popup.UpdateAnnouncementText(popup.selectedPanel.AnnouncementNumber, true);
            if (popup.ListStateHUD != null) popup.ListStateHUD.SetActive(false);
            if (popup.ReadingStateHUD != null) popup.ReadingStateHUD.SetActive(false);
        }
    }
}

/// <summary>
/// 版本更新后自动"点一下"主菜单的新闻按钮。
/// 版本比较在 VersionMaker.MakeVersion() 写入 version.json 之前完成（那里设置标记），
/// 这里只负责在主菜单就绪后消费那个一次性标记。
/// </summary>
[HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.Start))]
public static class NewsAutoOpenOnUpdatePatch
{
    [HarmonyPostfix]
    public static void Postfix(MainMenuManager __instance)
    {
        try
        {
            if (!VersionMaker.ConsumeJustUpdated()) return;
            __instance.StartCoroutine(CoAutoOpen(__instance).WrapToIl2Cpp());
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[News.NewsAutoOpenOnUpdatePatch]", ex);
        }
    }

    /// <summary>自动打开前，最多等这么久让公告列表就绪（秒）</summary>
    private const float WaitAnnouncementsSeconds = 10f;

    private static IEnumerator CoAutoOpen(MainMenuManager mm)
    {
        // ⚠️ 原版 AnnouncementPopUp.Show() 会立刻 UpdateAnnouncementText(newestAnnouncement, ...)，
        // 而该方法在找不到 id 时会直接取 DataManager.Player.Announcements.AllAnnouncements[0]；
        // 列表还是空的时候（服务器公告尚未拉回来）就会抛 ArgumentOutOfRangeException。
        // 所以这里先等列表就绪，最多等 WaitAnnouncementsSeconds 秒；一直为空就放弃这次自动打开。
        float waited = 0f;
        while (waited < WaitAnnouncementsSeconds && !HasAnnouncements())
        {
            yield return null;
            waited += Time.deltaTime;
        }

        if (!HasAnnouncements())
        {
            LightLogger.LogWarning($"[News] 等待 {WaitAnnouncementsSeconds} 秒后公告列表仍为空，跳过自动打开（避免原版 Show() 越界）");
            yield break;
        }

        try
        {
            var btn = mm.newsButton;
            if (btn != null) btn.OnClick.Invoke();             // 模拟点击
            else if (mm.announcementPopUp != null) mm.announcementPopUp.Show();   // 兜底
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[News.CoAutoOpen]", ex);
        }
    }

    /// <summary>公告列表是否已经拿到内容（任何异常都当作"没就绪"，绝不外抛）。</summary>
    private static bool HasAnnouncements()
    {
        try
        {
            var list = DataManager.Player.Announcements.AllAnnouncements;
            return list != null && list.Count > 0;
        }
        catch { return false; }
    }
}

/// <summary>
/// 新闻面板右上角标题的「AMONG US」/ 模组名切换。
///
/// 那串文字（显示为「《AMONG US》公告」）来自 StringNames.AmongUsAnnouncements，
/// 由 TextTranslatorTMP 翻译后写进 TextMeshPro；语言切换时 ResetText() 会再跑一遍。
/// 挂 ResetText 的 Postfix：只认这一个翻译键，其它文本一概不碰。
///
/// 显示规则跟随分类页签：
///   分类 = MOD   → 「《LIGHT IN DARK》公告」
///   分类 = 原版  → 「《AMONG US》公告」
/// 切换页签时由 NewsFilterButtons 调 <see cref="Refresh"/> 立刻换过来。
/// </summary>
[HarmonyPatch(typeof(TextTranslatorTMP), nameof(TextTranslatorTMP.ResetText))]
public static class NewsHeaderRenamePatch
{
    private const string VanillaName = "AMONG US";
    private const string ModName = "LIGHT IN DARK";

    private static TextMeshPro? _header;
    private static string _vanillaText = "";
    private static bool _warnedNoVanillaName;

    [HarmonyPostfix]
    public static void Postfix(TextTranslatorTMP __instance)
    {
        try
        {
            if (__instance == null) return;
            if (__instance.TargetText != StringNames.AmongUsAnnouncements) return;

            var tmp = __instance.GetComponent<TextMeshPro>();
            if (tmp == null) return;

            _header = tmp;

            // 记下"原版形态"的基准文本（只有当前文本里还有 AMONG US 时才更新，
            // 免得把我们自己改过的结果当成基准，导致切换时越改越乱）。
            string cur = tmp.text ?? "";
            if (string.IsNullOrEmpty(_vanillaText)
                || cur.IndexOf(VanillaName, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _vanillaText = cur;
            }

            Apply();
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[News.NewsHeaderRenamePatch]", ex);
        }
    }

    /// <summary>分类页签切换后调用：MOD → 模组名，原版 → AMONG US。</summary>
    public static void Refresh()
    {
        try { Apply(); }
        catch (Exception ex) { LightLogger.LogError("[News.NewsHeaderRenamePatch.Refresh]", ex); }
    }

    private static void Apply()
    {
        var tmp = _header;
        if (tmp == null) return;

        string baseline = string.IsNullOrEmpty(_vanillaText) ? (tmp.text ?? "") : _vanillaText;
        bool isMod = NewsFilterPatch.Current == NewsFilterPatch.FilterMod;

        string result = baseline;                       // 原版分类：保持原样
        if (isMod)
        {
            int i = baseline.IndexOf(VanillaName, StringComparison.OrdinalIgnoreCase);
            if (i >= 0)
            {
                result = baseline.Substring(0, i) + ModName + baseline.Substring(i + VanillaName.Length);
            }
            else if (!_warnedNoVanillaName)
            {
                _warnedNoVanillaName = true;
                LightLogger.LogWarning($"[News] 公告标题里没找到 \"{VanillaName}\"，原文=\"{baseline}\"（未改动）");
            }
        }

        if (tmp.text != result)
        {
            tmp.text = result;
            tmp.ForceMeshUpdate();
        }
        LightLogger.Log($"[News] 公告标题 → {result}（当前分类={(isMod ? "MOD" : "原版")}）");
    }
}

#endregion
