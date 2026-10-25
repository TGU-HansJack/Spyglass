# 打包与部署

- 用户要求每次打包都部署到本机 `C:\Users\29596\AppData\Roaming\MVL\Modpack\测试服务器\Mods`，除非用户明确指定其他目标。
- 使用 `./package.ps1 -GamePath E:/MVL/Release/1.22.3`，脚本会生成发布 ZIP 并自动部署。打包后核对部署文件的 SHA256 与发布包一致，并确认新增资源已经包含在 ZIP 中。
- 更新本机配置时保留用户已有设置；新增配置缺失时补齐默认值。
