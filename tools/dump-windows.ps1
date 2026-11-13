Add-Type @"
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class Win {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  public static List<string> Dump(uint targetPid, bool children, IntPtr parent) {
    var list = new List<string>();
    EnumProc cb = delegate(IntPtr h, IntPtr l) {
      uint pid; GetWindowThreadProcessId(h, out pid);
      if (targetPid != 0 && pid != targetPid) return true;
      var t = new StringBuilder(512); GetWindowText(h, t, 512);
      var c = new StringBuilder(256); GetClassName(h, c, 256);
      list.Add(string.Format("0x{0:X} vis={1} class={2} title={3}", h.ToInt64(), IsWindowVisible(h), c, t));
      return true;
    };
    if (children) EnumChildWindows(parent, cb, IntPtr.Zero); else EnumWindows(cb, IntPtr.Zero);
    return list;
  }
}
"@
$p = Get-Process Revit -ErrorAction SilentlyContinue
if (-not $p) { "NO REVIT"; exit }
"MAIN=0x{0:X} TITLE={1}" -f $p.MainWindowHandle.ToInt64(), $p.MainWindowTitle
"--- top-level windows of revit ---"
[Win]::Dump([uint32]$p.Id, $false, [IntPtr]::Zero) | ForEach-Object { $_ }
"--- child windows ---"
[Win]::Dump([uint32]$p.Id, $true, $p.MainWindowHandle) | Where-Object { $_ -notmatch "title=\s*$" } | Select-Object -First 20
