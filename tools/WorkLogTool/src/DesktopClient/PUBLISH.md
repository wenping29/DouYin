# 工作日志工具 - 发布指南

## 快速发布

运行发布脚本：

```powershell
.\publish.ps1
```

输出位置：`publish/win-x64/Release/net8.0-windows/publish/`

---

## 发布选项

### 选项 1: PowerShell 脚本发布（推荐）

```powershell
# 发布发布版本
.\publish.ps1 -Configuration Release

# 清理后重新发布
.\publish.ps1 -Clean

# 指定目标框架
.\publish.ps1 -TargetFramework net8.0-windows
```

### 选项 2: 命令行发布

```powershell
# 基础发布（自包含、单文件）
dotnet publish -c Release

# 指定输出目录
dotnet publish -c Release -o ./publish

# 仅依赖框架（需要目标机器安装 .NET 8）
dotnet publish -c Release -p:SelfContained=false
```

### 选项 3: 使用 Visual Studio

1. 在 Solution Explorer 右键项目
2. 选择 **Publish**
3. 选择目标文件夹或发布配置文件
4. 点击 **Publish**

---

## 生成安装包

### Inno Setup（推荐）

1. 安装 [Inno Setup](https://jrsoftware.org/isdl.php)
2. 编辑 `installer/WorkLogTool.iss` 中的路径
3. 运行：
   ```powershell
   iscc installer/WorkLogTool.iss
   ```
4. 输出：`installer/WorkLogTool-Setup-1.0.0.exe`

### MSIX 打包（需要签名）

```powershell
# 安装 Windows SDK 后使用
makeappx pack /d publish /p WorkLogTool.msix
```

---

## 目录结构

```
src/DesktopClient/
├── publish.ps1              # 发布脚本
├── installer/               # 安装程序配置
│   └── WorkLogTool.iss      # Inno Setup 配置
└── publish/                 # 发布输出（生成后）
    └── WorkLogTool.exe      # 单文件可执行程序
```

---

## 注意事项

1. **首次发布需联网** - 下载 .NET 运行时
2. **防病毒软件** - 首次运行可能被拦截，可提交签名
3. **更新版本** - 修改 csproj 中的 Version 属性
