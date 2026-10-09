using Avala.Components.Meters;

namespace Avala.Workbench.Inspector;

internal sealed class DesignEvidenceSectionViewModel : IEvidenceSectionViewModel
{
    public bool IsLoaded => true;

    public string Fact => "4 of 4 passed";

    public string Summary => "Verified on attempt 2 of 2";

    public IReadOnlyList<string> Attempts { get; } =
    [
        "Attempt 1: failed · tests failed (exit 1), lint passed (exit 0)",
        "Attempt 2: passed · tests passed (exit 0), lint passed (exit 0)",
    ];
}

internal sealed class DesignAuditSectionViewModel : IAuditSectionViewModel
{
    public bool IsLoaded => true;

    public string Fact => "1 assumption";

    public bool IsAttention => false;

    public string Summary => "6 allowed by rules · 1 answered by you · 0 denied · 1 assumption";

    public IReadOnlyList<string> Decisions { get; } =
    [
        "Allowed Command npm test -- test/auth · rule tests",
        "Allowed FileEdit src/auth/rateLimit.ts · rule source",
        "You allowed Command npm install express-rate-limit",
    ];

    public IReadOnlyList<string> Assumptions { get; } = ["How many failed logins before the limit?: 5 attempts per minute"];
}

internal sealed class DesignUsageSectionViewModel : IUsageSectionViewModel
{
    public DesignUsageSectionViewModel()
    {
        var meter = new MeterViewModel("Cost cap", Sdk.Option<double>.None);
        meter.Show(0.168);
        Meter = meter;
    }

    public bool IsLoaded => true;

    public string Fact => "USD 0.84 of USD 5";

    public bool IsAttention => false;

    public string Spent => "USD 0.84 · 61,250 tokens";

    public IReadOnlyList<string> Caps { get; } = ["Cost cap USD 5", "Held at 90% of a usage limit"];

    public IReadOnlyList<string> Interventions { get; } = [];

    public string Carve => string.Empty;

    public IMeterViewModel? Meter { get; }
}

internal sealed class DesignAutonomySectionViewModel : IAutonomySectionViewModel
{
    public bool IsLoaded => true;

    public string Fact => "Supervised";

    public string Autonomy => "Supervised, as the repository declares";

    public string Connection => "claude-personal";

    public string Reason => "Chosen by capacity: claude-personal had the most left";

    public IReadOnlyList<CapacityLine> Compared { get; } =
    [
        new("claude-work", "88% of 5h · holds at 90%", false, false),
        new("claude-personal", "31% of 5h · holds at 90%", true, false),
    ];
}

internal sealed class DesignWorktreeSectionViewModel : IWorktreeSectionViewModel
{
    public bool IsLoaded => true;

    public string Fact => "rate-limit-post-login";

    public string Branch => "avala/rate-limit-post-login";

    public string Base => "main at 4f2c9e1";

    public string Path => "~/.avala/worktrees/shop-api/rate-limit-post-login";

    public string Ports => "Ports 41000–41009";
}

internal sealed class DesignDelegationSectionViewModel : IDelegationSectionViewModel
{
    public bool IsLoaded => true;

    public string Fact => "a sub-agent";

    public string Parent => "Delegated by Harden the auth endpoints";

    public IReadOnlyList<string> Children { get; } = [];

    public bool IsEmpty => false;
}
