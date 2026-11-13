Add-Type -AssemblyName UIAutomationClient
$root = [System.Windows.Automation.AutomationElement]::RootElement
$children = $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
foreach ($c in $children) {
  $pid2 = $c.Current.ProcessId
  $pname = "?"
  try { $pname = (Get-Process -Id $pid2 -ErrorAction Stop).ProcessName } catch { }
  "{0,-7} {1,-18} {2}" -f $pid2, $pname, $c.Current.Name
}
