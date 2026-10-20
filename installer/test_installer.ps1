# Logic test of the installer on STAND-IN game folders (a new folder per scenario under -Base; nothing is deleted).
# The game folder holds only LostOdysseyRecomp.exe (a copy) and what each scenario puts there.
#  A  clean game, Arabic not installed -> install, reinstall, uninstall -> folder exactly as it began (settings.ini too)
#  B  a recomp build without right-to-left support -> refused, nothing written; --force installs; uninstall -> start
#  C  no settings.ini yet -> installed, not selected; uninstall -> back to the start
#  D  the manual zip extracted by hand -> detected as installed; uninstall removes it, mods\ stays
#  E  a player's own file in mods\ar -> kept through install and uninstall
#  F  another folder with a language.ini id=ar -> reported
#  G  another language pack selected before -> back to it after uninstall
#  H  a release older than the minimum (manifest.json version) -> refused; the minimum itself -> installed
#  I  mod-list.ini listing the HD pack -> ar-images=on put first; uninstall -> mod-list.ini exactly as before
param(
    [Parameter(Mandatory)][string]$Base,                 # an empty folder for the stand-ins
    [Parameter(Mandatory)][string]$NewExe,               # LostOdysseyRecomp.exe with right-to-left support
    [Parameter(Mandatory)][string]$OldExe,               # LostOdysseyRecomp.exe without it
    [string]$MinVersion = '1.0.5'                        # the installer's MinRecomp
)
$ErrorActionPreference = 'Stop'
$exe = 'H:\Games\LostOdyssey-Arabic\installer\pub\Lost Odyssey Arabic Installer.exe'
$manual = 'H:\Games\LostOdyssey-Arabic\RELEASE\Lost Odyssey - Arabic v1.0.1 (Manual).zip'
$out = "$Base\out.txt"
$fails = 0
$settings = "ui_language=0`r`ngame_language=1`r`ngame_language_pack=`r`ndebug_language=0`r`nwidth=1920`r`n"

function New-Game($name, $exeFile, [switch]$NoSettings) {
    $g = "$Base\$name"
    New-Item -ItemType Directory -Force "$g\mods" | Out-Null
    Copy-Item -LiteralPath $exeFile "$g\LostOdysseyRecomp.exe"
    if (-not $NoSettings) { [IO.File]::WriteAllText("$g\settings.ini", $settings) }
    $g
}
function Put($g, $rel, $text) { $p = Join-Path $g $rel; New-Item -ItemType Directory -Force (Split-Path $p) | Out-Null; [IO.File]::WriteAllText($p, $text) }
function Run($g, $arg) {
    $p = Start-Process -FilePath $exe -ArgumentList $arg, "`"$g`"" -Wait -PassThru -WindowStyle Hidden -RedirectStandardOutput $out -RedirectStandardError "$out.err"
    $t = ((Get-Content -LiteralPath $out -Raw -Encoding UTF8) + (Get-Content -LiteralPath "$out.err" -Raw -Encoding UTF8)) -replace '[^\x00-\x7F]+', '<ar>'
    $script:last = $p.ExitCode
    "  {0,-12} exit {1}: {2}" -f $arg, $p.ExitCode, ("$t".Trim() -replace "`r?`n", ' | ')
}
function Snap($g) { Get-ChildItem -LiteralPath $g -Recurse -Force | ForEach-Object {
        $r = $_.FullName.Substring($g.Length)
        if ($_.PSIsContainer) { "D $r" } else { "F $r " + (Get-FileHash -LiteralPath $_.FullName -Algorithm MD5).Hash } } | Sort-Object }
function Same($g, $before, $label) {
    $d = Compare-Object @($before) @(Snap $g)
    if ($d) { $script:fails++; "  FAIL $label differs:"; $d | Select-Object -First 8 | ForEach-Object { "    $($_.SideIndicator) $($_.InputObject)" } }
    else { "  ok   ${label}: folder exactly as before" }
}
function Check($cond, $label) { if ($cond) { "  ok   $label" } else { $script:fails++; "  FAIL $label" } }
function Images($g) { if (Test-Path "$g\mods\ar-images") { (Get-ChildItem -LiteralPath "$g\mods\ar-images" -Recurse -File).Count } else { 0 } }
function Count($g) { if (Test-Path "$g\mods\ar") { (Get-ChildItem -LiteralPath "$g\mods\ar" -Recurse -File).Count } else { 0 } }
function Setting($g, $key) { (Get-Content -LiteralPath "$g\settings.ini" | Where-Object { $_ -like "$key=*" }) -replace "^$key=", '' }

'== A: clean game'
$g = New-Game 'A' $NewExe; $s0 = Snap $g
Run $g '--status'; Run $g '--install'
Check ((Count $g) -eq 789) "789 pack files in mods\ar ($(Count $g))"
Check ((Images $g) -eq 66) "66 files in mods\ar-images ($(Images $g))"
Check (-not (Test-Path "$g\mod-list.ini")) 'no mod-list.ini created'
Check ((Setting $g 'game_language') -eq '2' -and (Setting $g 'game_language_pack') -eq 'ar') 'settings.ini: game_language=2, game_language_pack=ar'
Check ((Get-Content -LiteralPath "$g\settings.ini" -Raw) -match "width=1920`r`n$") 'settings.ini keeps its other lines and CRLF ends'
Run $g '--install'; Run $g '--status'; Run $g '--uninstall'
Same $g $s0 'A after uninstall'

'== B: recomp without right-to-left support'
$g = New-Game 'B' $OldExe; $s0 = Snap $g
Run $g '--status'; Run $g '--install'
Check ($last -eq 1) 'install refused'
Same $g $s0 'B after the refused install'
$p = Start-Process -FilePath $exe -ArgumentList '--install', "`"$g`"", '--force' -Wait -PassThru -WindowStyle Hidden -RedirectStandardOutput $out
Check ($p.ExitCode -eq 0 -and (Count $g) -eq 789) 'installed anyway with --force (the window''s "No" answer)'
Run $g '--uninstall'
Same $g $s0 'B after uninstall'

'== C: no settings.ini yet'
$g = New-Game 'C' $NewExe -NoSettings; $s0 = Snap $g
Run $g '--install'
Check ((Count $g) -eq 789 -and -not (Test-Path "$g\settings.ini")) 'installed, settings.ini not created'
Run $g '--uninstall'
Same $g $s0 'C after uninstall'

'== D: manual zip extracted by hand'
$g = New-Game 'D' $NewExe
Expand-Archive -LiteralPath $manual -DestinationPath "$Base\D-zip"
Copy-Item -Recurse "$Base\D-zip\mods\*" "$g\mods"
Run $g '--status'
Check ((Get-Content -LiteralPath $out -Raw) -match '^Installed') 'detected as installed'
Run $g '--uninstall'
Check (-not (Test-Path "$g\mods\ar") -and -not (Test-Path "$g\mods\ar-images") -and (Test-Path "$g\mods")) 'mods\ar and mods\ar-images removed, mods\ kept'

'== E: a player''s own file in mods\ar'
$g = New-Game 'E' $NewExe
Put $g 'mods\ar\my-notes.txt' 'mine'
Run $g '--install'; Run $g '--uninstall'
Check ((Count $g) -eq 1 -and (Test-Path "$g\mods\ar\my-notes.txt")) 'only my-notes.txt left in mods\ar'

'== F: another pack with id=ar'
$g = New-Game 'F' $NewExe
Put $g 'mods\arabic-old\language.ini' "id=ar`r`nname=Old`r`nbase=jpn`r`n"
Run $g '--install'
Check ((Get-Content -LiteralPath $out -Raw) -match 'other packs with id ar: arabic-old') 'other pack reported'
Run $g '--uninstall'

'== G: another language pack selected before'
$g = New-Game 'G' $NewExe
[IO.File]::WriteAllText("$g\settings.ini", ($settings -replace 'game_language_pack=', 'game_language_pack=pt-br'))
$s0 = Snap $g
Run $g '--install'
Check ((Setting $g 'game_language_pack') -eq 'ar') 'Arabic selected'
Run $g '--uninstall'
Same $g $s0 'G after uninstall (pt-br back)'

'== H: a release older than the minimum (manifest.json), then a current one'
$g = New-Game 'H' $NewExe
Put $g 'manifest.json' '{"version": "v1.0.4"}'
$s0 = Snap $g
Run $g '--status'; Run $g '--install'
Check ($last -eq 1) 'v1.0.4 refused'
Same $g $s0 'H after the refused install'
Put $g 'manifest.json' ('{"version": "v' + $MinVersion + '"}')
Run $g '--status'; Run $g '--install'
Check ($last -eq 0 -and (Count $g) -eq 789) "v$MinVersion installed"
Run $g '--uninstall'

'== I: mod-list.ini that lists the HD pack'
$g = New-Game 'I' $NewExe
Put $g 'mod-list.ini' "# Mod order`r`nhd-textures-4x=on`r`n"
$s0 = Snap $g
Run $g '--install'
Check ((Get-Content -LiteralPath "$g\mod-list.ini" -Raw) -eq "# Mod order`r`nar-images=on`r`nhd-textures-4x=on`r`n") 'ar-images=on above hd-textures-4x, CRLF kept'
Run $g '--install'
Check (@(Get-Content -LiteralPath "$g\mod-list.ini" | Where-Object { $_ -like 'ar-images=*' }).Count -eq 1) 'reinstall adds no second line'
Run $g '--uninstall'
Same $g $s0 'I after uninstall'

"== $fails failure(s)"
exit $fails
