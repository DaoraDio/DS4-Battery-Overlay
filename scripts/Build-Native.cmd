@echo off
setlocal
call "%~1" >nul
if errorlevel 1 exit /b 1
pushd "%~3"
if errorlevel 1 exit /b 1
if "%~4"=="plugin" (
  cl /nologo /LD /MT /EHsc /std:c++17 /O2 /W4 /Fo:DS4PCStats.obj "%~2" /link /OUT:DS4PCStats.dll user32.lib comctl32.lib gdi32.lib
) else (
  cl /nologo /MT /EHsc /std:c++17 /O2 /W4 /Fo:Tests.obj "%~2" /link /OUT:Tests.exe user32.lib comctl32.lib gdi32.lib
)
set "buildResult=%errorlevel%"
popd
exit /b %buildResult%
