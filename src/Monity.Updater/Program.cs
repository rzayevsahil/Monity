using System.Diagnostics;
using System.Windows.Forms;
using Monity.Updater;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        var sourceDir = args.Length > 0 ? args[0].Trim('"') : "";
        var targetDir = args.Length > 1 ? args[1].Trim('"') : "";
        var parentPid = args.Length > 2 && int.TryParse(args[2], out var p) ? p : 0;

        if (string.IsNullOrEmpty(sourceDir) || string.IsNullOrEmpty(targetDir) || !Directory.Exists(sourceDir))
        {
            MessageBox.Show("Usage: Updater.exe <sourceDir> <targetDir> [parentPid]", "Monity Updater", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Monity", "updater.log");

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var form = new UpdaterForm(logPath);
        Action<string> log = msg =>
        {
            try { File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {msg}{Environment.NewLine}"); } catch { }
            form.BeginInvoke(() => form.AppendLog(msg));
        };

        form.Shown += (_, _) =>
        {
            Task.Run(() =>
            {
                var ok = RunUpdate(sourceDir, targetDir, parentPid, log);
                form.BeginInvoke(() => form.Finish(ok));
            });
        };

        Application.Run(form);
    }

    static bool RunUpdate(string sourceDir, string targetDir, int parentPid, Action<string> Log)
    {
        try
        {
            Log($"Updater started: source={sourceDir}, target={targetDir}, parentPid={parentPid}");

            if (parentPid > 0)
            {
                Log("Ana uygulama kapatılıyor...");
                try
                {
                    using var parent = Process.GetProcessById(parentPid);
                    parent.WaitForExit(25000);
                }
                catch { /* process zaten kapanmis */ }
            }
            Thread.Sleep(3000);

            Log("Dosyalar kopyalanıyor...");
            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(sourceDir, file);
                var dest = Path.Combine(targetDir, relative);
                var destDir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(destDir))
                    Directory.CreateDirectory(destDir);

                bool copied = false;
                Exception? lastEx = null;
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    try
                    {
                        File.Copy(file, dest, true);
                        copied = true;
                        break;
                    }
                    catch (IOException ex)
                    {
                        lastEx = ex;
                        if (attempt < 11)
                            Thread.Sleep(1500);
                    }
                }
                if (!copied && lastEx != null)
                {
                    Log($"Copy failed: {dest} - {lastEx.Message}");
                    if (string.Equals(Path.GetFileName(dest), "Monity.App.exe", StringComparison.OrdinalIgnoreCase))
                        Log("Monity.App.exe could not be updated – old version may still run.");
                }
            }

            var appExe = Path.Combine(targetDir, "Monity.App.exe");
            if (File.Exists(appExe))
            {
                Log("Uygulama başlatılıyor...");
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = appExe,
                        WorkingDirectory = targetDir,
                        UseShellExecute = true
                    });
                    Log("Update applied successfully; Monity.App.exe started.");
                    return true;
                }
                catch (Exception ex)
                {
                    Log($"Failed to start Monity.App.exe: {ex.Message}");
                    return false;
                }
            }

            Log($"WARNING: Monity.App.exe not found at {appExe}");
            return false;
        }
        catch (Exception ex)
        {
            Log($"ERROR: {ex.Message}");
            return false;
        }
    }
}
