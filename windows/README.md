# Windows 通用版

原生 Windows WinForms 自动登录程序，目标框架为 `net8.0-windows`。

## 环境

- Windows 10 或更高版本
- Visual Studio 2022/2026，安装“.NET 桌面开发”工作负载
- .NET 8 SDK

## 编译

用 Visual Studio 打开 `CampusNetManager.slnx`，或在本目录执行：

```powershell
dotnet build .\CampusNetManager.slnx -c Release
```

生成自包含单文件：

```powershell
dotnet publish .\CampusNetManager\CampusNetManager.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o .\publish
```

如已安装 Inno Setup 6，可在 `publish` 生成后编译 `CampusNetManager-Setup.iss` 制作安装包。

## 首次配置

1. 打开程序并点击“账号设置”。
2. 账号栏只填写自己的学号或校园账号。
3. 选择校园网、联通、移动或电信，程序会自动拼接对应后缀。
4. 输入密码并保存。
5. 连接校园 Wi-Fi，点击“添加当前 Wi-Fi”。

家庭网络、手机热点和其他非名单 Wi-Fi 会被跳过。

设备数达到上限时，程序会通过自助服务释放一个名额。找不到本机 MAC
（读取不到或不在在线列表中）时，注销登录时间最早的一个会话；通用版
不保存用户固定 MAC。

自动切换校园网默认关闭；开启后也只有当前网络无法上网且附近存在
校园网时才会切换。网络事件连续触发时会进行 3 秒去抖，门户失败日志
会区分 DNS、连接拒绝、超时、TLS、HTTP 状态和响应解析错误。

## 自动运行

开启“登录 Windows 后自动在后台运行”后，程序会创建当前用户级别的计划任务。关闭主窗口后程序仍在托盘运行。

## 隐私

密码使用 Windows DPAPI 加密，仅保存在当前 Windows 用户的数据目录。仓库不包含账号、密码、受信 MAC 或任何个人网络配置。

## 适用范围

当前主要在江西理工大学南昌校区测试。赣州校区尚未做具体测试，使用前请核对门户地址、自助服务地址、校园 Wi-Fi 名称和运营商后缀。
