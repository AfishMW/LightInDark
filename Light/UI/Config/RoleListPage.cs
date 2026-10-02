using System;
using System.Collections.Generic;
using Light.Config;
using LightInDark.Configuration;
using LightInDark.Core;
using LightInDark.UI.Window;
using Light.UI.Window;
using UnityEngine;
using UnityEngine.Events;

namespace Light.UI.Config;

/// <summary>
/// 职业按钮列表页：把某分类下的所有职业块渲染成圆角长方形按钮，
/// 点击按钮进入该职业的配置页（由回调处理，见 GameSettingMenuPatch）。
/// </summary>
internal static class RoleListPage
{
    // ---- 按钮排版（标签按钮在 y=0.8、底部到 0.4，列表从其下方开始） ----
    private const float BtnWidth = 2.2f;
    private const float BtnHeight = 0.45f;
    private const float SpacingX = 2.5f;
    private const float SpacingY = 0.52f;
    private const float StartY = 0.05f;
    private const int Columns = 2;

    private static GameObject _page;

    public static bool Built => _page != null;

    /// <summary>显示职业按钮列表（覆盖式：先清掉旧列表）。</summary>
    public static void Show(List<ConfigBlock> blocks, Transform parent, Action<ConfigBlock> onSelected)
    {
        Clear();
        if (blocks == null || blocks.Count == 0) return;

        try
        {
            _page = NewUIObject("LightRoleListPage", parent, new Vector3(0f, 0f, -2.5f));

            for (int i = 0; i < blocks.Count; i++)
            {
                int row = i / Columns;
                int rowStart = row * Columns;
                int inRow = Math.Min(Columns, blocks.Count - rowStart); // 末行不满也居中
                int col = i % Columns;

                float x = (col - (inRow - 1) * 0.5f) * SpacingX;
                float y = StartY - row * SpacingY;
                var block = blocks[i];
                CreateRoleButton(_page.transform, block, new Vector2(x, y),
                    () => onSelected?.Invoke(block));
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[RoleListPage.Show]", ex);
        }
    }

    public static void Clear()
    {
        if (_page != null)
        {
            UnityEngine.Object.Destroy(_page);
            _page = null;
        }
    }

    /// <summary>圆角长方形按钮：背景 = 职业色暗色调，文字 = 职业名。</summary>
    private static void CreateRoleButton(Transform parent, ConfigBlock block, Vector2 pos, Action onClick)
    {
        var go = NewUIObject($"LightRoleBtn_{block.Key}", parent, new Vector3(pos.x, pos.y, 0f));

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = ConfigUIPanel.GetRoundedSprite();
        sr.drawMode = SpriteDrawMode.Sliced;
        sr.size = new Vector2(BtnWidth, BtnHeight);

        var baseColor = block.HeaderColor ?? new Color(0.3f, 0.3f, 0.3f, 1f);
        var idle = new Color(baseColor.r * 0.45f, baseColor.g * 0.45f, baseColor.b * 0.45f, 0.95f);
        var hover = new Color(baseColor.r * 0.75f, baseColor.g * 0.75f, baseColor.b * 0.75f, 1f);
        sr.color = idle;

        MenuTextTemplate.Create(go.transform, new Vector3(0f, 0f, -0.1f), block.DisplayName, 1.0f);

        var col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = new Vector2(BtnWidth, BtnHeight);

        var pb = go.SetUpButton(true, null, null, null, false);
        pb.OnClick.AddListener((UnityAction)onClick);
        pb.OnMouseOver.AddListener((UnityAction)(() => sr.color = hover));
        pb.OnMouseOut.AddListener((UnityAction)(() => sr.color = idle));
    }

    private static GameObject NewUIObject(string name, Transform parent, Vector3 localPos)
    {
        var go = new GameObject(name);
        go.layer = LayerExpansion.GetUILayer();
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = Vector3.one;
        return go;
    }
}
