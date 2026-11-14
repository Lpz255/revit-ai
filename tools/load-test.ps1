$revit = "D:\software\revit\Revit 2020"
$dll = "E:\revit-ai\dist\RevitAI.dll"
try { [Reflection.Assembly]::LoadFrom((Join-Path $revit "RevitAPI.dll")) | Out-Null; "revitapi ok" } catch { "revitapi fail: " + $_.Exception.Message }
try { [Reflection.Assembly]::LoadFrom((Join-Path $revit "RevitAPIUI.dll")) | Out-Null; "revitapiui ok" } catch { "revitapiui fail: " + $_.Exception.Message }
try {
  $asm = [Reflection.Assembly]::LoadFrom($dll)
  "plugin loaded: " + $asm.FullName
  $t = $asm.GetType("RevitAI.App")
  if ($t) { "type ok: " + $t.FullName } else { "type MISSING" }
} catch {
  "plugin load failed: " + $_.Exception.GetType().Name + " :: " + $_.Exception.Message
}
