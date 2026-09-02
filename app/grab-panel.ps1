# 离屏抓取面板窗口 (PrintWindow, 不需要屏幕点亮)
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;using System.Runtime.InteropServices;
public class PW{
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
 public struct RECT{public int L,T,R,B;}
}
'@
$p = Get-Process ZCodeWallpaper | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
$hwnd = $p.MainWindowHandle
$r = New-Object PW+RECT
[PW]::GetWindowRect($hwnd, [ref]$r) | Out-Null
$w = $r.R - $r.L; $h = $r.B - $r.T
$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$dc = $g.GetHdc()
$ok = [PW]::PrintWindow($hwnd, $dc, 2)  # 2 = PW_RENDERFULLCONTENT
$g.ReleaseHdc($dc)
$g.Dispose()
$out = "D:\Tools\zcode-wallpaper-cdp\docs\wallpaper-panel.png"
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output ("PrintWindow=$ok saved $out (${w}x${h})")
