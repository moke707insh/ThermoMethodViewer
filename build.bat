@echo off
setlocal enabledelayedexpansion

echo Building MethViewer - Single File Executable
echo ===========================================

:: Build from temp dir with no spaces in path
set SRC=%~dp0
set TMP=%TEMP%\MethViewerBuild
rd /s /q "%TMP%" 2>nul
mkdir "%TMP%"
xcopy /y /q "%SRC%*.*" "%TMP%\" >nul

cd /d "%TMP%"
echo Build directory: %CD%

set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set OUT=MethViewer.exe

:: Generate response file
set RSP=%TMP%\build.rsp
(
echo -target:winexe
echo -out:%OUT%
echo -win32icon:MethViewer.ico
echo -platform:x64
echo -optimize
echo -reference:System.dll
echo -reference:System.Windows.Forms.dll
echo -reference:System.Drawing.dll
echo -reference:System.Core.dll
echo -reference:System.Xml.dll
) > "%RSP%"

:: Add DLL references and embedded resources
for %%f in (*.dll) do (
    echo -reference:%%~nxf>> "%RSP%"
    echo -resource:%%~nxf,MethViewer.%%~nxf>> "%RSP%"
)
:: Add icon file as embedded resource
echo -resource:MethViewer.ico,MethViewer.MethViewer.ico>> "%RSP%"

echo MethViewer.cs>> "%RSP%"

type "%RSP%"
echo.
echo Compiling...
"%CSC%" @"%RSP%"

if %ERRORLEVEL% EQU 0 (
    copy /y "%TMP%\%OUT%" "%SRC%\%OUT%" >nul
    echo.
    echo ==========================================
    echo  BUILD SUCCESS
    echo  Output: %SRC%\%OUT%
    for %%A in ("%SRC%\%OUT%") do echo  Size: %%~zA bytes
    echo ==========================================
) else (
    echo.
    echo BUILD FAILED
    exit /b 1
)

rd /s /q "%TMP%" 2>nul
