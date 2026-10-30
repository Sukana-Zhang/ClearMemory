$ErrorActionPreference='Stop'
Set-Location $PSScriptRoot
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskRefs=@('/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Xml.Linq.dll','/r:System.Web.Extensions.dll','/r:Microsoft.CSharp.dll')
& $taskCompiler /nologo /codepage:65001 /main:Tests /resource:assets\ClearMemory.ico,ClearMemory.ico /out:build\Tests.exe @taskRefs src\*.cs tests\Tests.cs
if($LASTEXITCODE){throw 'Test compilation failed'}
& .\build\Tests.exe
if($LASTEXITCODE){throw 'Standalone tests failed'}
& $taskCompiler /nologo /codepage:65001 /main:SystemMemoryTests /out:build\NativeTests.exe @taskRefs src\*.cs tests\SystemMemoryTests.cs
if($LASTEXITCODE){throw 'Native test compilation failed'}
& .\build\NativeTests.exe
if($LASTEXITCODE){throw 'Native API contract tests failed'}
& $taskCompiler /nologo /codepage:65001 /main:CompatibilityTests /resource:assets\ClearMemory.ico,ClearMemory.ico /out:build\CompatibilityTests.exe @taskRefs src\*.cs tests\CompatibilityTests.cs
if($LASTEXITCODE){throw 'Compatibility test compilation failed'}
& .\build\CompatibilityTests.exe
if($LASTEXITCODE){throw 'Compatibility tests failed'}
