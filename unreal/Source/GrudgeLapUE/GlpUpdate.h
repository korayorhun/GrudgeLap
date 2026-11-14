#pragma once

#include "CoreMinimal.h"
#include "grudgelap_update.h"

/**
 * The game's updater (0.78): GrudgeLap.Update.Native.dll, the Godot version's updater behind a C interface
 * (update.native, built with NativeAOT), loaded at run time like the simulation core. Nothing is fetched unless the
 * player asks (the owner's rule of 14 November 2026): Check reads the release's description, Download fetches the
 * package for this copy and checks its SHA-256, Apply hands it to the installer (an installed copy) or to the unpack
 * script (a portable one), after which Phase() tells the game to quit so that its files can be replaced. Poll every
 * frame: the work runs on the library's own threads and Poll never waits.
 *
 * Only the packaged game updates itself: it has a launcher (GrudgeLap.exe in the root folder) and a root folder that
 * is its own. The editor's build has neither; for checks, -GlpUpdateRoot=<folder> names a root for it (with
 * <folder>/GrudgeLap.exe as the launcher). The library's own hooks work in both: GRUDGELAP_UPDATE_FEED (a stand-in
 * for GitHub), GRUDGELAP_UPDATE_DRYRUN (the check only) and GRUDGELAP_FAKE_VERSION.
 */
class FGlpUpdate
{
public:
	/**
	 * Loads the library and starts its session: Version is this copy's ("0.78.0"), DataDir where update.log and
	 * update-state.ini are kept. False, with Why() for the menu, when this copy cannot update itself. It contacts
	 * nobody; State().status may tell how the update handed over in the last session ended.
	 */
	bool Start(const FString& Version, const FString& DataDir);

	/** The game is closing: whatever runs stops without being waited for. */
	void Shutdown();

	/** Moves the updater on and reads where it stands; every frame. */
	void Poll();

	bool Running() const { return bRunning; }
	const FString& Why() const { return Reason; }
	/** GLP_UPDATE_IDLE .. GLP_UPDATE_FAILED; GLP_UPDATE_IDLE when it does not run. */
	int32 Phase() const { return Current.phase; }
	const glp_update_state& State() const { return Current; }
	/** One line for the menu: what the updater is doing, offering or failed at. Empty when there is nothing to say. */
	const FString& Status() const { return StatusText; }
	/** The newer release's version ("0.79.0"), empty when none is known. */
	const FString& Latest() const { return LatestText; }
	/** The release's page, when opening it is all that is left to do. */
	const FString& Page() const { return PageText; }

	/** Each is true when the request was taken; otherwise Refusal() says what was in its way. */
	bool Check();
	bool Download();
	bool Apply();
	void Cancel();
	const FString& Refusal() const { return RefusalText; }

private:
	bool Load();
	bool Request(int32 (*Function)(), const TCHAR* What);
	FString LastError() const;

	bool bRunning = false;
	FString Reason;
	FString RefusalText;
	glp_update_state Current{};
	FString StatusText;
	FString LatestText;
	FString PageText;

	// The library, loaded once and kept for the process: the NativeAOT runtime in it cannot be unloaded.
	static void* Handle;
	static int32 (*Abi)();
	static int32 (*Init)(const char*, const char*, const char*, const char*);
	static int32 (*CheckNow)();
	static int32 (*DownloadNow)();
	static int32 (*ApplyNow)();
	static void (*CancelNow)();
	static void (*ShutdownNow)();
	static int32 (*PollNow)(glp_update_state*);
	static int32 (*LastErrorText)(char*, int32);
};
