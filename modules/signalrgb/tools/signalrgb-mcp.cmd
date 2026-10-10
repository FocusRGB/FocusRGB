@echo off
REM Launch SignalRGB's MCP server, resolving the newest installed app-* version.
REM Pinning the version directly breaks on every SignalRGB update.
setlocal
set "BASE=%LOCALAPPDATA%\VortxEngine"
set "LATEST="
for /f "delims=" %%d in ('dir /b /ad /o-d "%BASE%\app-*" 2^>nul') do (
  if not defined LATEST set "LATEST=%%d"
)
if not defined LATEST (
  echo SignalRGB install not found under %BASE% 1>&2
  exit /b 1
)
"%BASE%\%LATEST%\Signal-x64\SignalRgbMcp.exe" %*
