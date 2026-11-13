Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$p = Get-Process Revit -ErrorAction SilentlyContinue
if (-not $p) { "NO REVIT"; exit }
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
$wins = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)
"windows: " + $wins.Count
foreach ($w in $wins) {
  "WIN: " + $w.Current.Name
  $all = $w.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
  $n = 0
  foreach ($e in $all) {
    $nm = $e.Current.Name
    if ($nm -and $nm.Trim().Length -gt 0) {
      "  [" + $e.Current.ControlType.ProgrammaticName.Replace("ControlType.","") + "] " + $nm
      $n++
    }
    if ($n -ge 25) { break }
  }
}
