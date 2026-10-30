param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
Set-Location $PSScriptRoot
if(-not $OutputDirectory){$OutputDirectory=Join-Path $PSScriptRoot 'dist'}
$taskReleaseRoot=[IO.Path]::GetFullPath($OutputDirectory)
$taskExe=Join-Path $PSScriptRoot 'dist\ClearMemory.exe'
if(-not(Test-Path -LiteralPath $taskExe)){throw 'Run build.ps1 first.'}
$taskVersion=[Version][Diagnostics.FileVersionInfo]::GetVersionInfo($taskExe).FileVersion
$taskVersionText='{0}.{1}.{2}' -f $taskVersion.Major,$taskVersion.Minor,$taskVersion.Build
$taskStage=Join-Path $PSScriptRoot ('build\package\'+[Guid]::NewGuid().ToString('N'))
$taskPackageRoot=Join-Path $taskStage 'ClearMemory'
New-Item -ItemType Directory -Force $taskPackageRoot,$taskReleaseRoot | Out-Null
try {
    Copy-Item -LiteralPath $taskExe -Destination (Join-Path $taskPackageRoot 'ClearMemory.exe')
    $taskReadme=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'README.md'),[Text.Encoding]::UTF8)
    [IO.File]::WriteAllText((Join-Path $taskPackageRoot 'README.txt'),$taskReadme,(New-Object Text.UTF8Encoding($true)))
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination (Join-Path $taskPackageRoot 'LICENSE')
    $taskHash=(Get-FileHash -LiteralPath $taskExe -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText((Join-Path $taskPackageRoot 'ClearMemory.exe.sha256'),$taskHash+'  ClearMemory.exe'+[Environment]::NewLine,[Text.Encoding]::ASCII)
    $taskArchive=Join-Path $taskReleaseRoot ('ClearMemory-'+$taskVersionText+'-win-x64.zip')
    Compress-Archive -LiteralPath $taskPackageRoot -DestinationPath $taskArchive -Force
    $taskZipHash=(Get-FileHash -LiteralPath $taskArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText(($taskArchive+'.sha256'),$taskZipHash+'  '+[IO.Path]::GetFileName($taskArchive)+[Environment]::NewLine,[Text.Encoding]::ASCII)
    Write-Output $taskArchive
} finally {
    $taskResolved=[IO.Path]::GetFullPath($taskStage)
    $taskExpectedRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'build\package'))+[IO.Path]::DirectorySeparatorChar
    if(-not $taskResolved.StartsWith($taskExpectedRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Package cleanup path escaped workspace.'}
    if(Test-Path -LiteralPath $taskResolved){Remove-Item -LiteralPath $taskResolved -Recurse -Force}
}
