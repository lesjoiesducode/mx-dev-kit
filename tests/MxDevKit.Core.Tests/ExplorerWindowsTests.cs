namespace MxDevKit.Tests;

public class ExplorerWindowsTests
{
    [Fact]
    public void A_window_with_one_tab_gives_its_folder()
    {
        var tabs = new (string?, string?)[] { (@"C:\src\api", "api") };

        Assert.Equal(@"C:\src\api", ExplorerWindows.Pick(tabs, "api - File Explorer"));
        Assert.Equal(@"C:\src\api", ExplorerWindows.Pick(tabs, null));
    }

    [Fact]
    public void The_title_picks_the_active_tab()
    {
        var tabs = new (string?, string?)[] { (@"C:\src\api", "api"), (@"C:\src\api-legacy", "api-legacy"), (@"C:\Users\me\Downloads", "Downloads") };

        Assert.Equal(@"C:\src\api-legacy", ExplorerWindows.Pick(tabs, "api-legacy and 2 more tabs - File Explorer"));
        Assert.Equal(@"C:\src\api", ExplorerWindows.Pick(tabs, "api and 2 more tabs - File Explorer"));
        Assert.Equal(@"C:\Users\me\Downloads", ExplorerWindows.Pick(tabs, "Downloads"));
    }

    [Fact]
    public void No_folder_when_the_tabs_cannot_be_told_apart()
    {
        var sameName = new (string?, string?)[] { (@"C:\a\api", "api"), (@"C:\b\api", "api") };
        var unknown = new (string?, string?)[] { (@"C:\src\api", "api"), (@"C:\src\web", "web") };

        Assert.Null(ExplorerWindows.Pick(sameName, "api - File Explorer"));
        Assert.Null(ExplorerWindows.Pick(unknown, "Home - File Explorer"));
        Assert.Null(ExplorerWindows.Pick(unknown, null));
    }

    [Fact]
    public void A_tab_without_a_folder_gives_none()
    {
        var tabs = new (string?, string?)[] { (null, "This PC"), (null, "Home"), (@"C:\src\api", "api") };

        Assert.Null(ExplorerWindows.Pick(tabs, "This PC and 2 more tabs - File Explorer"));
        Assert.Equal(@"C:\src\api", ExplorerWindows.Pick(tabs, "api and 2 more tabs - File Explorer"));
        Assert.Null(ExplorerWindows.Pick(new (string?, string?)[] { (null, "This PC") }, null));
    }

    [Fact]
    public void The_longest_name_that_starts_the_title_wins()
    {
        var tabs = new (string?, string?)[] { (@"C:\My", "My"), (@"C:\src\My Project", "My Project") };

        Assert.Equal(@"C:\src\My Project", ExplorerWindows.Pick(tabs, "My Project and 1 more tab - File Explorer"));
        Assert.Equal(@"C:\My", ExplorerWindows.Pick(tabs, "My - File Explorer"));
    }

    [Fact]
    public void Two_tabs_on_the_same_folder_give_that_folder()
    {
        var tabs = new (string?, string?)[] { (@"C:\src\api", "api"), (@"c:\src\API", "API") };

        Assert.Equal(@"C:\src\api", ExplorerWindows.Pick(tabs, "Home"));
    }
}
