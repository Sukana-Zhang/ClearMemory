$ErrorActionPreference='Stop'
Set-Location $PSScriptRoot
New-Item -ItemType Directory -Force build,dist,assets | Out-Null
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $taskCompiler /nologo /target:exe /r:System.Drawing.dll /out:build\MakeIcon.exe tools\MakeIcon.cs
if($LASTEXITCODE){throw 'Icon compiler failed'}
& .\build\MakeIcon.exe assets\ClearMemory.ico
& $taskCompiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 /win32manifest:app.manifest /win32icon:assets\ClearMemory.ico /resource:assets\ClearMemory.ico,ClearMemory.ico /out:dist\ClearMemory.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Xml.Linq.dll /r:System.Web.Extensions.dll /r:Microsoft.CSharp.dll src\*.cs
if($LASTEXITCODE){throw 'Application compiler failed'}
Write-Output 'Built ClearMemory.exe'
