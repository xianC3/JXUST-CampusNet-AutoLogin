# 江西理工大学校园网自动登录

面向江西理工大学校园网的 Windows 与 Android 自动登录工具，包含两个独立项目：

- `windows/`：原生 Windows WinForms 程序，支持系统托盘、登录后自动运行和 Wi-Fi 变化检测。
- `android/`：原生 Android 应用，支持开机检查、前台服务、定时任务和多校园 Wi-Fi 名单。

> 兼容范围：目前主要在**江西理工大学南昌校区**网络环境中开发和测试。赣州校区的网络名称、认证门户、运营商规则和设备管理方式可能不同，尚未做具体测试。使用前请先核对自己所在校区的网络参数。

## 下载安装

普通用户无需编译源码，请从 [GitHub Releases](https://github.com/xianC3/JXUST-CampusNet-AutoLogin/releases/latest) 下载最新的通用版安装包：

- Android 2.5.1：[CampusNet-AutoLogin-Android-2.5.1.apk](https://github.com/xianC3/JXUST-CampusNet-AutoLogin/releases/latest/download/CampusNet-AutoLogin-Android-2.5.1.apk)
- Windows 1.4.4：[CampusNet-AutoLogin-Windows-1.4.4-Setup.exe](https://github.com/xianC3/JXUST-CampusNet-AutoLogin/releases/latest/download/CampusNet-AutoLogin-Windows-1.4.4-Setup.exe)
- 文件校验值：[SHA256SUMS.txt](https://github.com/xianC3/JXUST-CampusNet-AutoLogin/releases/latest/download/SHA256SUMS.txt)

发布包均为通用版，不预置个人账号、密码、MAC 地址或校园 Wi‑Fi 名称。Android 安装后需要填写自己的校园网账号，并把实际使用的校园 Wi‑Fi 加入名单。Windows 安装包需要管理员权限安装，不需要额外安装 .NET 运行时。

## 主要功能

- 支持“校园网、联通、移动、电信”四种接入方式。
- 只处理用户加入名单的校园 Wi-Fi，家庭网络、手机热点和普通 Wi-Fi 会跳过。
- 同时检查校园门户认证状态和外部网络连通性。
- 支持添加、查看和删除多个校园 Wi-Fi 名称。
- 设备数达到上限时，通用策略只注销最旧的一条会话；找不到本机 MAC（读取不到或在线列表中不存在）时同样只注销最旧会话，并尽量保留另一台设备在线。
- Windows 账号密码通过 DPAPI 加密后保存在当前用户目录。
- Android 账号密码只保存在应用私有存储中。

## 仓库不包含的内容

- 不包含任何个人账号、密码、MAC 地址或个人校园 Wi-Fi 名称。
- 源码提交中不包含已签名的 APK、EXE 或安装包；正式构建通过 GitHub Releases 单独发布。
- 不包含 Android 签名密钥、Visual Studio 缓存或本机运行数据。

## 快速开始

Windows 编译和安装说明见 [windows/README.md](windows/README.md)。

Android 编译和安装说明见 [android/README.md](android/README.md)。

## 使用限制

本项目不是学校官方软件，不能绕过账号欠费、停用、密码错误或学校门户自身的限制。登录失败时，请先确认账号状态和运营商选择。

设备上限清理只会操作学校自助服务返回的在线会话。找不到本机 MAC（读取不到或列表中不存在）时，程序会注销登录时间最早的一条会话；账号密码不正确、验证码持续失败、在线列表为空或接口格式异常时会停止注销。

## 校区适配

南昌校区是当前主要测试环境。赣州校区需要自行确认以下内容：

- 校园门户地址和端口。
- 自助服务地址。
- 校园 Wi-Fi 名称。
- 运营商账号后缀。
- 门户返回的错误文本和设备管理接口。

如认证接口不同，需要修改 Windows 的 `CampusNetManager/AppSettings.cs`、`CampusLoginClient.cs`，以及 Android 的 `AppConfig.java`、`CampusLoginClient.java`。

## 安全说明

请只在自己的账号和已授权设备上使用。不要提交账号、密码、验证码、登录 Cookie、MAC 地址、运行日志或个人网络配置。
