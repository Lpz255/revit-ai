@echo off
setlocal
set SRC=%~dp0
set DEST=%APPDATA%\Autodesk\Revit\Addins\2020
set DLLSRC=%SRC%dist\RevitAI.dll
set DLLDST=%DEST%\RevitAI\RevitAI.dll
if not exist "%DLLSRC%" (
  echo [ERROR] %DLLSRC% not found. Build first:  dotnet build src\RevitAI.csproj -c Release
  pause
  exit /b 1
)
if not exist "%DEST%\RevitAI" mkdir "%DEST%\RevitAI"
copy /Y "%DLLSRC%" "%DLLDST%" >nul
powershell -NoProfile -Command "$t = Get-Content -Raw -Encoding UTF8 '%SRC%RevitAI.addin.template'; $t = $t.Replace('{{DLL_PATH}}', '%DLLDST%'); [IO.File]::WriteAllText('%DEST%\RevitAI.addin', $t, (New-Object Text.UTF8Encoding $true))"
echo installed: %DEST%\RevitAI.addin
echo restart Revit and click "Always Load" on the unsigned add-in prompt
pause
