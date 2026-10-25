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
    public void AcceptsOnlyTheJavaScriptOfTheDocsSiteTemplateItsOwnFolder() =>
        Assert.Equal(
            ["src/Modules/Documentation/Avala.Documentation/SiteTemplate/public/nested/tool.js", "src/Modules/Documentation/Avala.Documentation/SiteTemplate/public/build.sh", "src/Modules/Workbench/Avala.Workbench/SiteTemplate/public/main.js", "src/Modules/Documentation/Avala.Documentation/SiteTemplate/public/types.ts"],
            ScriptLanguages.ForeignScripts([
                "/home/dev/avala/src/Modules/Documentation/Avala.Documentation/SiteTemplate/public/main.js",
                "src/Modules/Documentation/Avala.Documentation/SiteTemplate/public/avala-c4.js",
                "src/Modules/Documentation/Avala.Documentation/SiteTemplate/public/nested/tool.js",
                "src/Modules/Documentation/Avala.Documentation/SiteTemplate/public/build.sh",
                "src/Modules/Workbench/Avala.Workbench/SiteTemplate/public/main.js",
                "src/Modules/Documentation/Avala.Documentation/SiteTemplate/public/types.ts",
            ]));

    [Fact]
    public void AcceptsOnlyTheJavaScriptOfTheSiteTemplateLaidOutInTheDocsItsOwnFolder() =>
        Assert.Equal(
            ["docs/template/public/nested/tool.js", "docs/template/public/build.sh", "docs/template/main.js"],
            ScriptLanguages.ForeignScripts([
                "/home/dev/avala/docs/template/public/main.js",
                "docs/template/public/avala-c4.js",
                "docs/template/public/nested/tool.js",
                "docs/template/public/build.sh",
                "docs/template/main.js",
            ]));

    [Fact]
    public void AcceptsOnlyTheJavaScriptOfTheModelViewerAtAnyDepthOfItsOwnFolder() =>
        Assert.Equal(
            ["src/Modules/Preview/Avala.Preview/Viewer/three/build.sh", "src/Modules/Preview/Avala.Preview/Viewer/viewer.ts", "src/Modules/Preview/Avala.Preview/Pages/viewer.js"],
            ScriptLanguages.ForeignScripts([
                "/home/dev/avala/src/Modules/Preview/Avala.Preview/Viewer/viewer.js",
                "src/Modules/Preview/Avala.Preview/Viewer/three/addons/loaders/GLTFLoader.js",
                "src/Modules/Preview/Avala.Preview/Viewer/three/build.sh",
                "src/Modules/Preview/Avala.Preview/Viewer/viewer.ts",
                "src/Modules/Preview/Avala.Preview/Pages/viewer.js",
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
