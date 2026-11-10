# 工作日志工具

这是一个使用WPF开发的桌面小工具，用于记录工作事项并自动生成工作日志文件。

## 功能特点

- 添加工作记录（日期和工作内容）
- 查看工作记录列表
- 生成工作日志文件（文本格式）
- 加载已有的工作日志文件
- 日历选择日期查看记录
- 状态颜色区分（已完成/进行中/待办/超期）
- 工作提醒功能
- 看板视图
- 数据可视化（图表）
- SQLite 数据库持久化存储

## 如何使用

1. **添加工作记录**：
   - 点击"添加工作计划"按钮
   - 输入日期、工作内容、耗时等
   - 点击确认添加

2. **查看工作记录**：
   - 使用左侧日历选择日期
   - 使用下拉框筛选：全部记录/新增计划/未完成计划

3. **生成工作日志文件**：
   - 点击"生成工作日志文件"按钮
   - 选择保存位置和文件名
   - 系统会生成一个按日期分组的文本文件

4. **加载工作日志文件**：
   - 点击"加载工作日志文件"按钮
   - 选择要加载的工作日志文件
   - 系统会解析文件并显示在记录列表中

## 生成的日志文件格式

```
工作日志
生成时间: 2026-04-15 10:00:00
====================================
日期: 2026-04-15
------------------------------------
- 完成项目需求分析
- 编写代码实现
- 测试功能

日期: 2026-04-14
------------------------------------
- 参加团队会议
- 讨论项目计划
```

## 技术栈

- C#
- WPF
- .NET 8.0
- SQLite 数据库
- LiveCharts 图表

## 如何构建和运行

1. 确保安装了 .NET SDK 8.0 或更高版本
2. 打开命令行工具，进入项目目录
3. 运行以下命令构建项目：
   ```powershell
   dotnet build
   ```
4. 运行以下命令启动应用：
   ```powershell
   dotnet run
   ```

## 如何打包发布

### 方法一：发布为单文件 EXE（推荐）

1. 打开命令行，进入项目目录：
   ```powershell
   cd D:\Project\WorkLogTool
   ```

2. 发布为自包含单文件：
   ```powershell
   dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./publish
   ```

3. 生成的 exe 文件位于 `./publish/WorkLogTool.exe`

### 方法二：使用 PowerShell 脚本发布

1. 创建发布脚本 `publish.ps1`：
   ```powershell
   $ErrorActionPreference = "Stop"

   Write-Host "开始发布..." -ForegroundColor Green

   # 清理旧发布
   if (Test-Path "./publish") { Remove-Item "./publish" -Recurse -Force }

   # 发布
   dotnet publish -c Release -r win-x64 --self-contained true `
       -p:PublishSingleFile=true `
       -p:IncludeNativeLibrariesForSelfExtract=true `
       -p:EnableCompressionInSingleFile=true `
       -o ./publish

   Write-Host "发布完成: ./publish/WorkLogTool.exe" -ForegroundColor Green
   Write-Host "文件大小: $((Get-Item ./publish/WorkLogTool.exe).Length / 1MB) MB" -ForegroundColor Cyan

   # 创建 zip 压缩包
   Compress-Archive -Path ./publish/* -DestinationPath ./WorkLogTool-v1.0.zip -Force
   Write-Host "压缩包已生成: ./WorkLogTool-v1.0.zip" -ForegroundColor Green
   ```

2. 运行发布脚本：
   ```powershell
   .\publish.ps1
   ```

### 方法三：使用 Inno Setup 创建安装程序

1. 下载安装 Inno Setup：https://jrsoftware.org/isinfo.php

2. 创建安装脚本 `setup.iss`：
   ```iss
   [Setup]
   AppName=工作日志工具
   AppVersion=1.0
   AppPublisher=YourName
   DefaultDirName={autopf}\WorkLogTool
   DefaultGroupName=工作日志工具
   OutputDir=.\installer
   Compression=lzma2
   SolidCompression=yes

   [Files]
   Source="publish\*"; DestDir="{app}"; Flags: recursesubdirs

   [Icons]
   Name: "{group}\工作日志工具"; Filename: "{app}\WorkLogTool.exe"
   Name: "{commondesktop}\工作日志工具"; Filename: "{app}\WorkLogTool.exe"
   ```

3. 编译安装程序：
   ```powershell
   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" setup.iss
   ```

### 打包参数说明

| 参数 | 说明 |
|------|------|
| `-r win-x64` | 目标平台为 64 位，改为 `win-x86` 支持 32 位 |
| `--self-contained true` | 自包含运行时，exe 体积约 80-120MB |
| `--self-contained false` | 框架依赖，exe 约 200KB，需目标机安装 .NET 8 |
| `-p:PublishSingleFile=true` | 合并为单个 exe 文件 |
| `-p:IncludeNativeLibrariesForSelfExtract=true` | 包含原生库 |

## 数据存储

- 数据库文件：`worklog.db`（SQLite）
- 配置文件：`worklog/` 目录
- 首次运行会在 exe 同目录创建 `worklog/` 文件夹

## 注意事项

- 加载日志文件时，系统会解析符合特定格式的文本文件
- 生成的日志文件使用 UTF-8 编码
- 状态颜色：绿色=已完成，黄色=进行中，蓝色=待办，红色=超期
