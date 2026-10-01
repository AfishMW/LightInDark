# Among Us 19.0 大厅「规则 / 角色设置菜单」技术报告

> 源码基准：`D:\Coding\参考\Among Us Source code\19.0\Assembly-CSharp\`（全局命名空间为主）
> 目的：为「新增一个分类页签 + 新增若干选项行（克隆原版 UI）」提供精确 API 依据。
>
> **一句话结论**：`GameSettingMenu` 三个主页签是**硬编码的 3 个 `[SerializeField]` 字段 + `switch(tabNum)`**，
> 没有任何「页签列表」结构 —— **要加第 4 个主页签必须自己 patch `ChangeTab`/`Start`，无法靠数据驱动**；
> 而 `RolesSettingsMenu` 内部**已经**有数据驱动的角色页签列表（`AddRoleTab`）和
> **可动态新增分类头 + 选项行**的范例（`SetQuotaTab` / `CreateAdvancedSettings`），
> 这是最适合「克隆」的两段代码。

---

## 1. `GameSettingMenu`（大厅规则编辑主菜单）

文件：`GameSettingMenu.cs`（220 行）。挂在大厅 `GameStartManager` 的 `PlayerOptionsMenu` 预制体上。

### 1.1 生命周期 / 打开方式

```csharp
// GameStartManager.ClickEdit()   —— 只有房主能打开
this.RulesEditPanel = Object.Instantiate<GameObject>(this.PlayerOptionsMenu);
this.RulesEditPanel.transform.SetParent(Camera.main.transform, false);
this.RulesEditPanel.transform.localPosition = this.GameOptionsPosition;
DestroyableSingleton<TransitionFade>.Instance.DoTransitionFade(null, this.RulesEditPanel.gameObject, null);
```

- 字段：`GameStartManager.RulesEditPanel`（`private GameObject`，行 625）、
  `PlayerOptionsMenu`（`private GameObject`，行 641）、`GameOptionsPosition`（`private Vector3`，行 637）。
- 关闭：`GameStartManager` 行 529 `this.RulesEditPanel.GetComponent<GameSettingMenu>().Close();`
- `KeyboardJoystick` 行 126-128：按 Esc 时 `if (GameSettingMenu.Instance) GameSettingMenu.Instance.Close();`

### 1.2 `Start()`

```csharp
private void Start()
{
    if (GameSettingMenu.Instance && GameSettingMenu.Instance != this) { Object.Destroy(base.gameObject); }
    else { GameSettingMenu.Instance = this; }

    this.GamePresetsButton.OnClick.AddListener(delegate { this.ChangeTab(0, false); });
    this.GamePresetsButton.OnMouseOver.AddListener(delegate { this.ChangeTab(0, true); });
    this.GameSettingsButton.OnClick.AddListener(delegate { this.ChangeTab(1, false); });
    this.GameSettingsButton.OnMouseOver.AddListener(delegate { this.ChangeTab(1, true); });
    this.RoleSettingsButton.OnClick.AddListener(delegate { this.ChangeTab(2, false); });
    this.RoleSettingsButton.OnMouseOver.AddListener(delegate { this.ChangeTab(2, true); });

    if (GameManager.Instance.IsHideAndSeek())
        this.RoleSettingsButton.gameObject.SetActive(false);   // 捉迷藏模式隐藏角色页
}
```

⚠️ 注意：**单击走 `ChangeTab(n, false)`，鼠标悬浮走 `ChangeTab(n, true)`（预览）**。
按钮是 `PassiveButton`，`OnClick` / `OnMouseOver` 是 `UnityEvent`（`AddListener` **追加**，克隆原版按钮时必须先清空，见 §8）。

### 1.3 `Update()` / `OnEnable()` / `OnDisable()`

```csharp
private void Update()
{
    if (ShipStatus.Instance) { this.Close(); return; }            // 进游戏就自动关
    if (Controller.currentTouchType != Controller.TouchType.Joystick)
    { this.ToggleLeftSideDarkener(false); this.ToggleRightSideDarkener(false); }
}

private void OnEnable()
{
    ControllerManager.Instance.OpenOverlayMenu(base.name, this.BackButton, this.DefaultButtonSelected, this.ControllerSelectable, false);
    if (Controller.currentTouchType != Controller.TouchType.Joystick)
        this.ChangeTab(0, Controller.currentTouchType == Controller.TouchType.Joystick);
    base.StartCoroutine(this.CoSelectDefault());                  // 等一帧后 SetCurrentSelected(DefaultButtonSelected)
}
private void OnDisable() { ControllerManager.Instance.CloseOverlayMenu(base.name); }
```

### 1.4 `ChangeTab(int tabNum, bool previewOnly)` —— 核心

**这是「开关 SetActive + SelectButton + 换描述文本」的四段式，没有页签集合。**

```csharp
public void ChangeTab(int tabNum, bool previewOnly)
{
    if ((previewOnly && Controller.currentTouchType == Controller.TouchType.Joystick) || !previewOnly)
    {
        // ① 全部关掉
        this.PresetsTab.gameObject.SetActive(false);
        this.GameSettingsTab.gameObject.SetActive(false);
        this.RoleSettingsTab.gameObject.SetActive(false);
        this.GamePresetsButton.SelectButton(false);
        this.GameSettingsButton.SelectButton(false);
        this.RoleSettingsButton.SelectButton(false);

        // ② 按 tabNum 打开内容 + 设置中间描述文本
        switch (tabNum)
        {
        case 0: this.PresetsTab.gameObject.SetActive(true);
                this.MenuDescriptionText.text = ...GetString(StringNames.GamePresetsDescription...); break;
        case 1: this.GameSettingsTab.gameObject.SetActive(true);
                this.MenuDescriptionText.text = ...GetString(StringNames.GameSettingsDescription...); break;
        case 2: this.RoleSettingsTab.gameObject.SetActive(true);
                this.RoleSettingsTab.OpenMenu(false);
                this.MenuDescriptionText.text = ...GetString(StringNames.RoleSettingsDescription...); break;
        }
    }
    // ③ 预览模式：只把右侧遮暗
    if (previewOnly) { this.ToggleLeftSideDarkener(false); this.ToggleRightSideDarkener(true); return; }
    // ④ 正式切换：左侧遮暗 + OpenMenu + 按钮选中态
    this.ToggleLeftSideDarkener(true);
    this.ToggleRightSideDarkener(false);
    switch (tabNum)
    {
    case 0: this.PresetsTab.OpenMenu();     this.GamePresetsButton.SelectButton(true);  return;
    case 1: this.GameSettingsTab.OpenMenu();this.GameSettingsButton.SelectButton(true); return;
    case 2: this.RoleSettingsTab.OpenMenu(true); this.RoleSettingsButton.SelectButton(true); return;
    default: return;
    }
}
```

> 📌 **加第 4 个主页签的精确改法**：`tabNum == 3` 必须同时出现在**上面两个 switch** 里
> （否则 `previewOnly` 分支不 SetActive，正式分支不 OpenMenu），并新增一个 `[SerializeField]` 字段。
> 由于字段是 `private [SerializeField]`，运行时只能靠 `transform.Find(...)` 拿容器 + 反射/interop 写字段，或直接
> **完全绕开 `ChangeTab`，自己写一个 `ModChangeTab`**（本工程 `GameSettingMenuPatch` 走的就是后者思路）。

### 1.5 遮暗器 / 关闭页签

```csharp
private void ToggleLeftSideDarkener(bool on)  { this.LeftSideDarkener.SetActive(Controller.currentTouchType == Controller.TouchType.Joystick && on); }
private void ToggleRightSideDarkener(bool on) { this.RightSideDarkener.SetActive(Controller.currentTouchType == Controller.TouchType.Joystick && on); }
public  void CloseTab() { this.ToggleLeftSideDarkener(false); this.ToggleRightSideDarkener(true); }
```

### 1.6 `Close()`

```csharp
public void Close()
{
    GameSettingMenu.Instance = null;
    Object.Destroy(base.gameObject);
}
```
（在 interop 里 `Instance` 是 **public static 属性**，`Close()` 是 public 方法 —— 可 patch / 可直接调用。）

### 1.7 字段全表（**精确名称与类型**）

| 字段 | 声明 | 类型 | 运行时可见性(interop) |
|---|---|---|---|
| `Instance` | `public static GameSettingMenu Instance;` | `GameSettingMenu` | public 静态属性 |
| `GamePresetsButton` | `[SerializeField] private PassiveButton` | **`PassiveButton`** | public 属性 |
| `GameSettingsButton` | `[SerializeField] private PassiveButton` | **`PassiveButton`** | public 属性 |
| `RoleSettingsButton` | `[SerializeField] private PassiveButton` | **`PassiveButton`** | public 属性 |
| `PresetsTab` | `[SerializeField] private GamePresetsTab` | **`GamePresetsTab`** | public 属性 |
| `GameSettingsTab` | `[SerializeField] private GameOptionsMenu` | **`GameOptionsMenu`** ⚠️ | public 属性 |
| `RoleSettingsTab` | `[SerializeField] private RolesSettingsMenu` | **`RolesSettingsMenu`** | public 属性 |
| `MenuDescriptionText` | `[SerializeField] private TextMeshPro` | `TextMeshPro` | public 属性 |
| `LeftSideDarkener` | `[SerializeField] private GameObject` | `GameObject` | public 属性 |
| `RightSideDarkener` | `[SerializeField] private GameObject` | `GameObject` | public 属性 |
| `BackButton` | `public UiElement BackButton;` | `UiElement` | public |
| `DefaultButtonSelected` | `public UiElement DefaultButtonSelected;` | `UiElement` | public |
| `ControllerSelectable` | `public List<UiElement> ControllerSelectable;` | `List<UiElement>` | public |

### 1.8 ⚠️ 关于 GameObject / Transform 名字 —— 重要更正

源码里**没有任何字符串形式的容器名**。三个页签容器就是上面三个 `[SerializeField]` 字段，
**C# 侧名字 `PresetsTab` / `GameSettingsTab` / `RoleSettingsTab` 只是字段名**，
真实预制体里的 GameObject 名**不在反编译源码里**（源码只有字段引用，没有 `transform.Find(...)`）。

- 实测可用（本工程 `GameSettingMenuPatch.BuildPages` 已在用）：
  `menu.transform.Find("PresetsTab")`、`menu.transform.Find("RoleSettingsTab")`
  —— 即 Unity 层级里 GameObject **恰好**叫这两个名字。
- `GameSettingsTab`（第 2 个，规则页）在层级里的名字**未在源码中确认**；用
  `menu.GetComponentInChildren<GameOptionsMenu>(true)` 拿组件更稳。
- 三个**按钮**对象的定位：不要猜名字，用 §1.7 的 `PassiveButton` 字段（interop 里是属性）直接取
  `GamePresetsButton.gameObject` / `GameSettingsButton.gameObject` / `RoleSettingsButton.gameObject`。
  兜底方案见 `GameSettingMenuPatch.FindButtonByText(menu, keyword)`（按按钮上的 TMP 文本找）。

---

## 2. `RolesSettingsMenu`（角色页）

文件：`RolesSettingsMenu.cs`（652 行）。

### 2.1 顶层结构：它内部有 **2 个互斥的"子页"**（不是页签控件，是 SetActive 切换）

| 子页 GameObject 字段 | 内容 | 由谁控制 |
|---|---|---|
| `RoleChancesSettings` | 「所有角色」配额/概率列表（**滚动**，全角色一行行 `RoleOptionSetting`） | `OpenChancesTab()` |
| `AdvancedRolesSettings` | 单个角色的高级设置（`NumberOption`/`ToggleOption`/`StringOption` 动态生成） | `ChangeTab(role, button)` |

**没有第三个"category tab"概念** —— 但有**一行横向的角色图标页签**（`roleTabs`），
它由 `SetQuotaTab()` **数据驱动生成**（这是全工程最值得克隆的"动态加页签"范例）。

### 2.2 `Awake()` —— stencil 遮罩层 = 20

```csharp
private void Awake()
{
    this.MaskBg.material.SetInt(PlayerMaterial.MaskLayer, 20);
    this.MaskArea.material.SetInt(PlayerMaterial.MaskLayer, 20);
    this.quotaHeader.SetHeader(StringNames.RoleQuotaLabel, 20);
    this.advHeader.SetHeader(StringNames.RoleSettingsLabel, 20);
}
```
> ⚠️ **`MASK_LAYER = 20` 是全菜单统一约定**。任何新克隆的 SpriteRenderer / TMP
> 都必须设置 `material.SetInt(PlayerMaterial.MaskLayer, 20)` 与
> `fontMaterial.SetFloat("_StencilComp", 3f); SetFloat("_Stencil", 20f)`，否则会被遮罩裁掉或不显示。
> 基类 `OptionBehaviour.SetUpFromData(data, maskLayer)` 与 `CategoryHeaderMasked.SetHeader(name, maskLayer)`
> 已自动做这件事。

### 2.3 `InitialSetup()`（惰性初始化）

```csharp
private void InitialSetup()
{
    this.QuotaTabSelectables = new List<UiElement>();
    this.advancedSettingChildren = new List<OptionBehaviour>();
    this.roleChances = new List<RoleOptionSetting>();
    this.SetQuotaTab();
}
```

### 2.4 `OnEnable()`

```csharp
private void OnEnable()
{
    this.RoleChancesSettings.SetActive(true);
    this.AdvancedRolesSettings.SetActive(false);
    this.RefreshChildren();
    // 给 AdvancedRolesSettings 整棵子树补 mask layer 20
    foreach (var sr in this.AdvancedRolesSettings.GetComponentsInChildren<SpriteRenderer>(true))
        sr.material.SetInt(PlayerMaterial.MaskLayer, 20);
    foreach (var tmp in this.AdvancedRolesSettings.GetComponentsInChildren<TextMeshPro>(true))
    { tmp.fontMaterial.SetFloat("_StencilComp", 3f); tmp.fontMaterial.SetFloat("_Stencil", 20f); }
}
private void OnDisable() { this.CloseMenu(); }
```

### 2.5 `OpenChancesTab(bool controllerSelected = true)` —— 「回到全部角色页」

```csharp
public void OpenChancesTab(bool controllerSelected = true)
{
    this.selectedRoleTab = 0;
    this.RoleChancesSettings.SetActive(true);
    this.AdvancedRolesSettings.SetActive(false);
    this.ControllerSelectable.Clear();
    if (this.QuotaTabSelectables == null) { this.InitialSetup(); }      // 首次进入才建行
    if (controllerSelected) { this.ControllerSelectable.AddRange(this.QuotaTabSelectables); }
    this.scrollBar.CalculateAndSetYBounds((float)(this.roleChances.Count + 3), 1f, 6f, 0.43f);
    this.scrollBar.ScrollToTop();
    if (controllerSelected)
    {
        ControllerManager.Instance.CurrentUiState.SelectableUiElements = this.ControllerSelectable;
        ControllerManager.Instance.SetDefaultSelection(this.roleChances[0].ControllerSelectable[0], null);
    }
    var pb = this.currentTabButton; if (pb != null) pb.SelectButton(false);
    this.AllButton.SelectButton(true);
    this.currentTabButton = this.AllButton;
}
```

`OpenMenu(bool controllerSelected = true)` = `OpenOverlayMenu` + `EnableTabControllerGlyphs(true)` + `OpenChancesTab(controllerSelected)`。
`CloseMenu()` = `CloseOverlayMenu` + `roleSettingsTabParent.localPosition = Vector3.zero` + `EnableTabControllerGlyphs(false)`。

### 2.6 `SetQuotaTab()` —— **分类头 + 行 的完整实例化范例（★最该抄的一段）**

```csharp
public void SetQuotaTab()
{
    float num  = 0.662f;      // Y_START
    float num2 = -1.928f;     // X_START
    this.roleTabs = new List<PassiveButton>();
    this.roleTabs.Add(this.AllButton);

    var list  = DestroyableSingleton<RoleManager>.Instance.AllRoles.FindAll(
        r => r.TeamType == RoleTeamTypes.Crewmate && r.Role != RoleTypes.Crewmate && r.Role != RoleTypes.CrewmateGhost);
    var list2 = DestroyableSingleton<RoleManager>.Instance.AllRoles.FindAll(
        r => r.TeamType == RoleTeamTypes.Impostor && r.Role != RoleTypes.Impostor && r.Role != RoleTypes.ImpostorGhost);

    for (int i = 0; i < list.Count;  i++) this.AddRoleTab(list[i],  ref num2);
    for (int j = 0; j < list2.Count; j++) this.AddRoleTab(list2[j], ref num2);

    // ↓↓↓ 分类头 1：Crewmate
    CategoryHeaderEditRole h1 = Object.Instantiate<CategoryHeaderEditRole>(
        this.categoryHeaderEditRoleOrigin, Vector3.zero, Quaternion.identity, this.RoleChancesSettings.transform);
    h1.SetHeader(StringNames.CrewmateRolesHeader, 20);
    h1.transform.localPosition = new Vector3(4.986f, num, -2f);   // X_START_ROLE_HEADER
    num -= 0.522f;                                                // ROLE_HEADER_OFFSET

    int num3 = 0;
    for (int k = 0; k < list.Count; k++) { this.CreateQuotaOption(list[k], ref num, num3); num3++; }
    num -= 0.22f;

    // ↓↓↓ 分类头 2：Impostor
    CategoryHeaderEditRole h2 = Object.Instantiate<CategoryHeaderEditRole>(
        this.categoryHeaderEditRoleOrigin, Vector3.zero, Quaternion.identity, this.RoleChancesSettings.transform);
    h2.SetHeader(StringNames.ImpostorRolesHeader, 20);
    h2.transform.localPosition = new Vector3(4.986f, num, -2f);
    num -= 0.522f;
    for (int l = 0; l < list2.Count; l++) { this.CreateQuotaOption(list2[l], ref num, num3); num3++; }
}
```

> **只有 Crewmate / Impostor 两个分类头。19.0 原版没有 "NeutralRoles"**
> （`grep` 全源码 `NeutralRoles` **零命中**；`RoleTeamTypes` 里只有 `Crewmate` / `Impostor`）。
> 若要做「中立阵营」分类，需要自己造第三段 `CategoryHeaderEditRole` + 颜色（`CategoryHeaderEditRole.SetHeader`
> 只认 `CrewmateRolesHeader` / `ImpostorRolesHeader` 两个 `StringNames` 上色，**其它名字不设色 → 保持预制体原色**）。

### 2.7 `AddRoleTab(RoleBehaviour role, ref float tabXPos)` —— 数据驱动的**横向页签**

```csharp
private void AddRoleTab(RoleBehaviour role, ref float tabXPos)
{
    RoleSettingsTabButton tab;
    if (role.TeamType == RoleTeamTypes.Crewmate)
        tab = Object.Instantiate<RoleSettingsTabButton>(this.roleSettingsTabButtonOrigin,        Vector3.zero, Quaternion.identity, this.roleSettingsTabParent);
    else
        tab = Object.Instantiate<RoleSettingsTabButton>(this.roleSettingsTabButtonOriginImpostor, Vector3.zero, Quaternion.identity, this.roleSettingsTabParent);

    tab.transform.localPosition = new Vector3(tabXPos, 2.275f, -2f);   // TAB_Y_START = 2.275
    tab.SetButton(role, delegate { this.ChangeTab(role, tab.Button); });
    tabXPos += 0.762f;                                                // X_OFFSET = 0.762
    this.roleTabs.Add(tab.Button);
    if (this.roleTabs.Count > 8)
        this.roleSettingsTabScroller.SetBounds(new FloatRange(0f, 0f),
            new FloatRange(-((float)(this.roleTabs.Count - 8) * 0.762f), 0f));
}
```

`RoleSettingsTabButton`（`RoleSettingsTabButton.cs`）：
```csharp
public PassiveButton Button { get; }                                 // 只读属性
public void SetButton(RoleBehaviour role, Action onClick) {
    this.icon.sprite = role.RoleIconWhite;
    this.button.OnClick.AddListener(delegate { onClick(); });
}
// [SerializeField] private SpriteRenderer background / icon;  private PassiveButton button;
```

### 2.8 `CreateQuotaOption(...)` —— **动态新增一行「角色配额」行**

```csharp
private void CreateQuotaOption(RoleBehaviour role, ref float yPos, int index)
{
    RoleOptionSetting ros = Object.Instantiate<RoleOptionSetting>(
        this.roleOptionSettingOrigin, Vector3.zero, Quaternion.identity, this.RoleChancesSettings.transform);
    ros.transform.localPosition = new Vector3(-0.15f, yPos, -2f);      // X_START_CHANCE = -0.15
    ros.SetRole(GameOptionsManager.Instance.CurrentGameOptions.RoleOptions, role, 20);
    ros.OnValueChanged = new Action<OptionBehaviour>(this.ValueChanged);
    ros.SetClickMask(this.ButtonClickMask);
    this.roleChances.Add(ros);
    // 控制器上下导航串联
    for (int i = 0; i < ros.ControllerSelectable.Count; i++) { ... selectOnUp/selectOnDown ... }
    this.QuotaTabSelectables.AddRange(ros.ControllerSelectable);
    if (index < DestroyableSingleton<RoleManager>.Instance.AllRoles.Count - 1) yPos += -0.43f;  // Y_OFFSET
}
```

### 2.9 `CreateAdvancedSettings(RoleBehaviour role)` —— **★「按类型动态建行」的模板**

对某个角色的 `role.AllGameSettings`（`List<BaseGameSetting>`）逐条按 `baseGameSetting.Type` 建行：

```csharp
private void CreateAdvancedSettings(RoleBehaviour role)
{
    for (int i = 0; i < this.advancedSettingChildren.Count; i++)
        Object.Destroy(this.advancedSettingChildren[i].gameObject);
    this.ControllerSelectable.Clear();
    this.advancedSettingChildren.Clear();

    float num = -0.872f;                                   // Y_ADVANCED_START
    foreach (BaseGameSetting baseGameSetting in role.AllGameSettings)
    {
        switch (baseGameSetting.Type)
        {
        case OptionTypes.Checkbox:
        {
            OptionBehaviour ob = Object.Instantiate<ToggleOption>(this.checkboxOrigin, Vector3.zero, Quaternion.identity, this.AdvancedRolesSettings.transform);
            ob.transform.localPosition = new Vector3(2.17f, num, -2f);   // X_ADVANCED_START
            ob.SetUpFromData(baseGameSetting, 20);
            ob.SetClickMask(this.ButtonClickMask);
            ob.LabelBackground.enabled = false;
            ob.AssociatedRole = role.Role;
            this.advancedSettingChildren.Add(ob);
            this.ControllerSelectable.AddRange(ob.GetComponentsInChildren<UiElement>(false));
            break;
        }
        case OptionTypes.String:      /* 同上，用 this.stringOptionOrigin  → StringOption  */ break;
        case OptionTypes.Float:
        case OptionTypes.Int:         /* 同上，用 this.numberOptionOrigin  → NumberOption  */ break;
        }
        num += -0.45f;                                          // Y_ADVANCED_OFFSET
    }
    this.scrollBar.CalculateAndSetYBounds((float)(role.AllGameSettings.Count + 3), 1f, 6f, 0.45f);
    this.InitializeControllerNavigation();
    this.scrollBar.ScrollToTop();
}
```

> **注意缺少 `case OptionTypes.Player:`** —— 角色高级设置**不支持** Player 类型。
> 而 `GameOptionsMenu.CreateSettings` **有** Player 分支。对照 §7。

### 2.10 `ChangeTab(RoleBehaviour role, PassiveButton button)`（进入单个角色页）

```csharp
private void ChangeTab(RoleBehaviour role, PassiveButton button)
{
    var pb = this.currentTabButton; if (pb != null) pb.SelectButton(false);
    button.SelectButton(true);
    this.currentTabButton = button;
    this.CreateAdvancedSettings(role);
    this.roleDescriptionText.text = ...GetString(role.BlurbNameMed...);
    this.roleTitleText.text       = ...GetString(role.StringName...);
    this.roleScreenshot.sprite    = role.RoleScreenshot;
    if (role.TeamType == RoleTeamTypes.Crewmate)
    { this.roleHeaderSprite.color = Palette.CrewmateRoleHeaderBlue; this.roleHeaderText.color = Palette.CrewmateRoleHeaderTextBlue; }
    else
    { this.roleHeaderSprite.color = Palette.ImpostorRoleHeaderRed;  this.roleHeaderText.color = Palette.ImpostorRoleHeaderTextRed; }

    for (int i = 0; i < this.advancedSettingChildren.Count; i++)
    {
        var ob = this.advancedSettingChildren[i];
        ob.OnValueChanged = new Action<OptionBehaviour>(this.ValueChanged);
        if (AmongUsClient.Instance && !AmongUsClient.Instance.AmHost) ob.SetAsPlayer();   // 非房主：禁掉按钮
    }
    this.RoleChancesSettings.SetActive(false);
    this.AdvancedRolesSettings.SetActive(true);
    this.RefreshChildren();
    ControllerManager.Instance.CurrentUiState.SelectableUiElements = this.ControllerSelectable;
    ControllerManager.Instance.SetDefaultSelection(this.ControllerSelectable[0], null);
}
```

### 2.11 `RefreshChildren()`（每帧 `Update` 里按数据引用变化触发）

```csharp
private void RefreshChildren()
{
    // 强制 OptionBehaviour 走一遍 OnDisable/OnEnable → 重新 Initialize() 读值
    if (this.advancedSettingChildren != null)
        foreach (var ob in this.advancedSettingChildren) { ob.enabled = false; ob.enabled = true; }
    if (this.roleChances != null)
        foreach (var rc in this.roleChances) rc.UpdateValuesAndText(GameOptionsManager.Instance.CurrentGameOptions.RoleOptions);
}
```
`Update()` 里的触发条件：`if (this.cachedData != GameOptionsManager.Instance.CurrentGameOptions.RoleOptions) { cachedData = ...; RefreshChildren(); }`
（**引用比较**，不是内容比较。）

### 2.12 `ValueChanged(OptionBehaviour obj)` —— 提交 + 宣告

```csharp
private void ValueChanged(OptionBehaviour obj)
{
    if (obj is RoleOptionSetting ros)
    {
        GameOptionsManager.Instance.CurrentGameOptions.RoleOptions
            .SetRoleRate(ros.Role.Role, ros.RoleMaxCount, ros.RoleChance);
        ros.UpdateValuesAndText(GameOptionsManager.Instance.CurrentGameOptions.RoleOptions);
        DestroyableSingleton<HudManager>.Instance.Notifier
            .AddRoleSettingsChangeMessage(ros.Role.StringName, ros.RoleMaxCount, ros.RoleChance, ros.Role.TeamType, false);
    }
    else
    {
        float value = GameOptionsManager.Instance.CurrentGameOptions.GetValue(obj.Data);
        DestroyableSingleton<HudManager>.Instance.Notifier
            .AddSettingsChangeMessage(obj.Title, obj.GetValueString(value), false, obj.AssociatedRole);
    }
    GameOptionsManager.Instance.CurrentGameOptions.SetInt(Int32OptionNames.RulePreset, 100);  // 100 = Custom
    GameOptionsManager.Instance.CurrentGameOptions.SetBool(BoolOptionNames.IsDefaults, false);
    this.RefreshChildren();
    GameOptionsManager.Instance.GameHostOptions = GameOptionsManager.Instance.CurrentGameOptions;
    GameManager.Instance.LogicOptions.SyncOptions();          // ★ 同步给所有客户端
}
```

### 2.13 `RolesSettingsMenu` 字段全表

| 字段 | 类型 | 用途 |
|---|---|---|
| `MaskBg`, `MaskArea` | `SpriteRenderer` | 遮罩底图（layer 20） |
| `quotaHeader`, `advHeader` | `CategoryHeaderMasked` | 两页各自的固定标题条 |
| `roleSettingsTabButtonOrigin` | `RoleSettingsTabButton` | **船员**页签预制（克隆源） |
| `roleSettingsTabButtonOriginImpostor` | `RoleSettingsTabButton` | **内鬼**页签预制（克隆源） |
| `roleSettingsTabParent` | `Transform` | 横向页签父节点 |
| `roleSettingsTabScroller` | `Scroller` | 页签横向滚动 |
| `roleOptionSettingOrigin` | `RoleOptionSetting` | **配额行预制（克隆源）** |
| `RoleChancesSettings` | `GameObject` | 「所有角色」页容器 |
| `categoryHeaderEditRoleOrigin` | `CategoryHeaderEditRole` | **分类头预制（克隆源）** |
| `checkboxOrigin` | `ToggleOption` | 复选行预制（克隆源） |
| `numberOptionOrigin` | `NumberOption` | 数值行预制（克隆源） |
| `stringOptionOrigin` | `StringOption` | 字符串行预制（克隆源） |
| `roleTitleText`, `roleDescriptionText` | `TextMeshPro` | 角色名 / 简介 |
| `AdvancedRolesSettings` | `GameObject` | 单角色高级设置页容器 |
| `roleScreenshot` | `SpriteRenderer` | 角色截图 |
| `roleHeaderSprite`, `roleHeaderText` | `SpriteRenderer` / `TextMeshPro` | 角色页头 |
| `scrollBar` | `Scroller` | 垂直滚动 |
| `ButtonClickMask` | `Collider2D` | 点击遮罩（`SetClickMask` 用） |
| `roleTabsScroller` | `Scroller` | 页签滚动 |
| `roleTabsGradient` | `GameObject` | 页签左侧渐隐 |
| `roleTabsMinPos`, `roleTabsMaxPos` | `Transform` | 页签滚动边界参考 |
| `advancedSettingChildren` | `List<OptionBehaviour>` | **private** 动态行列表 |
| `roleChances` | `List<RoleOptionSetting>` | **private** 配额行列表 |
| `roleTabs` | `List<PassiveButton>` | **private** 页签按钮列表 |
| `selectedRoleTab` | `int` | **private** 当前页签索引 |
| `cachedData` | `IRoleOptionsCollection` | 数据变更检测 |
| `AllButton` | `PassiveButton` | 「全部」页签 |
| `BackButton`, `DefaultButtonSelected` | `UiElement` | 控制器 |
| `ControllerSelectable` | `List<UiElement>` | 控制器可选元素 |
| `QuotaTabSelectables` | `List<UiElement>` | **private** |
| `glyphL`, `glyphR` | `SpriteRenderer` | 左右肩键图标 |
| `glyphUnavailableColor` | `readonly Color(0.28,0.28,0.28,0.95)` | 不可用色 |
| `currentTabButton` | `PassiveButton` | **private** |

### 2.14 常量（**布局精确值，克隆新行时直接复用**）

```csharp
ROLE_HEADER_OFFSET = 0.522f;   X_START_ROLE_HEADER = 4.986f;   X_START_CHANCE = -0.15f;
Y_START = 0.662f;              Y_OFFSET = -0.43f;              X_START = -1.928f;
X_OFFSET = 0.762f;             TAB_Y_START = 2.275f;
Y_ADVANCED_START = -0.872f;    Y_ADVANCED_OFFSET = -0.45f;     X_ADVANCED_START = 2.17f;
MASK_LAYER = 20;
```
> 所有运行时新建的对象 **localPosition.z 一律 `-2f`**（原版约定：内容层在 -2）。

---

## 3. `GamePresetsTab`（预设页）

文件：`GamePresetsTab.cs`（211 行）。**只有 2 个硬编码预设按钮，没有列表、没有 import/export。**

### 3.1 字段全表

| 字段 | 类型 | 备注 |
|---|---|---|
| `SpritesToDesaturate` | `List<SpriteRenderer>` | Start 里全部 `_Desat = 1f`（变灰） |
| `StandardPresetButton` | `PassiveButton` | 标准预设按钮 |
| `SecondPresetButton` | `PassiveButton` | 备用预设按钮（**名字不叫 Alternate**） |
| `GameOptionsMenu` | `GameOptionsMenu` | **字段名与类型同名**（C# 允许），用于回调 `ClickPresetButton(preset)` |
| `StandardRulesSprites` | `SpriteRenderer[]` | 贴 `GameSettingsList.StandardRulesImage` |
| `AlternateRulesSprites` | `SpriteRenderer[]` | 贴 `GameSettingsList.AlternateRulesImage` |
| `StandardRulesText` | `TextMeshPro` | `GameSettingsList.StandardRulesName` |
| `AlternateRulesText` | `TextMeshPro` | `GameSettingsList.AlternateRulesName` |
| `PresetDescriptionText` | `TextMeshPro` | 悬浮/选中时显示描述 |
| `ConfirmPresetPopUp` | `TransitionOpen` | **确认弹窗** |
| `PresetConfirmButton` | `PassiveButton` | 确认 |
| `PresetCancelButtons` | `PassiveButton[]` | 取消 |
| `BackButton`, `DefaultButtonSelected` | `UiElement` | public |
| `ControllerSelectable` | `List<UiElement>` | public |

### 3.2 `Start()` —— 按钮接线

```csharp
private void Start()
{
    this.StandardPresetButton.OnClick.AddListener(delegate { this.ClickPresetButton(RulesPresets.Standard, true); });
    this.StandardPresetButton.OnMouseOver.AddListener(delegate {
        this.PresetDescriptionText.text = ...GetString(GameManager.Instance.GameSettingsList.StandardPresetDescription...); });
    this.StandardPresetButton.OnMouseOut.AddListener(delegate { this.SetSelectedText(); });

    this.SecondPresetButton.OnClick.AddListener(delegate {
        this.ClickPresetButton(GameManager.Instance.GameSettingsList.AlternateRulesType, false); });
    this.SecondPresetButton.OnMouseOver.AddListener(delegate {
        this.PresetDescriptionText.text = ...GetString(GameManager.Instance.GameSettingsList.AlternatePresetDescription...); });
    this.SecondPresetButton.OnMouseOut.AddListener(delegate { this.SetSelectedText(); });

    this.StandardRulesText.text = ...GetString(GameManager.Instance.GameSettingsList.StandardRulesName...);
    this.AlternateRulesText.text = ...GetString(GameManager.Instance.GameSettingsList.AlternateRulesName...);
    this.StandardRulesSprites.ForEach(s => s.sprite = GameManager.Instance.GameSettingsList.StandardRulesImage);
    this.AlternateRulesSprites.ForEach(s => s.sprite = GameManager.Instance.GameSettingsList.AlternateRulesImage);
    this.SpritesToDesaturate.ForEach(s => s.material.SetFloat("_Desat", 1f));
}
```

### 3.3 `OnEnable()` —— 按当前 `RulesPreset` 回显选中态

```csharp
private void OnEnable()
{
    if (GameOptionsManager.Instance.CurrentGameOptions.RulesPreset == RulesPresets.Standard)
    { SecondPresetButton.SelectButton(false); StandardPresetButton.SelectButton(true);
      StandardPresetButton.ReceiveMouseOut(); SecondPresetButton.ReceiveMouseOut(); }
    else if (... != RulesPresets.Custom)
    { SecondPresetButton.SelectButton(true); StandardPresetButton.SelectButton(false);
      StandardPresetButton.ReceiveMouseOut(); SecondPresetButton.ReceiveMouseOut(); }
    else { SecondPresetButton.SelectButton(false); StandardPresetButton.SelectButton(false); }
    this.SetSelectedText();
}
private void OnDisable() { this.CloseMenu(); }
public  void OpenMenu()  { ControllerManager.Instance.OpenOverlayMenu(base.name, this.BackButton, this.DefaultButtonSelected, this.ControllerSelectable, false); }
public  void CloseMenu() { ControllerManager.Instance.CloseOverlayMenu(base.name); }
```

### 3.4 `ClickPresetButton` —— 走确认弹窗

```csharp
private void ClickPresetButton(RulesPresets preset, bool standardButtonSelected)
{
    if (GameOptionsManager.Instance.CurrentGameOptions.RulesPreset == preset) return;   // 已选中则忽略
    this.ConfirmPresetPopUp.gameObject.SetActive(true);
    this.PresetConfirmButton.OnClick.RemoveAllListeners();                              // ★ 先清空再挂
    this.PresetCancelButtons.ForEach(x => x.OnClick.RemoveAllListeners());
    this.PresetConfirmButton.OnClick.AddListener(delegate {
        this.GameOptionsMenu.ClickPresetButton(preset);          // 真正改数据
        this.SetSelectedText();
        this.SecondPresetButton.SelectButton(!standardButtonSelected);
        this.StandardPresetButton.SelectButton(standardButtonSelected);
        ControllerManager.Instance.CloseOverlayMenu(this.ConfirmPresetPopUp.name);
        this.ConfirmPresetPopUp.Close();
    });
    this.PresetCancelButtons.ForEach(x => x.OnClick.AddListener(delegate {
        ControllerManager.Instance.CloseOverlayMenu(this.ConfirmPresetPopUp.name);
        this.ConfirmPresetPopUp.Close();
    }));
    ControllerManager.Instance.OpenOverlayMenu(this.ConfirmPresetPopUp.name,
        this.PresetCancelButtons.First<PassiveButton>(), this.PresetConfirmButton);
}
```

### 3.5 数据来源 `GameSettingsCategoryList`（ScriptableObject）

```csharp
[CreateAssetMenu] public class GameSettingsCategoryList : ScriptableObject {
    public StringNames GameModeName;
    public Sprite StandardRulesImage, AlternateRulesImage;
    public StringNames StandardRulesName, AlternateRulesName;
    public StringNames StandardPresetDescription, AlternatePresetDescription;
    public RulesPresets AlternateRulesType;
    public BaseGameSetting MapNameSetting;
    public List<BaseGameSetting> OverviewSettings;
    public List<RulesCategory> AllCategories;     // ★ GameOptionsMenu 用它生成分类头
}
```
取用点：`GameManager.Instance.GameSettingsList`（`GameSettingsCategoryList` 类型）。
真正改预设：`GameOptionsMenu.ClickPresetButton(RulesPresets)` → `SetRecommendations(...)`。
**没有 import/export / 存盘 / 读盘 —— 预设完全是硬编码的 Standard / Alternate 两套。**

---

## 4. 选项行控件继承体系

```
MonoBehaviour
├── OptionBehaviour                 (abstract, OptionBehaviour.cs)
│   ├── NumberOption                (NumberOption.cs)
│   ├── ToggleOption                (ToggleOption.cs)
│   ├── StringOption                (StringOption.cs)
│   ├── PlayerOption                (PlayerOption.cs)
│   └── RoleOptionSetting           (RoleOptionSetting.cs)   ★ 不在任务清单里但很关键
├── CategoryHeaderMasked            (MonoBehaviour, 不是 OptionBehaviour!)
│   ├── CategoryHeaderEditRole      (角色配额页上色版)
│   └── CategoryHeaderRoleVariant   (大厅查看页版, 带 icon)
├── GameOptionButton : PassiveButton   (给 +/- 用)
└── GameOptionsMapPicker            (地图选择器, 实现为 OptionBehaviour 用法)
```

> ⚠️ **`KeyValueOption` 在 19.0 源码中完全不存在**（`grep KeyValueOption|KeyValueGameSetting` **零命中**）。
> 它属于**旧版本/模组**（18.0 及更早或第三方 mod 自定义）。19.0 的等价物是 **`StringOption`**（`StringNames[] Values` 左右翻）。
> **不要再找 KeyValueOption。**

### 4.1 `OptionBehaviour`（基类，`abstract`）

```csharp
public abstract class OptionBehaviour : MonoBehaviour
{
    public BaseGameSetting Data { get; }                  // 只读
    public virtual float GetFloat()  => throw new NotImplementedException();
    public virtual int   GetInt()    => throw new NotImplementedException();
    public virtual bool  GetBool()   => throw new NotImplementedException();

    public void SetAsPlayer();                            // 非房主：GetComponentsInChildren<PassiveButton>() 全部 SetActive(false)
    public void SetClickMask(Collider2D clickMask);       // 给所有子 PassiveButton 设 ClickMask
    public virtual void SetUpFromData(BaseGameSetting data, int maskLayer);  // 设 data + 全子树 mask layer/stencil
    public virtual void Initialize() { }                  // 空实现，子类重写
    public string GetValueString(float value) => this.data.GetValueString(value);

    // ---- 字段 ----
    public SpriteRenderer LabelBackground;
    public StringNames    Title;                          // ★ 标题的翻译键（不是字符串！）
    public Action<OptionBehaviour> OnValueChanged;        // ★ 值改变回调
    public RoleTypes      AssociatedRole;
    protected BaseGameSetting data;
    private PassiveButton[] buttons;
}
```

⚠️ 注意：
- **没有 `TitleText` / `LabelText` 在基类**，`TitleText` 是各子类自己的字段；
- **没有 `SetValue(...)` 方法** —— 改值要用子类的公开字段（`Value`）或 `Increase()/Decrease()`；
- **`Initialize()` 是 public virtual**，`GameOptionsMenu.RefreshChildren()` 就是靠调它刷新。

### 4.2 `SetUpFromData` 的 mask 处理（基类实现）

```csharp
public virtual void SetUpFromData(BaseGameSetting data, int maskLayer)
{
    this.data = data;
    foreach (var sr in base.GetComponentsInChildren<SpriteRenderer>(true))
        sr.material.SetInt(PlayerMaterial.MaskLayer, maskLayer);
    foreach (var tmp in base.GetComponentsInChildren<TextMeshPro>(true))
    { tmp.fontMaterial.SetFloat("_StencilComp", 3f); tmp.fontMaterial.SetFloat("_Stencil", (float)maskLayer); }
}
```

### 4.3 `NumberOption` —— **数值行**

```csharp
public class NumberOption : OptionBehaviour
{
    public TextMeshPro TitleText;
    public TextMeshPro ValueText;
    public float  Value = 1f;
    public float  Increment;
    public FloatRange ValidRange = new FloatRange(0f, 2f);
    public string FormatString = "0.0";               // ★ 显示格式（ToString(format)）
    public bool   ZeroIsInfinity;
    public NumberSuffixes SuffixType = NumberSuffixes.Multiplier;   // ★ 后缀类型

    private float oldValue = float.MaxValue;
    private FloatOptionNames floatOptionName;
    private Int32OptionNames intOptionName;
    [SerializeField] private GameOptionButton PlusBtn;
    [SerializeField] private GameOptionButton MinusBtn;

    public override void SetUpFromData(BaseGameSetting data, int maskLayer);   // 按 FloatGameSetting/IntGameSetting 填上面字段
    public override void Initialize();        // TitleText.text = GetString(Title); Value = CurrentGameOptions.GetValue(data); AdjustButtonsActiveState();
    public void Increase();                   // Value = ValidRange.Clamp(Value + Increment); UpdateValue(); OnValueChanged(this); AdjustButtonsActiveState();
    public void Decrease();                   // 同理 -Increment
    public override float GetFloat() => this.Value;
    public override int   GetInt()   => (int)this.Value;
    private void UpdateValue();               // ★ 写回：SetFloat(floatOptionName,..) 或 SetInt(intOptionName,..)
    private void AdjustButtonsActiveState();  // 到 min/max 时 SetInteractable(false)
    private void FixedUpdate();               // 检测 Value 变化 → ValueText.text = data.GetValueString(Value)
}
```

**⚠️ 非常关键：#. ValueText 的文字不是 `NumberOption` 自己拼的，而是委托给 `data.GetValueString(Value)`：**

```csharp
private void FixedUpdate()
{
    if (this.oldValue != this.Value)
    {
        this.oldValue = this.Value;
        this.ValueText.text = this.data.GetValueString(this.Value);   // ★ 后缀/单位在这里决定
    }
}
```

### 4.4 后缀 / 单位是**怎么渲染的**（★ 任务重点）

**没有独立的 "suffix 字段在 `NumberOption` 上用于显示"** —— 真正的后缀逻辑在 **`BaseGameSetting.GetValueString(float)`** 的
两个子类里，`NumberOption.SuffixType` 只是从 setting 拷贝过来的**镜像副本**（`NumberOption.Increase()`/`FixedUpdate` 都不读它，
只有 `SetUpFromData` 写它）。**改显示单位必须改 setting，或者绕过 `FixedUpdate` 直接写 `ValueText.text`。**

```csharp
public enum NumberSuffixes { None, Multiplier, Seconds }

// FloatGameSetting.GetValueString / IntGameSetting.GetValueString（两者逻辑一致）
public override string GetValueString(float value)
{
    string text = string.Empty;
    if (this.ZeroIsInfinity && Mathf.Abs(value) < 0.0001f)
        text = "<b>∞</b>";                                          // IntGameSetting 版是 "∞"（无 <b>）
    else if (this.SuffixType == NumberSuffixes.None)
        text = value.ToString(this.FormatString);                   // 无后缀，纯数字按 FormatString
    else if (this.SuffixType == NumberSuffixes.Multiplier)
        text = value.ToString(this.FormatString) + "x";             // ★ "1.0x"（速度/亮度倍率）
    else
        text = DestroyableSingleton<TranslationController>.Instance.GetString(
                   StringNames.GameSecondsAbbrev, new object[] { value.ToString(this.FormatString) });  // ★ "15s"（秒，走翻译表）
    return text;
}
```

> **"Kill Distance" 不是 NumberOption，是 StringOption**（见下），所以它显示成 Short/Normal/Long 而不是数字。
> **百分比（如角色概率）在 `RoleOptionSetting` 里是裸 `int.ToString()`，没有 % 号**（见 §4.7）。

对应的 setting 数据类：

```csharp
public abstract class BaseGameSetting : ScriptableObject {
    public StringNames Title;
    public OptionTypes Type;
    public abstract string GetValueString(float value);
}

// FloatGameSetting (OptionTypes.Float)
public FloatOptionNames OptionName; public float Value, Increment;
public FloatRange ValidRange; public bool ZeroIsInfinity;
public NumberSuffixes SuffixType; public string FormatString;

// IntGameSetting (OptionTypes.Int)
public Int32OptionNames OptionName; public int Value, Increment;
public IntRange ValidRange; public bool ZeroIsInfinity;
public NumberSuffixes SuffixType; public string FormatString;
```

### 4.5 `ToggleOption` —— 复选行

```csharp
public class ToggleOption : OptionBehaviour
{
    public TextMeshPro    TitleText;
    public SpriteRenderer CheckMark;
    private bool oldValue;
    private BoolOptionNames boolOptionName;

    public override void SetUpFromData(BaseGameSetting data, int maskLayer);  // 取 CheckboxGameSetting.Title / .OptionName
    public override void Initialize();     // TitleText.text = ...; CheckMark.enabled = (GetValue(data) == 1f)
    public void Toggle();                  // CheckMark.enabled = !CheckMark.enabled; UpdateValue(); OnValueChanged(this);
    public override bool GetBool() => this.CheckMark.enabled;   // ★ 状态就存在 SpriteRenderer.enabled 上
    private void UpdateValue();            // SetBool(boolOptionName, GetBool())
    private void FixedUpdate();            // 外部改了值时同步 CheckMark.enabled
}
```
> **没有 `Increase/Decrease`**，只有 `Toggle()`。**值 = `CheckMark.enabled`**（"没有 Value 字段"）。

### 4.6 `StringOption` —— 字符串/枚举行（**Kill Distance / TaskBarMode 用它**）

```csharp
public class StringOption : OptionBehaviour
{
    public TextMeshPro   TitleText;
    public TextMeshPro   ValueText;
    public StringNames[] Values;      // ★ 候选项的翻译键数组
    public int           Value;       // ★ 当前索引
    private int oldValue = -1;
    private Int32OptionNames stringOptionName;      // ★ 注意底层是 Int32OptionNames
    [SerializeField] private GameOptionButton PlusBtn;
    [SerializeField] private GameOptionButton MinusBtn;

    public override void SetUpFromData(BaseGameSetting data, int maskLayer);  // 从 StringGameSetting 取 Title/Index/Values/OptionName
    public override void Initialize();   // Value = (int)GetValue(data); TitleText=...; ValueText.text = GetString(Values[Value]);
    public void Increase();              // Value = Mathf.Clamp(Value + 1, 0, Values.Length - 1); UpdateValue(); OnValueChanged(this); AdjustButtonsActiveState();
    public void Decrease();              // -1
    public override int GetInt() => this.Value;
    private void UpdateValue();          // SetInt(stringOptionName, GetInt())
    private void FixedUpdate();          // ValueText.text = GetString(Values[Value])
}
```
数据类：
```csharp
public class StringGameSetting : BaseGameSetting {
    public Int32OptionNames OptionName; public StringNames[] Values; public int Index;
    public override string GetValueString(float value) => GetString(this.Values[(int)value], ...);
}
```

### 4.7 `PlayerOption` —— 玩家选择行

```csharp
public class PlayerOption : OptionBehaviour
{
    public TextMeshPro TitleText;
    public TextMeshPro ValueText;
    public int Value = -1;                 // ★ 值是 PlayerId，-1 = RoundRobin（轮换）
    private List<NetworkedPlayerInfo> Values;
    private int oldValue = -1, playerIndex = -1;
    private Int32OptionNames optionName;
    [SerializeField] private TextMeshPro PlusTxt, MinusTxt, OptionUnavailableTxt;
    [SerializeField] private GameOptionButton PlusBtn, MinusBtn;
    [SerializeField] private GameObject ValueBox;

    public void OnEnable();        // 不是 override（★ 它没有 override Initialize！）；从 GameData.Instance.AllPlayers 建列表
    public void Increase();        // playerIndex++; CheckValueChanged(); OnValueChanged(this); AdjustButtonsActiveState();
    public void Decrease();
    public override int  GetInt(); // 公开房返回 -1，否则 Value
    private void SetValueText();   // ★ 公开房：隐藏 ValueText/ValueBox/Plus/Minus，显示 OptionUnavailableTxt
    private void UpdateValue();    // SetInt(optionName, GetInt())
}
```
> ⚠️ `PlayerOption` **重写的是 `OnEnable()` 而不是 `Initialize()`**，
> 而 `GameOptionsMenu.RefreshChildren()` 调的是 `Children[i].Initialize()` —— 对 PlayerOption 是空实现。
> 它靠 `FixedUpdate` 里 `CheckValueChanged()` 自刷。

### 4.8 `RoleOptionSetting` —— **双数值行（数量 + 概率）**，角色配额专用

```csharp
public class RoleOptionSetting : OptionBehaviour
{
    public int RoleMaxCount { get; }              // 只读
    public int RoleChance   { get; }              // 只读
    public RoleBehaviour Role { get; }            // 只读
    public List<PassiveButton> ControllerSelectable { get; }   // ★ 4 个按钮

    public void SetRole(IRoleOptionsCollection options, RoleBehaviour role, int maskLayer);
    public void UpdateValuesAndText(IRoleOptionsCollection options);   // 从 options 读回 count/chance 刷新文本
    public void IncreaseCount();  public void DecreaseCount();
    public void IncreaseChance(); public void DecreaseChance();
    // 联动：IncreaseCount 从 0→1 时把 roleChance 设为 50；DecreaseCount 到 0 时 roleChance=0
    //       IncreaseChance 从 0→10 时把 roleMaxCount 设为 1；DecreaseChance 到 0 时 roleMaxCount=0
    private int RoleMax => Mathf.Min(this.role.MaxCount, 15);

    [SerializeField] private TextMeshPro titleText, countText, chanceText;
    [SerializeField] private SpriteRenderer labelSprite;              // 队伍色 Palette.CrewmateRoleBlue / ImpostorRoleRed
    [SerializeField] private List<PassiveButton> controllerSelectable;
    [SerializeField] private GameOptionButton CountPlusBtn, CountMinusBtn, ChancePlusBtn, ChanceMinusBtn;
    private RoleBehaviour role; private int roleMaxCount, roleChance;
}
```
**概率显示原文**（**无 `%` 后缀**）：
```csharp
public void UpdateValuesAndText(IRoleOptionsCollection options)
{
    this.roleMaxCount = options.GetNumPerGame(this.role.Role);
    this.roleChance   = options.GetChancePerGame(this.role.Role);
    this.countText.text  = this.roleMaxCount.ToString();
    this.chanceText.text = this.roleChance.ToString();      // ★ 裸数字，没有 "%"
    this.AdjustCountButtonsActiveState();
    this.AdjustChanceButtonsActiveState();
}
```
> 若新行要显示 `50%`，**必须自己改 `chanceText.text`**（原版就是纯 `"50"`）。
> 角色名走 `this.titleText.text = this.role.NiceName;`（**不是 TranslationController**）。

### 4.9 `GameOptionButton : PassiveButton` —— +/- 按钮

```csharp
public class GameOptionButton : PassiveButton
{
    public void SetInteractable(bool interactable);   // ★ 只改 buttonSprite.color + isInteractable 标志，不 SetActive
    public override void ReceiveMouseOver();  ReceiveMouseOut();  ReceiveClickUp();  ReceiveClickDown();
    private void AdjustHoverColors(bool isHovering);  AdjustClickedColors(bool clickDown);
    private IEnumerator DelayControllerClickUp();
    [SerializeField] private SpriteRenderer buttonSprite;
    [SerializeField] private Color interactableColor, interactableHoveredColor, interactableClickColor,
                                    uninteractableColor, uninteractableHoveredColor, uninteractableClickColor;
    private bool isInteractable = true;
}
```
> ⚠️ `SetInteractable(false)` **只变灰，点击仍会触发 `OnClick`**。要真正禁用需另想办法。

### 4.10 `OptionTypes` 与行的对应关系（**总表**）

| `OptionTypes` | Setting 类 | 行控件 | 有行的菜单 |
|---|---|---|---|
| `Checkbox` | `CheckboxGameSetting` | `ToggleOption` | GameOptionsMenu ✅ / RolesSettingsMenu ✅ |
| `String` | `StringGameSetting` | `StringOption` | 两者 ✅ |
| `Float` | `FloatGameSetting` | `NumberOption` | 两者 ✅ |
| `Int` | `IntGameSetting` | `NumberOption` | 两者 ✅ |
| `Player` | `PlayerSelectionGameSetting` | `PlayerOption` | **仅 GameOptionsMenu** ✅ / RolesSettingsMenu ❌ |
| `Map` | `MapSelectionGameSetting` | `GameOptionsMapPicker` | 仅 GameOptionsMenu（走 `MapPicker` 特殊路径） |

---

## 5. 分类头（Category Header）的实例化模式

### 5.1 类层次

```csharp
public class CategoryHeaderMasked : MonoBehaviour      // ★ 注意：不是 OptionBehaviour 子类！
{
    public virtual void SetHeader(StringNames name, int maskLayer)
    {
        this.Title.text = DestroyableSingleton<TranslationController>.Instance.GetString(name, Array.Empty<object>());
        this.Background.material.SetInt(PlayerMaterial.MaskLayer, maskLayer);
        if (this.Divider != null) this.Divider.material.SetInt(PlayerMaterial.MaskLayer, maskLayer);
        this.Title.fontMaterial.SetFloat("_StencilComp", 3f);
        this.Title.fontMaterial.SetFloat("_Stencil", (float)maskLayer);
    }
    [SerializeField] protected TextMeshPro    Title;
    [SerializeField] protected SpriteRenderer Background;
    [SerializeField] protected SpriteRenderer Divider;
}
```

**三个具体类对比：**

| 类 | 继承 | 额外成员 | 上色行为 |
|---|---|---|---|
| `CategoryHeaderMasked` | `MonoBehaviour` | — | **不上色**（用预制体原色） |
| `CategoryHeaderEditRole` | `CategoryHeaderMasked` | `blankLabel`, `chanceLabel`, `countLabel` (`SpriteRenderer`) | 只认 `CrewmateRolesHeader` / `ImpostorRolesHeader`，其余**不上色** |
| `CategoryHeaderRoleVariant` | `CategoryHeaderMasked` | `icon` (`SpriteRenderer`) | 同上；另有 `SetHeader(name, maskLayer, bool crewmate, Sprite roleIcon = null)` 重载 |

`CategoryHeaderEditRole.SetHeader` 的颜色（**角色配额页头**）：
```csharp
if (name == StringNames.CrewmateRolesHeader) {
    Title.color = Palette.CrewmateRoleHeaderTextBlue;  Background.color = Palette.CrewmateRoleHeaderBlue;
    blankLabel.color = Palette.CrewmateRoleHeaderVeryDarkBlue;
    chanceLabel.color = Palette.CrewmateRoleHeaderDarkBlue;  countLabel.color = Palette.CrewmateRoleHeaderDarkBlue;
}
else if (name == StringNames.ImpostorRolesHeader) {
    Title.color = Palette.ImpostorRoleHeaderTextRed;   Background.color = Palette.ImpostorRoleHeaderRed;
    blankLabel.color = Palette.ImpostorRoleHeaderVeryDarkRed;
    chanceLabel.color = Palette.ImpostorRoleHeaderDarkRed;  countLabel.color = Palette.ImpostorRoleHeaderDarkRed;
}
// 注意：SetHeader 开头还会对整棵子树的 SpriteRenderer/TMP 统一设 mask layer
```

### 5.2 实例化模式（`GameOptionsMenu.CreateSettings`，**标准写法**）

```csharp
float num = 0.713f;                                   // START_POS_Y
foreach (RulesCategory rulesCategory in GameManager.Instance.GameSettingsList.AllCategories)
{
    CategoryHeaderMasked hdr = Object.Instantiate<CategoryHeaderMasked>(
        this.categoryHeaderOrigin, Vector3.zero, Quaternion.identity, this.settingsContainer);
    hdr.SetHeader(rulesCategory.CategoryName, 20);     // ★ 必须传 maskLayer=20
    hdr.transform.localScale    = Vector3.one * 0.63f; // ★ HEADER_SCALE = 0.63（分类头要缩放！）
    hdr.transform.localPosition = new Vector3(-0.903f, num, -2f);   // HEADER_X
    num -= 0.63f;                                      // HEADER_HEIGHT

    foreach (BaseGameSetting baseGameSetting in rulesCategory.AllGameSettings) { /* 建行, num -= 0.45f */ }
}
```

`RolesSettingsMenu.SetQuotaTab` 里的版本**不缩放**（用 `Scale = 1`，`X = 4.986f`，`Y 步进 0.522f`）——
**分类头的 scale 是每个菜单各自的手工调整，不是组件内置。**

### 5.3 `LobbyViewSettingsPane` 里的第三种用法（大厅「查看设置」页，供对照）

```csharp
CategoryHeaderMasked categoryHeaderMasked = Object.Instantiate<CategoryHeaderMasked>(this.categoryHeaderOrigin);
categoryHeaderRoleVariant.SetHeader((i == 0) ? StringNames.CrewmateRolesHeader : StringNames.ImpostorRolesHeader, 61);
// 注意 maskLayer = 61（不是 20）—— 每套 UI 的遮罩层不同
```

### 5.4 分类头显示什么

- `Title` → `TranslationController.GetString(CategoryName)`，即**分类名**
- `Background` / `Divider` → 分类底的色条与分隔线
- `CategoryHeaderEditRole` 额外有 3 个色块（`blankLabel` / `chanceLabel` / `countLabel`）作为**列标题底色**
  （对应角色配额行的「空列 / 概率列 / 数量列」）
- ⚠️ **分类头没有图标字段**（`CategoryHeaderRoleVariant.icon` 除外，那是单角色页头）

---

## 6. 大厅游戏选项的存储与读写

### 6.1 类型链条

```
GameOptionsManager                        (GameOptionsManager.cs, 单例, 非 MonoBehaviour)
  └── Instance                            (public static GameOptionsManager Instance { get; private set; })

IGameOptions                              (Amongus.GameOptions.IGameOptions — 接口)
  ├── NormalGameOptionsV12                ★ 19.0 当前 Normal 的实际实现（Version == 12）
  │     └── 历史: LegacyGameOptions, NormalGameOptionsV07/V08
  ├── HideNSeekGameOptionsV12             ★ 19.0 当前 HNS 的实现
  └── (V09/V10/V11 类都还在，仅作为迁移源)

RoleOptionsCollectionV12 : IRoleOptionsCollection     (角色 数量/概率)
NormalGameOptionsV12.roleOptions (private readonly)  →  .RoleOptions (public 属性, IRoleOptionsCollection)

LogicOptions (abstract, GameLogicComponent)
  ├── LogicOptionsNormal
  └── LogicOptionsHnS
```

> ⚠️ **任务里提到的 `NormalGameOptionsV09` 在 19.0 已不是当前版本**。
> `GameOptionsManager.GetGameOptions() => typeof(NormalGameOptionsV12)` 是硬编码的当前类型。
> 19.0 反编译源码里**没有** `GameOptionsManagerCreator` 之外的 V09 当前路径。

### 6.2 四个"桶"（三个用途 × 两种模式）

```csharp
public IGameOptions CurrentGameOptions  { get; set; }   // ★ 当前正在编辑/使用的
public IGameOptions GameHostOptions     { get; set; }   // ★ 房主权威版本（setter 会触发 SaveXxxHostOptions() 落盘）
public IGameOptions GameSearchOptions   { get; set; }   // 匹配用
public bool         HasOptions          { get; }        // currentGameOptions != null
public void Initialize();  public void SwitchGameMode(GameModes gameMode);
public Type GetGameOptions() => typeof(NormalGameOptionsV12);
```

`CurrentGameOptions` 的 getter 有兜底：
```csharp
get { if (this.currentGameOptions == null) { this.CurrentGameOptions = this.GameHostOptions; } return this.currentGameOptions; }
```
setter 会按 `value.GameMode` 做 `SwitchGameMode` 并 cast 到 `NormalGameOptionsV12` / `HideNSeekGameOptionsV12`
（**cast 失败只写 error 日志，不抛异常**，所以拿到 null 字段是可能的）。

**落盘**：`DataManager.Settings.Multiplayer.RawNormalHostOptions` (byte[]) → `GameOptionsFactory.ToBytes(...)`，
存于 `DataManager.Settings.Multiplayer`。旧版迁移读 `Path.Combine(PlatformPaths.persistentDataPath, "gameHostOptions")`。

### 6.3 读写任意选项的**统一访问器**（★ 核心）

```csharp
public interface IGameOptions
{
    byte Version { get; }  GameModes GameMode { get; }  SpecialGameModes SpecialMode { get; }
    RulesPresets RulesPreset { get; }  int MaxPlayers { get; }  GameKeywords Keywords { get; }
    byte MapId { get; }  int NumImpostors { get; }  int TotalTaskCount { get; }  bool IsDefaults { get; }
    IRoleOptionsCollection RoleOptions { get; }

    void SetByte(ByteOptionNames optionName, byte value);
    void SetFloat(FloatOptionNames optionName, float value);
    void SetBool(BoolOptionNames optionName, bool value);
    void SetInt(Int32OptionNames optionName, int value);
    void SetUInt(UInt32OptionNames optionName, uint value);

    [Obsolete] byte  GetByte(ByteOptionNames optionName);     // 失败返回 0
    [Obsolete] float GetFloat(FloatOptionNames optionName);   // 失败返回 0f
    [Obsolete] bool  GetBool(BoolOptionNames optionName);     // 失败返回 false
    [Obsolete] int   GetInt(Int32OptionNames optionName);     // 失败返回 0
    [Obsolete] int[] GetIntArray(Int32ArrayOptionNames optionName);
    [Obsolete] float[] GetFloatArray(FloatArrayOptionNames optionName);

    bool TryGetByte(ByteOptionNames o, out byte v);
    bool TryGetFloat(FloatOptionNames o, out float v);
    bool TryGetBool(BoolOptionNames o, out bool v);
    bool TryGetInt(Int32OptionNames o, out int v);
    bool TryGetIntArray(Int32ArrayOptionNames o, out int[] v);
    bool TryGetFloatArray(FloatArrayOptionNames o, out float[] v);
}
```

**运行时读写配方（推荐用 `TryGet`，不推荐 `[Obsolete]` 的 `GetX`）：**

```csharp
var opts = GameOptionsManager.Instance.CurrentGameOptions;

// 读
float cd;  bool ok1 = opts.TryGetFloat(FloatOptionNames.KillCooldown, out cd);
int   n;   bool ok2 = opts.TryGetInt(Int32OptionNames.NumImpostors, out n);
bool  b;   bool ok3 = opts.TryGetBool(BoolOptionNames.VisualTasks, out b);

// 写
opts.SetFloat(FloatOptionNames.KillCooldown, 25f);
opts.SetInt  (Int32OptionNames.MaxPlayers, 15);
opts.SetBool (BoolOptionNames.VisualTasks, false);

// 角色
var ro = opts.RoleOptions;                     // IRoleOptionsCollection
int cnt = ro.GetNumPerGame(RoleTypes.Engineer);
int pct = ro.GetChancePerGame(RoleTypes.Engineer);
ro.SetRoleRate(RoleTypes.Engineer, 1, 100);
```
**枚举名大全文件**：`ByteOptionNames.cs` / `FloatOptionNames.cs` / `Int32OptionNames.cs` / `BoolOptionNames.cs` /
`UInt32OptionNames.cs` / `Int32ArrayOptionNames.cs` / `FloatArrayOptionNames.cs`（都在 `AmongUs.GameOptions` 命名空间）。

**通过 `BaseGameSetting` 的通用桥**（`IGameOptionsExtensions.GetValue`，**行控件就靠这个**）：
```csharp
public static float GetValue(this IGameOptions gameOptions, BaseGameSetting data)
{
    float num = -1f; int num2 = -1; bool flag = false, flag2 = false;
    switch (data.Type)
    {
    case OptionTypes.Checkbox: gameOptions.TryGetBool((data as CheckboxGameSetting).OptionName, out flag); flag2 = true; break;
    case OptionTypes.String:   gameOptions.TryGetInt ((data as StringGameSetting).OptionName, out num2); break;
    case OptionTypes.Float:    gameOptions.TryGetFloat((data as FloatGameSetting).OptionName, out num); break;
    case OptionTypes.Int:      gameOptions.TryGetInt ((data as IntGameSetting).OptionName, out num2); break;
    case OptionTypes.Player:   gameOptions.TryGetInt ((data as PlayerSelectionGameSetting).OptionName, out num2); break;
    case OptionTypes.Map:      (data as MapSelectionGameSetting)?.TryGetInt(gameOptions, out num2); break;
    default: Debug.LogError("Could not parse type of " + data.Title.ToString()); break;
    }
    if (num == -1f && num2 != -1) num = (float)num2;
    else if (num == -1f && flag2) num = (float)((!flag) ? 0 : 1);
    return num;
}
```

### 6.4 ⚠️ 关键陷阱：**枚举里没有对应项 → 静默失败**

`NormalGameOptionsV12.SetFloat/SetInt/SetBool` 都是**白名单 switch**，未匹配就只写日志：

```csharp
public void SetInt(Int32OptionNames optionName, int value)
{
    switch (optionName) {
    case Int32OptionNames.NumImpostors:        this.NumImpostors = value; return;
    ...
    default: if (optionName == Int32OptionNames.RulePreset) { this.RulesPreset = (RulesPresets)value; return; } break;
    }
    this.logger.WriteError($"Options for Mode {GameMode}, Version {Version} could not set int named {optionName}");
}
```
同理 `TryGetFloat`/`TryGetInt`/`TryGetBool` 未匹配返回 **false**（不是抛异常）。
👉 **想加自定义选项，不能指望往 `FloatOptionNames` 里塞新枚举值**；必须存在客户端自己那边
（配置/JSON/RPC 自定义消息），或借用现有 `Tag`（int）等可写槽位。

### 6.5 是否同步到客户端 —— **是的，房主权威**

`LogicOptions.SyncOptions()`（`LogicOptions.cs` 行 74）：
```csharp
public void SyncOptions()
{
    if (!AmongUsClient.Instance.AmHost) return;                 // ★ 只有房主会同步
    base.SetDirty();
    if (PlayerControl.LocalPlayer != null)
        PlayerControl.LocalPlayer.RpcSyncSettings(
            this.gameOptionsFactory.ToBytes(this.currentGameOptions, AprilFoolsMode.IsAprilFoolsModeToggledOn));
}
```
接收端（`LogicOptions.Deserialize`）：
```csharp
public override void Deserialize(MessageReader reader)
{
    IGameOptions gameOptions = this.gameOptionsFactory.FromNetworkMessageWithSize(reader);
    this.SetGameOptions(gameOptions);
    GameOptionsManager.Instance.CurrentGameOptions = gameOptions;
    if (GameOptionsManager.Instance.CurrentGameOptions.TryClearAprilFoolsMode())
        AprilFoolsMode.IsAprilFoolsModeToggledOn = true;
    else AprilFoolsMode.IsAprilFoolsModeToggledOn = false;
}
```
序列化内容（`NormalGameOptionsV12.Serialize`）**是逐字段硬编码的** ——
`SpecialMode, RulesPreset, MaxPlayers, Keywords, MapId, PlayerSpeedMod, CrewLightMod, ImpostorLightMod,
KillCooldown, NumCommonTasks, NumLongTasks, NumShortTasks, NumEmergencyMeetings, NumImpostors, KillDistance,
DiscussionTime, VotingTime, IsDefaults, EmergencyCooldown, ConfirmImpostor, VisualTasks, AnonymousVotes,
TaskBarMode, Tag, RoleOptionsCollectionV12`。
👉 **新选项没有网络通道**，必须自己发 RPC / 或借 `Tag`。

**写入标准流程（照抄 `GameOptionsMenu.ValueChanged`）：**
```csharp
GameOptionsManager.Instance.CurrentGameOptions.SetInt(Int32OptionNames.RulePreset, 100);  // 100 = Custom
GameOptionsManager.Instance.CurrentGameOptions.SetBool(BoolOptionNames.IsDefaults, false);
GameOptionsManager.Instance.GameHostOptions = GameOptionsManager.Instance.CurrentGameOptions; // 落盘
GameManager.Instance.LogicOptions.SyncOptions();                                             // 同步
```

---

## 7. `GameOptionsMenu` —— **动态加行 / 分类头的官方 API**

文件：`GameOptionsMenu.cs`（336 行）。

### 7.1 方法签名全表

| 签名 | 可见性 | 作用 |
|---|---|---|
| `private void Awake()` | private | 设 `MaskBg`/`MaskArea` mask layer = 20 |
| **`private void CreateSettings()`** | **private** | ★ **建所有分类头 + 所有行**（见下） |
| `private void Initialize()` | private | 惰性：`MapPicker.Initialize(20)` + 加 `MapPicker` 到 `Children` + `CreateSettings()` + 挂 `OnValueChanged` + `SetAsPlayer` + 控制器导航 |
| `private void Update()` | private | `cachedData != CurrentGameOptions` → `RefreshChildren()` |
| `private void OnEnable()` | private | 调 `Initialize()` |
| `private void OnDisable()` | private | `CloseMenu()` |
| `public void OpenMenu()` | **public** | `ControllerManager.Instance.OpenOverlayMenu(base.name, BackButton, MapPicker.MapButtons[0].Button, ControllerSelectable, false)` |
| `public void CloseMenu()` | **public** | `ControllerManager.Instance.CloseOverlayMenu(base.name)` |
| **`private void RefreshChildren()`** | private | 对 `Children` 逐个 `Initialize()` |
| `private void ValueChanged(OptionBehaviour option)` | private | 写回 + `SyncOptions()` + Notifier（**开头有 `if (!AmHost) return;`**） |
| `private void InitializeControllerNavigation()` | private | 串上下导航 |
| **`public void ClickPresetButton(RulesPresets preset)`** | **public** | 应用预设 + `RefreshChildren()` + `this.RolesMenu.RefreshChildren()` + `SyncOptions()` |

> ⚠️ **`CreateSettings()` 是 `private`，没有任何 `public` 的"加行"API。**
> 模组里说的 `GameOptionsMenu.CreateSettings` 就是**这个 private 方法**，只能通过
> **Harmony patch** 或 **反射** 触发；它本身也**只读 `GameManager.Instance.GameSettingsList.AllCategories`**，
> 所以你"加分类"的正宗做法是**往那个 ScriptableObject 的 `AllCategories` 里插一个 `RulesCategory`**，
> 然后重新跑 `CreateSettings()`。但注意：它是 `if (Children == null || Children.Count == 0)` 保护的一次性初始化，
> **重跑不会清空旧行** → 需要先自己清 `settingsContainer` 的子物体。

### 7.2 `CreateSettings()` 完整源码（★ 克隆模板）

```csharp
private void CreateSettings()
{
    float num = 0.713f;                                                 // START_POS_Y
    foreach (RulesCategory rulesCategory in GameManager.Instance.GameSettingsList.AllCategories)
    {
        CategoryHeaderMasked categoryHeaderMasked = Object.Instantiate<CategoryHeaderMasked>(
            this.categoryHeaderOrigin, Vector3.zero, Quaternion.identity, this.settingsContainer);
        categoryHeaderMasked.SetHeader(rulesCategory.CategoryName, 20);
        categoryHeaderMasked.transform.localScale    = Vector3.one * 0.63f;      // HEADER_SCALE
        categoryHeaderMasked.transform.localPosition = new Vector3(-0.903f, num, -2f);  // HEADER_X
        num -= 0.63f;                                                          // HEADER_HEIGHT

        foreach (BaseGameSetting baseGameSetting in rulesCategory.AllGameSettings)
        {
            switch (baseGameSetting.Type)
            {
            case OptionTypes.Checkbox:
                OptionBehaviour ob = Object.Instantiate<ToggleOption>(this.checkboxOrigin, Vector3.zero, Quaternion.identity, this.settingsContainer);
                ob.transform.localPosition = new Vector3(0.952f, num, -2f);      // START_POS_X
                ob.SetClickMask(this.ButtonClickMask);
                ob.SetUpFromData(baseGameSetting, 20);                           // ★ 顺序：先定位，再 SetClickMask，再 SetUpFromData
                this.Children.Add(ob);
                break;
            case OptionTypes.String:   /* this.stringOptionOrigin   → StringOption */     break;
            case OptionTypes.Float:
            case OptionTypes.Int:      /* this.numberOptionOrigin   → NumberOption */     break;
            case OptionTypes.Player:   /* this.playerOptionOrigin   → PlayerOption */     break;
            }
            num -= 0.45f;                                                        // SPACING_Y
        }
    }
    this.ControllerSelectable.AddRange(this.scrollBar.GetComponentsInChildren<UiElement>());
    this.scrollBar.SetYBoundsMax(-num - 1.65f);                              // MAP_PICKER_HEIGHT 补偿
}
```

`Initialize()` 里的回调挂载：
```csharp
private void Initialize()
{
    if (this.Children == null || this.Children.Count == 0)
    {
        this.MapPicker.Initialize(20);
        this.MapPicker.SetUpFromData(GameManager.Instance.GameSettingsList.MapNameSetting, 20);
        this.Children = new List<OptionBehaviour>();
        this.Children.Add(this.MapPicker);
        this.CreateSettings();
        this.cachedData = GameOptionsManager.Instance.CurrentGameOptions;
        for (int i = 0; i < this.Children.Count; i++)
        {
            var ob = this.Children[i];
            ob.OnValueChanged = new Action<OptionBehaviour>(this.ValueChanged);
            if (AmongUsClient.Instance && !AmongUsClient.Instance.AmHost) ob.SetAsPlayer();
        }
        this.InitializeControllerNavigation();
    }
}
```

### 7.3 `GameOptionsMenu` 字段全表

| 字段 | 类型 | 备注 |
|---|---|---|
| `cachedData` | `IGameOptions` | private，变更检测 |
| **`Children`** | **`List<OptionBehaviour>`** | **private**（无 `[SerializeField]`）★ 所有行的列表 |
| `MapPicker` | `GameOptionsMapPicker` | `[SerializeField]` |
| `categoryHeaderOrigin` | `CategoryHeaderMasked` | **克隆源** |
| `checkboxOrigin` | `ToggleOption` | **克隆源** |
| `numberOptionOrigin` | `NumberOption` | **克隆源** |
| `playerOptionOrigin` | `PlayerOption` | **克隆源** |
| `stringOptionOrigin` | `StringOption` | **克隆源** |
| `settingsContainer` | `Transform` | ★ **所有分类头/行都挂到这里** |
| `scrollBar` | `Scroller` | |
| `HideForOnline` | `Transform[]` | |
| `RolesMenu` | `RolesSettingsMenu` | `ClickPresetButton` 里会回调它 |
| `ButtonClickMask` | `Collider2D` | |
| `MaskBg`, `MaskArea` | `SpriteRenderer` | layer 20 |
| `BackButton`, `DefaultButtonSelected` | `UiElement` | public |
| `ControllerSelectable` | `List<UiElement>` | public |

常量：
```csharp
START_POS_Y = 0.713f;   START_POS_X = 0.952f;   HEADER_HEIGHT = 0.63f;
MAP_PICKER_HEIGHT = 1.65f;  SPACING_Y = 0.45f;  HEADER_X = -0.903f;
MASK_LAYER = 20;        HEADER_SCALE = 0.63f;
```

### 7.4 `ClickPresetButton`（**唯一的 public 数据操作方法**）

```csharp
public void ClickPresetButton(RulesPresets preset)
{
    GameOptionsManager.Instance.CurrentGameOptions.SetRecommendations(
        GameData.Instance.PlayerCount, AmongUsClient.Instance.NetworkMode, preset);
    this.RefreshChildren();
    this.RolesMenu.RefreshChildren();
    GameOptionsManager.Instance.CurrentGameOptions.SetBool(BoolOptionNames.IsDefaults, false);
    GameOptionsManager.Instance.GameHostOptions = GameOptionsManager.Instance.CurrentGameOptions;
    GameManager.Instance.LogicOptions.SyncOptions();
    DestroyableSingleton<HudManager>.Instance.Notifier.AddSettingsChangeMessage(
        StringNames.ModeLabel,
        DestroyableSingleton<TranslationController>.Instance.GetString(
            GameOptionsManager.Instance.CurrentGameOptions.GetRulesPresetTitle(), Array.Empty<object>()),
        false, RoleTypes.Crewmate);
}
```

### 7.5 ⚠️ `GameSettingsTab` 不是类

`grep class GameSettingsTab` **零命中**。
`GameSettingsTab` **只是 `GameSettingMenu` 里的一个字段名**（类型 `GameOptionsMenu`）。
没有 `GameOptionsTab.cs` 这个文件，也没有 `GameSettingsTab` 这个类型。
**规则页 = `GameOptionsMenu` 组件**。

---

## 8. 新增「分类页签 + 选项行」的落地建议（基于以上事实）

### 8.1 三种可选切入点

| 方案 | 做法 | 代价 |
|---|---|---|
| **A. 塞进现有规则页分类（最省事）** | 往 `GameManager.Instance.GameSettingsList.AllCategories` 插 `RulesCategory{CategoryName, AllGameSettings}`，再触发 `CreateSettings()` | 需要新建 `ScriptableObject`(FloatGameSetting 等) 或反射构造；`CreateSettings` 是 private；重跑不清旧行 |
| **B. 自建一个"伪页签"容器（**本工程 `GameSettingMenuPatch` 的路子**）** | 新建 `GameObject` 挂到 `menu.transform`，克隆 `NumberOption`/`ToggleOption` 行进去；按钮克隆 `PassiveButton`；自己实现 `ChangeTab` | 需自己处理 `ChangeTab` 的 `SetActive`、`sortingLayer/Order`、layer 5（见 AGENTS.md §4.3/§4.5） |
| **C. patch `ChangeTab`** | Harmony postfix `GameSettingMenu.ChangeTab(int, bool)`，在 `tabNum==3` 时 `SetActive` 自己的容器 | 必须精确复刻 `previewOnly` 两段逻辑；`tabNum` 参数名在 interop 里可能变 `__0`/`tabNum` |

### 8.2 克隆行时的**必做清单**（源码依据）

1. **替换 OnClick，不是追加** —— `GamePresetsTab.ClickPresetButton` 用 `OnClick.RemoveAllListeners()` 就是范例（AGENTS.md §4.5）。
2. **`SetUpFromData(setting, 20)` 必调** —— 它负责 `data` 赋值 + 全子树 mask layer/stencil（基类 §4.2）。
3. **`SetClickMask(ButtonClickMask)` 必调** —— 否则点击穿透（`OptionBehaviour.SetClickMask`）。
4. **`OnValueChanged = new Action<OptionBehaviour>(ValueChanged)` 必挂** —— 否则改值不写回。
5. **localPosition.z = -2f**，X/Y 用 §2.14 / §7.3 的常量。
6. **分类头 `localScale = Vector3.one * 0.63f`**（规则页）/ 不缩放（角色页）。
7. **`ScrollBar.CalculateAndSetYBounds/SetYBoundsMax` 要跟着行数更新**，否则滚不到底。
8. **`SetAsPlayer()`**：非房主要调（`AmongUsClient.Instance.AmHost` 判断）。
9. **控制器导航**：新行要进 `ControllerSelectable`，并设 `ControllerNav.selectOnUp/selectOnDown`（范例 `CreateAdvancedSettings` + `InitializeControllerNavigation`）。
10. **自定义 `BaseGameSetting` 的 `Title` 是 `StringNames`（翻译键）** —— 想显示自定义文本，
    必须绕过 `Initialize()` 或 patch `TranslationController.GetString`（本项目 `SettingsTabPatch` 的同类问题，
    见 AGENTS.md §5.2 `ToggleButtonBehaviour.ResetText()`）。

### 8.3 ⚠️ 已知的"能不能加自定义选项"硬约束

- `IGameOptions.SetXxx` **只接受枚举白名单**，未匹配静默失败（§6.4）。**无法新增自定义 option 键**。
- `NormalGameOptionsV12.Serialize/Deserialize` **字段硬编码**（§6.5）。**新选项不会被同步**。
- 因此模组的通行做法是：**用自定义 RPC / 自定义 byte 通道同步**，
  或**借 `Int32OptionNames.Tag`（int，可收发）**当容器。
- `NumberOption.Value` 是 **public float**，可直接写；但写完后要手动
  `ValueText.text = data.GetValueString(Value)`（因为只靠 `FixedUpdate` 检测变化，且 `UpdateValue()` 会把值写进 `IGameOptions`）。

---

## 9. 索引：本报告涉及的文件

| 文件 | 关键内容 |
|---|---|
| `GameSettingMenu.cs` | 3 个主页签、`ChangeTab`、`Start`/`Close` |
| `RolesSettingsMenu.cs` | 角色页 2 子页 + 数据驱动页签 + 动态建行 ★ |
| `GamePresetsTab.cs` | 2 预设按钮 + 确认弹窗 |
| `GameOptionsMenu.cs` | `CreateSettings`（分类头 + 行）★ |
| `OptionBehaviour.cs` | 行控件基类 |
| `NumberOption.cs` / `ToggleOption.cs` / `StringOption.cs` / `PlayerOption.cs` / `RoleOptionSetting.cs` | 五种行控件 |
| `BaseGameSetting.cs` + `FloatGameSetting.cs` / `IntGameSetting.cs` / `StringGameSetting.cs` / `CheckboxGameSetting.cs` / `PlayerSelectionGameSetting.cs` / `MapSelectionGameSetting.cs` | 行数据 + **后缀/单位渲染** ★ |
| `NumberSuffixes.cs` | `None / Multiplier / Seconds` |
| `CategoryHeaderMasked.cs` / `CategoryHeaderEditRole.cs` / `CategoryHeaderRoleVariant.cs` | 分类头三种 |
| `RoleSettingsTabButton.cs` | 角色横向页签按钮 |
| `GameOptionButton.cs` | `+/-` 按钮 |
| `GameSettingsCategoryList.cs` / `RulesCategory.cs` | 分类 + 选项的 ScriptableObject 数据模型 ★ |
| `GameOptionsManager.cs` | 选项单例 |
| `Amongus\GameOptions\IGameOptions.cs` / `NormalGameOptionsV12.cs` / `IRoleOptionsCollection.cs` | 选项接口与实现 ★ |
| `IGameOptionsExtensions.cs` | `GetValue(BaseGameSetting)` 通用桥 |
| `LogicOptions.cs` | `SyncOptions()` 网络同步 |
| `GameStartManager.cs` | `ClickEdit()` 打开菜单 |

### 不存在的符号（避免再找）
- ❌ `KeyValueOption` / `KeyValueGameSetting` —— 19.0 无
- ❌ `GameSettingsTab` 类型 —— 只是字段名（类型是 `GameOptionsMenu`）
- ❌ `GameOptionsTab.cs` —— 文件不存在
- ❌ `StringNames.NeutralRoles` / `RoleTeamTypes.Neutral` —— 19.0 无（只有 Crewmate / Impostor）
- ❌ `CategoryHeaderRoleRestriction` —— 任务里提到但 19.0 不存在（只有 `Masked` / `EditRole` / `RoleVariant`）
- ❌ `GameOptionsMenu` 的 public `CreateSettings` —— 是 **private**
- ❌ `NormalGameOptionsV09` 作为当前类型 —— 19.0 是 **V12**
