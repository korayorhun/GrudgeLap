using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text;
using GrudgeLap.Game;
using Xunit;

namespace GrudgeLap.Update.Tests;

/// <summary>
/// The hand-over and what it leaves: the installer's command line, the portable copy's unpack script run for real
/// on a folder of its own, the reasons read back from their logs at the next start, the download folder's cleaning
/// and the update log's cap.
/// </summary>
public sealed class UpdateApplyTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("grudgelap-apply-tests-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // Left for the system's temp cleaning.
        }
    }

    [Fact]
    public void Starts_the_installer_silently_with_a_log_and_no_cancel()
    {
        var args = UpdateCommands.InstallerArguments(@"C:\Users\Ayşe Yılmaz\AppData\Local\Temp\GrudgeLap-Update\setup-0.77.0.log", relaunch: true);

        Assert.Equal(@"/SILENT /NORESTART /SP- /NOCANCEL /UPDATE=1 /LOG=""C:\Users\Ayşe Yılmaz\AppData\Local\Temp\GrudgeLap-Update\setup-0.77.0.log""", args);
        Assert.Contains("/UPDATE=2", UpdateCommands.InstallerArguments(@"C:\t\s.log", relaunch: false));
        // A check without a player: no progress window, no message box that waits for a click, no game afterwards.
        Assert.Equal(@"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /NOCANCEL /UPDATE=2 /LOG=""C:\t\s.log""",
            UpdateCommands.InstallerArguments(@"C:\t\s.log", relaunch: false, unattended: true));
        Assert.True(Path.IsPathFullyQualified(UpdateCommands.PowerShell));
        Assert.EndsWith(@"\WindowsPowerShell\v1.0\powershell.exe", UpdateCommands.PowerShell, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("", "\"\"")]
    [InlineData(@"C:\Program Files\GrudgeLap", @"""C:\Program Files\GrudgeLap""")]
    [InlineData(@"C:\Program Files\GrudgeLap\", @"""C:\Program Files\GrudgeLap\\""")]
    [InlineData("say \"hi\"", @"""say \""hi\""""")]
    public void Quotes_arguments_for_the_command_line(string arg, string expected) => Assert.Equal(expected, UpdateCommands.Quote(arg));

    [Fact]
    public void Reads_why_an_install_failed_from_its_log()
    {
        var log = Path.Combine(_folder, "setup-0.77.0.log");
        File.WriteAllLines(log,
        [
            "2026-11-09 01:10:00.100   Log opened. (Time zone: UTC+03:00)",
            "2026-11-09 01:10:00.100   Setup version: Inno Setup version 6.5.4",
            "2026-11-09 01:10:01.000   Waited 2250 ms for the game to exit",
            "2026-11-09 01:10:02.000   Dest filename: C:\\Users\\x\\AppData\\Local\\Programs\\GrudgeLap\\GrudgeLap.pck",
            "2026-11-09 01:10:02.500   The existing file appears to be in use (5). Retrying.",
            "2026-11-09 01:10:07.000   Message box (Abort/Retry/Ignore):",
            "                           C:\\Users\\x\\AppData\\Local\\Programs\\GrudgeLap\\GrudgeLap.pck",
            "",
            "                           An error occurred while trying to replace the existing file:",
            "                           DeleteFile failed; code 5.",
            "                           Access is denied.",
            "2026-11-09 01:10:09.000   User chose Abort.",
            "2026-11-09 01:10:09.000   Rolling back changes.",
            "2026-11-09 01:10:10.000   Deinitializing Setup.",
            "2026-11-09 01:10:10.000   Log closed.",
        ]);

        Assert.Equal("An error occurred while trying to replace the existing file: DeleteFile failed; code 5. Access is denied.", InstallerOutcome.Reason("setup", log));
    }

    [Theory]
    [InlineData("Message box (OK):")]
    [InlineData("Defaulting to OK for suppressed message box (OK):")]
    public void Reads_the_installer_s_own_refusal_for_lack_of_room(string box)
    {
        // GrudgeLap.iss asks for room before it removes the version in place (PrepareToInstall) and stops with these
        // words, shown in a box to a player and only logged in a run without one. The line fits the status whole.
        const string refusal = "GrudgeLap needs 1581 MB free on this drive and has 1200 MB. Nothing was changed: make room and update again.";
        var log = Path.Combine(_folder, "setup-0.79.0.log");
        File.WriteAllLines(log,
        [
            "2026-11-14 19:33:56.000   Log opened. (Time zone: UTC+03:00)",
            "2026-11-14 19:33:56.108   Room in C:\\Users\\x\\AppData\\Local\\Programs\\GrudgeLap: 1200 MB free, 1581 MB needed (0 MB of the version in place counted in)",
            "2026-11-14 19:33:56.108   Not enough room: nothing was changed",
            "2026-11-14 19:33:56.108   PrepareToInstall failed: " + refusal,
            "2026-11-14 19:33:56.108   " + box,
            "                          " + refusal,
            "2026-11-14 19:33:56.120   Deinitializing Setup.",
            "2026-11-14 19:33:56.130   Log closed.",
        ]);

        Assert.Equal(refusal, InstallerOutcome.Reason("setup", log));
        Assert.True(refusal.Length <= UpdateTexts.ReasonLength);
        Assert.Equal("the installer could not prepare (exit code 7)", InstallerOutcome.ExitReason("setup", 7));
    }

    [Fact]
    public void Reads_an_exception_from_an_install_log()
    {
        var log = Path.Combine(_folder, "setup-0.77.0.log");
        File.WriteAllLines(log,
        [
            "2026-11-09 01:10:00.100   Log opened. (Time zone: UTC+03:00)",
            "2026-11-09 01:10:05.000   Fatal exception during installation process (EOutOfResources):",
            "2026-11-09 01:10:05.000   There is not enough space on the disk.",
            "2026-11-09 01:10:05.100   Got EAbort exception.",
            "2026-11-09 01:10:05.200   Rolling back changes.",
        ]);

        Assert.Equal("Fatal exception during installation process (EOutOfResources): There is not enough space on the disk.", InstallerOutcome.Reason("setup", log));
    }

    [Fact]
    public void Tells_an_installer_that_never_ran_from_one_that_finished()
    {
        Assert.Contains("did not run", InstallerOutcome.Reason("setup", Path.Combine(_folder, "missing.log")));
        Assert.Contains("did not run", InstallerOutcome.Reason("setup", null));
        var log = Path.Combine(_folder, "setup-0.77.0.log");
        File.WriteAllLines(log, ["2026-11-09 01:10:00.100   Log opened.", "2026-11-09 01:10:30.000   Installation process succeeded.", "2026-11-09 01:10:31.000   Log closed."]);
        Assert.Equal("the installer finished, but this copy is still the old version", InstallerOutcome.Reason("setup", log));
        File.WriteAllLines(log, ["2026-11-09 01:10:00.100   Log opened.", "2026-11-09 01:10:01.000   Starting the installation process."]);
        Assert.Equal("the installer stopped before the end (last step: Starting the installation process.)", InstallerOutcome.Reason("setup", log));
        Assert.Equal("the installer needs a restart of Windows first", InstallerOutcome.ExitReason("setup", 8));
        Assert.Equal("the installer was cancelled", InstallerOutcome.ExitReason("setup", 5));
        Assert.Equal("the unpack script ended at once (exit code 1)", InstallerOutcome.ExitReason("portable", 1));
    }

    [Fact]
    public void Cleans_the_download_folder_down_to_one_package()
    {
        string[] files =
        [
            "GrudgeLap-Setup-0.69.0.exe", "GrudgeLap-Setup-0.71.0.exe", "GrudgeLap-Setup-0.77.0.exe", "GrudgeLap-Setup-0.77.0.exe.part",
            "GrudgeLap-Portable-0.70.0.zip.part", "GrudgeLap-Setup-0.72.0.exe.sha256", "apply-setup.ps1", "apply.ps1", "apply.log",
            "setup-0.76.0.log", "notes.txt",
        ];
        foreach (var name in files) File.WriteAllText(Path.Combine(_folder, name), name);
        Directory.CreateDirectory(Path.Combine(_folder, "unpack-0123456789abcdef", "data"));

        var deleted = UpdateFolder.Clean(_folder, keep: "GrudgeLap-Setup-0.77.0.exe");

        Assert.Equal(["GrudgeLap-Setup-0.77.0.exe", "GrudgeLap-Setup-0.77.0.exe.part", "apply.log", "notes.txt", "setup-0.76.0.log"],
            Directory.GetFiles(_folder).Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal).ToArray());
        Assert.Empty(Directory.GetDirectories(_folder));
        Assert.Equal(7, deleted.Count);

        // After a confirmed update: no package at all, and the logs go too (their last lines are in update.log by then).
        UpdateFolder.Clean(_folder, keep: null, logsToo: true);
        Assert.Equal(["notes.txt"], Directory.GetFiles(_folder).Select(f => Path.GetFileName(f)).ToArray());
    }

    [Fact]
    public void Clears_the_packages_of_versions_already_running()
    {
        // What the development laptop's folder held on 8 November (three applied installers, 1.45 GB), and more.
        string[] files =
        [
            "GrudgeLap-Setup-0.69.0.exe", "GrudgeLap-Setup-0.72.0.exe", "GrudgeLap-Setup-0.77.0.exe", "GrudgeLap-Portable-0.76.0.zip.part",
            "GrudgeLap-Setup-0.78.0.exe.part", "GrudgeLap-Portable-0.78.0.zip", "apply-setup.ps1", "apply.log", "GrudgeLapUE-Preview-0.70.0.zip",
        ];
        foreach (var name in files) File.WriteAllText(Path.Combine(_folder, name), name);

        var deleted = UpdateFolder.CleanApplied(_folder, new Version(0, 77, 0, 0));

        Assert.Equal(["GrudgeLap-Portable-0.76.0.zip.part", "GrudgeLap-Setup-0.69.0.exe", "GrudgeLap-Setup-0.72.0.exe", "GrudgeLap-Setup-0.77.0.exe", "apply-setup.ps1"],
            deleted.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(["GrudgeLap-Portable-0.78.0.zip", "GrudgeLap-Setup-0.78.0.exe.part", "GrudgeLapUE-Preview-0.70.0.zip", "apply.log"],
            Directory.GetFiles(_folder).Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Keeps_the_update_log_near_its_limit()
    {
        var path = Path.Combine(_folder, "update.log");
        var log = new UpdateLog(path, limit: 4096);
        Parallel.For(0, 400, i => log.Write($"line {i:000} " + new string('x', 40)));

        var lines = File.ReadAllLines(path).Concat(File.ReadAllLines(log.OldPath)).ToList();
        Assert.True(new FileInfo(path).Length <= 4096 + 100);
        Assert.EndsWith("update.old.log", log.OldPath);
        // Nothing lost or torn by eight threads writing at once, within the two files' reach.
        Assert.All(lines, l => Assert.Matches(@"^\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d{3} line \d{3} x{40}$", l));
        Assert.Equal(lines.Count, lines.Distinct().Count());
    }

    [Fact]
    public void Unpacks_a_portable_update_over_the_game_folder()
    {
        if (!OperatingSystem.IsWindows()) return;
        var game = Directory.CreateDirectory(Path.Combine(_folder, "Games and Toys", "GrudgeLap")).FullName;
        File.WriteAllText(Path.Combine(game, "GrudgeLap.exe"), "old exe");
        File.WriteAllText(Path.Combine(game, "GrudgeLap.pck"), "old pck");
        File.WriteAllText(Path.Combine(game, "settings-of-mine.txt"), "kept");
        var downloads = Directory.CreateDirectory(Path.Combine(_folder, "Grudge Lap-Update")).FullName;
        var zip = Path.Combine(downloads, "GrudgeLap-Portable-0.77.0.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            Add(archive, "GrudgeLap.exe", "new exe");
            Add(archive, "GrudgeLap.pck", "new pck");
            Add(archive, "data_GrudgeLap_windows_x86_64/GrudgeLap.dll", "new dll");
        }

        var exitCode = RunUnpackScript(downloads, zip, game);

        Assert.Equal(0, exitCode);
        Assert.Equal("new exe", File.ReadAllText(Path.Combine(game, "GrudgeLap.exe")));
        Assert.Equal("new pck", File.ReadAllText(Path.Combine(game, "GrudgeLap.pck")));
        Assert.Equal("new dll", File.ReadAllText(Path.Combine(game, "data_GrudgeLap_windows_x86_64", "GrudgeLap.dll")));
        Assert.Equal("kept", File.ReadAllText(Path.Combine(game, "settings-of-mine.txt")));
        Assert.False(File.Exists(zip));
        Assert.Empty(Directory.GetDirectories(downloads));
        var applyLog = Path.Combine(downloads, "apply.log");
        Assert.Contains(File.ReadAllLines(applyLog), l => l.EndsWith(" done", StringComparison.Ordinal));
        Assert.Equal("the files were unpacked, but this copy is still the old version", InstallerOutcome.Reason("portable", applyLog));

        // A second run on a zip that is not there: the failure is logged in words the next start shows (PowerShell's
        // own, in the language of Windows: only the method's name is the same everywhere).
        Assert.Equal(0, RunUnpackScript(downloads, zip, game));
        Assert.Contains("ExtractToDirectory", InstallerOutcome.Reason("portable", applyLog));
        Assert.Equal("new exe", File.ReadAllText(Path.Combine(game, "GrudgeLap.exe")));
    }

    /// <summary>
    /// A portable copy as the Unreal package lays it out, in a folder whose name has a space, letters outside ASCII and
    /// the brackets PowerShell reads as a pattern, and the zip of its next version beside a download folder.
    /// </summary>
    private (string Game, string Launcher, string GameExe, string Downloads, string Zip) PortableCopy()
    {
        var game = Directory.CreateDirectory(Path.Combine(_folder, "Oyunlar [yeni] Çağrı", "GrudgeLap")).FullName;
        var launcher = Path.Combine(game, "GrudgeLap.exe");
        var gameExe = Path.Combine(game, "GrudgeLapUE", "Binaries", "Win64", "GrudgeLapUE.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(gameExe)!);
        File.WriteAllText(launcher, "old launcher");
        File.WriteAllText(gameExe, "old game");
        File.WriteAllText(Path.Combine(game, "README.md"), "old readme");
        var downloads = Directory.CreateDirectory(Path.Combine(_folder, "Grudge Lap-Update")).FullName;
        var zip = Path.Combine(downloads, "GrudgeLap-Portable-0.79.0.zip");
        using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
        Add(archive, "GrudgeLap.exe", "new launcher");
        Add(archive, "GrudgeLapUE/Binaries/Win64/GrudgeLapUE.exe", "new game");
        Add(archive, "README.md", "new readme");
        return (game, launcher, gameExe, downloads, zip);
    }

    [Fact]
    public void The_unpack_script_replaces_nothing_while_the_game_runs()
    {
        if (!OperatingSystem.IsWindows()) return;
        var (game, launcher, gameExe, downloads, zip) = PortableCopy();
        var applyLog = Path.Combine(downloads, "apply.log");

        // The player started the game again while the script was unpacking: a program that runs cannot be opened for
        // writing. Here it is the game's own executable, two folders below the root, with the launcher free.
        int exitCode;
        using (new FileStream(gameExe, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            exitCode = RunUnpackScript(downloads, zip, game, closeWait: 2);
        }

        Assert.Equal(0, exitCode);
        // Not one file was touched, the README that sorts first and is free included: no mix of two versions.
        Assert.Equal("old launcher", File.ReadAllText(launcher));
        Assert.Equal("old game", File.ReadAllText(gameExe));
        Assert.Equal("old readme", File.ReadAllText(Path.Combine(game, "README.md")));
        // The verified zip stays for the next try, and what was unpacked beside it is gone.
        Assert.True(File.Exists(zip));
        Assert.Empty(Directory.GetDirectories(downloads));
        Assert.Equal("the game was started again and still runs: nothing was replaced. Close it and ask for the update again",
            InstallerOutcome.Reason("portable", applyLog));
        Assert.False(InstallerOutcome.Unfinished("portable", applyLog));
    }

    [Fact]
    public async Task The_unpack_script_waits_for_a_game_that_is_still_closing()
    {
        if (!OperatingSystem.IsWindows()) return;
        var (game, launcher, gameExe, downloads, zip) = PortableCopy();
        var applyLog = Path.Combine(downloads, "apply.log");

        // The game closes a few seconds after the script has begun (0.78.0 does when it finds the script at work).
        var held = new FileStream(launcher, FileMode.Open, FileAccess.Read, FileShare.Read);
        var closing = Task.Run(async () =>
        {
            // While the script is at work its log has begun and not ended: what a game started now goes by.
            for (var waited = 0; waited < 100 && !File.Exists(applyLog); waited++) await Task.Delay(100);
            var unfinished = InstallerOutcome.Unfinished("portable", applyLog);
            await Task.Delay(3000);
            held.Dispose();
            return unfinished;
        });
        var exitCode = RunUnpackScript(downloads, zip, game, closeWait: 40);

        Assert.True(await closing);
        Assert.Equal(0, exitCode);
        Assert.Equal("new launcher", File.ReadAllText(launcher));
        Assert.Equal("new game", File.ReadAllText(gameExe));
        Assert.Equal("new readme", File.ReadAllText(Path.Combine(game, "README.md")));
        Assert.False(File.Exists(zip));
        Assert.Empty(Directory.GetDirectories(downloads));
        Assert.Contains(File.ReadAllLines(applyLog), l => l.EndsWith(" done", StringComparison.Ordinal));
        Assert.False(InstallerOutcome.Unfinished("portable", applyLog));
    }

    [Fact]
    public void An_installer_s_log_without_its_last_line_is_a_run_that_has_not_ended()
    {
        var log = Path.Combine(_folder, "setup-0.79.0.log");
        // No log: nothing ran, so nothing is at work.
        Assert.False(InstallerOutcome.Unfinished("setup", log));
        Assert.False(InstallerOutcome.Unfinished("portable", log));
        File.WriteAllLines(log, ["2026-11-14 19:14:28.920   Log opened. (Time zone: UTC+03:00)", "2026-11-14 19:14:32.199   Waited 3000 ms for the game to exit"]);
        Assert.True(InstallerOutcome.Unfinished("setup", log));
        File.AppendAllLines(log, ["2026-11-14 19:15:07.539   Installation process succeeded.", "2026-11-14 19:15:07.548   Log closed."]);
        Assert.False(InstallerOutcome.Unfinished("setup", log));
        // The script's log keeps earlier runs: only the last one counts.
        File.WriteAllLines(log, ["2026-11-14 19:10:31 start 0.79.0 from a.zip over C:\\Games (waiting for process 1)", "2026-11-14 19:10:43 FAILED: no room",
            "2026-11-14 19:12:31 start 0.79.0 from a.zip over C:\\Games (waiting for process 2)", "2026-11-14 19:12:41 unpacked 8 entries"]);
        Assert.True(InstallerOutcome.Unfinished("portable", log));
        File.AppendAllLines(log, ["2026-11-14 19:12:43 done"]);
        Assert.False(InstallerOutcome.Unfinished("portable", log));
    }

    private static void Add(ZipArchive archive, string name, string text)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open(), Encoding.UTF8);
        writer.Write(text);
    }

    /// <summary>
    /// The game's own command line for the script, run to its end; -NoRestart as no game is there to start.
    /// <paramref name="closeWait"/> shortens the script's wait for the game's programs to close (three minutes).
    /// </summary>
    private static int RunUnpackScript(string downloads, string zip, string game, int? closeWait = null)
    {
        var script = Path.Combine(downloads, "apply.ps1");
        File.WriteAllText(script, UpdateCommands.UnpackScript);
        // A process that has ended: the script's wait for the game returns at once.
        using var gone = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 0") { CreateNoWindow = true, UseShellExecute = false })!;
        gone.WaitForExit();
        var args = UpdateCommands.UnpackArguments(script, gone.Id, zip, game, Path.Combine(game, "GrudgeLap.exe"), "0.77.0", relaunch: false).ToList();
        if (closeWait is { } seconds) args.AddRange(["-CloseWait", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        using var run = Process.Start(new ProcessStartInfo(UpdateCommands.PowerShell, UpdateCommands.CommandLine(args)) { CreateNoWindow = true, UseShellExecute = false })!;
        Assert.True(run.WaitForExit(120_000), "the unpack script did not finish within two minutes");
        return run.ExitCode;
    }

    [Fact]
    public void Logs_a_proxy_without_its_sign_in()
    {
        var github = new Uri("https://github.com/");

        // A proxy given with its sign-in in its address, as HTTPS_PROXY=http://alice:s3cret@host:port makes .NET's.
        var line = UpdateDiagnostics.Proxy(github, new WebProxy(new Uri("http://alice:s3cret@proxy.example:3128/")));
        var escaped = UpdateDiagnostics.Proxy(github, new FixedProxy(new UriBuilder("http", "10.0.0.1", 8080) { UserName = "alice", Password = Uri.EscapeDataString("p@ss:w/rd") }.Uri));

        Assert.Equal("http://proxy.example:3128 (with a sign-in in its address, not logged)", line);
        Assert.Equal("http://10.0.0.1:8080 (with a sign-in in its address, not logged)", escaped);
        Assert.Equal("http://proxy.example:3128", UpdateDiagnostics.Proxy(github, new WebProxy(new Uri("http://proxy.example:3128/"))));
        Assert.Equal("none (direct)", UpdateDiagnostics.Proxy(github, new WebProxy()));
    }

    [Fact]
    public void Masks_every_sign_in_a_logged_line_quotes()
    {
        Assert.Equal("The proxy tunnel request to proxy 'http://***@127.0.0.1:3128/' failed with status code '407'.",
            UpdateLog.Redact("The proxy tunnel request to proxy 'http://alice:s3cret@127.0.0.1:3128/' failed with status code '407'."));
        Assert.Equal("proxy http://***@host:3128 and https://***@other/path", UpdateLog.Redact("proxy http://alice:p@ss@host:3128 and https://bob@other/path"));
        // Lines without a sign-in stay as they are, an address's '@' further on included.
        foreach (var plain in new[] { "downloading GrudgeLap-Setup-0.77.0.exe from https://github.com/x/y", "mail me@example.com", "https://host/a@b", "" })
        {
            Assert.Equal(plain, UpdateLog.Redact(plain));
        }

        var log = new UpdateLog(Path.Combine(_folder, "update.log"));
        log.Write("proxy for GitHub: http://alice:s3cret@127.0.0.1:3128/");
        var written = File.ReadAllText(log.FilePath);
        Assert.DoesNotContain("s3cret", written);
        Assert.EndsWith(" proxy for GitHub: http://***@127.0.0.1:3128/\n", written);
    }

    [Fact]
    public void Says_what_a_copy_misses_in_plain_words()
    {
        Assert.Equal("v0.77.0 has no installer for this copy - F10 opens its page", UpdateTexts.NoPackage("0.77.0", installed: true));
        Assert.Equal("v0.77.0 has no portable zip for this copy - F10 opens its page", UpdateTexts.NoPackage("0.77.0", installed: false));
    }

    [Fact]
    public void Keeps_the_cause_when_the_game_gives_an_update_up()
    {
        // The reason the game read from a real failed install's log (Reads_why_an_install_failed_from_its_log).
        const string reason = "An error occurred while trying to replace the existing file: DeleteFile failed; code 5. Access is denied.";

        Assert.Equal($"v0.77.0 did not install twice ({reason}) - F10 opens its download page", UpdateTexts.GaveUp("0.77.0", reason));
        Assert.Equal("v0.77.0 did not install twice - F10 opens its download page", UpdateTexts.GaveUp("0.77.0", ""));
        // Longer than a line can hold: the end, where Windows and installers put the cause, is what stays.
        var longer = "the installer ended at once (exit code 4): " + reason;
        var fitted = UpdateTexts.Fit(longer);
        Assert.Equal(UpdateTexts.ReasonLength, fitted.Length);
        Assert.StartsWith("...", fitted);
        Assert.EndsWith("DeleteFile failed; code 5. Access is denied.", fitted);
        Assert.Equal(reason, UpdateTexts.Fit(reason));
    }

    [Fact]
    public void Names_the_antivirus_that_took_a_file()
    {
        const string setup = "GrudgeLap-Setup-0.77.0.exe";
        var virus = new IOException("Operation did not complete successfully because the file contains a virus or potentially unwanted software.", unchecked((int)0x800700E1));
        var sharing = new IOException("The process cannot access the file because it is being used by another process.", unchecked((int)0x80070020));

        Assert.True(FileBlock.IsThreat(virus));
        Assert.True(FileBlock.IsThreat(new Win32Exception(225)));
        Assert.False(FileBlock.IsThreat(sharing));
        Assert.True(FileBlock.IsHeld(sharing));
        Assert.True(FileBlock.IsHeld(new UnauthorizedAccessException()));
        Assert.False(FileBlock.IsHeld(new IOException("disk full", unchecked((int)0x80070070))));
        Assert.False(FileBlock.IsHeld(new FileNotFoundException()));

        Assert.Equal("Microsoft Defender Antivirus blocked the downloaded installer as a threat", FileBlock.Threat(setup, "Microsoft Defender Antivirus"));
        Assert.Equal("the downloaded zip vanished as soon as it was saved: an antivirus most likely removed it", FileBlock.Vanished("GrudgeLap-Portable-0.77.0.zip", null));
        Assert.Equal("Microsoft Defender Antivirus blocked the installer as a threat", UpdateTexts.StartFailure(new Win32Exception(225), setup, "Microsoft Defender Antivirus"));
        Assert.Equal("the installer was gone when it was to start: an antivirus most likely removed it", UpdateTexts.StartFailure(new Win32Exception(2), setup, null));
        Assert.Equal("the installer could not start (Access is denied)", UpdateTexts.StartFailure(new Win32Exception(5, "Access is denied."), setup, null));
        Assert.Equal("update failed: Microsoft Defender Antivirus blocked the downloaded installer as a threat - F10 opens its page",
            UpdateTexts.Blocked(FileBlock.Threat(setup, "Microsoft Defender Antivirus")));
    }

    /// <summary>A proxy that answers one address for everything, as .NET's HTTPS_PROXY one does.</summary>
    private sealed class FixedProxy(Uri address) : IWebProxy
    {
        public ICredentials? Credentials { get; set; }
        public Uri GetProxy(Uri destination) => address;
        public bool IsBypassed(Uri host) => false;
    }

    [Fact]
    public void Names_the_antivirus_states_of_security_center()
    {
        Assert.Equal("on", UpdateDiagnostics.StateText(0x041000));
        Assert.Equal("passive", UpdateDiagnostics.StateText(0x060100));
        Assert.Equal("off", UpdateDiagnostics.StateText(0x040000));
        Assert.Equal("state unknown", UpdateDiagnostics.StateText(-1));
        Assert.False(string.IsNullOrWhiteSpace(UpdateDiagnostics.Antivirus()));
        Assert.Contains("GB free", UpdateDiagnostics.FreeSpace(Path.GetTempPath()));
        Assert.True(UpdateDiagnostics.CanWrite(_folder));
        Assert.False(UpdateDiagnostics.CanWrite(Path.Combine(_folder, "no such folder")));
    }
}
