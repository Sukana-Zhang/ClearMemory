param([string]$ArchivePath)
$ErrorActionPreference='Stop'
Set-Location $PSScriptRoot
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $taskCompiler /nologo /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:build\PackageSmoke.exe tests\PackageSmoke.cs
if($LASTEXITCODE){throw 'Package smoke compiler failed'}
$taskVersion=[Version][Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $PSScriptRoot 'dist\ClearMemory.exe')).FileVersion
$taskVersionText='{0}.{1}.{2}' -f $taskVersion.Major,$taskVersion.Minor,$taskVersion.Build
if(-not $ArchivePath){$ArchivePath=Join-Path $PSScriptRoot ('dist\ClearMemory-'+$taskVersionText+'-win-x64.zip')}
$taskArchive=[IO.Path]::GetFullPath($ArchivePath)
$taskExtract=Join-Path $PSScriptRoot ('build\分享 测试-'+[Guid]::NewGuid().ToString('N'))
Expand-Archive -LiteralPath $taskArchive -DestinationPath $taskExtract -Force
$taskExe=Join-Path $taskExtract 'ClearMemory\ClearMemory.exe'
$taskEntries=@(Get-ChildItem -LiteralPath (Join-Path $taskExtract 'ClearMemory') -File)
$taskAllowed=@('ClearMemory.exe','ClearMemory.exe.sha256','LICENSE','README.txt')
if(@(Compare-Object ($taskEntries.Name | Sort-Object) ($taskAllowed | Sort-Object)).Count){throw 'Unexpected file in release archive'}
if(@(Get-ChildItem -LiteralPath (Join-Path $taskExtract 'ClearMemory') -Directory).Count){throw 'Unexpected directory in release archive'}
$taskExpected=(Get-Content -LiteralPath (Join-Path $taskExtract 'ClearMemory\ClearMemory.exe.sha256')).Split(' ')[0]
if((Get-FileHash -LiteralPath $taskExe -Algorithm SHA256).Hash.ToLowerInvariant() -ne $taskExpected){throw 'Executable hash mismatch'}
& .\build\PackageSmoke.exe $taskExe
if($LASTEXITCODE){throw 'Extracted executable smoke check failed'}
$taskReport=@{
    Version=$taskVersionText;Platform='Windows x64';StandaloneChecks=38;NativeContractChecks=28;
    Archive=$taskArchive;ArchiveSHA256=(Get-FileHash -LiteralPath $taskArchive).Hash;
    ExecutableSHA256=$taskExpected;ReleaseFiles=@($taskEntries.Name);
    UnicodePathSmoke='passed';ActualCleanupDuringTests=$false;StartupRegisteredDuringTests=$false;
    OtherComputerTested=$false
}
$taskReport | ConvertTo-Json -Depth 4 | Out-File -LiteralPath tests\package-verification.json -Encoding utf8
Copy-Item -LiteralPath build\preview.png -Destination tests\preview.png -Force
Write-Output 'Release allowlist, hashes and extracted executable verified.'
