# 参与开发

欢迎提交 Issue 和 Pull Request，说明修改原因、行为变化与验证结果。

- 使用 Windows、.NET SDK 和 .NET Framework 4.8 目标包，运行 `./build.ps1` 编译。
- 补丁变更需自行提供匹配的原始启动器，并运行 `./build.ps1 -Test`。
- 新增构建支持时必须核对完整 SHA-256 和 IL 结构；不得仅按版本号放行。
- 补丁行为变化时更新 `PatchEngine.PatchRevision`。
- 不提交启动器、修改副本、上游源码副本、个人设置、日志、令牌或测试产物。
- 提交前检查 `git diff --cached`，保持构建和测试串行、轻量。

贡献的自身代码按仓库 MIT 许可证提供。
