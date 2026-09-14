@echo off
setlocal
call "%~dp0build.cmd" || exit /b 1
set DEST=%LOCALAPPDATA%\Programs\GhAccounts
if not exist "%DEST%" mkdir "%DEST%"
copy /y "%~dp0bin\GhAccounts.exe" "%DEST%\" >nul
echo Installed to %DEST%
powershell -NoProfile -Command ^
  "$sh = New-Object -ComObject WScript.Shell;" ^
  "$t = Join-Path $env:LOCALAPPDATA 'Programs\GhAccounts\GhAccounts.exe';" ^
  "foreach ($d in @([Environment]::GetFolderPath('Desktop'),[Environment]::GetFolderPath('Programs'))) {" ^
  "  $l = $sh.CreateShortcut((Join-Path $d 'GitHub Accounts.lnk'));" ^
  "  $l.TargetPath = $t; $l.WorkingDirectory = (Split-Path $t);" ^
  "  $l.IconLocation = $t + ',0';" ^
  "  $l.Description = 'Switch repositories between your GitHub accounts'; $l.Save() };" ^
  "Write-Host 'Shortcuts created on the Desktop and in the Start Menu'"
echo.
echo Launch "GitHub Accounts" from the Desktop to finish setup.
