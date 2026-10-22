using System.Collections.Concurrent;
using System.Reflection;
using Avala.Host.Tests;
using Xunit.v3;

[assembly: InvariantsKept]

namespace Avala.Host.Tests;

[AttributeUsage(AttributeTargets.Assembly)]
internal sealed class InvariantsKeptAttribute : Attribute, IBeforeAfterTestAttribute
{
    private static readonly ConcurrentDictionary<string, ConcurrentQueue<string>> Broken = new();

    public static void Report(IReadOnlyList<string> broken)
    {
        if (broken.Count == 0)
        {
            return;
        }

        var found = Broken.GetOrAdd(TestContext.Current.Test?.UniqueID ?? string.Empty, _ => new ConcurrentQueue<string>());

        foreach (var invariant in broken)
        {
            found.Enqueue(invariant);
        }
    }

    public void Before(MethodInfo methodUnderTest, IXunitTest test)
    {
    }

    public void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        if (Broken.TryRemove(test.UniqueID, out var broken))
        {
            Assert.Fail($"The scenario broke an invariant:{Environment.NewLine}{string.Join(Environment.NewLine, broken.Distinct())}");
        }
    }
}
