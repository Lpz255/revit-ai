@echo off
setlocal
set SRC=%~dp0
set DEST=%APPDATA%\Autodesk\Revit\Addins\2020
set DLL=%SRC%dist\RevitAI.dll
if not exist "%DLL%" (
  echo [ERROR] %DLL% not found. Build first:  dotnet build src\RevitAI.csproj -c Release
  pause
  exit /b 1
)
if not exist "%DEST%" mkdir "%DEST%"
powershell -NoProfile -Command "(Get-Content -Raw -Encoding UTF8 '%SRC%RevitAI.addin.template') -replace '\{\{DLL_PATH\}\}', '%DLL%' | Set-Content -Encoding UTF8 '%DEST%\RevitAI.addin'"
echo installed: %DEST%\RevitAI.addin
echo restart Revit, click "Always Load" on the unsigned add-in prompt
pause
