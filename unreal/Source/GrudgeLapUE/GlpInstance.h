#pragma once

#include "CoreMinimal.h"

/**
 * What the game is on this computer besides a process (0.78): the one-copy lock the Godot version held
 * (SingleInstance.cs), which the installer also waits on before it replaces the game's files, and the player's data
 * folder with what the earlier versions kept.
 */
namespace GlpInstance
{
	/** Whether this is the packaged game, with a launcher and a root folder of its own, and not the editor's build. */
	bool Packaged();

	/**
	 * Takes the one-copy lock: the mutex Global\GrudgeLap.SingleInstance, the name the installer knows (GameMutex in
	 * installer/GrudgeLap.iss). False when a copy already runs: that copy has then been asked to come forward and this
	 * one should end. Only a packaged game on a screen takes it; the editor's build and the off-screen automation runs
	 * return true without it (the Godot version's hidden runs skipped it too).
	 */
	bool Claim();

	/** True once after another copy was started and asked this one to come forward. Cheap: asked every frame. */
	bool Summoned();

	/** Puts the game's window in front of the others, restored when it was minimised. */
	void ComeForward();

	/**
	 * The player's data folder: the settings, the high scores, the best laps, the replay music, the updater's log.
	 * %LOCALAPPDATA%\GrudgeLap for the packaged game: outside the game's own folder, so that an update, a reinstall or
	 * a second copy finds it (the previews kept it in the game's Saved folder, which a portable copy took with it or
	 * lost). The project's Saved/GrudgeLap for the editor's build, whose automation runs must not write into the
	 * player's own. -GlpData=<folder> names another.
	 */
	const FString& DataDir();

	/**
	 * The first start with this data folder takes over what the earlier versions kept, without replacing anything that
	 * is already here: the previews' files from the game's Saved/GrudgeLap, and the Godot version's high scores, best
	 * laps and choices (%APPDATA%\Godot\app_userdata\GrudgeLap; the files are the simulation core's own formats in both
	 * versions). The replay tune is read from the Godot version's folder where it lies (GlpAudio). What was taken is
	 * listed in imported.txt, whose presence says that it has been done. The packaged game does it by itself; the
	 * editor's build only with -GlpImport, and -GlpGodotData=<folder> names another folder to read the Godot version's
	 * files from (both for checks).
	 */
	void Import();
}
