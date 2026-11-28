# Builds every mod in Mods\catalog.json and gathers one GitHub release: catalog.json, each mod's DLL and thumbnail.
# Every Schedule Loader picks the release up the next time it opens, so mod updates need no new launcher.
#
# To publish an update: raise the mod's "version" in Mods\catalog.json, run this, and publish the release it prepares.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$catalogPath = Join-Path $root 'Mods\catalog.json'
$catalog = Get-Content $catalogPath -Raw | ConvertFrom-Json
$repo = "$($catalog.repo)".Trim()

# the game folder, so the mods build against this version of Schedule I
$gameDir = $null
$saved = Join-Path $env:LOCALAPPDATA 'ScheduleLoader\gamepath.txt'
if (Test-Path $saved) { $gameDir = (Get-Content $saved -Raw).Trim() }
if (-not $gameDir -or -not (Test-Path (Join-Path $gameDir 'Schedule I.exe'))) { $gameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Schedule I' }
if (-not (Test-Path (Join-Path $gameDir 'MelonLoader\Il2CppAssemblies\Assembly-CSharp.dll'))) {
    throw "Schedule I with MelonLoader was not found at '$gameDir'. Run Set up in Schedule Loader once, then try again."
}

$out = Join-Path $root 'release'
$work = Join-Path $env:TEMP ('sl-mods-' + [guid]::NewGuid().ToString('N'))
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory $out | Out-Null

try {
    foreach ($m in $catalog.mods) {
        $name = [IO.Path]::GetFileNameWithoutExtension($m.file)
        $proj = Join-Path $root "Mods\$name\$name.csproj"
        if (-not (Test-Path $proj)) { throw "$($m.name): no project at Mods\$name\$name.csproj" }
        Write-Host "Building $($m.name) $($m.version)..."
        & dotnet build $proj -c Release "-p:GameDir=$gameDir" -o (Join-Path $work $name) --nologo -v q
        if ($LASTEXITCODE -ne 0) { throw "$($m.name) did not build. Nothing was published." }
        Copy-Item (Join-Path $work "$name\$($m.file)") $out
        if ($m.thumb) {
            $thumb = Join-Path $root "Launcher\Assets\$($m.thumb)"
            if (Test-Path $thumb) { Copy-Item $thumb $out } else { Write-Warning "$($m.name): thumbnail $($m.thumb) not found in Launcher\Assets" }
        }
    }
    Copy-Item $catalogPath $out
}
finally {
    if (Test-Path $work) { Remove-Item $work -Recurse -Force }
    Get-ChildItem (Join-Path $root 'Mods') -Directory | ForEach-Object { Remove-Item (Join-Path $_.FullName 'obj'), (Join-Path $_.FullName 'bin') -Recurse -Force -ErrorAction SilentlyContinue }
}

$tag = 'mods-' + (Get-Date -Format 'yyyy.MM.dd-HHmm')
$notes = ($catalog.mods | ForEach-Object { "- $($_.name) $($_.version)" }) -join "`n"
Write-Host ""
Write-Host "Release files are ready in: $out"

if ($repo -eq '') {
    Write-Warning 'Mods\catalog.json has no "repo" yet. Put your GitHub repository there (like "yourname/ScheduleLoader-Mods") before publishing.'
}
elseif (Get-Command gh -ErrorAction SilentlyContinue) {
    Write-Host "Publishing $tag to github.com/$repo..."
    & gh release create $tag (Get-ChildItem $out).FullName --repo $repo --title "Mods $tag" --notes $notes
    if ($LASTEXITCODE -ne 0) { throw 'GitHub did not accept the release. If this is the first time, run "gh auth login" first.' }
    Write-Host "Published. Every Schedule Loader will see it the next time it opens."
}
else {
    Write-Host "To publish: open https://github.com/$repo/releases/new , choose a new tag named $tag ,"
    Write-Host "drag in every file from the release folder, and press Publish release."
    Start-Process "https://github.com/$repo/releases/new?tag=$tag"
    Start-Process explorer.exe $out
}
