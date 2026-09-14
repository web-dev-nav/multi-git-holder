@echo off
setlocal
rem Builds with the C# compiler that ships with Windows - no SDK needed.
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo Could not find the .NET Framework 4.x C# compiler.
  echo It normally lives in %WINDIR%\Microsoft.NET\Framework64\v4.0.30319\.
  exit /b 1
)
if not exist "%~dp0bin" mkdir "%~dp0bin"
"%CSC%" /nologo /target:winexe /out:"%~dp0bin\GhAccounts.exe" /win32icon:"%~dp0src\app.ico" ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  /r:System.Core.dll /r:System.Web.Extensions.dll ^
  "%~dp0src\GhAccounts.cs" "%~dp0src\Ui.cs"
if errorlevel 1 exit /b 1
echo Built %~dp0bin\GhAccounts.exe
