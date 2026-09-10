Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$screenshotDir = "E:\led-sup-bot\screenshots"
if (!(Test-Path $screenshotDir)) {
    New-Item -ItemType Directory -Path $screenshotDir -Force | Out-Null
}

$csharpCode = @'
using System;
using System.Text;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Imaging;

public class Win32WindowHelper
{
    [DllImport("user32.dll")]
    public static extern bool SetProcessDPIAware();

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public static IntPtr FindLedSupportMainWindow(uint pid)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hWnd, lParam) =>
        {
            uint wPid;
            GetWindowThreadProcessId(hWnd, out wPid);
            if (wPid == pid && IsWindowVisible(hWnd))
            {
                StringBuilder sb = new StringBuilder(256);
                GetWindowText(hWnd, sb, 256);
                string title = sb.ToString();
                if (title.Contains("LED Support") && !title.Contains("Başlatılıyor"))
                {
                    RECT r;
                    GetWindowRect(hWnd, out r);
                    if (r.Right - r.Left > 400 && r.Bottom - r.Top > 400)
                    {
                        found = hWnd;
                        return false;
                    }
                }
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static string CaptureWindow(IntPtr hWnd, string filePath)
    {
        if (hWnd == IntPtr.Zero) return "Zero hWnd";
        SetProcessDPIAware();
        ShowWindow(hWnd, 1);
        SetForegroundWindow(hWnd);
        System.Threading.Thread.Sleep(500);

        RECT rect;
        GetWindowRect(hWnd, out rect);
        int w = rect.Right - rect.Left;
        int h = rect.Bottom - rect.Top;
        if (w <= 0 || h <= 0) return "Invalid dimensions (" + w + "x" + h + ")";

        using (Bitmap bmp = new Bitmap(w, h))
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(w, h));
            }
            bmp.Save(filePath, ImageFormat.Png);
        }
        return "OK: " + w + "x" + h;
    }

    public static bool Resize(IntPtr hWnd, int width, int height)
    {
        if (hWnd == IntPtr.Zero) return false;
        SetProcessDPIAware();
        ShowWindow(hWnd, 1);
        SetForegroundWindow(hWnd);
        System.Threading.Thread.Sleep(300);
        return MoveWindow(hWnd, 40, 40, width, height, true);
    }
}
'@

Add-Type -TypeDefinition $csharpCode -ReferencedAssemblies System.Drawing, System.Windows.Forms

# Kill any existing desktop app process
Get-Process LedSupport -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

# Launch the Desktop application
$exePath = "E:\led-sup-bot\src\LedSupBot.Desktop\bin\Debug\net8.0-windows\LedSupport.exe"
Write-Output "Launching LedSupport: $exePath"
$appProc = Start-Process -FilePath $exePath -PassThru

# Wait for StartupWindow to complete and MainWindow to show
$hWnd = [IntPtr]::Zero
for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Milliseconds 500
    $proc = Get-Process LedSupport -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($proc) {
        $hWnd = [Win32WindowHelper]::FindLedSupportMainWindow($proc.Id)
        if ($hWnd -ne [IntPtr]::Zero) {
            break
        }
    }
}

if ($hWnd -eq [IntPtr]::Zero) {
    Write-Output "MainWindow not found within timeout."
    exit 1
}

Write-Output "Found MainWindow Handle: $hWnd"
Start-Sleep -Seconds 2

# 1. Capture Login Screen
$loginShot = Join-Path $screenshotDir "01_login_screen.png"
$res = [Win32WindowHelper]::CaptureWindow($hWnd, $loginShot)
Write-Output "Login screen capture: $res ($loginShot)"

# Focus window and type password with key delays
[Win32WindowHelper]::SetForegroundWindow($hWnd)
Start-Sleep -Milliseconds 600

$pass = "led-sup-admin"
foreach ($char in $pass.ToCharArray()) {
    [System.Windows.Forms.SendKeys]::SendWait($char.ToString())
    Start-Sleep -Milliseconds 60
}
Start-Sleep -Milliseconds 300
[System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
Start-Sleep -Seconds 4

# 2. Capture Main Screen at Default Size (1280x820)
$mainShot = Join-Path $screenshotDir "02_main_screen_1280x820.png"
$res = [Win32WindowHelper]::CaptureWindow($hWnd, $mainShot)
Write-Output "Main screen capture: $res ($mainShot)"

# 3. Test at 1920x1080
[Win32WindowHelper]::Resize($hWnd, 1920, 1080)
Start-Sleep -Seconds 2
$shot1920 = Join-Path $screenshotDir "03_screen_1920x1080.png"
$res = [Win32WindowHelper]::CaptureWindow($hWnd, $shot1920)
Write-Output "1920x1080 capture: $res ($shot1920)"

# 4. Test at 1440x900
[Win32WindowHelper]::Resize($hWnd, 1440, 900)
Start-Sleep -Seconds 2
$shot1440 = Join-Path $screenshotDir "04_screen_1440x900.png"
$res = [Win32WindowHelper]::CaptureWindow($hWnd, $shot1440)
Write-Output "1440x900 capture: $res ($shot1440)"

# 5. Test at 1366x768
[Win32WindowHelper]::Resize($hWnd, 1366, 768)
Start-Sleep -Seconds 2
$shot1366 = Join-Path $screenshotDir "05_screen_1366x768.png"
$res = [Win32WindowHelper]::CaptureWindow($hWnd, $shot1366)
Write-Output "1366x768 capture: $res ($shot1366)"

# Cleanup
Get-Process LedSupport -ErrorAction SilentlyContinue | Stop-Process -Force
Write-Output "Visual QA completed successfully."
