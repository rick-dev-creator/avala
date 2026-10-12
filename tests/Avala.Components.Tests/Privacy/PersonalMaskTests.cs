using Avala.Components.Privacy;

namespace Avala.Components.Tests.Privacy;

public sealed class PersonalMaskTests
{
    [Theory]
    [InlineData("rick.martinez@gmail.com", "r•••••@g••••.com")]
    [InlineData("dana@mail.example.co.uk", "d•••••@m••••.uk")]
    public void AnEmailKeepsItsFirstLetterTheFirstLetterOfItsDomainAndItsEnding(string email, string masked) =>
        Assert.Equal(masked, Personal.Email(email).Masked);

    [Theory]
    [InlineData("octocat", "oc•••••")]
    [InlineData("ab", "a•••••")]
    public void ALoginKeepsAtMostItsFirstTwoLetters(string login, string masked) =>
        Assert.Equal(masked, Personal.Login(login).Masked);

    [Fact]
    public void AnAccountNameKeepsTheInitialOfEachWordAndItsPunctuation() =>
        Assert.Equal("D••••• M••••• (c•••••)", Personal.AccountName("Dana Martinez (claude-work)").Masked);

    [Fact]
    public void AnOrganizationIdKeepsItsFirstFourCharacters() =>
        Assert.Equal("3f2a•••••", Personal.OrganizationId("3f2a9c1e-1d2b-4c3d-8e4f-5a6b7c8d9e0f").Masked);

    [Theory]
    [InlineData("/home/dana/Projects/avala", "~/Projects/avala")]
    [InlineData("/Users/dana/.claude-work", "~/.claude-work")]
    [InlineData(@"C:\Users\dana\source\avala", @"~\source\avala")]
    [InlineData("/home/dana", "~")]
    public void AHomeFolderPathShowsTheTildeInsteadOfTheUserName(string path, string masked) =>
        Assert.Equal(masked, Personal.Path(path).Masked);

    [Fact]
    public void APathOutsideAnyHomeFolderIsShownWhole() =>
        Assert.Equal("/srv/repositories/avala", Personal.Path("/srv/repositories/avala").Masked);

    [Fact]
    public void ThePersonsOwnHomeFolderShowsTheTildeWhereverItIs() =>
        Assert.Equal("~/notes", Personal.Path(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "notes")).Masked.Replace('\\', '/'));

    [Theory]
    [InlineData("dana@example.com", PersonalKind.Email)]
    [InlineData("3f2a9c1e-1d2b-4c3d-8e4f-5a6b7c8d9e0f", PersonalKind.OrganizationId)]
    [InlineData("Dana Martinez", PersonalKind.AccountName)]
    public void AnAccountLabelIsMaskedAsAnEmailAnOrganizationIdOrAName(string label, PersonalKind kind) =>
        Assert.Equal(kind, Personal.Account(label).Kind);

    [Fact]
    public void ARevealedDetailShowsItsValueAndAHiddenOneItsMask() =>
        Assert.Equal(("oc•••••", "octocat", "oc•••••"), (Personal.Login("octocat").Show(hidden: true), Personal.Login("octocat").Show(hidden: false), Personal.Login("octocat").ToString()));

    [Fact]
    public void AnEmptyDetailStaysEmpty() =>
        Assert.Equal(string.Empty, Personal.Email(string.Empty).Masked);

    [Fact]
    public void PlainTextIsNeverMasked() =>
        Assert.Equal("claude-work", Personal.Plain("claude-work").Masked);

    [Fact]
    public void APhraseMasksOnlyItsPersonalParts()
    {
        var phrase = new PersonalPhrase("Signed in as ", Personal.Login("octocat"), " on github.com");

        Assert.Equal(("Signed in as oc••••• on github.com", "Signed in as octocat on github.com"), (phrase.Show(hidden: true), phrase.Show(hidden: false)));
    }

    [Fact]
    public void ScrubbingATextMasksEveryEmailAndEveryHomeFolder() =>
        Assert.Equal(
            "Read ~/Projects/avala/README.md for r•••••@g••••.com and d•••••@e••••.com, then ~/notes",
            PersonalMask.Scrub("Read /home/dana/Projects/avala/README.md for rick@gmail.com and dana@example.com, then /Users/sam/notes"));

    [Fact]
    public void ScrubbingLeavesPackageVersionsThatOnlyLookLikeEmails() =>
        Assert.Equal("pnpm add -D @testing-library/user-event@14.5.2", PersonalMask.Scrub("pnpm add -D @testing-library/user-event@14.5.2"));
}
