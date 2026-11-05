# 一键打包：先编译主程序，再把安装程序编译到 dist
# 用法：在项目根目录执行  powershell -ExecutionPolicy Bypass -File build.ps1

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

Write-Host "[1/3] 编译主程序 -> build\app"
dotnet publish "PowerWatch\PowerWatch.csproj" -c Release --no-self-contained -o "build\app" --nologo -v q

if (-not (Test-Path "build\app\PowerWatch.exe")) {
    throw "主程序编译失败，安装包无法生成。"
}

Write-Host "[2/3] 打包安装程序 -> dist"
if (Test-Path "dist") { Remove-Item "dist" -Recurse -Force }
dotnet publish "Installer\Installer.csproj" -c Release --no-self-contained -o "dist" --nologo -v q

Write-Host "[3/3] 完成"
Get-ChildItem "dist" | Select-Object Name, @{n='大小';e={"{0:N0} KB" -f ($_.Length/1KB)}} | Format-Table -AutoSize
