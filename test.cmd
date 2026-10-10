@echo off
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo Windows .NET Framework compiler was not found.
    exit /b 1
)
if not exist "work" mkdir "work"
"%CSC%" /nologo /target:exe /platform:anycpu /out:"work\ZhongWenSnapSelfTest.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.Security.dll /reference:System.Core.dll src\Core.cs src\Windows.cs src\Program.cs
if errorlevel 1 exit /b 1
"work\ZhongWenSnapSelfTest.exe" --self-test
if errorlevel 1 exit /b 1
"%CSC%" /nologo /target:exe /platform:anycpu /main:ZhongWenSnap.AskProbe /out:"work\AskProbe.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.Security.dll /reference:System.Core.dll src\Core.cs src\Windows.cs src\Program.cs tests\AskProbe.cs
if errorlevel 1 exit /b 1
"work\AskProbe.exe"
exit /b %errorlevel%
