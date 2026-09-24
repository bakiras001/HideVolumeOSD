@echo off
rem Builds HideVolumeOSD (Release, x86 - runs fine on 64-bit Windows 10/11).
rem Needs "Visual Studio 2019/2022" or "Build Tools for Visual Studio" with the
rem ".NET desktop build tools" workload (includes the .NET Framework 4.8 targeting pack).
setlocal
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" goto :novs
for /f "usebackq delims=" %%i in (`"%VSWHERE%" -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do set "MSBUILD=%%i"
if not defined MSBUILD goto :novs
"%MSBUILD%" "%~dp0HideVolumeOSD.sln" /t:Rebuild /p:Configuration=Release /p:Platform=x86 /v:m
if errorlevel 1 exit /b 1
echo.
echo Done: %~dp0x86\Release\HideVolumeOSD.exe
exit /b 0
:novs
echo Visual Studio / Build Tools 2019 or newer with MSBuild was not found.
exit /b 1
