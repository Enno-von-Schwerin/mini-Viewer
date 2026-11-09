using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace SimpleViewer;

static class Program
{
    [DllImport("kernel32.dll")]
    static extern bool AttachConsole(int processId);

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0].StartsWith("--", StringComparison.Ordinal))
            return RunCommand(args);
        return new Application().Run(new MainWindow(args.Length > 0 ? args[0] : null));
    }

    static int RunCommand(string[] args)
    {
        int code = 0;
        string output;
        try
        {
            output = args[0] switch
            {
                "--install" => Installer.Install(args.Contains("--contextmenu")),
                "--uninstall" => Installer.Uninstall(),
                "--update-readme" => UpdateReadme(args.Length > 1 ? args[1] : "README.md"),
                _ => Usage(ref code),
            };
        }
        catch (Exception ex)
        {
            output = "Fehler: " + ex.Message;
            code = 1;
        }

        // WinExe has no console of its own: borrow the parent's if there is one, otherwise show a message box.
        if (AttachConsole(-1)) Console.WriteLine(Environment.NewLine + output);
        else MessageBox.Show(output, "SimpleViewer");
        return code;
    }

    static string Usage(ref int code)
    {
        code = 1;
        return "Aufruf: SimpleViewer.exe <bild> | --install [--contextmenu] | --uninstall | --update-readme [README.md]";
    }

    // Replaces the block between the key markers in the README with the table from KeyTable.
    static string UpdateReadme(string path)
    {
        const string start = "<!-- keys:start -->", end = "<!-- keys:end -->";
        string text = File.ReadAllText(path);
        int a = text.IndexOf(start, StringComparison.Ordinal), b = text.IndexOf(end, StringComparison.Ordinal);
        if (a < 0 || b < a) throw new InvalidDataException($"Marker {start} / {end} nicht gefunden in {path}");
        text = text[..(a + start.Length)] + "\n" + KeyTable.ToMarkdown() + "\n" + text[b..];
        File.WriteAllText(path, text);
        return $"{path} aktualisiert.";
    }
}
