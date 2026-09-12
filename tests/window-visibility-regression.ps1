param([Parameter(Mandatory=$true)][string]$AssemblyPath)
$ErrorActionPreference = 'Stop'

$asm = [Reflection.Assembly]::LoadFile((Resolve-Path $AssemblyPath))
$type = $asm.GetTypes() | Where-Object { $_.Name -eq 'WindowVisibility' }
if ($null -eq $type) { throw 'WindowVisibility type not found' }
$method = $type.GetMethod('VisibleRatio', [Reflection.BindingFlags]'Static,NonPublic,Public', $null,
    [Type[]]@([Drawing.Rectangle], [Drawing.Rectangle[]], [Drawing.Rectangle[]]), $null)
if ($null -eq $method) { throw 'VisibleRatio method not found' }

function Ratio([Drawing.Rectangle]$target, [Drawing.Rectangle[]]$screens, [Drawing.Rectangle[]]$occluders) {
    return [double]$method.Invoke($null, @($target, $screens, $occluders))
}
function Near([double]$actual, [double]$expected, [string]$name) {
    if ([Math]::Abs($actual - $expected) -gt 0.0001) { throw "$name expected=$expected actual=$actual" }
}

$target = [Drawing.Rectangle]::new(0, 0, 1000, 1000)
$screen = [Drawing.Rectangle]::new(0, 0, 1000, 1000)
Near (Ratio $target @($screen) @()) 1.0 'fully visible'
Near (Ratio $target @($screen) @([Drawing.Rectangle]::new(500, 0, 500, 1000))) 0.5 'split screen'
Near (Ratio $target @($screen) @([Drawing.Rectangle]::new(0, 0, 900, 1000))) 0.1 'exact threshold'
Near (Ratio $target @($screen) @([Drawing.Rectangle]::new(0, 0, 901, 1000))) 0.099 'below threshold'
Near (Ratio $target @($screen) @([Drawing.Rectangle]::new(0, 0, 400, 1000), [Drawing.Rectangle]::new(600, 0, 400, 1000))) 0.2 'two occluders'
Near (Ratio ([Drawing.Rectangle]::new(-200, 0, 400, 1000)) @($screen) @()) 0.5 'cross monitor clipping'
Near (Ratio ([Drawing.Rectangle]::new(0, 0, 100, 100)) @([Drawing.Rectangle]::new(0, 0, 100, 100)) @([Drawing.Rectangle]::new(0, 0, 60, 100), [Drawing.Rectangle]::new(40, 0, 60, 100))) 0.0 'overlap union'
Near (Ratio ([Drawing.Rectangle]::new(0, 0, 300, 100)) @([Drawing.Rectangle]::new(0, 0, 100, 100), [Drawing.Rectangle]::new(200, 0, 100, 100)) @()) (2.0 / 3.0) 'multi-monitor gap'

Write-Output 'window visibility regression: PASS'
