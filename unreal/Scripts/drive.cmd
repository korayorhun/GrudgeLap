@echo off
rem Runs the game in /Game/Maps/Feasibility off screen (no window): the scripted driver (attract mode) takes the car
rem round, or a key script plays the keyboard; screenshots go to Saved\Screenshots\Glp (seconds counted from the
rem start of the drive) and the log has the car's state every second.
rem   drive.cmd [cockpit|chase] [shot seconds joined by +, e.g. 3+8+15] [quit after seconds] [key script]
rem The key script replaces the scripted driver, e.g. 2:Enter+4-20:Up+9:E+13:E (see Source\GrudgeLapUE\GlpInput.h);
rem with "title" the game stays on the title screen (shot seconds then count from the start).
if not defined UE_ROOT set UE_ROOT=C:\UE\5.8
set VIEW=%~1
if "%VIEW%"=="" set VIEW=chase
set SHOTS=%~2
if "%SHOTS%"=="" set SHOTS=3+8+15
set QUIT=%~3
if "%QUIT%"=="" set QUIT=16
set DRIVER=-GlpAttract
if not "%~4"=="" set DRIVER=-GlpKeys=%~4
if /i "%~4"=="title" set DRIVER=-GlpTitleShots
if not defined DRIVE_LOG set DRIVE_LOG=%TEMP%\grudgelap-ue-drive.log
"%UE_ROOT%\Engine\Binaries\Win64\UnrealEditor-Cmd.exe" "%~dp0..\GrudgeLapUE.uproject" /Game/Maps/Feasibility -game -RenderOffScreen -unattended -nosplash -NoSound -ResX=1920 -ResY=1080 %DRIVER% -GlpView=%VIEW% -GlpShots=%SHOTS% -GlpQuitAfter=%QUIT% -abslog="%DRIVE_LOG%"
