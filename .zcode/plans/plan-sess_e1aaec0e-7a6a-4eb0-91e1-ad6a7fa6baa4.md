# UI 全面统一优化方案（IndustrialCommDemo WPF 应用）

## 一、App.xaml 建立设计令牌与共享样式（核心）

**新增语义画笔**（替代散落各处的 157 处硬编码颜色和代码里的蜡笔色）：
- `SuccessBrush #0F7B0F` / `SuccessFillBrush #E6F4EA` / `SuccessStrokeBrush #C3E6CB`
- `WarningBrush #8A6100` / `WarningFillBrush #FFF4CE` / `WarningStrokeBrush #F2D675`
- `DangerBrush #C42B1C` / `DangerFillBrush #FDECEA` / `DangerStrokeBrush #F5C6C0`
- `InfoBrush #174A73` / `InfoFillBrush #E8F2FF` / `InfoStrokeBrush #B8D8F8`
- `MutedBrush #64748B`、`PanelBrush #F8FAFC`、`PanelStrokeBrush #D8E2EE`

**新增共享样式**（从 NetworkServicesTab 本地样式上移 + 新增）：
- `FieldLabelStyle`（表单标签）/ `FieldControlStyle`（表单控件）——全 Tab 统一表单节奏
- `LogBoxStyle`（深色控制台日志框，统一 #1E1E1E 和 #1E293B 两种为一种：#1E293B/#E2E8F0）
- `PasswordBox` 样式上移到全局
- 按钮语义样式：`PrimaryButtonStyle`（主题蓝，主操作：连接/启动/发送/保存）、`SuccessButtonStyle`（启动全部设备/启动虚拟 PLC）、`DangerButtonStyle`（删除等破坏性操作）、`NeutralButtonStyle`（断开/停止等次要操作）

## 二、MainWindow 修复三处明显 bug

1. **乱码标题**：`Title="???"` 和页头 `Text="???"` 恢复为「工业设备运行中心」（与现有日志文案 "工业设备运行中心已就绪" 一致）
2. **头部布局 hack**：删除 `91*/1072*` 奇怪列宽和 `Margin="1072,0,0,0"`，改为标准两列（标题 `*` / 状态胶囊 `Auto` 右对齐）
3. **状态胶囊颜色跟随状态**：`SetHeaderStatus(string, Brush)` 签名不变，内部按传入的 ThemeBrush 引用查映射表，同步更新胶囊的 Background/BorderBrush/Foreground（现在是绿色底配任意状态色）
4. 底部日志 TextBox 改用共享 `LogBoxStyle`

## 三、新增 Helpers/ThemeBrush.cs

命名空间用 `IndustrialCommDemo`（根命名空间，所有 View 无需加 using），Frozen 的 SolidColorBrush 静态实例：Success/Warning/Danger/Info/Muted。

## 四、代码后台机械替换（约 15 个文件、80 处）

sed 批量替换（含 SharedHelpers.cs 的全限定写法）：
- `Brushes.LightGreen`、`Brushes.ForestGreen` → `ThemeBrush.Success`
- `Brushes.Khaki`、`Brushes.DarkGoldenrod` → `ThemeBrush.Warning`
- `Brushes.IndianRed`、`Brushes.OrangeRed` → `ThemeBrush.Danger`
- `Brushes.SlateGray`、`Brushes.DimGray` → `ThemeBrush.Muted`
- `Brushes.SteelBlue` → `ThemeBrush.Info`

## 五、13 个视图 XAML 统一改造

- **NetworkServicesTab**：删除本地重复样式改用全局；5 处 IndianRed → DangerBrush
- **SiemensS7Tab**：彩色按钮（#1D4ED8/#0891B2/#059669/#7C3AED/#334155）→ 语义按钮样式；#F8FAFC 面板 → PanelBrush；表单标签 → FieldLabelStyle
- **DeviceRuntimeTab**：头部 #F1F5F9 → PanelBrush；启动/停止按钮 → Success/Danger 样式；设备状态固定 170 行 → Auto + MinHeight（点位表占满剩余空间）
- **其余 Tab**（Modbus/S7/Mc/OPC UA/Socket/MES/网卡/存储/历史数据/JSON 配置）：硬编码 slate 色系 → TextSecondaryBrush/PanelBrush/StrokeBrush；状态色 → 语义画笔；日志框 → LogBoxStyle；表单标签 → FieldLabelStyle

## 六、验证

1. `dotnet build IndustrialCommDemo/IndustrialCommDemo.csproj`（失败则回退 VS msbuild）编译通过
2. grep 确认无残留 `Brushes.LightGreen/Khaki/IndianRed` 等，XAML 中硬编码颜色仅剩语义色定义处
3. 逐文件复查 XAML 资源引用拼写（StaticResource key 与 App.xaml 定义一致）

## 风险控制

- 不改任何事件处理逻辑、控件名称、绑定，只动外观属性与样式引用
- `DemoAppContext.SetHeaderStatus` 委托签名不变，不触碰 SDK 层
- 文件保持 UTF-8 编码，避免再次出现 ??? 乱码