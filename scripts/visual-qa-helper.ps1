Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# Create output folder for screenshots
$screenshotDir = "E:\led-sup-bot\screenshots"
if (!(Test-Path $screenshotDir)) {
    New-Item -ItemType Directory -Path $screenshotDir -Force | Out-Null
}

function Capture-WindowScreenshot([string]$processName, [string]$filename) {
    $proc = Get-Process $processName -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $proc) {
        Write-Output "Process $processName not found."
        return
    }

    # Window rect via user32
    $sig = @'
    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
    public struct RECT {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
'@
    $type = Add-Type -MemberDefinition $sig -Name "Win32Utils_$([Guid]::NewGuid().ToString('N'))" -Namespace "Win32" -PassThru

    $hWnd = $proc.MainWindowHandle
    if ($hWnd -eq [IntPtr]::Zero) {
        Write-Output "No main window handle for $processName"
        return
    }

    [Win32.Win32Utils_... ]::SetForegroundWindow($hWnd)
    Start-Sleep -Milliseconds 500

    $rect = New-Object Win32.RECT
    $type::GetWindowRect($hWnd, [ref]$rect)
    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top

    if ($width -le 0 -or $height -le 0) {
        Write-Output "Invalid window size: $width x $height"
        return
    }

    $bmp = New-Object System.Drawing.Bitmap($width, $height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size($width, $height)))
    $g.Dispose()

    $destPath = Join-Path $screenshotDir $filename
    $bmp.Save($destPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()

    Write-Output "Saved screenshot: $destPath ($width x $height)"
}
