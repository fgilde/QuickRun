using QuickRun.App.Daemon;
using QuickRun.Core.Tests;

namespace QuickRun.App.Tests;

public class PairingTests
{
    private static Pairing At(TempHome home, DateTimeOffset now) => new(home.Path, () => now);

    [Fact]
    public void No_token_is_handed_out_without_an_open_window()
    {
        using var home = new TempHome();
        Assert.Null(new Pairing(home.Path).Claim());
    }

    [Fact]
    public void An_open_window_hands_out_a_token()
    {
        using var home = new TempHome();
        var pairing = new Pairing(home.Path);

        pairing.OpenWindow();

        Assert.NotNull(pairing.Claim());
    }

    [Fact]
    public void Claiming_closes_the_window()
    {
        using var home = new TempHome();
        var pairing = new Pairing(home.Path);
        pairing.OpenWindow();

        Assert.NotNull(pairing.Claim());
        Assert.False(pairing.WindowOpen);
        Assert.Null(pairing.Claim());
    }

    [Fact]
    public void A_window_expires()
    {
        using var home = new TempHome();
        var opened = DateTimeOffset.UtcNow;

        At(home, opened).OpenWindow();

        var later = At(home, opened + Pairing.WindowLength + TimeSpan.FromSeconds(1));
        Assert.False(later.WindowOpen);
        Assert.Null(later.Claim());
    }

    [Fact]
    public void A_token_is_long_and_hex()
    {
        using var home = new TempHome();
        var pairing = new Pairing(home.Path);
        pairing.OpenWindow();

        var token = pairing.Claim()!;

        Assert.Equal(64, token.Length);
        Assert.Matches("^[0-9a-f]+$", token);
    }

    [Fact]
    public void The_issued_token_validates_and_others_do_not()
    {
        using var home = new TempHome();
        var pairing = new Pairing(home.Path);
        pairing.OpenWindow();
        var token = pairing.Claim()!;

        Assert.True(pairing.IsValid(token));
        Assert.False(pairing.IsValid("wrong"));
        Assert.False(pairing.IsValid(null));
        Assert.False(pairing.IsValid(""));
        Assert.False(pairing.IsValid(token + "a"));
    }

    /// <summary>
    /// The window lives in a file precisely so that `quickrun pair` in one process can open it for
    /// a daemon running in another.
    /// </summary>
    [Fact]
    public void A_window_opened_in_one_instance_is_visible_to_another()
    {
        using var home = new TempHome();

        new Pairing(home.Path).OpenWindow();

        Assert.True(new Pairing(home.Path).WindowOpen);
        Assert.NotNull(new Pairing(home.Path).Claim());
    }

    [Fact]
    public void A_token_survives_a_restart()
    {
        using var home = new TempHome();
        var first = new Pairing(home.Path);
        first.OpenWindow();
        var token = first.Claim()!;

        Assert.True(new Pairing(home.Path).IsValid(token));
    }

    [Fact]
    public void Pairing_again_keeps_the_same_token()
    {
        using var home = new TempHome();
        var pairing = new Pairing(home.Path);

        pairing.OpenWindow();
        var first = pairing.Claim();
        pairing.OpenWindow();
        var second = pairing.Claim();

        Assert.Equal(first, second);
    }

    [Fact]
    public void Revoking_invalidates_the_token()
    {
        using var home = new TempHome();
        var pairing = new Pairing(home.Path);
        pairing.OpenWindow();
        var token = pairing.Claim()!;

        pairing.Reset();

        Assert.False(pairing.IsValid(token));
        Assert.False(pairing.WindowOpen);
    }

    [Fact]
    public void A_revoked_pairing_issues_a_different_token()
    {
        using var home = new TempHome();
        var pairing = new Pairing(home.Path);
        pairing.OpenWindow();
        var first = pairing.Claim()!;

        pairing.Reset();
        pairing.OpenWindow();

        Assert.NotEqual(first, pairing.Claim());
    }
}
