using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;

namespace Avala.Host.Tests;

internal static class DogfoodChecks
{
    public const string Sub = "module.exports = { add: (a, b) => a + b, sub: (a, b) => a - b };\n";

    public const string SubTests = """
        const test = require('node:test');
        const assert = require('node:assert');
        const { add, sub } = require('../calc');

        test('adds', () => assert.strictEqual(add(2, 3), 5));
        test('subtracts', () => assert.strictEqual(sub(5, 3), 2));

        """;

    public static DogfoodScript Calculator(string name) =>
        new DogfoodScript(name).Turn()
            .Say("I will add sub and a test file.")
            .Write("calc.js", Sub)
            .Write("test/calc.test.js", SubTests)
            .Run("node --test", "# pass 2")
            .Say("Added sub(a, b) to calc.js and tests for add and sub; node --test passes.")
            .Bill(0.0123m, 0.21)
            .Finish();

    public static Option<JobId> Job(DogfoodEvidence evidence, string label) =>
        evidence.Jobs.Where(job => job.ToString() == evidence.Fact($"submitted:{label}")).Select(Option<JobId>.Some).FirstOrDefault(Option<JobId>.None);

    public static DogfoodVerdict Expect(DogfoodEvidence evidence, bool holds, string seen) =>
        holds ? DogfoodVerdict.Pass(seen)
        : evidence.Outcome != "completed" ? DogfoodVerdict.NotReached($"{evidence.Outcome}; {seen}")
        : DogfoodVerdict.Fail(seen);

    public static DogfoodCheck OfJob(string then, string label, Func<DogfoodEvidence, JobId, DogfoodVerdict> judge) =>
        new(then, evidence => DogfoodVerdict.Unless(Job(evidence, label), $"no job '{label}' was submitted ({evidence.Outcome})", job => judge(evidence, job)));

    public static DogfoodCheck Journey(string then, string label, params JobStatus[] expected) =>
        OfJob(then, label, (evidence, job) =>
        {
            var journey = evidence.Journey(job);

            return Expect(evidence, InOrder(journey, expected), $"journey [{string.Join(" → ", journey)}]");
        });

    public static DogfoodCheck Verified(string then, string label, string check) =>
        OfJob(then, label, (evidence, job) =>
        {
            var reports = evidence.All<AttemptVerified>(verified => verified.Report.Job == job).Select(verified => verified.Report).ToList();
            var passed = reports.SelectMany(report => report.Checks).FirstOrDefault(found => found.Name == check && found.Status == CheckStatus.Passed && found.ExitCode == Option<int>.Some(0));

            return Expect(evidence, passed is not null, $"verifications [{string.Join("; ", reports.Select(report => $"attempt {report.Attempt} {report.Outcome}: {string.Join(", ", report.Checks.Select(found => $"{found.Name} {found.Status} exit {found.ExitCode.Match(code => code.ToString(System.Globalization.CultureInfo.InvariantCulture), () => "none")} ({found.Command})"))}"))}]");
        });

    public static DogfoodCheck Priced(string then) =>
        new(then, evidence =>
        {
            var costs = evidence.Usage.Select(reported => reported.Cost).Where(cost => cost.IsSome).Select(cost => cost.Match(found => found, () => default)).ToList();
            var labelled = evidence.FactsOf("inspector:").Where(text => text.Contains("Spent='", StringComparison.Ordinal)).ToList();
            var holds = costs.Count > 0 && costs.All(cost => cost.Basis == CostBasis.ApiEquivalent) && labelled.Count > 0 && labelled.All(text => text.Contains("at API prices", StringComparison.Ordinal));

            return Expect(evidence, holds, $"{costs.Count} costs with bases [{string.Join(", ", costs.Select(cost => cost.Basis).Distinct())}]; usage sections: {string.Join(" / ", labelled.Select(text => Between(text, "Spent='", "'")))}");
        });

    public static DogfoodCheck Fact(string then, string key, Func<string, bool> holds) =>
        new(then, evidence => evidence.Has(key)
            ? Expect(evidence, holds(evidence.Fact(key)), $"{key}: {Short(evidence.Fact(key))}")
            : DogfoodVerdict.NotReached($"{key} was never captured ({evidence.Outcome})"));

    public static bool InOrder<T>(IReadOnlyList<T> seen, IReadOnlyList<T> expected)
    {
        var next = 0;

        foreach (var item in seen)
        {
            if (next < expected.Count && EqualityComparer<T>.Default.Equals(item, expected[next]))
            {
                next++;
            }
        }

        return next == expected.Count;
    }

    public static string Short(string text) => text.Length > 500 ? string.Concat(text.AsSpan(0, 500), "…") : text;

    public static string Between(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal);

        if (from < 0)
        {
            return string.Empty;
        }

        from += start.Length;
        var to = text.IndexOf(end, from, StringComparison.Ordinal);

        return to < 0 ? text[from..] : text[from..to];
    }

    public static IEnumerable<string> Texts(DogfoodEvidence evidence, JobId job) =>
        evidence.AgentOf(job).OfType<ItemProgressed>().Select(progressed => progressed.Text);
}
