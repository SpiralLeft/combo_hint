param(
    [string]$Sts2Path = 'D:/SteamLibrary/steamapps/common/Slay the Spire 2',
    [string]$BaseLibPath = 'D:/SteamLibrary/steamapps/workshop/content/2868840/3737335127/BaseLib',
    [string]$WorkingRoot,
    [ValidateSet('eng','zhs')][string]$Language = 'eng',
    [switch]$Graphics,
    [switch]$NegativeControl,
    [switch]$CardCatalog,
    [int]$TimeoutSeconds = 180
)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (-not $WorkingRoot) { $WorkingRoot = Join-Path (Split-Path $repoRoot) '.research/scenario-runtime' }
$WorkingRoot = [IO.Path]::GetFullPath($WorkingRoot)
$Sts2Path = [IO.Path]::GetFullPath($Sts2Path)
if ($WorkingRoot.StartsWith($Sts2Path, [StringComparison]::OrdinalIgnoreCase)) { throw 'Test runtime must be outside the game installation.' }
if (-not (Test-Path (Join-Path $Sts2Path 'SlayTheSpire2.exe'))) { throw 'Game executable missing.' }
if (-not (Test-Path (Join-Path $BaseLibPath 'BaseLib.pck'))) { throw 'Complete BaseLib installation required.' }
foreach ($running in Get-Process SlayTheSpire2 -ErrorAction SilentlyContinue) {
    if ($running.Path -eq (Join-Path $WorkingRoot 'SlayTheSpire2.exe')) { throw 'This test runtime is already in use. Wait for its current run to finish.' }
}
$null = New-Item -ItemType Directory -Path $WorkingRoot -Force

# Preserve the installed game; only the disposable workspace copy is launched.
foreach ($file in Get-ChildItem $Sts2Path -File | Where-Object { $_.Extension -in @('.exe','.dll','.pck') -or $_.Name -eq 'release_info.json' }) {
    $destination = Join-Path $WorkingRoot $file.Name
    if (-not (Test-Path $destination) -or (Get-Item $destination).Length -ne $file.Length) {
        Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
    }
}
$dataDir = Join-Path $WorkingRoot 'data_sts2_windows_x86_64'
if (-not (Test-Path $dataDir)) { Copy-Item -LiteralPath (Join-Path $Sts2Path 'data_sts2_windows_x86_64') -Destination $dataDir -Recurse }
@'
[application]
config/use_custom_user_dir=true
config/custom_user_dir_name="ComboHintAutomation"
'@ | Set-Content (Join-Path $WorkingRoot 'override.cfg') -Encoding utf8NoBOM

& dotnet build (Join-Path $PSScriptRoot 'ComboHint.ScenarioTests.csproj') -c Release "-p:Sts2Path=$Sts2Path"
if ($LASTEXITCODE -ne 0) { throw 'Test mod build failed.' }
$mods = Join-Path $WorkingRoot 'mods'
foreach ($name in @('ComboHint','BaseLib','ComboHintScenarioTests')) { $null = New-Item -ItemType Directory -Path (Join-Path $mods $name) -Force }
Copy-Item -Path (Join-Path $repoRoot 'dist/ComboHint/*') -Destination (Join-Path $mods 'ComboHint') -Force
Get-ChildItem $BaseLibPath -File | Where-Object Extension -in @('.dll','.json','.pck') | Copy-Item -Destination (Join-Path $mods 'BaseLib') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'bin/Release/net9.0/ComboHintScenarioTests.dll'),(Join-Path $PSScriptRoot 'ComboHintScenarioTests.json') -Destination (Join-Path $mods 'ComboHintScenarioTests') -Force

$mode = if ($Graphics) { 'graphics' } else { 'headless' }
if ($NegativeControl) { $mode += '-negative-control' }
$runName = "combat-$Language-$mode"
if ($CardCatalog) { $runName = "catalog-$Language-$mode" }
$runDir = Join-Path $WorkingRoot $runName
$null = New-Item -ItemType Directory -Path $runDir -Force
$roaming = Join-Path $runDir 'roaming'
$userData = Join-Path $roaming 'ComboHintAutomation'
$settingsDir = Join-Path $userData 'default/1'
$null = New-Item -ItemType Directory -Path $settingsDir -Force
$settings = @{
    schema_version = 8; language = $Language; seen_ea_disclaimer = $true; skip_intro_logo = $true
    fullscreen = $false; resize_windows = $true; window_size = @{ X=1280; Y=720 }
    window_position = @{ X=50; Y=50 }; target_display = 0; volume_master = 0
    mod_settings = @{ mods_enabled = $true; mod_list = @(
        @{ id='BaseLibCompatibilityHelper'; is_enabled=$true; source='mods_directory' }
        @{ id='BaseLib'; is_enabled=$true; source='mods_directory' }
        @{ id='ComboHint'; is_enabled=$true; source='mods_directory' }
        @{ id='ComboHintScenarioTests'; is_enabled=$true; source='mods_directory' }
    ) }
}
$settings | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $settingsDir 'settings.save') -Encoding utf8NoBOM

function Start-TestProcess([string[]]$Arguments, [int]$Seconds) {
    $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $WorkingRoot 'SlayTheSpire2.exe'))
    $start.WorkingDirectory = $WorkingRoot
    $start.UseShellExecute = $false
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.Environment['APPDATA'] = $roaming
    $start.Environment['LOCALAPPDATA'] = (Join-Path $runDir 'local')
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    try {
        if (-not $process.WaitForExit($Seconds * 1000)) {
            $process.Kill($true)
            $process.WaitForExit()
            throw "Test process timed out after $Seconds seconds."
        }
        return $process.ExitCode
    } finally {
        $stdout.GetAwaiter().GetResult() | Set-Content (Join-Path $runDir 'process-stdout.log') -Encoding utf8NoBOM
        $stderr.GetAwaiter().GetResult() | Set-Content (Join-Path $runDir 'process-stderr.log') -Encoding utf8NoBOM
        $process.Dispose()
    }
}

# MainLoop probe runs no game autoloads. Fail before gameplay if isolation did not apply.
$probeLog = Join-Path $runDir 'probe.log'
$probeResult = Start-TestProcess @('--headless','--script',(Join-Path $PSScriptRoot 'probe.gd'),'--force-steam=off','--log-file',$probeLog) 20
$probe = Get-Content $probeLog -Raw
if ($probeResult -ne 0 -or -not $probe.Contains('COMBOHINT_PROBE_USERDIR=' + $userData.Replace('\','/'))) { throw 'User data isolation probe failed.' }

$script = Get-Content (Join-Path $PSScriptRoot 'combat-eng.scenario') -Raw
if ($Language -eq 'zhs') {
    $script = $script.Replace('No combo effects available','当前没有连携效果').Replace('Vulnerable','易伤').Replace('Weak','虚弱').Replace('Lethal','斩杀').Replace('contains Feed','contains 狂宴')
    foreach ($pair in @{
        'Dominate'='主宰'; 'Haze'='迷雾'; 'The Empress'='女皇'; 'Shockwave'='震荡波';
        'Doom'='灾厄'; 'Dislike You'='不喜欢你'; 'Strength loss'='失去力量'; 'Enfeeblement'='弱化'; 'Poison'='中毒'; 'Malaise'='萎靡'; 'Lift'='托举'; 'Huddle Up'='抱团'; 'Echoing Slash'='回响斩击'
        'Flanking'='夹击'; 'Expose'='暴露'; 'contains Strangle'='contains 紧勒'
    }.GetEnumerator()) { $script = $script.Replace($pair.Key, $pair.Value) }
    $script = $script.Replace('combohint-effects-eng','combohint-effects-zhs').Replace('combohint-second-combat-eng','combohint-second-combat-zhs')
    $script = $script.Replace('combohint-groups-eng','combohint-groups-zhs')
    $script = $script.Replace('combohint-hover-eng','combohint-hover-zhs')
    $script = $script.Replace('-eng','-zhs')
}
if ($NegativeControl) { $script = "fight SLIMES_WEAK`nwait combat-started`ncombohint_test bubbles 999`n" }
if ($CardCatalog) { $script = "combohint_test catalog`ncombohint_test complete`n" }
$scenarioPath = Join-Path $runDir 'combat.scenario'
$script | Set-Content $scenarioPath -Encoding utf8NoBOM
$scenarioLog = Join-Path $runDir 'scenario.log'
$engineLog = Join-Path $runDir 'engine.log'
$launchArgs = @('--force-steam=off','--combohint-tests',"--combohint-test-root=$WorkingRoot","--scenario=$scenarioPath",'--seed=COMBOHINT051',"--log-file=$scenarioLog",'--log-file',$engineLog)
if ($Graphics) { $launchArgs += @('--rendering-method','gl_compatibility','--audio-driver','Dummy','--max-fps','60') }
else { $launchArgs += '--headless' }
$runStartedAt = Get-Date
'' | Set-Content $scenarioLog -Encoding utf8NoBOM
$exitCode = Start-TestProcess $launchArgs $TimeoutSeconds
$scenarioText = if (Test-Path $scenarioLog) { Get-Content $scenarioLog -Raw } else { '' }
$engineText = Get-Content $engineLog -Raw
$passed = $exitCode -eq 0 -and $scenarioText.Contains('Scenario completed successfully:') -and $engineText.Contains('ALL ASSERTIONS PASSED:')
if ($engineText -match '\[ScenarioTests\] FAIL|\[ComboHint\] (Combat hint callback failed|Could not|Failed to create)') { $passed = $false }
if ($NegativeControl) {
    $passed = ($exitCode -ne 0 -and $scenarioText.Contains('Scenario failed:') -and
        $engineText.Contains('[ScenarioTests] FAIL bubbles 999'))
}
if ($engineText -notmatch 'Steam initialization skipped' -or $engineText -match 'Steamworks initialization succeeded') { $passed = $false }
$assertions = [regex]::Match($engineText,'ALL ASSERTIONS PASSED: (\d+)').Groups[1].Value
$screenshots = @(Get-ChildItem (Join-Path $userData 'screenshots') -Filter '*.png' -ErrorAction SilentlyContinue | Where-Object LastWriteTime -GE $runStartedAt)
if ($Graphics -and -not $NegativeControl -and -not $CardCatalog -and $screenshots.Count -lt 2) { $passed = $false }
$summary = [ordered]@{ passed=$passed; expected_failure=[bool]$NegativeControl; language=$Language; mode=$mode; exit_code=$exitCode; assertions=$assertions; screenshots=@($screenshots | ForEach-Object FullName); scenario_log=$scenarioLog; engine_log=$engineLog; userdata=$userData }
$summary | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $runDir 'result.json') -Encoding utf8NoBOM
$summary | ConvertTo-Json -Depth 5
if (-not $passed) { throw "Scenario failed. Inspect $runDir" }
