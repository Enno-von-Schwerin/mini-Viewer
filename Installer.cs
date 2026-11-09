using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SimpleViewer;

/// <summary>Per-user (HKCU) registration for "Open with". Never sets the default app.</summary>
internal static class Installer
{
    const string AppKey = @"Software\Classes\Applications\SimpleViewer.exe";
    const string ProgId = "SimpleViewer.Image";
    const string ProgIdKey = @"Software\Classes\" + ProgId;
    const string VendorKey = @"Software\SimpleViewer";
    const string CapabilitiesKey = VendorKey + @"\Capabilities";
    const string RegisteredAppsKey = @"Software\RegisteredApplications";
    const string ContextMenuKey = @"Software\Classes\SystemFileAssociations\image\shell\SimpleViewer";
    static readonly string[] Extensions = [".jpg", ".jpeg", ".png"];

    [DllImport("shell32.dll")]
    static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);

    public static string Install(bool contextMenu)
    {
        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("Pfad der exe unbekannt");
        string command = $"\"{exe}\" \"%1\"";
        var cu = Registry.CurrentUser;

        using (var k = cu.CreateSubKey(AppKey))
        {
            k.SetValue("FriendlyAppName", "SimpleViewer");
            using (var c = k.CreateSubKey(@"shell\open\command")) c.SetValue("", command);
            using (var i = k.CreateSubKey("DefaultIcon")) i.SetValue("", $"\"{exe}\",0");
            using var t = k.CreateSubKey("SupportedTypes");
            foreach (var ext in Extensions) t.SetValue(ext, "");
        }

        using (var k = cu.CreateSubKey(ProgIdKey))
        {
            k.SetValue("", "SimpleViewer Bild");
            using (var i = k.CreateSubKey("DefaultIcon")) i.SetValue("", $"\"{exe}\",0");
            using var c = k.CreateSubKey(@"shell\open\command");
            c.SetValue("", command);
        }

        foreach (var ext in Extensions)
        {
            using var k = cu.CreateSubKey($@"Software\Classes\{ext}\OpenWithProgids");
            k.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
        }

        using (var k = cu.CreateSubKey(CapabilitiesKey))
        {
            k.SetValue("ApplicationName", "SimpleViewer");
            k.SetValue("ApplicationDescription", "Minimaler Bildbetrachter für JPG und PNG");
            using var f = k.CreateSubKey("FileAssociations");
            foreach (var ext in Extensions) f.SetValue(ext, ProgId);
        }
        using (var k = cu.CreateSubKey(RegisteredAppsKey)) k.SetValue("SimpleViewer", CapabilitiesKey);

        if (contextMenu)
        {
            using var k = cu.CreateSubKey(ContextMenuKey);
            k.SetValue("", "Mit SimpleViewer öffnen");
            k.SetValue("Icon", $"\"{exe}\",0");
            k.SetValue("AppliesTo", string.Join(" OR ", Extensions.Select(e => "System.FileExtension:=" + e)));
            using var c = k.CreateSubKey("command");
            c.SetValue("", command);
        }

        NotifyShell();
        return $"SimpleViewer registriert ({exe})" + (contextMenu ? " inkl. Kontextmenü." : ".") + "\n" +
               "Hinweis: Als Standard-App muss SimpleViewer einmalig manuell gewählt werden:\n" +
               "  Rechtsklick auf ein Bild > Öffnen mit > Andere App auswählen > SimpleViewer > \"Immer\",\n" +
               "  oder Einstellungen > Apps > Standard-Apps.";
    }

    public static string Uninstall()
    {
        var cu = Registry.CurrentUser;
        cu.DeleteSubKeyTree(AppKey, false);
        cu.DeleteSubKeyTree(ProgIdKey, false);
        cu.DeleteSubKeyTree(VendorKey, false);
        cu.DeleteSubKeyTree(ContextMenuKey, false);
        using (var k = cu.OpenSubKey(RegisteredAppsKey, true)) k?.DeleteValue("SimpleViewer", false);
        foreach (var ext in Extensions)
        {
            using var k = cu.OpenSubKey($@"Software\Classes\{ext}\OpenWithProgids", true);
            k?.DeleteValue(ProgId, false);
        }
        NotifyShell();
        return "SimpleViewer-Registrierung entfernt.";
    }

    static void NotifyShell() => SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, IntPtr.Zero, IntPtr.Zero);
}
