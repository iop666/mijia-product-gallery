param(
    # dotnet publish 的输出目录；默认为本仓库的 Release 发布输出。
    [string]$PublishDir = '',
    # 产物输出目录；默认为仓库同级 mijia-product-gallery-issue\<版本号>。
    [string]$OutRoot = '',
    # 发布版本号：决定默认输出目录、压缩包名与使用说明中的版本。
    [string]$Version = '0.2.4'
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $PublishDir) {
    $PublishDir = Join-Path $repoRoot 'src/MijiaProductGallery.App/bin/Release/net10.0-windows10.0.19041.0/win-x64/publish'
}
if (-not $OutRoot) {
    $OutRoot = Join-Path (Split-Path -Parent $repoRoot) "mijia-product-gallery-issue/$Version"
}
$src = $PublishDir
$stage = Join-Path $outRoot 'staging\App'
$zipPath = Join-Path $outRoot "MijiaProductGallery-$Version-Portable.zip"

if (-not (Test-Path (Join-Path $src 'MijiaProductGallery.App.exe'))) { throw "publish source missing: $src" }

# 干净重建 staging（只清理 staging 自身，不影响输出目录中的其他产物）
if (Test-Path $stage) {
    Remove-Item $stage -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $stage | Out-Null
$copyErrors = @()
Copy-Item -Path (Join-Path $src '*') -Destination $stage -Recurse -Force -ErrorVariable copyErrors -ErrorAction Continue
if ($copyErrors.Count -gt 0) {
    foreach ($e in $copyErrors) { Write-Output ("COPY-FAIL: " + $e.TargetObject + " :: " + $e.Exception.Message) }
}
if (-not (Test-Path (Join-Path $stage 'MijiaProductGallery.App.exe'))) {
    throw ("main exe missing after copy; copyErrors=" + $copyErrors.Count)
}

# 去除调试符号（PDB 内含构建机路径）。框架多语言卫星目录与 PRI 必须保留：
# SelfContained 模式下缺失会导致 WinUI 资源初始化失败（进程启动即闪退）。
Get-ChildItem $stage -Recurse -Filter *.pdb | Remove-Item -Force
$buildRoot = Split-Path -Parent $src
$buildPri = Join-Path $buildRoot 'MijiaProductGallery.App.pri'
if ((Test-Path $buildPri) -and -not (Test-Path (Join-Path $stage 'MijiaProductGallery.App.pri'))) {
    Copy-Item $buildPri (Join-Path $stage 'MijiaProductGallery.App.pri') -Force
}
# XAML 编译产物（.xbf 按项目目录布局输出，运行时从磁盘加载），发布必须携带。
foreach ($sub in 'Controls', 'Views') {
    $xbfSrc = Join-Path $buildRoot $sub
    if (Test-Path $xbfSrc) {
        Copy-Item $xbfSrc (Join-Path $stage $sub) -Recurse -Force
    }
}
# 根级 xbf（App / MainWindow）
Get-ChildItem $buildRoot -Filter '*.xbf' | Copy-Item -Destination $stage -Force

# 顶层使用说明：解压后第一眼可见（主程序保持发布原名，确保运行时文件解析一致）
Set-Content -Path (Join-Path $stage '使用说明.txt') -Value @(
    "米家产品示例图库 v$Version（便携版）"
    ''
    '双击「MijiaProductGallery.App.exe」运行。'
    '全部数据（数据库 / 图片 / 缩略图 / 备份 / 日志）保存在本目录的 Data 文件夹内，'
    '整个文件夹可直接复制移动。'
    ''
    '首次启动（图库为空时）：会先弹出一个控制台加载窗口，从米家百科获取产品数据与'
    '10,000+ 张示例样图（约 5-15 分钟，取决于网速）；窗口内显示获取进度、剩余时间'
    '以及图片下载目录和数据目录。加载完成前请勿关闭该窗口，完成后将自动进入图库界面。'
    '初始化不支持断点续传：中途关闭或初始化失败后，下次启动会从头重新获取（不会进入主界面）。'
    ''
    '首次运行如提示 Windows 已保护你的电脑，请点击"更多信息"→"仍要运行"。'
    '详细说明见应用内「设置 → 使用说明」。'
) -Encoding UTF8

# 打包：复制为中文名顶层目录（解压后第一眼可见）
$portableDir = Join-Path $outRoot 'MijiaProductGallery'
if (Test-Path $portableDir) { Remove-Item $portableDir -Recurse -Force }
Copy-Item $stage $portableDir -Recurse -Force
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path $portableDir -DestinationPath $zipPath -CompressionLevel Optimal
Remove-Item $portableDir -Recurse -Force

# 语言卫星目录为 SelfContained 运行时必需资源（裁剪会导致 WinUI 资源初始化失败、启动闪退），保留并仅报告数量。
$langLeft = (Get-ChildItem $stage -Directory | Where-Object { $_.Name -cmatch '^[a-z]{2,3}(-[A-Za-z]+)*$' }).Count
$files = (Get-ChildItem $stage -Recurse -File | Measure-Object).Count
$z = Get-Item $zipPath
Write-Output ("STAGED files=" + $files + " langDirs=" + $langLeft)
Write-Output ("PORTABLE_ZIP_MB=" + [math]::Round($z.Length / 1MB, 1) + " path=" + $z.Name)
