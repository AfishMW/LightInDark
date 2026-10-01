using System;
using LightInDark.Core;
using TMPro;
using UnityEngine;
using UColor = UnityEngine.Color;
using Object = UnityEngine.Object;

namespace Light.UI.Window;

/// <summary>
/// 通用「详情提示框」（鼠标悬浮时弹出一段说明）。
///
/// 自己写 UI 时的用法：
///     DetailPopup.Show("这是说明文本", canPinByTab: true, anchor: someRowTransform);
///     DetailPopup.Hide();
///
/// 规则：
///  * text 为 null / 空 / 全空白 → **什么都不显示**（也不会建出提示框）。
///  * canPinByTab = true 时：正文下面空一行再追加 “可按tab固定介绍”，并按 Tab 可钉住 / 取消钉住。
///  * canPinByTab = false 时：不追加那行提示，也不响应 Tab。
///
/// 外观与尺寸（代码绘制，不用原版弹窗贴图）：
///  * 半透明灰底 + 淡金细边框（四条细条拼的，粗细/颜色见下面常量）。
///  * **文字直接克隆自 anchor（被悬浮那一行）身上的 TextMeshPro** —— 字号、字体、缩放与那一行完全一致；
///    anchor 上没有文字时才退回统一文本模板（这时用 <see cref="FallbackFontSize"/>）。
///  * 框的大小按 TMP 的 <c>textBounds</c>（**真正画出来的字形范围**）算，不用 preferredWidth —— 后者
///    受预制体的 rect / margin 影响，会比实际字形大好几倍（就是"框很大、字很小"的原因）。
/// </summary>
public static class DetailPopup
{
    /// <summary>可按 Tab 固定时：正文下面空一行，前面加几个空格，营造"在右下角"的感觉。</summary>
    private const string PinHint = "\n\n        <b>可按tab固定介绍</b>";

    // ── 尺寸 / 配色（要调外观改这里）────────────────────────────────
    private const float FallbackFontSize = 1.6f;                      // anchor 上没文字时才用这个字号
    private const float SizeScale = 1.0f;                             // 说明文字相对"那一行文字"的大小（1.0 = 一样大）
    private const float BoldOutlineWidth = 0.06f;                     // 加粗强度：**越小越干净**，0.15 以上会糊
    private const float PadX = 0.20f;                                 // 字形左右留白
    private const float PadY = 0.12f;                                 // 字形上下留白
    private const float BoxMinWidth = 0.30f;
    private const float BoxMinHeight = 0.20f;
    private const float BorderThickness = 0.020f;                     // 淡金边框粗细
    private const float BoxZOffset = -1.0f;                           // 相对被悬浮行的 z 偏移（更靠前）

    private static readonly UColor FillColor = new UColor(0.22f, 0.22f, 0.22f, 0.85f);      // 半透明灰底
    private static readonly UColor BorderColor = new UColor(1f, 0.90f, 0.63f, 0.95f);       // 淡金边框
    private static readonly UColor TextColor = new UColor(1f, 0.95f, 0.85f, 1f);            // 辉光白

    private static GameObject? _root;
    private static Transform? _parent;
    private static TextMeshPro? _text;
    private static Sprite? _fallbackWhite;                            // 自建 1×1 白图（自己持有，不会被场景切换带走）
    private static SpriteRenderer? _fill;
    private static SpriteRenderer? _top;
    private static SpriteRenderer? _bottom;
    private static SpriteRenderer? _left;
    private static SpriteRenderer? _right;

    private static float _boxHeight = BoxMinHeight;                   // 当前框高（把框挂在行的下面用）
    private static float _loggedW = -1f;                              // 上次记录的框宽/高（只在变化时打日志）
    private static float _loggedH = -1f;
    private static bool _showing;                                     // 鼠标当前停在带 detail 的行上
    private static bool _pinned;                                      // 被 Tab 钉住
    private static string _currentText = "";

    /// <summary>显示提示框。</summary>
    public static void Show(string? text, bool canPinByTab = true, Transform? anchor = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text)) { Hide(); return; }

            var parent = anchor != null ? anchor.parent : null;
            if (parent == null)
            {
                LightLogger.LogWarning("[DetailPopup] anchor 没有父物体，无法定位，跳过显示");
                return;
            }

            Ensure(parent, anchor);
            if (_root == null || _text == null) return;

            // ① 每次显示都校验一次背景贴图：主菜单卸载后缓存的 WhiteSprite 会变成"假 null"，
            //    那样 SpriteRenderer 的 sprite 为空 → 框和背景整个不渲染（只有 TMP 文字还在）。
            RefreshPartSprites();

            // ② 渲染设置：GameObject.layer（相机 cullingMask）+ sortingLayer/Order 都跟着行走
            ApplyRenderSettings(anchor);

            string body = text!.Trim();
            if (canPinByTab) body += PinHint;              // ← 可按 Tab 固定时，空一行补提示

            if (_currentText != body)
            {
                _text.text = body;
                _text.ForceMeshUpdate();
                _currentText = body;
            }
            FitBox();

            // 挂在被悬浮行的下面；按实际框高定位，框小就贴得近
            Vector3 pos = anchor != null ? anchor.localPosition : Vector3.zero;
            pos.y -= _boxHeight * 0.5f + 0.10f;
            pos.x = Mathf.Clamp(pos.x, -2.2f, 2.2f);       // 简单夹紧，别跑出面板
            pos.y = Mathf.Clamp(pos.y, -2.4f, 2.4f);
            // z 直接用"行自己的 z"：那是已被证明能显示出来的深度，
            // 免得在大厅里猜错方向被面板/相机裁掉（前后关系交给 sortingOrder，见 ApplySorting）
            _root.transform.localPosition = pos;

            _showing = true;
            _pinned = false;                               // 每次悬停都是"未固定"状态
            _root.SetActive(true);

            LogDiagnostics(anchor);                        // 一次性诊断（最多 6 次）
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[DetailPopup.Show]", ex);
        }
    }

    /// <summary>隐藏提示框（被 Tab 钉住时不隐藏）。</summary>
    public static void Hide()
    {
        try
        {
            _showing = false;
            if (_pinned) return;
            if (_root != null) _root.SetActive(false);
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[DetailPopup.Hide] {ex.Message}");
        }
    }

    /// <summary>每帧调用（挂在设置菜单的 Update 上）：处理 Tab 固定 / 取消固定。</summary>
    public static void Update()
    {
        try
        {
            if (_root == null || !_root.activeSelf) return;
            if (!Input.GetKeyDown(KeyCode.Tab)) return;

            _pinned = !_pinned;
            if (!_pinned && !_showing && _root != null)
            {
                _root.SetActive(false);                    // 取消固定时鼠标已移开 → 直接收起
            }
            LightLogger.Log($"[DetailPopup] Tab → {(_pinned ? "已固定介绍" : "取消固定")}");
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[DetailPopup.Update] {ex.Message}");
        }
    }

    /// <summary>重置状态（设置菜单重建 / 关闭时调用），并收起提示框。</summary>
    public static void Reset()
    {
        _showing = false;
        _pinned = false;
        _currentText = "";
        if (_root != null) _root.SetActive(false);
    }

    // ────────────────────────────────────────────────────────────────

    private static void Ensure(Transform parent, Transform? anchor)
    {
        if (_root != null && _parent == parent) return;    // 已建好且父物体没变（Unity 假 null 会自动判为需要重建）

        Destroy();

        _parent = parent;
        _root = new GameObject("LightDetailPopup");
        _root.transform.SetParent(parent);
        _root.transform.localScale = Vector3.one;

        var white = GetWhiteSprite();

        // 半透明灰底
        _fill = MakePart("Fill", Vector3.zero, FillColor, white);

        // 淡金细边框（四条细条；z 略小 = 更靠前）
        _top = MakePart("BorderTop", Vector3.zero, BorderColor, white);
        _bottom = MakePart("BorderBottom", Vector3.zero, BorderColor, white);
        _left = MakePart("BorderLeft", Vector3.zero, BorderColor, white);
        _right = MakePart("BorderRight", Vector3.zero, BorderColor, white);

        // 文字：优先克隆 anchor（那一行）自己的文字，字号/字体/缩放就和那一行完全一致
        _text = CreateText(_root.transform, anchor);

        _root.SetActive(false);
        LightLogger.Log($"[DetailPopup] 提示框已创建（灰底+淡金边框；文字来源={(_textSourceIsRowText ? "行内文字" : "统一文本模板")}）");
    }

    private static bool _textSourceIsRowText;

    private static TextMeshPro? CreateText(Transform parent, Transform? anchor)
    {
        _textSourceIsRowText = false;

        // ① 用那一行自己的文字当模板（字号/字体/缩放最准，也避开标准预制体的大 rect/margin）
        try
        {
            var source = anchor != null ? anchor.GetComponentInChildren<TextMeshPro>(true) : null;
            if (source != null)
            {
                var clone = Object.Instantiate(source, parent);
                clone.name = "DetailText";
                clone.transform.localPosition = Vector3.zero;
                clone.text = "";

                clone.enableAutoSizing = false;                    // 必须关：否则改 fontSize 不生效
                clone.fontSize = source.fontSize * SizeScale;      // 比那一行文字再大一点
                clone.alignment = TextAlignmentOptions.Center;
                clone.enableWordWrapping = false;                  // 不换行：宽高由我们按字形算
                clone.overflowMode = TextOverflowModes.Overflow;   // 别被原版省略号截断
                clone.raycastTarget = false;
                clone.color = TextColor;

                var tr = clone.GetComponent<TextTranslatorTMP>();
                if (tr != null) tr.enabled = false;                // 别让翻译器改写我们的文本

                ApplyBold(clone);
                _textSourceIsRowText = true;
                return clone;
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[DetailPopup] 克隆行内文字失败，退回统一模板：{ex.Message}");
        }

        // ② 退回统一文本模板（主界面字体 + 辉光白）
        var fallback = MenuTextTemplate.Create(parent, Vector3.zero, "", FallbackFontSize * SizeScale, TextColor);
        ApplyBold(fallback);
        return fallback;
    }

    /// <summary>
    /// 加粗：用**材质描边**（描边颜色 = 文字颜色）把笔画加粗。
    /// 不用 &lt;b&gt; / FontStyles.Bold —— 那是顶点偏移式伪粗体，笔画容易糊在一起；
    /// <c>fontMaterial</c> 是实例材质，只影响这一处文字，不会污染共享材质。
    /// </summary>
    private static void ApplyBold(TextMeshPro? tmp)
    {
        try
        {
            if (tmp == null) return;

            var mat = tmp.fontMaterial;
            if (mat == null) return;

            mat.SetFloat("_OutlineWidth", BoldOutlineWidth);
            mat.SetColor("_OutlineColor", TextColor);
            mat.SetFloat("_OutlineSoftness", 0f);      // 必须为 0：柔化会把笔画糊开（"糊"的元凶）
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[DetailPopup.ApplyBold] {ex.Message}");
        }
    }

    /// <summary>
    /// 画框用的 1×1 白图。
    ///
    /// 为什么不用 <c>VanillaAsset.WhiteSprite</c>：它是懒加载缓存的原版资源，
    /// **主菜单卸载后会被销毁成"假 null"**，那时 SpriteRenderer.sprite 变空 →
    /// 框和背景整个不渲染（只有 TMP 文字还在，因为文字走的是字体材质）。
    /// 大厅里"框和背景消失"就是这个原因。
    /// 这里自己造一张并自己持有引用，不受场景切换影响。
    /// </summary>
    private static Sprite? GetWhiteSprite()
    {
        try
        {
            if (_fallbackWhite != null) return _fallbackWhite;

            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, UColor.white);
            tex.Apply();
            _fallbackWhite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 100f);
            LightLogger.Log("[DetailPopup] 已创建自建白图（1×1）");
            return _fallbackWhite;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[DetailPopup.GetWhiteSprite]", ex);
            return null;
        }
    }

    /// <summary>确保 5 个部件的贴图都有效（失效就重新赋上）。每次显示前调用，很便宜。</summary>
    private static void RefreshPartSprites()
    {
        try
        {
            var white = GetWhiteSprite();
            if (white == null) return;

            int fixedCount = 0;
            foreach (var sr in new[] { _fill, _top, _bottom, _left, _right })
            {
                if (sr == null) continue;
                if (sr.sprite != null) continue;       // 有效贴图 → 不动（被销毁的对象在 Unity 里判为 null，会走下面重赋）
                sr.sprite = white;
                fixedCount++;
            }

            if (fixedCount > 0)
            {
                LightLogger.Log($"[DetailPopup] 重新赋予背景贴图 {fixedCount} 个部件（原贴图已失效）");
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[DetailPopup.RefreshPartSprites] {ex.Message}");
        }
    }

    /// <summary>取一个"确实在显示"的参考渲染器：优先行上的 ToggleButtonBehaviour.Background，其次行内任意激活的 SpriteRenderer。</summary>
    private static SpriteRenderer? FindReferenceRenderer(Transform? anchor)
    {
        try
        {
            if (anchor == null) return null;

            var tbb = anchor.GetComponent<ToggleButtonBehaviour>();
            if (tbb != null && tbb.Background != null && tbb.Background.gameObject.activeInHierarchy)
            {
                return tbb.Background;
            }

            foreach (var sr in anchor.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sr == null) continue;
                if (!sr.gameObject.activeInHierarchy) continue;
                return sr;
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// 让提示框和"被悬浮行"用同一套渲染设置：**GameObject layer**（相机 cullingMask 按它过滤）、
    /// sortingLayer、以及明显更高的 sortingOrder（+10 / 文字 +11）。
    ///
    /// 为什么需要：行是从原版预制体克隆来的，自带正确的 layer / sorting；
    /// 提示框的部件是我们运行时 new GameObject + AddComponent 出来的 —— 默认 layer 0、Default 排序层、order 0，
    /// 在不同菜单（尤其大厅）里可能既被相机剔除、又被面板挡住。
    /// </summary>
    private static void ApplyRenderSettings(Transform? anchor)
    {
        try
        {
            const int margin = 10;

            int layerId = 0;              // sorting layer
            int order = 0;
            int goLayer = 0;              // GameObject.layer（相机 cullingMask）
            bool hasReference = false;

            var reference = FindReferenceRenderer(anchor);
            if (reference != null)
            {
                layerId = reference.sortingLayerID;
                order = reference.sortingOrder;
                goLayer = reference.gameObject.layer;
                hasReference = true;
            }
            else
            {
                LightLogger.LogWarning("[DetailPopup] 行上找不到可用的 SpriteRenderer，渲染设置沿用默认值（大厅里可能看不见）");
            }

            bool changed = false;

            foreach (var sr in new[] { _fill, _top, _bottom, _left, _right })
            {
                if (sr == null) continue;
                changed |= SetRenderSettings(sr.gameObject, sr, layerId, order + margin, goLayer, hasReference);
            }

            // 文字：TMP 在 interop 里不是 Renderer，直接设它自己的 layer/sorting 属性
            if (_text != null)
            {
                if (hasReference && _text.gameObject.layer != goLayer)
                {
                    _text.gameObject.layer = goLayer;
                    changed = true;
                }
                if (_text.sortingLayerID != layerId)
                {
                    _text.sortingLayerID = layerId;
                    changed = true;
                }
                if (_text.sortingOrder != order + margin + 1)
                {
                    _text.sortingOrder = order + margin + 1;
                    changed = true;
                }
            }

            if (changed)
            {
                string spriteState = _fill != null && _fill.sprite != null ? "有" : "空";
                LightLogger.Log($"[DetailPopup] 渲染设置已同步：layer(排序)={layerId} 参考order={order}（框 {order + margin} / 文字 {order + margin + 1}），GameObject.layer={goLayer}，背景图={spriteState}");
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[DetailPopup.ApplyRenderSettings] {ex.Message}");
        }
    }

    /// <summary>把 layer / sorting 设置套到某个部件上；返回是否发生了修改。</summary>
    private static bool SetRenderSettings(GameObject go, Renderer renderer, int sortingLayerId, int sortingOrder, int goLayer, bool setGoLayer)
    {
        bool changed = false;

        if (setGoLayer && go.layer != goLayer)
        {
            go.layer = goLayer;
            changed = true;
        }

        if (renderer.sortingLayerID != sortingLayerId)
        {
            renderer.sortingLayerID = sortingLayerId;
            changed = true;
        }

        if (renderer.sortingOrder != sortingOrder)
        {
            renderer.sortingOrder = sortingOrder;
            changed = true;
        }

        return changed;
    }

    // ── 一次性诊断（排查"大厅里看不见"用，最多打 6 次）─────────────────

    private static int _diagLogs;

    private static void LogDiagnostics(Transform? anchor)
    {
        if (_diagLogs >= 6) return;
        _diagLogs++;

        try
        {
            LightLogger.Log($"[DetailPopup][诊断#{_diagLogs}] ================ 开始 ================");

            var cam = Camera.main;
            if (cam != null)
            {
                LightLogger.Log($"[DetailPopup][诊断] Camera.main={cam.name} pos={cam.transform.position} ortho={cam.orthographic} size={cam.orthographicSize:F2} near={cam.nearClipPlane:F2} far={cam.farClipPlane:F2} mask={cam.cullingMask} depth={cam.depth} enabled={cam.enabled}");
            }
            else
            {
                LightLogger.Log("[DetailPopup][诊断] Camera.main = null");
            }

            foreach (var c in Camera.allCameras)
            {
                if (c == null) continue;
                LightLogger.Log($"[DetailPopup][诊断] 相机 {c.name}: depth={c.depth} mask={c.cullingMask} ortho={c.orthographic} size={c.orthographicSize:F2} pos={c.transform.position} active={c.gameObject.activeInHierarchy}");
            }

            if (anchor != null)
            {
                LightLogger.Log($"[DetailPopup][诊断] 行 root: name={anchor.name} layer={anchor.gameObject.layer} active={anchor.gameObject.activeInHierarchy} local={anchor.localPosition} world={anchor.position} lossyScale={anchor.lossyScale}");
            }

            DumpRenderer("[DetailPopup][诊断] 参考行", FindReferenceRenderer(anchor), cam);
            DumpRenderer("[DetailPopup][诊断] 框-底 ", _fill, cam);
            DumpRenderer("[DetailPopup][诊断] 框-上 ", _top, cam);

            if (_root != null)
            {
                LightLogger.Log($"[DetailPopup][诊断] 框 root: layer={_root.layer} active={_root.activeInHierarchy} local={_root.transform.localPosition} world={_root.transform.position} lossyScale={_root.transform.lossyScale}");
            }

            if (_text != null)
            {
                LightLogger.Log($"[DetailPopup][诊断] 文字: layer={_text.gameObject.layer} active={_text.gameObject.activeInHierarchy} text=\"{_text.text}\" size={_text.fontSize:F2} color={_text.color} sorting={_text.sortingLayerID}/{_text.sortingOrder} world={_text.transform.position} lossyScale={_text.transform.lossyScale}");
            }

            int depth = 0;
            for (var t = _root != null ? _root.transform.parent : null; t != null && depth < 4; t = t.parent, depth++)
            {
                LightLogger.Log($"[DetailPopup][诊断] 父[{depth}] {t.name}: layer={t.gameObject.layer} active={t.gameObject.activeInHierarchy} local={t.localPosition} world={t.position} lossyScale={t.lossyScale}");
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[DetailPopup.LogDiagnostics] {ex.Message}");
        }
    }

    private static void DumpRenderer(string tag, SpriteRenderer? sr, Camera? cam)
    {
        try
        {
            if (sr == null) { LightLogger.Log($"{tag}: (null)"); return; }

            var b = sr.bounds;
            string vp = cam != null ? cam.WorldToViewportPoint(sr.transform.position).ToString() : "?";
            string layerName = SortingLayer.IDToName(sr.sortingLayerID);
            string spriteInfo = sr.sprite != null ? $"{sr.sprite.name}/{sr.sprite.bounds.size}" : "(null)";

            LightLogger.Log($"{tag}: layer={sr.gameObject.layer} active={sr.gameObject.activeInHierarchy} enabled={sr.enabled} sprite={spriteInfo} size={sr.size} color={sr.color} sorting={sr.sortingLayerID}({layerName})/{sr.sortingOrder} world={sr.transform.position} bounds={b.size} vp={vp} lossyScale={sr.transform.lossyScale} mat={sr.sharedMaterial?.name}");
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"{tag} 输出失败：{ex.Message}");
        }
    }

    private static SpriteRenderer? MakePart(string name, Vector3 localPos, UColor color, Sprite? sprite)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_root!.transform);
        go.transform.localPosition = localPos;
        go.transform.localScale = Vector3.one;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.drawMode = SpriteDrawMode.Sliced;
        sr.color = color;
        return sr;
    }

    /// <summary>
    /// 按**真正画出来的字形范围**摆放灰底与四条边框，并把文字居中到框心。
    /// 用 textBounds（局部坐标）而不是 preferredWidth/Height —— 后者会带上预制体的 rect/margin，
    /// 导致"框很大、字很小"。
    /// </summary>
    private static void FitBox()
    {
        try
        {
            if (_text == null) return;

            Vector3 size = Vector3.zero;
            Vector3 center = Vector3.zero;

            try
            {
                var b = _text.textBounds;
                size = b.size;
                center = b.center;
            }
            catch { }

            // textBounds 拿不到（或为 0）时退回 preferred 尺寸
            float glyphW = size.x;
            float glyphH = size.y;
            if (glyphW <= 0.001f || glyphH <= 0.001f)
            {
                try
                {
                    glyphW = _text.preferredWidth;
                    glyphH = _text.preferredHeight;
                    center = Vector3.zero;
                }
                catch { }
            }

            float width = Mathf.Max(BoxMinWidth, glyphW + PadX * 2f);
            float height = Mathf.Max(BoxMinHeight, glyphH + PadY * 2f);
            _boxHeight = height;

            // 只在尺寸变化时记一行，方便排查"框大/字小"
            if (Mathf.Abs(width - _loggedW) > 0.001f || Mathf.Abs(height - _loggedH) > 0.001f)
            {
                _loggedW = width;
                _loggedH = height;
                LightLogger.Log($"[DetailPopup] 字形 {glyphW:F3}x{glyphH:F3} → 框 {width:F3}x{height:F3}，文字来源={(_textSourceIsRowText ? "行内文字" : "模板")}，字号={(_text != null ? _text.fontSize : 0f):F2}，缩放={(_text != null ? _text.transform.lossyScale.x : 0f):F3}");
            }

            // 把文字居中到框心：文字局部坐标 = -字形中心
            _text.transform.localPosition = new Vector3(-center.x, -center.y, -0.03f);

            float halfW = width * 0.5f;
            float halfH = height * 0.5f;
            float t = BorderThickness;

            if (_fill != null)
            {
                _fill.transform.localPosition = Vector3.zero;
                _fill.size = new Vector2(width, height);
            }

            Set(_top, new Vector3(0f, halfH - t * 0.5f, -0.01f), new Vector2(width, t));
            Set(_bottom, new Vector3(0f, -halfH + t * 0.5f, -0.01f), new Vector2(width, t));
            Set(_left, new Vector3(-halfW + t * 0.5f, 0f, -0.01f), new Vector2(t, height));
            Set(_right, new Vector3(halfW - t * 0.5f, 0f, -0.01f), new Vector2(t, height));
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[DetailPopup.FitBox] {ex.Message}");
        }
    }

    private static void Set(SpriteRenderer? sr, Vector3 localPos, Vector2 size)
    {
        if (sr == null) return;
        sr.transform.localPosition = localPos;
        sr.size = size;
    }

    private static void Destroy()
    {
        try
        {
            if (_root != null) Object.Destroy(_root);
        }
        catch { }

        _root = null;
        _parent = null;
        _text = null;
        _fill = null;
        _top = null;
        _bottom = null;
        _left = null;
        _right = null;
        _currentText = "";
    }
}
