@echo off
setlocal
rem Removes the app and its shortcuts. Your SSH keys, repositories and the
rem managed blocks in .gitconfig / .ssh\config are left alone - delete those
rem blocks by hand if you want a full revert.
set DEST=%LOCALAPPDATA%\Programs\GhAccounts
if exist "%DEST%" rmdir /s /q "%DEST%"
del "%USERPROFILE%\Desktop\GitHub Accounts.lnk" 2>nul
del "%APPDATA%\Microsoft\Windows\Start Menu\Programs\GitHub Accounts.lnk" 2>nul
echo Removed the app. Config kept at %USERPROFILE%\.gh-accounts.json
