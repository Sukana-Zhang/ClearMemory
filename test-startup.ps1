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
$script:taskChecks=0
function Assert-Check([bool]$ok,[string]$label){if(-not $ok){throw $label};$script:taskChecks++;Write-Output ('PASS '+$label)}
try {
    New-Item -ItemType Directory -Force (Join-Path $taskRoot 'first'),(Join-Path $taskRoot 'moved') | Out-Null
    $taskFirst=Join-Path $taskRoot 'first\ClearMemory.exe'
    $taskMoved=Join-Path $taskRoot 'moved\ClearMemory.exe'
    Copy-Item -LiteralPath $taskExe -Destination $taskFirst
    Copy-Item -LiteralPath $taskExe -Destination $taskMoved
    $taskXml=[ClearMemory.Startup]::TaskXml($taskFirst,$taskUser,$false)
    $taskService=New-Object -ComObject Schedule.Service
    $taskService.Connect();$taskFolder=$taskService.GetFolder('\')
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
} finally {
    try {
        if($taskCreated){
            $taskCurrent=$taskFolder.GetTask($taskName)
            try {
                $taskCommand=[IO.Path]::GetFullPath($taskCurrent.Definition.Actions.Item(1).Path)
                if($taskCurrent.Definition.RegistrationInfo.Author -ne 'ClearMemory' -or -not $taskCommand.StartsWith([IO.Path]::GetFullPath($taskRoot)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Test task ownership verification failed; task was not removed.'}
                $taskFolder.DeleteTask($taskName,0)
            } finally {[Runtime.InteropServices.Marshal]::FinalReleaseComObject($taskCurrent) | Out-Null}
        }
    } finally {
        foreach($taskCom in @($taskRegistered,$taskFolder,$taskService)){if($null -ne $taskCom -and [Runtime.InteropServices.Marshal]::IsComObject($taskCom)){[Runtime.InteropServices.Marshal]::FinalReleaseComObject($taskCom) | Out-Null}}
        $taskResolved=[IO.Path]::GetFullPath($taskRoot)
        $taskBuild=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'build'))+[IO.Path]::DirectorySeparatorChar
        if(-not $taskResolved.StartsWith($taskBuild,[StringComparison]::OrdinalIgnoreCase)){throw 'Cleanup path escaped test workspace.'}
        if(Test-Path -LiteralPath $taskResolved){Remove-Item -LiteralPath $taskResolved -Recurse -Force}
    }
}
@{checks=$script:taskChecks;taskRemoved=$true;applicationStarted=$false;actualCleanup=$false} | ConvertTo-Json | Out-File -LiteralPath build\startup-integration.json -Encoding utf8
Write-Output ($script:taskChecks.ToString()+' isolated startup integration checks passed; test task removed.')
