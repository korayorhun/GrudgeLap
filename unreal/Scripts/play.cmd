@echo off
rem Plays the Unreal Engine version from the project, without the editor: a window of 1920x1080 with sound.
rem   play.cmd [extra arguments, e.g. -fullscreen or -GlpView=chase]
rem The packaged build (see README.md) runs the same game as GrudgeLapUE.exe.
if not defined UE_ROOT set UE_ROOT=C:\UE\5.8
start "" "%UE_ROOT%\Engine\Binaries\Win64\UnrealEditor.exe" "%~dp0..\GrudgeLapUE.uproject" /Game/Maps/Feasibility -game -windowed -ResX=1920 -ResY=1080 %*
