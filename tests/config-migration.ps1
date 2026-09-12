param(
    [Parameter(Mandatory = $true)]
    [string]$AssemblyPath
)

$ErrorActionPreference = 'Stop'
$assemblyFile = (Resolve-Path -LiteralPath $AssemblyPath).Path
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('zcwp-config-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null

function Assert-Equal($Actual, $Expected, [string]$Message) {
    if ($Actual -ne $Expected) {
        throw "$Message (expected=$Expected actual=$Actual)"
    }
}

try {
    $assembly = [Reflection.Assembly]::LoadFile($assemblyFile)
    $flags = [Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
    $programType = $assembly.GetType('Program', $true)
    $configType = $assembly.GetType('Config', $true)
    $programType.GetField('DataDir', $flags).SetValue($null, $scratch)
    $load = $configType.GetMethod('Load', $flags)

    $legacyMusic = '{"on":true,"type":"video","sourcePath":"video.mp4","cachedFile":"wallpaper.mp4","params":{},"music":"song.mp3","musicVolume":0.25,"musicAutoplay":false,"videoSound":false}'
    Set-Content -LiteralPath (Join-Path $scratch 'config.json') -Value $legacyMusic -Encoding UTF8
    $musicConfig = $load.Invoke($null, @())
    Assert-Equal $musicConfig.SoundSource 'music' 'legacy music must become the selected source'
    Assert-Equal $musicConfig.SoundEnabled $true 'legacy selected source must default to enabled'
    Assert-Equal $musicConfig.SoundVolume 0.25 'legacy music volume must migrate'
    Assert-Equal $musicConfig.FollowWindow $false 'legacy autoplay flag must migrate to follow-window'

    $musicConfig.Save()
    $saved = Get-Content -LiteralPath (Join-Path $scratch 'config.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-Equal $saved.soundSource 'music' 'saved config must use canonical source'
    Assert-Equal $saved.soundEnabled $true 'saved config must use canonical enabled state'
    Assert-Equal $saved.soundVolume 0.25 'saved config must use canonical volume'
    Assert-Equal $saved.followWindow $false 'saved config must use canonical focus policy'
    if ($null -ne $saved.PSObject.Properties['musicVolume'] -or
        $null -ne $saved.PSObject.Properties['musicAutoplay'] -or
        $null -ne $saved.PSObject.Properties['videoSound']) {
        throw 'legacy sound keys must not remain authoritative after save'
    }

    $legacyVideo = '{"on":true,"type":"video","sourcePath":"video.mp4","cachedFile":"wallpaper.mp4","params":{},"music":"","musicVolume":0.6,"musicAutoplay":true,"videoSound":true}'
    Set-Content -LiteralPath (Join-Path $scratch 'config.json') -Value $legacyVideo -Encoding UTF8
    $videoConfig = $load.Invoke($null, @())
    Assert-Equal $videoConfig.SoundSource 'video' 'legacy video soundtrack must become the selected source'
    Assert-Equal $videoConfig.SoundEnabled $true 'legacy video soundtrack must default to enabled'

    $invalidVideo = '{"on":true,"type":"image","sourcePath":"image.jpg","cachedFile":"wallpaper.jpg","params":{},"music":"","videoSound":true}'
    Set-Content -LiteralPath (Join-Path $scratch 'config.json') -Value $invalidVideo -Encoding UTF8
    $imageConfig = $load.Invoke($null, @())
    Assert-Equal $imageConfig.SoundSource 'none' 'image wallpaper cannot retain video soundtrack source'
    Assert-Equal $imageConfig.SoundEnabled $false 'invalid source must be disabled'

    Write-Output 'config migration: PASS'
}
finally {
    $resolvedScratch = [IO.Path]::GetFullPath($scratch)
    $resolvedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ($resolvedScratch.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $resolvedScratch -Recurse -Force
    }
}
