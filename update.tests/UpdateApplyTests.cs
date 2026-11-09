using System.Diagnostics;
using System.IO.Compression;
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

    private static void Add(ZipArchive archive, string name, string text)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open(), Encoding.UTF8);
        writer.Write(text);
    }

    /// <summary>The game's own command line for the script, run to its end; -NoRestart as no game is there to start.</summary>
    private static int RunUnpackScript(string downloads, string zip, string game)
    {
        var script = Path.Combine(downloads, "apply.ps1");
        File.WriteAllText(script, UpdateCommands.UnpackScript);
        // A process that has ended: the script's wait for the game returns at once.
        using var gone = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 0") { CreateNoWindow = true, UseShellExecute = false })!;
        gone.WaitForExit();
        var args = UpdateCommands.UnpackArguments(script, gone.Id, zip, game, Path.Combine(game, "GrudgeLap.exe"), "0.77.0", relaunch: false);
        using var run = Process.Start(new ProcessStartInfo(UpdateCommands.PowerShell, UpdateCommands.CommandLine(args)) { CreateNoWindow = true, UseShellExecute = false })!;
        Assert.True(run.WaitForExit(60_000), "the unpack script did not finish within a minute");
        return run.ExitCode;
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
