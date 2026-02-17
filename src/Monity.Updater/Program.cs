using System.Diagnostics;

string sourceDir = args.Length > 0 ? args[0].Trim('"') : "";
string targetDir = args.Length > 1 ? args[1].Trim('"') : "";
int parentPid = args.Length > 2 && int.TryParse(args[2], out var p) ? p : 0;

if (string.IsNullOrEmpty(sourceDir) || string.IsNullOrEmpty(targetDir) || !Directory.Exists(sourceDir))
{
    Console.WriteLine("Usage: Updater.exe <sourceDir> <targetDir> [parentPid]");
    return 1;
}

var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Monity", "updater.log");
void Log(string msg) { try { File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {msg}{Environment.NewLine}"); } catch { } }

Log($"Updater started: source={sourceDir}, target={targetDir}, parentPid={parentPid}");

try
{
    // Ana uygulama tamamen kapansin diye bekle (process ID verildiyse)
    if (parentPid > 0)
    {
        try
        {
            using var parent = Process.GetProcessById(parentPid);
            parent.WaitForExit(15000);
        }
        catch { /* process zaten kapanmis */ }
    }
    Thread.Sleep(1500);

    foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
    {
        var relative = Path.GetRelativePath(sourceDir, file);
        var dest = Path.Combine(targetDir, relative);
        var destDir = Path.GetDirectoryName(dest);
        if (!string.IsNullOrEmpty(destDir))
            Directory.CreateDirectory(destDir);

        // Dosya kilitliyse birkaç kez dene
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                File.Copy(file, dest, true);
                break;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(800);
            }
        }
    }

    var appExe = Path.Combine(targetDir, "Monity.App.exe");
    if (File.Exists(appExe))
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = appExe,
            WorkingDirectory = targetDir,
            UseShellExecute = true
        });
        Log("Update applied successfully; Monity.App.exe started.");
    }
    else
        Log($"WARNING: Monity.App.exe not found at {appExe}");
}
catch (Exception ex)
{
    Log($"ERROR: {ex.Message}");
    return 1;
}

return 0;
