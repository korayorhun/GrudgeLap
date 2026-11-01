@echo off
rem Builds /Game/Maps/Feasibility and the render queue manifest (build_scene.py) in the editor commandlet: no window.
rem The export is cleaned (clean_glb.py), the meadow scattered (prepare_scatter.py) and the foliage prepared first.
rem The previous run's level, imported track, materials and sequences go first: the editor cannot delete an asset
rem and create a new one at the same path in one session. Imported Poly Haven meshes and textures are kept (their
rem Nanite builds take minutes) and reused.
if not defined UE_ROOT set UE_ROOT=C:\UE\5.8
set CONTENT=%~dp0..\Content
if exist "%CONTENT%\Maps\Feasibility.umap" del /q "%CONTENT%\Maps\Feasibility.umap"
for %%d in (Track Cars GrudgeLap\Materials GrudgeLap\Render) do if exist "%CONTENT%\%%d" rmdir /s /q "%CONTENT%\%%d"
python "%~dp0clean_glb.py" "%~dp0..\Import\track.glb" "%~dp0..\Import\track_ue.glb" || exit /b 1
rem A real car model (Import\Models, fetch_models.py) in place of the lofted body where models.json names one (prepare_car_model.py).
python "%~dp0prepare_car_model.py" "%~dp0..\Import\track_ue.glb" "%~dp0..\Import\track_ue.glb" flat12 || exit /b 1
rem The other cars come from the game's exports with --car=<name> (the everyday cars by index: 5, 6, 7); only their
rem car is kept (extract_car.py), then its model, if any.
for %%c in (v8 hothatch five turbohatch saloon estate fleet) do (
    if exist "%~dp0..\Import\track_%%c.glb" (
        python "%~dp0clean_glb.py" "%~dp0..\Import\track_%%c.glb" "%~dp0..\Import\track_%%c_ue.glb" || exit /b 1
        python "%~dp0extract_car.py" "%~dp0..\Import\track_%%c_ue.glb" "%~dp0..\Import\car_%%c.glb" PlayerCar || exit /b 1
        python "%~dp0prepare_car_model.py" "%~dp0..\Import\car_%%c.glb" "%~dp0..\Import\car_%%c.glb" %%c || exit /b 1
    )
)
python "%~dp0make_decals.py" "%~dp0..\Import\Decals" || exit /b 1
python "%~dp0prepare_scatter.py" "%~dp0..\Import\track.glb" "%~dp0..\Import\track.json" "%~dp0..\Import\scatter.json" || exit /b 1
python "%~dp0prepare_foliage.py" "%~dp0..\Import\PolyHaven" || exit /b 1
if not defined BUILD_LOG set BUILD_LOG=%TEMP%\grudgelap-ue-build-scene.log
"%UE_ROOT%\Engine\Binaries\Win64\UnrealEditor-Cmd.exe" "%~dp0..\GrudgeLapUE.uproject" -run=pythonscript -script="%~dp0build_scene.py" -unattended -nosplash -nop4 -NoSound -abslog="%BUILD_LOG%"
