Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$screenshotDir = "E:\led-sup-bot\screenshots"

$csharpCode = @'
using System;
using System.Text;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Imaging;

public class Win32WindowHelper3
{
    [DllImport("user32.dll")]
    public static extern bool SetProcessDPIAware();

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int X, int Y);

    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;

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
        System.Threading.Thread.Sleep(300);

        RECT rect;
        GetWindowRect(hWnd, out rect);
        int w = rect.Right - rect.Left;
        int h = rect.Bottom - rect.Top;
        if (w <= 0 || h <= 0) return "Invalid dimensions";

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

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    public static void ClickAt(IntPtr hWnd, int clientX, int clientY)
    {
        SetProcessDPIAware();
        SetForegroundWindow(hWnd);
        RECT rect;
        GetWindowRect(hWnd, out rect);
        int x = rect.Left + clientX;
        int y = rect.Top + clientY;
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(100);
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(50);
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
    }

    public static int GetWindowWidth(IntPtr hWnd)
    {
        RECT r;
        GetWindowRect(hWnd, out r);
        return r.Right - r.Left;
    }
}
'@

Add-Type -TypeDefinition $csharpCode -ReferencedAssemblies System.Drawing, System.Windows.Forms

# Kill any existing desktop app process
Get-Process LedSupport -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

# Launch the Desktop application
$exePath = "E:\led-sup-bot\src\LedSupBot.Desktop\bin\Debug\net8.0-windows\LedSupport.exe"
$appProc = Start-Process -FilePath $exePath -PassThru

# Wait for MainWindow
$hWnd = [IntPtr]::Zero
for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Milliseconds 500
    $proc = Get-Process LedSupport -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($proc) {
        $hWnd = [Win32WindowHelper3]::FindLedSupportMainWindow($proc.Id)
        if ($hWnd -ne [IntPtr]::Zero) {
            break
        }
    }
}

if ($hWnd -eq [IntPtr]::Zero) {
    Write-Output "MainWindow not found."
    exit 1
}

Start-Sleep -Seconds 1
# Log in
[Win32WindowHelper3]::SetForegroundWindow($hWnd)
Start-Sleep -Milliseconds 400
$pass = "led-sup-admin"
foreach ($char in $pass.ToCharArray()) {
    [System.Windows.Forms.SendKeys]::SendWait($char.ToString())
    Start-Sleep -Milliseconds 50
}
Start-Sleep -Milliseconds 200
[System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
Start-Sleep -Seconds 4

# Click the "📊 Teknik Panel" button (approx 75px from right edge, 58px from top)
$w = [Win32WindowHelper3]::GetWindowWidth($hWnd)
$btnX = $w - 75
$btnY = 58
[Win32WindowHelper3]::ClickAt($hWnd, $btnX, $btnY)
Start-Sleep -Seconds 2

# Capture with panel collapsed
$collapsedShot = Join-Path $screenshotDir "07_panel_collapsed.png"
$res = [Win32WindowHelper3]::CaptureWindow($hWnd, $collapsedShot)
Write-Output "Panel collapsed screenshot: $res ($collapsedShot)"

# Click again to re-expand
[Win32WindowHelper3]::ClickAt($hWnd, $btnX, $btnY)
Start-Sleep -Seconds 2

# Capture re-expanded
$expandedShot = Join-Path $screenshotDir "08_panel_re_expanded.png"
$res = [Win32WindowHelper3]::CaptureWindow($hWnd, $expandedShot)
Write-Output "Panel re-expanded screenshot: $res ($expandedShot)"

# Cleanup
Get-Process LedSupport -ErrorAction SilentlyContinue | Stop-Process -Force
Write-Output "Toggle test completed."
