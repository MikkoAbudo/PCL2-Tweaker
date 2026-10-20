# PCL2 第三方微调加载器

中文界面的第三方独立工具，可选择关闭指定 PCL2 构建的自动更新与下载限制。本项目与 PCL 官方无隶属关系。

## 使用

运行环境：Windows、.NET Framework 4.8。

1. 解压二进制包，保留 `PCL2.TweakLoader.exe` 及同目录的 `.exe.config`。
2. 打开加载器，点击「浏览」选择自己的原始 `Plain Craft Launcher 2.exe`。
3. 勾选微调选项，点击「启动 PCL2」。「保存」只保存设置。

| 选项 | 勾选后的行为 |
| --- | --- |
| 关闭自动更新 | 跳过自动更新公告与更新动作，保留手动检查更新。 |
| 关闭 RestrictMcDownload 下载限制 | 恢复 Minecraft 下载、整合包安装及拖放处理。 |

选项在下次启动时生效。两个选项均关闭时启动原版。更改前请先关闭 PCL。

## 兼容性

仅支持版本号为 **2.99.0.0**，且 SHA-256 为以下值的原始构建：

```text
6b6e1260cb5a1d6a303855f1c60cab3d0bc1aebba7e40a964180188b271e7386
```

启用微调时拒绝未知或修改过的文件。原始启动器保持不变，加载器在其旁边生成 `.tweaked.exe` 和校验用的 `.tweaks.json`。更换选项时从原始文件重新生成。设置保存在加载器旁的 `PCL2.TweakLoader.settings.json`。

发布包及源码包不包含 PCL 本体，请自行准备匹配文件。

## 编译与测试

需要 .NET SDK 和 .NET Framework 4.8 目标包。

```powershell
.\build.ps1
```

将匹配的原始启动器放入仓库根目录后，可运行集成检查：

```powershell
.\build.ps1 -Test
```

构建使用一个 MSBuild 工作进程。依赖 Mono.Cecil 0.11.6 已包含于 `lib/` 并嵌入程序。当前检查覆盖四种开关组合、资源保留、修改方法 JIT 编译、整合包路径和文件保护。安装 PEVerify 时还会对比原版的 IL 校验结果。

尚未验证完整游戏下载或整合包安装。原版存在一项泛型委托校验错误，补丁不应新增校验错误。

## 命令行

```powershell
.\PCL2.TweakLoader.exe --launch --target 'D:\Minecraft\Plain Craft Launcher 2.exe'
.\PCL2.TweakLoader.exe --prepare --target 'D:\Minecraft\Plain Craft Launcher 2.exe' --disable-updates --unrestrict-downloads
```

`--prepare` 只准备文件，未指定的微调视为关闭；`--launch` 默认读取已保存的选项。`--result <文件>` 写入程序路径，`--error <文件>` 写入异常代替弹窗，`--arguments <字符串>` 将参数传给 PCL。成功退出码为 `0`，失败为 `1`。

## 开源说明

自身代码采用 [MIT 许可证](LICENSE)。Mono.Cecil 的许可证见 `lib/Mono.Cecil.LICENSE.txt`。PCL 与本地生成的 PCL 副本仍受上游条款约束，见 [第三方声明](THIRD_PARTY_NOTICES.md)。参与开发见 [CONTRIBUTING.md](CONTRIBUTING.md)。
