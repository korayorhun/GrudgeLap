@echo off
rem Renders the Movie Render Queue manifest that build_scene.py saved (Saved\MovieRenderPipeline\QueueManifest.utxt).
rem -RenderOffScreen renders on the GPU without creating a window, so the run never shows on the desktop.
rem The PNGs go where the queue's output setting says: the project's Saved\Renders.
if not defined UE_ROOT set UE_ROOT=C:\UE\5.8
"%UE_ROOT%\Engine\Binaries\Win64\UnrealEditor-Cmd.exe" "%~dp0..\GrudgeLapUE.uproject" /MovieRenderPipeline/MoviePipelineEntryMap?game=/Script/MovieRenderPipelineCore.MoviePipelineGameMode -game -RenderOffScreen -unattended -nosplash -nohmd -windowed -ResX=1920 -ResY=1080 -MoviePipelineConfig="MovieRenderPipeline/QueueManifest.utxt" -abslog="%TEMP%\grudgelap-ue-render.log"
