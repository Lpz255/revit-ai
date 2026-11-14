#!/usr/bin/env bash
set -e
SRC="/e/0个人文件夹/dsh/revit-ai/dist/RevitAI.dll"
DEST="$APPDATA/Autodesk/Revit/Addins/2020"
mkdir -p "$DEST/RevitAI"
cp -f "$SRC" "$DEST/RevitAI/RevitAI.dll"
DLL_WIN="C:\\Users\\lipez\\AppData\\Roaming\\Autodesk\\Revit\\Addins\\2020\\RevitAI\\RevitAI.dll"
{
  printf '\xef\xbb\xbf'
  printf '<?xml version="1.0" encoding="utf-8"?>\n'
  printf '<RevitAddIns>\n'
  printf '  <AddIn Type="Application">\n'
  printf '    <Name>RevitAI Bridge</Name>\n'
  printf '    <Assembly>%s</Assembly>\n' "$DLL_WIN"
  printf '    <AddInId>7E2C1B44-9F3A-4C21-8D77-2B5E0A6C1F01</AddInId>\n'
  printf '    <FullClassName>RevitAI.App</FullClassName>\n'
  printf '    <VendorId>DSH</VendorId>\n'
  printf '    <VendorDescription>Local AI modeling bridge for Revit</VendorDescription>\n'
  printf '  </AddIn>\n'
  printf '</RevitAddIns>\n'
} > "$DEST/RevitAI.addin"
echo "== installed =="
ls -la "$DEST" "$DEST/RevitAI"
echo "== manifest =="
cat "$DEST/RevitAI.addin"
