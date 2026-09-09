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

public class Win32WindowHelper2
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
        System.Threading.Thread.Sleep(400);

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

    public static void ClickRel(IntPtr hWnd, double relX, double relY)
    {
        SetProcessDPIAware();
        SetForegroundWindow(hWnd);
        RECT rect;
        GetWindowRect(hWnd, out rect);
        int x = (int)(rect.Left + (rect.Right - rect.Left) * relX);
        int y = (int)(rect.Top + (rect.Bottom - rect.Top) * relY);
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(100);
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(50);
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
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
        $hWnd = [Win32WindowHelper2]::FindLedSupportMainWindow($proc.Id)
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
[Win32WindowHelper2]::SetForegroundWindow($hWnd)
Start-Sleep -Milliseconds 400
$pass = "led-sup-admin"
foreach ($char in $pass.ToCharArray()) {
    [System.Windows.Forms.SendKeys]::SendWait($char.ToString())
    Start-Sleep -Milliseconds 50
}
Start-Sleep -Milliseconds 200
[System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
Start-Sleep -Seconds 4

# Click second conversation in sidebar (approx relX=0.10, relY=0.25)
[Win32WindowHelper2]::ClickRel($hWnd, 0.10, 0.25)
Start-Sleep -Seconds 2

# Capture test conversation active chat (showing 'Sen' and 'Müşteri' message bubbles)
$chatShot = Join-Path $screenshotDir "06_conversation_active_chat.png"
$res = [Win32WindowHelper2]::CaptureWindow($hWnd, $chatShot)
Write-Output "Active chat screenshot: $res ($chatShot)"

# Toggle Technical Panel off
[Win32WindowHelper2]::ClickRel($hWnd, 0.94, 0.035)
Start-Sleep -Seconds 1

# Capture with panel collapsed
$collapsedShot = Join-Path $screenshotDir "07_panel_collapsed.png"
$res = [Win32WindowHelper2]::CaptureWindow($hWnd, $collapsedShot)
Write-Output "Panel collapsed screenshot: $res ($collapsedShot)"

# Toggle Technical Panel back on
[Win32WindowHelper2]::ClickRel($hWnd, 0.94, 0.035)
Start-Sleep -Seconds 1

# Cleanup
Get-Process LedSupport -ErrorAction SilentlyContinue | Stop-Process -Force
Write-Output "Interaction tests completed."
