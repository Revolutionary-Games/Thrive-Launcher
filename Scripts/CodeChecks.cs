namespace Scripts;

using System.Collections.Generic;
using System.Text.RegularExpressions;
using ScriptsBase.Checks;

public class CodeChecks : CodeChecksBase<Program.CheckOptions>
{
    public CodeChecks(Program.CheckOptions opts) : base(opts)
    {
        ValidChecks = new Dictionary<string, CodeCheck>
        {
            { "files", new FileChecks() },
            { "compile", new CompileCheck(!opts.NoExtraRebuild) },
            { "inspectcode", new InspectCode() },
            { "cleanupcode", new CleanupCode() },
            { "rewrite", new RewriteTool() },
        };

        FilePathsToAlwaysIgnore.Add(new Regex(@"\.Designer\.cs"));
    }

    protected override Dictionary<string, CodeCheck> ValidChecks { get; }

    // This seems to complain about custom constants in ThriveLauncher.csproj (and backend) with no way to
    // turn off so we need to ignore UnknownProperty
    // TODO: could perhaps ignore just the few known good names? in case this inspection can find actual problems
    protected override IEnumerable<string> ForceIgnoredJetbrainsInspections =>
    [
        "UnknownProperty",
    ];

    protected override IEnumerable<string> ExtraIgnoredJetbrainsInspectWildcards => ["**.Designer.cs"];

    protected override string MainSolutionFile => "ThriveLauncher.sln";
}
