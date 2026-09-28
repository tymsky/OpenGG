using OpenGG.Core.Original;

namespace OpenGG.Core.Tests;

/// <summary>Finding the game's folder from the one a player picked (the sign-in's GAME FOLDER…).</summary>
public class GameFolderTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "opengg-folder-" + Guid.NewGuid().ToString("N"));

    public GameFolderTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); }
        catch (IOException) { }
    }

    string Game(params string[] parts)
    {
        var dir = Path.Combine([root, .. parts]);
        Directory.CreateDirectory(Path.Combine(dir, "Data", "Cars"));
        File.WriteAllBytes(Path.Combine(dir, "Data", "Cars", "car.car"), [0]);
        return dir;
    }

    [Fact]
    public void TheGameFolderItself() => Assert.Equal(Game("GG"), OriginalGame.FindGameFolder(Path.Combine(root, "GG")));

    [Fact]
    public void DataOrItsCarsFolderPickedLeadsBackUp()
    {
        var game = Game("GG");
        Assert.Equal(game, OriginalGame.FindGameFolder(Path.Combine(game, "Data")));
        Assert.Equal(game, OriginalGame.FindGameFolder(Path.Combine(game, "Data", "Cars")));
    }

    [Fact]
    public void TheOnlyCopyOneOrTwoFoldersDown()
    {
        var game = Game("HeadGames", "Gearhead Garage");
        Assert.Equal(game, OriginalGame.FindGameFolder(Path.Combine(root, "HeadGames")));
        Assert.Equal(game, OriginalGame.FindGameFolder(root));
    }

    [Fact]
    public void TwoCopiesOrNoneGiveNothing()
    {
        Assert.Null(OriginalGame.FindGameFolder(root));
        Game("A");
        Game("B");
        Assert.Null(OriginalGame.FindGameFolder(root));
        Assert.Null(OriginalGame.FindGameFolder(Path.Combine(root, "missing")));
    }

    [Fact]
    public void AFolderWithoutCarsIsNoGame()
    {
        Directory.CreateDirectory(Path.Combine(root, "X", "Data", "Cars"));
        Assert.False(OriginalGame.IsGameFolder(Path.Combine(root, "X")));
        Assert.Null(OriginalGame.FindGameFolder(Path.Combine(root, "X")));
    }
}
