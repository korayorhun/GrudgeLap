@echo off
rem Measures the real-time frame rate of /Game/Maps/Feasibility through the cockpit camera, off screen at 1920x1080:
rem the CSV profiler records 900 frames once the map is up and the run exits; the CSV lands in
rem Saved\Profiling\CSV (frame time, game/render thread and GPU times per frame). Capped at 120 fps: an uncapped
rem off-screen loop is not what a player sees, and this laptop's GPU has dropped out under one before.
if not defined UE_ROOT set UE_ROOT=C:\UE\5.8
if not defined PERF_LOG set PERF_LOG=%TEMP%\grudgelap-ue-perf.log
"%UE_ROOT%\Engine\Binaries\Win64\UnrealEditor-Cmd.exe" "%~dp0..\GrudgeLapUE.uproject" /Game/Maps/Feasibility -game -RenderOffScreen -unattended -nosplash -NoSound -ResX=1920 -ResY=1080 -csvGpuStats -ExitAfterCsvProfiling -ExecCmds="t.MaxFPS 120,CsvProfile Frames=900" -abslog="%PERF_LOG%"
