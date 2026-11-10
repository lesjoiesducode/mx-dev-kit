namespace Loupedeck.MxDevKitPlugin;

using System.Diagnostics;
using System.Text;

internal sealed record ProcessResult(int ExitCode, string Stdout, string Stderr)
{
    public bool Ok => this.ExitCode == 0;
}

// Runs a tool without a shell. Null when it cannot start or runs past its timeout.
internal static class ProcessRunner
{
    public static ProcessResult? Run(string exe, string? cwd, TimeSpan timeout, IEnumerable<string> args, string? stdin = null)
    {
        var argList = args.ToList();
        try
        {
            var psi = new ProcessStartInfo(exe)
            {
                WorkingDirectory = cwd ?? DevKitPaths.Home,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = stdin != null,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (var a in argList)
            {
                psi.ArgumentList.Add(a);
            }

            ToolLocator.PrepareEnvironment(psi.Environment);

            using var p = Process.Start(psi);
            if (p == null)
            {
                return null;
            }

            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            if (stdin != null)
            {
                try
                {
                    p.StandardInput.Write(stdin);
                    p.StandardInput.Close();
                }
                catch (IOException)
                {
                    // The tool exited without reading everything; its exit code tells the rest.
                }
            }

            if (!p.WaitForExit(timeout))
            {
                try
                {
                    p.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }

                PluginLog.Warning($"Timed out after {timeout.TotalSeconds:0}s: {Path.GetFileName(exe)} {string.Join(' ', argList.Take(3))}");
                return null;
            }

            p.WaitForExit();
            return new ProcessResult(p.ExitCode, stdout.Result, stderr.Result);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            PluginLog.Warning($"Could not run {exe}: {e.Message}");
            return null;
        }
    }

    // Starts a program that keeps running on its own (a terminal window): no redirection, no timeout.
    public static bool Spawn(string exe, string cwd, IEnumerable<string> args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe) { WorkingDirectory = cwd, UseShellExecute = false };
            foreach (var a in args)
            {
                psi.ArgumentList.Add(a);
            }

            ToolLocator.PrepareEnvironment(psi.Environment);
            Process.Start(psi)?.Dispose();
            return true;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            PluginLog.Warning($"Could not start {exe}: {e.Message}");
            return false;
        }
    }

    public static void Detach(Action action) => Task.Run(() =>
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            PluginLog.Warning(e, "Background task failed");
        }
    });

    // Opens a URL or a folder with the default app.
    public static void Open(string? target)
    {
        if (string.IsNullOrEmpty(target))
        {
            return;
        }

        try
        {
            var psi = OperatingSystem.IsMacOS()
                ? new ProcessStartInfo("/usr/bin/open", new[] { target }) { UseShellExecute = false }
                : new ProcessStartInfo(target) { UseShellExecute = true };
            Process.Start(psi)?.Dispose();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            PluginLog.Warning($"Could not open {target}: {e.Message}");
        }
    }
}
