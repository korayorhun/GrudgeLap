@echo off
rem Drives the car with the simulation core in /Game/Maps/Feasibility, off screen (no window): the scripted driver
rem (attract mode) takes it round, screenshots go to Saved\Screenshots\Glp and the log has the car's state every second.
rem   drive.cmd [cockpit|chase] [shot seconds joined by +, e.g. 3+8+15] [quit after seconds]
if not defined UE_ROOT set UE_ROOT=C:\UE\5.8
set VIEW=%~1
if "%VIEW%"=="" set VIEW=chase
set SHOTS=%~2
if "%SHOTS%"=="" set SHOTS=3+8+15
set QUIT=%~3
if "%QUIT%"=="" set QUIT=16
if not defined DRIVE_LOG set DRIVE_LOG=%TEMP%\grudgelap-ue-drive.log
"%UE_ROOT%\Engine\Binaries\Win64\UnrealEditor-Cmd.exe" "%~dp0..\GrudgeLapUE.uproject" /Game/Maps/Feasibility -game -RenderOffScreen -unattended -nosplash -NoSound -ResX=1920 -ResY=1080 -GlpAttract -GlpView=%VIEW% -GlpShots=%SHOTS% -GlpQuitAfter=%QUIT% -abslog="%DRIVE_LOG%"
