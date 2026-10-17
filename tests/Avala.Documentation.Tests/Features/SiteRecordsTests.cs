using Avala.Documentation.Features;

namespace Avala.Documentation.Tests.Features;

public sealed class SiteRecordsTests
{
    [Fact]
    public void ADecisionIsNumberedAfterTheLastRecordAndNamedFromItsTitle()
    {
        var next = SiteRecords.Next(["docs/architecture/decisions/0001-use-postgres.md", "docs/architecture/decisions/0007-idempotency-store.md", "docs/architecture/decisions/index.md", "docs/features/x/0009-not-a-record.md"], "docs", "# Keep idempotency keys in the ledger\n\n## Status\n");

        Assert.Equal(new DecisionRecord(8, "Keep idempotency keys in the ledger", "0008-keep-idempotency-keys-in-the-ledger.md"), next);
        Assert.Equal("ADR-0008", next.Name);
        Assert.Equal(1, SiteRecords.Next([], "docs", "# First").Number);
    }

    [Fact]
    public void AVersionAndItsDecisionLinkEachOtherWithPathsRelativeToTheirFolders()
    {
        var record = new DecisionRecord(8, "Keys in the ledger", "0008-keys-in-the-ledger.md");

        Assert.Equal("../../", SiteRecords.Up("docs/features/refunds", "docs"));
        Assert.Equal("../../", SiteRecords.Up("docs/architecture/decisions", "docs"));
        Assert.Equal("# v2\n\nBody.\n\n## Decisions\n\n- [ADR-0008: Keys in the ledger](../../architecture/decisions/0008-keys-in-the-ledger.md)\n", SiteRecords.Explained("# v2\n\nBody.\n", record, "../../architecture/decisions/0008-keys-in-the-ledger.md"));
        Assert.Equal("# Keys in the ledger\n\n## Versions it shaped\n\n- [Refund webhooks v2](../../features/refunds/v2-deviation.md)\n", SiteRecords.Shaped("# Keys in the ledger\n", "Refund webhooks", 2, "../../features/refunds/v2-deviation.md"));
    }

    [Fact]
    public void WhatsNewPutsTheNewestEntryRightAfterItsLead() =>
        Assert.Equal(
            "---\nms.topic: whats-new\n---\n\n# What's new\n\nNewest first.\n\n## 2026-10-17 · Refund webhooks\n\nRefunds apply once.\n\n## 2026-10-01 · Payments\n",
            SiteRecords.WhatsNew("---\nms.topic: whats-new\n---\n\n# What's new\n\nNewest first.\n\n## 2026-10-01 · Payments\n", "## 2026-10-17 · Refund webhooks\n\nRefunds apply once.\n"));
}
