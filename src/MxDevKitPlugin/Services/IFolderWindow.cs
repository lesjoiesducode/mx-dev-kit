namespace Loupedeck.MxDevKitPlugin;

// The file manager window the project bubbles follow: the Finder on macOS, File Explorer on Windows.
internal interface IFolderWindow
{
    // Whether it is the active window. Cheap: called at every tick.
    bool IsFront();

    // The folder its front window shows, null when it shows none.
    string? FrontWindowFolder();
}
