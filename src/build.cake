///////////////////////////////////////////////////////////////////////////////
// TOOLS / ADDINS
///////////////////////////////////////////////////////////////////////////////

#tool GitVersion.CommandLine
#tool vswhere
#tool xunit.runner.console
#addin Cake.Figlet

///////////////////////////////////////////////////////////////////////////////
// ARGUMENTS
///////////////////////////////////////////////////////////////////////////////

var target = Argument("target", "Default");
var configuration = Argument("configuration", "Release");
var verbosity = Argument("verbosity", Verbosity.Minimal);

///////////////////////////////////////////////////////////////////////////////
// PREPARATION
///////////////////////////////////////////////////////////////////////////////

var repoName = "SimpleMusicPlayer";
var isLocal = BuildSystem.IsLocalBuild;

// Set build version
if (isLocal == false || verbosity == Verbosity.Verbose)
{
    GitVersion(new GitVersionSettings { OutputType = GitVersionOutput.BuildServer });
}
GitVersion gitVersion = GitVersion(new GitVersionSettings { OutputType = GitVersionOutput.Json });

var latestInstallationPath = VSWhereLatest(new VSWhereLatestSettings { IncludePrerelease = true });
var msBuildPath = latestInstallationPath.Combine("./MSBuild/Current/Bin");
var msBuildPathExe = msBuildPath.CombineWithFilePath("./MSBuild.exe");

if (FileExists(msBuildPathExe) == false)
{
    throw new NotImplementedException("You need at least Visual Studio 2019 to build this project.");
}

var isPullRequest = AppVeyor.Environment.PullRequest.IsPullRequest;
var branchName = gitVersion.BranchName;
var isDevelopBranch = StringComparer.OrdinalIgnoreCase.Equals("dev", branchName);
var isReleaseBranch = StringComparer.OrdinalIgnoreCase.Equals("master", branchName);
var isTagged = AppVeyor.Environment.Repository.Tag.IsTag;

// Directories and Paths
var solution = "./SimpleMusicPlayer.sln";
var publishDir = "./Publish";

///////////////////////////////////////////////////////////////////////////////
// SETUP / TEARDOWN
///////////////////////////////////////////////////////////////////////////////

Setup(ctx =>
{
    // Executed BEFORE the first task.

    if (!IsRunningOnWindows())
    {
        throw new NotImplementedException($"{repoName} will only build on Windows because it's not possible to target WPF and Windows Forms from UNIX.");
    }

    Information(Figlet(repoName));

    Information("Informational   Version: {0}", gitVersion.InformationalVersion);
    Information("SemVer          Version: {0}", gitVersion.SemVer);
    Information("AssemblySemVer  Version: {0}", gitVersion.AssemblySemVer);
    Information("MajorMinorPatch Version: {0}", gitVersion.MajorMinorPatch);
    Information("NuGet           Version: {0}", gitVersion.NuGetVersion);
    Information("IsLocalBuild           : {0}", isLocal);
    Information("Branch                 : {0}", branchName);
    Information("Configuration          : {0}", configuration);
    Information("MSBuildPath            : {0}", msBuildPath);
});

Teardown(ctx =>
{
   // Executed AFTER the last task.
});

///////////////////////////////////////////////////////////////////////////////
// TASKS
///////////////////////////////////////////////////////////////////////////////

Task("Clean")
    .ContinueOnError()
    .Does(() =>
{
    var directoriesToDelete = GetDirectories("./**/obj")
        .Concat(GetDirectories("./**/bin"))
        .Concat(GetDirectories("./**/Publish"));
    DeleteDirectories(directoriesToDelete, new DeleteDirectorySettings { Recursive = true, Force = true });
});

Task("Restore")
    .Does(() =>
{
    StartProcess("nuget", new ProcessSettings {
        Arguments = new ProcessArgumentBuilder()
            .Append("restore")
            .Append(solution)
            .Append("-msbuildpath")
            .AppendQuoted(msBuildPath.ToString())
       }
   );
});

Task("Build")
    .Does(() =>
{
    var msBuildSettings = new MSBuildSettings {
        Verbosity = verbosity
        , ToolPath = msBuildPathExe
        , Configuration = configuration
        , ArgumentCustomization = args => args.Append("/m")
        , BinaryLogger = new MSBuildBinaryLogSettings() { Enabled = isLocal }
        };
    MSBuild(solution, msBuildSettings
            .UseToolVersion(MSBuildToolVersion.VS2017) // for now
            .SetMaxCpuCount(0)
            .WithProperty("Version", isReleaseBranch ? gitVersion.MajorMinorPatch : gitVersion.NuGetVersion)
            .WithProperty("AssemblyVersion", gitVersion.AssemblySemVer)
            .WithProperty("FileVersion", gitVersion.AssemblySemFileVer)
            .WithProperty("InformationalVersion", gitVersion.InformationalVersion)
            );

    // var msBuildSettings = new MSBuildSettings { ArgumentCustomization = args => args.Append("/m") };
    // MSBuild(solution, msBuildSettings
    //         .UseToolVersion(MSBuildToolVersion.VS2015) // for now
    //         .SetMaxCpuCount(0)
    //         .SetConfiguration(configuration)
    //         .SetVerbosity(Verbosity.Normal)
    //         //.WithRestore() only with cake 0.28.x            
    //         .WithProperty("AssemblyVersion", gitVersion.AssemblySemVer)
    //         .WithProperty("FileVersion", gitVersion.AssemblySemFileVer)
    //         .WithProperty("InformationalVersion", gitVersion.InformationalVersion)
    //         );
});

Task("Zip")
    .Does(() =>
{
    EnsureDirectoryExists(Directory(publishDir));
    Zip("./SimpleMusicPlayer/bin/" + configuration, publishDir + $"/SimpleMusicPlayer-{configuration}-v{gitVersion.NuGetVersion}.zip");
});

Task("Tests")
    .Does(() =>
{
    XUnit2(
        "./SimpleMusicPlayer.Tests/bin/" + configuration + "/**/*.Tests.dll",
        new XUnit2Settings { ToolTimeout = TimeSpan.FromMinutes(5) }
    );
});

///////////////////////////////////////////////////////////////////////////////
// TASK TARGETS
///////////////////////////////////////////////////////////////////////////////

Task("Default")
    .IsDependentOn("Clean")
    .IsDependentOn("Restore")
    .IsDependentOn("Build");

Task("appveyor")
    .IsDependentOn("Default")
    .IsDependentOn("Tests")
    .IsDependentOn("Zip");

///////////////////////////////////////////////////////////////////////////////
// EXECUTION
///////////////////////////////////////////////////////////////////////////////

RunTarget(target);