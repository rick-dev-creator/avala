using Avala.ArchitectureTests.Source;

namespace Avala.ArchitectureTests.SelfTests;

public sealed class ScriptLanguagesTests
{
    [Fact]
    public void FlagsScriptsWrittenInOtherLanguages() =>
        Assert.Equal(
            ["tools/clean.sh", "build.ps1", "gen.py", "setup.CMD"],
            ScriptLanguages.ForeignScripts(["scripts/metrics.cs", "tools/clean.sh", "build.ps1", "gen.py", "setup.CMD", "README.md"]));

    [Fact]
    public void FlagsShebangScriptsWithoutAnExtensionExceptTheFleetCommand() =>
        Assert.Equal(
            ["tools/clean", "deploy/fleet/other"],
            ScriptLanguages.ShebangScripts([
                ("tools/clean", "#!/bin/sh"),
                ("deploy/fleet/fleet", "#!/bin/sh"),
                ("deploy/fleet/other", "#!/usr/bin/env bash"),
                ("LICENSE", "MIT License"),
                ("scripts/metrics.cs", "#!/usr/bin/env dotnet"),
            ]));

    [Fact]
    public void AcceptsJavaScriptAndTypeScriptAnywhere() =>
        Assert.Empty(
            ScriptLanguages.ForeignScripts([
                "src/Modules/Documentation/Avala.Documentation/SiteTemplate/public/avala-c4.js",
                "src/Modules/Preview/Avala.Preview/Viewer/three/addons/loaders/GLTFLoader.js",
                "web/three/three.module.mjs",
                "web/view.ts",
                "tools/bundle.cjs",
            ]));

    [Fact]
    public void AcceptsDotnetCommandsInEveryRunStyle()
    {
        const string workflow = """
            steps:
              - run: dotnet build Avala.slnx
              - name: Test
                run: >-
                  dotnet test --solution Avala.slnx
                  --no-build
            """;

        Assert.Empty(ScriptLanguages.NonDotnetCommands(workflow));
    }

    [Fact]
    public void FlagsShellCommandsInEveryRunStyle()
    {
        const string workflow = """
            steps:
              - run: cat summary.md >> "$GITHUB_STEP_SUMMARY"
              - name: Clean
                run: |
                  rm -rf artifacts
              - run: dotnet build
            """;

        Assert.Equal(
            ["cat summary.md >> \"$GITHUB_STEP_SUMMARY\"", "rm -rf artifacts"],
            ScriptLanguages.NonDotnetCommands(workflow));
    }
}
