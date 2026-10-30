@echo off
rem Builds DeskPicker.exe here: one file, nothing to install. Needs the .NET 10 SDK (dot.net).
rem Close DeskPicker before rebuilding, or the .exe can't be overwritten.
setlocal
cd /d "%~dp0"
dotnet publish src\DeskPicker.csproj -c Release -r win-x64 -o dist\win-x64 -nologo
if errorlevel 1 (echo Build failed.& exit /b 1)
copy /y dist\win-x64\DeskPicker.exe DeskPicker.exe >nul
if errorlevel 1 (echo Couldn't copy DeskPicker.exe - is DeskPicker still open?& exit /b 1)
echo Built DeskPicker.exe
