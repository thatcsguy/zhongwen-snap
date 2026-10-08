@echo off
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo Windows .NET Framework compiler was not found.
    exit /b 1
)
if not exist "dist" mkdir "dist"
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /out:"dist\ZhongWenSnap.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.Security.dll /reference:System.Core.dll src\Core.cs src\Windows.cs src\Program.cs
if errorlevel 1 exit /b 1
echo Built dist\ZhongWenSnap.exe
