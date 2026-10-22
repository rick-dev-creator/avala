using System.Text.Json.Nodes;

namespace Avala.Testing;

public sealed class SimulatedForgeRemote(string remote)
{
    public const string StateFile = "avala-forge.json";

    private string State => Path.Combine(remote, StateFile);

    public async Task<int> OpenIssueAsync(string title, string body, string author, CancellationToken cancellationToken, bool pullRequest = false, bool fork = false)
    {
        var number = 0;
        await ChangeAsync(
            state =>
            {
                var issues = List(state, "Issues");
                number = issues.Count + 100;
                issues.Add(new JsonObject
                {
                    ["Number"] = number,
                    ["Title"] = title,
                    ["Body"] = body,
                    ["Author"] = author,
                    ["PullRequest"] = pullRequest,
                    ["Fork"] = fork,
                });
                Happened(state, "opened", number, author);
            },
            cancellationToken);

        return number;
    }

    public async Task<int> OpenPullRequestAsync(string head, string target, string title, CancellationToken cancellationToken, string scenario = "green", string fork = "")
    {
        var number = 0;
        await ChangeAsync(
            state =>
            {
                var pulls = List(state, "Pulls");
                number = pulls.Count + 1;
                pulls.Add(new JsonObject
                {
                    ["Number"] = number,
                    ["Head"] = head,
                    ["Base"] = target,
                    ["Title"] = title,
                    ["Body"] = string.Empty,
                    ["Scenario"] = scenario,
                    ["Fork"] = fork,
                });
            },
            cancellationToken);

        return number;
    }

    public Task SetScenarioAsync(int pullRequest, string scenario, CancellationToken cancellationToken) =>
        ChangeAsync(state => List(state, "Pulls").OfType<JsonObject>().Single(pull => (int?)pull["Number"] == pullRequest)["Scenario"] = scenario, cancellationToken);

    public Task LabelAsync(int issue, string label, string actor, CancellationToken cancellationToken) =>
        ChangeAsync(state => Happened(state, "labeled", issue, actor)["Label"] = label, cancellationToken);

    public Task CommentAsync(int issue, string comment, string actor, CancellationToken cancellationToken) =>
        ChangeAsync(state => Happened(state, "commented", issue, actor)["Comment"] = comment, cancellationToken);

    public Task RequestReviewAsync(int pullRequest, string reviewer, string actor, CancellationToken cancellationToken) =>
        ChangeAsync(state => Happened(state, "review-requested", pullRequest, actor)["Reviewer"] = reviewer, cancellationToken);

    public Task FailChecksAsync(string head, string author, CancellationToken cancellationToken) =>
        ChangeAsync(state => List(state, "FailingHeads").Add(new JsonObject { ["Head"] = head, ["Author"] = author }), cancellationToken);

    private static JsonObject Happened(JsonObject state, string kind, int number, string actor)
    {
        var events = List(state, "Events");
        var happened = new JsonObject { ["Id"] = events.Count + 7001L, ["Kind"] = kind, ["Number"] = number, ["Actor"] = actor };
        events.Add(happened);

        return happened;
    }

    private static JsonArray List(JsonObject state, string name)
    {
        if (state[name] is JsonArray list)
        {
            return list;
        }

        var created = new JsonArray();
        state[name] = created;

        return created;
    }

    private async Task ChangeAsync(Action<JsonObject> change, CancellationToken cancellationToken)
    {
        var state = File.Exists(State) ? JsonNode.Parse(await File.ReadAllTextAsync(State, cancellationToken))!.AsObject() : [];
        change(state);
        Directory.CreateDirectory(remote);
        var written = $"{State}.{Guid.NewGuid():N}";
        await File.WriteAllTextAsync(written, state.ToJsonString(), cancellationToken);
        File.Move(written, State, overwrite: true);
    }
}
