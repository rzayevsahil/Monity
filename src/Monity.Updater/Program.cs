using System.Diagnostics;

string sourceDir = args.Length > 0 ? args[0].Trim('"') : "";
string targetDir = args.Length > 1 ? args[1].Trim('"') : "";

if (string.IsNullOrEmpty(sourceDir) || string.IsNullOrEmpty(targetDir) || !Directory.Exists(sourceDir))
{
    Console.WriteLine("Usage: Updater.exe <sourceDir> <targetDir>");
    return 1;
}

try
{
    Thread.Sleep(2000);

    foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
    {
        var relative = Path.GetRelativePath(sourceDir, file);
        var dest = Path.Combine(targetDir, relative);
        var destDir = Path.GetDirectoryName(dest);
        if (!string.IsNullOrEmpty(destDir))
            Directory.CreateDirectory(destDir);
        File.Copy(file, dest, true);
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
    }
}
catch (Exception ex)
{
    Console.WriteLine(ex.Message);
    return 1;
}

return 0;
