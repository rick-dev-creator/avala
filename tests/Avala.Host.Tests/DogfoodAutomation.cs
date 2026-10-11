using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Triggers.Contracts;
using static Avala.Host.Tests.DogfoodSettings;

namespace Avala.Host.Tests;

internal sealed class DogfoodAutomation(DogfoodApp app, DogfoodActions actions, DogfoodCamera camera, DogfoodEvidence evidence)
{
    public async Task<Bound> TriggersAsync(string label)
    {
        var page = app.Ui.Page("Triggers");

        if (!ReferenceEquals(app.Shell.SelectedPage, page.Target))
        {
            app.Shell.SelectedPage = (IPage)page.Target;
        }

        await app.Ui.UntilAsync(page.Presentation, () => page["Triggers"].Items.Count > 0, "the Triggers page to list its triggers");
        evidence.Note($"triggers:{label}", Describe(page));
        await camera.ShootAsync($"triggers-{label}");

        return page;
    }

    public async Task RunNowAsync(string trigger, string label)
    {
        var page = await TriggersAsync($"{label}-before");
        await app.Ui.UntilAsync(page.Presentation, () => page["Triggers"].Items.Any(item => item["Name"].Text == trigger), $"the trigger {trigger} on the Triggers page");
        var item = page["Triggers"].Items.First(candidate => candidate["Name"].Text == trigger);
        await page.ExecuteAsync("RunNowCommand", item.Target);
        evidence.Note($"run-now:{label}", $"notice '{page["Notice"].Text}', error '{page["Error"].Text}'");
        await camera.ShootAsync($"triggers-{label}-run-now");
        actions.ShowJobs();
    }

    public async Task<string> LoopsAsync(string label, Func<Bound, bool> shown)
    {
        var page = await TriggersAsync(label);
        var loops = page["Loops"];
        await app.Ui.UntilAsync(page.Presentation, () => shown(loops), $"the loop state on the Triggers page ({label})");
        var text = string.Join(Environment.NewLine, loops["Loops"].Items.Select(loop => $"{loop["Status"].Text} · {loop["Summary"].Text} · [{string.Join(" | ", loop["Iterations"].Items.Select(line => line.Text))}]"));
        evidence.Note($"loops:{label}", text);
        await camera.ShootAsync($"triggers-{label}-loops");
        actions.ShowJobs();

        return text;
    }

    public async Task<string> PullRequestAsync(JobId job, string label, bool checkNow, string status = "")
    {
        var section = await actions.SectionAsync(job, "PullRequestSectionViewModel", label);

        if (checkNow && DogfoodUi.CanInvoke(section, "CheckNowCommand"))
        {
            await section.ExecuteAsync("CheckNowCommand");
        }

        await app.Ui.UntilAsync(section.Value<IPresentation>(), () => section["Status"].Text.StartsWith(status, StringComparison.Ordinal), $"the Pull request section to read {status}");

        var text = DogfoodPolicy.Describe(section.Target);
        evidence.Note($"pull-request:{label}", text);

        return text;
    }

    public async Task<HttpStatusCode> PostAsync(string trigger, string body, DogfoodSigning signing, string label)
    {
        var url = app.Get<ITriggers>().Endpoint.Url.Match(found => found, () => throw new InvalidOperationException("The webhook listener is not running."));
        using var client = new HttpClient();
        var bytes = Encoding.UTF8.GetBytes(body);
        using var request = new HttpRequestMessage(HttpMethod.Post, url + trigger) { Content = new ByteArrayContent(bytes) };

        if (signing.Secret.Length > 0)
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            var signed = Encoding.UTF8.GetBytes($"{timestamp}.{signing.Nonce}.").Concat(bytes).ToArray();
            request.Headers.Add("X-Avala-Timestamp", timestamp);
            request.Headers.Add("X-Avala-Nonce", signing.Nonce);
            using var stream = new MemoryStream(signed);
            request.Headers.Add("X-Avala-Signature", "sha256=" + Convert.ToHexStringLower(await HMACSHA256.HashDataAsync(Encoding.UTF8.GetBytes(signing.Secret), stream, Cancellation)));
        }

        using var response = await client.SendAsync(request, Cancellation);
        evidence.Note($"webhook:{label}", $"{(int)response.StatusCode} {response.StatusCode}");

        return response.StatusCode;
    }

    private static string Describe(Bound page) =>
        new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture, $"Scope: {page["Scope"].Text}; endpoint: {page["Endpoint"].Text} {page["Url"].Text}; notice '{page["Notice"].Text}'; error '{page["Error"].Text}'")
            .AppendLine(CultureInfo.InvariantCulture, $"Triggers: {string.Join(" | ", page["Triggers"].Items.Select(item => DogfoodPolicy.Describe(item.Target)))}")
            .AppendLine(CultureInfo.InvariantCulture, $"Deliveries: {string.Join(" | ", page["Deliveries"].Items.Select(line => line.Text))}")
            .ToString();
}

internal sealed record DogfoodSigning(string Secret, string Nonce)
{
    public static DogfoodSigning Unsigned { get; } = new(string.Empty, string.Empty);
}
