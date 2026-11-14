/*
 * C interface of GrudgeLap.Update.Native.dll, the game's self-updater built with NativeAOT: the release feed on
 * GitHub, a resumable download checked against its SHA-256, and the hand-over to the installer (an installed copy)
 * or to the unpack script (a portable copy). glp_update_state below mirrors NativeUpdateState in
 * update.native/Exports.cs field for field; change both together and bump GLP_UPDATE_ABI, which glp_update_abi()
 * returns so a caller can refuse a mismatched library.
 *
 * Nothing is fetched unless the player asks: glp_update_init contacts nobody, glp_update_check only reads the
 * release's description, and only glp_update_download downloads a package. The work runs on the library's own
 * threads; the host calls every function from one thread (its game thread) and glp_update_poll every frame, which
 * moves the updater on and never blocks.
 *
 * Texts are UTF-8. Functions return 0 on success and -1 on failure (glp_update_last_error describes it), unless
 * noted; glp_update_check, glp_update_download and glp_update_apply also return 1 when the request is not possible
 * now (a download already runs, nothing is ready) and nothing changed.
 */
#pragma once

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define GLP_UPDATE_ABI 1

/*
 * glp_update_state.phase, and what the host does in each. In every phase it shows `status` when that is not empty.
 */
enum
{
    /* Nothing asked for yet. Offer "check for updates". A status here tells how the update handed over in the
     * last session ended ("updated to v0.79.0", or why it did not install): worth showing on the title screen. */
    GLP_UPDATE_IDLE = 0,
    /* The check runs, or waits to be tried again after a lost connection (the status then says when: "... - trying
     * again in 30 s"). Offer "cancel" (glp_update_cancel). */
    GLP_UPDATE_CHECKING = 1,
    /* This copy is the newest release. Offer "check again". */
    GLP_UPDATE_UP_TO_DATE = 2,
    /* A newer release exists: `latest` names it, `package_size` is its download. Nothing has been downloaded. Offer
     * "download and install" (glp_update_download); leaving it is fine, the player can come back to it. */
    GLP_UPDATE_AVAILABLE = 3,
    /* The download runs (bytes_done of bytes_total; 0 total until the server has answered), or waits to be tried
     * again after a lost connection. The player may drive meanwhile. Offer "cancel": what came stays on disk and a
     * later glp_update_download goes on from there. */
    GLP_UPDATE_DOWNLOADING = 4,
    /* The downloaded file's SHA-256 is being checked (bytes_done of bytes_total). As GLP_UPDATE_DOWNLOADING. */
    GLP_UPDATE_VERIFYING = 5,
    /* Downloaded and verified. Call glp_update_apply once the player is on the menu (not in the middle of a drive):
     * it starts the installer, after which the game has a few seconds to quit. */
    GLP_UPDATE_READY = 6,
    /* The installer (or the unpack script) has been started and is watched for a moment: one that an antivirus
     * stops at once ends in GLP_UPDATE_FAILED and the game stays. Keep polling; offer nothing. */
    GLP_UPDATE_STARTING = 7,
    /* The installer runs and waits for this process to exit. QUIT NOW, through the game's normal shutdown (save,
     * release the one-copy lock): it replaces the game's files and starts the launcher again. An installed copy
     * that has not quit after a minute is closed by the installer; a portable copy's script copies nothing while
     * one of the game's programs runs, and gives up after three minutes with nothing replaced.
     * A session can also START in this phase: the last session's installer or script is still at work and the player
     * started the game again too early. Quit in the same way; whatever is at work starts the new version. */
    GLP_UPDATE_QUIT_NOW = 8,
    /* The last step failed and `status` says why. `can_retry` names the call that tries again; with `has_page`
     * set and nothing to try again the updater can do no more by itself and the host offers to open `page` in the
     * browser instead. At a start after a version did not install twice both are given: the call is the check (a
     * newer release may have come since; one that finds the same version again ends at its page). */
    GLP_UPDATE_FAILED = 9
};

/*
 * glp_update_state.can_retry: zero when there is nothing to try again, otherwise the call that tries again now.
 * It is set in GLP_UPDATE_FAILED, and also while a check or a download waits to try again by itself, where the
 * call starts the next try at once.
 */
enum
{
    GLP_UPDATE_RETRY_NONE = 0,
    GLP_UPDATE_RETRY_CHECK = 1,    /* glp_update_check */
    GLP_UPDATE_RETRY_DOWNLOAD = 2, /* glp_update_download: goes on from where the download stopped */
    GLP_UPDATE_RETRY_APPLY = 3     /* glp_update_apply: the verified package is still there */
};

typedef struct glp_update_state
{
    int32_t phase;          /* GLP_UPDATE_IDLE .. GLP_UPDATE_FAILED */
    int32_t installed_copy; /* non-zero: put in place by the installer, which then updates it; zero: a portable copy, updated from the zip */
    int32_t can_retry;      /* GLP_UPDATE_RETRY_* */
    int32_t has_page;       /* non-zero: `page` holds the release's page for the player to open */
    int64_t bytes_done;     /* of the running download or verification */
    int64_t bytes_total;    /* 0 while unknown */
    int64_t package_size;   /* bytes of the available release's package for this copy; 0 when unknown or none */
    char latest[32];        /* the version of the release the last check found ("0.79.0"); empty before one, and when up to date */
    char status[256];       /* one line for the menu, without key hints: what the updater is doing, offering or failed at */
    char page[256];         /* the release's page (https://github.com/.../releases/tag/v0.79.0) when has_page, else empty */
} glp_update_state;

int32_t glp_update_abi(void);
/*
 * Starts the updater; call it once, when the game starts. version: this copy's, "0.78.0". launcher_exe: the
 * executable a player starts the game with (the small launcher in the root folder, not the engine's executable
 * under Binaries\Win64): a portable copy's update starts it again. root_dir: the folder the whole game lives under,
 * which is what the installer records and where a portable zip is unpacked. data_dir: the player's data folder,
 * where update.log and update-state.ini are kept. All paths absolute.
 *
 * It contacts nobody. It writes the log's first lines, judges the update the last session handed over (see
 * GLP_UPDATE_IDLE) and clears the download folder (%TEMP%\GrudgeLap-Update) of packages no longer needed.
 * Fails when version is not a version or a path is empty; the other functions then return -1.
 */
int32_t glp_update_init(const char* version, const char* launcher_exe, const char* root_dir, const char* data_dir);
/* Looks for a newer release: GLP_UPDATE_CHECKING, then GLP_UPDATE_UP_TO_DATE, GLP_UPDATE_AVAILABLE or GLP_UPDATE_FAILED. */
int32_t glp_update_check(void);
/* Downloads and verifies the available release's package for this copy: GLP_UPDATE_DOWNLOADING, GLP_UPDATE_VERIFYING,
 * then GLP_UPDATE_READY or GLP_UPDATE_FAILED. A package downloaded and verified earlier is not fetched again. */
int32_t glp_update_download(void);
/* Starts the installer or the unpack script on the verified package: GLP_UPDATE_STARTING, then GLP_UPDATE_QUIT_NOW
 * or GLP_UPDATE_FAILED. Possible in GLP_UPDATE_READY, and in GLP_UPDATE_FAILED with GLP_UPDATE_RETRY_APPLY. */
int32_t glp_update_apply(void);
/* Stops the check or the download that runs or waits (the player backed out), without waiting for it; the phase
 * goes back to GLP_UPDATE_AVAILABLE or GLP_UPDATE_IDLE. Does nothing once the installer has been started. */
void glp_update_cancel(void);
/* The game is closing: stops whatever runs, without waiting for it. Also call it on the way out of GLP_UPDATE_QUIT_NOW. */
void glp_update_shutdown(void);
/* Moves the updater on and fills state (which may be null); call it every frame. */
int32_t glp_update_poll(glp_update_state* state);
/* The last failure or refusal, cut to size - 1 bytes and terminated; returns its full length in bytes. */
int32_t glp_update_last_error(char* buffer, int32_t size);

#ifdef __cplusplus
}
#endif
