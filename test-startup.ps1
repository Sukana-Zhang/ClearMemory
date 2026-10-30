# Opt-in integration test. Creates only a uniquely named, disabled test task.
$ErrorActionPreference='Stop'
Set-Location $PSScriptRoot
$taskIdentity=[Security.Principal.WindowsIdentity]::GetCurrent()
if(-not (New-Object Security.Principal.WindowsPrincipal($taskIdentity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Run this optional startup integration test from an elevated Windows PowerShell.'}
$taskExe=Join-Path $PSScriptRoot 'dist\ClearMemory.exe'
if(-not(Test-Path -LiteralPath $taskExe)){throw 'Run build.ps1 first.'}
[Reflection.Assembly]::LoadFrom($taskExe) | Out-Null
$taskRoot=Join-Path $PSScriptRoot ('build\启动 测试-'+[Guid]::NewGuid().ToString('N'))
$taskName='ClearMemory-CompatibilityTest-'+[Guid]::NewGuid().ToString('N')
$taskUser=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$taskService=$null;$taskFolder=$null;$taskRegistered=$null;$taskCreated=$false
$taskPreviousData=[ClearMemory.Storage]::DirectoryPath
$script:taskChecks=0
function Assert-Check([bool]$ok,[string]$label){if(-not $ok){throw $label};$script:taskChecks++;Write-Output ('PASS '+$label)}
try {
    New-Item -ItemType Directory -Force (Join-Path $taskRoot 'first'),(Join-Path $taskRoot 'moved') | Out-Null
    [ClearMemory.Storage]::DirectoryPath=Join-Path $taskRoot 'settings'
    $taskFirst=Join-Path $taskRoot 'first\ClearMemory.exe'
    $taskMoved=Join-Path $taskRoot 'moved\ClearMemory.exe'
    Copy-Item -LiteralPath $taskExe -Destination $taskFirst
    Copy-Item -LiteralPath $taskExe -Destination $taskMoved
    $taskXml=[ClearMemory.Startup]::TaskXml($taskFirst,$taskUser,$false)
    $taskService=New-Object -ComObject Schedule.Service
    $taskService.Connect();$taskFolder=$taskService.GetFolder('\')
    Assert-Check (-not [ClearMemory.Startup]::ReadStatus($taskName).Exists) 'first launch reads an absent task without throwing'
    [ClearMemory.Startup]::SetEnabled($false,$taskFirst,$taskName)
    Assert-Check (-not [ClearMemory.Startup]::ReadStatus($taskName).Exists) 'disabling an absent task is idempotent'
    $taskRegistered=$taskFolder.RegisterTask($taskName,$taskXml,2,$taskUser,$null,3,$null)
    $taskCreated=$true
    Assert-Check (-not $taskRegistered.Enabled) 'test startup task remains disabled'
    Assert-Check ($taskRegistered.Definition.Actions.Item(1).Path -eq $taskFirst) 'Unicode and space-containing EXE path is registered correctly'
    Assert-Check ($taskRegistered.Definition.Actions.Item(1).Arguments -eq '--background') 'background startup argument is preserved'
    Assert-Check ($taskRegistered.Definition.Principal.RunLevel -eq 1) 'startup task uses highest available privileges'
    Assert-Check ($taskRegistered.Definition.Triggers.Count -eq 1) 'logon trigger is accepted by Windows Task Scheduler'
    $taskRefreshed=[ClearMemory.Startup]::RefreshXml($taskRegistered.Xml,$taskMoved)
    [Runtime.InteropServices.Marshal]::FinalReleaseComObject($taskRegistered) | Out-Null
    $taskRegistered=$taskFolder.RegisterTask($taskName,$taskRefreshed,6,$taskUser,$null,3,$null)
    Assert-Check (-not $taskRegistered.Enabled) 'path refresh does not enable a disabled task'
    Assert-Check ($taskRegistered.Definition.Actions.Item(1).Path -eq $taskMoved) 'startup path refresh handles a moved EXE'
    Assert-Check ($taskRegistered.Definition.Actions.Item(1).WorkingDirectory -eq (Split-Path $taskMoved)) 'working directory follows the moved EXE'
    # Exercise the actual checkbox backend, not only the COM registration API.
    # A no-op fixture EXE guarantees that the test can never clean system memory.
    $taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $taskFixture=Join-Path $taskRoot 'Fixture.cs'
    [IO.File]::WriteAllText($taskFixture,'class Fixture { static void Main() {} }',[Text.Encoding]::UTF8)
    $taskFixtureExe=Join-Path $taskRoot 'Fixture.exe'
    & $taskCompiler /nologo /target:winexe (('/out:')+$taskFixtureExe) $taskFixture
    if($LASTEXITCODE){throw 'Fixture build failed'}
    [ClearMemory.Startup]::SetEnabled($false,$taskMoved,$taskName)
    Assert-Check (-not [ClearMemory.Startup]::ReadStatus($taskName).Exists) 'checkbox backend removes a registered task'
    [ClearMemory.Startup]::SetEnabled($true,$taskFixtureExe,$taskName)
    $taskCreated=$true
    $taskActual=[ClearMemory.Startup]::ReadStatus($taskName)
    Assert-Check ($taskActual.Exists -and $taskActual.Enabled) 'first-time checkbox enable creates and verifies the task'
    Assert-Check ($taskActual.Path -eq $taskFixtureExe -and $taskActual.Arguments -eq '--background') 'checkbox enable registers the requested executable and arguments'
    Assert-Check ((Get-Content -LiteralPath (Join-Path ([ClearMemory.Storage]::DirectoryPath) 'startup.txt') -Raw).Trim() -eq 'enabled') 'startup preference is saved only after verification'
    [ClearMemory.Startup]::SetEnabled($false,$taskFixtureExe,$taskName)
    $taskCreated=$false
    Assert-Check (-not [ClearMemory.Startup]::ReadStatus($taskName).Exists) 'checkbox disable deletes and verifies removal'
    Assert-Check ((Get-Content -LiteralPath (Join-Path ([ClearMemory.Storage]::DirectoryPath) 'startup.txt') -Raw).Trim() -eq 'disabled') 'checkbox disable persists its verified state'
    Assert-Check ((Get-Content -LiteralPath ([ClearMemory.Storage]::LogPath) -Raw).Contains('startup.success')) 'startup operations write diagnostic records'
} finally {
    try {
        if($null -ne $taskFolder){
            $taskCurrent=$null
            try{$taskCurrent=$taskFolder.GetTask($taskName)}catch{$taskError=$_.Exception;while($null -ne $taskError.InnerException){$taskError=$taskError.InnerException};if(-not [ClearMemory.Startup]::IsMissingTaskError($taskError)){throw}}
            if($null -ne $taskCurrent){
            try {
                $taskCommand=[IO.Path]::GetFullPath($taskCurrent.Definition.Actions.Item(1).Path)
                if($taskCurrent.Definition.RegistrationInfo.Author -ne 'ClearMemory' -or -not $taskCommand.StartsWith([IO.Path]::GetFullPath($taskRoot)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Test task ownership verification failed; task was not removed.'}
                $taskFolder.DeleteTask($taskName,0)
            } finally {[Runtime.InteropServices.Marshal]::FinalReleaseComObject($taskCurrent) | Out-Null}
            }
        }
    } finally {
        [ClearMemory.Storage]::DirectoryPath=$taskPreviousData
        foreach($taskCom in @($taskRegistered,$taskFolder,$taskService)){if($null -ne $taskCom -and [Runtime.InteropServices.Marshal]::IsComObject($taskCom)){[Runtime.InteropServices.Marshal]::FinalReleaseComObject($taskCom) | Out-Null}}
        $taskResolved=[IO.Path]::GetFullPath($taskRoot)
        $taskBuild=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'build'))+[IO.Path]::DirectorySeparatorChar
        if(-not $taskResolved.StartsWith($taskBuild,[StringComparison]::OrdinalIgnoreCase)){throw 'Cleanup path escaped test workspace.'}
        if(Test-Path -LiteralPath $taskResolved){Remove-Item -LiteralPath $taskResolved -Recurse -Force}
    }
}
@{checks=$script:taskChecks;taskRemoved=$true;cleanupApplicationStarted=$false;actualCleanup=$false;noOpFixtureUsed=$true} | ConvertTo-Json | Out-File -LiteralPath build\startup-integration.json -Encoding utf8
Write-Output ($script:taskChecks.ToString()+' isolated startup integration checks passed; test task removed.')
