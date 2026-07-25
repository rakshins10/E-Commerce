using System.Xml.Linq;
using FluentAssertions;

namespace ECommerce.Architecture.Tests;

/// <summary>
/// Enforces the boundary rules that a monorepo cannot enforce physically.
/// </summary>
/// <remarks>
/// <para>
/// See <c>docs/adr/0008-monorepo.md</c>. With every project visible in one solution, adding a reference from
/// Ordering into Catalog's internals is one keystroke and nothing stops it. That is the genuine risk of a
/// monorepo, and the honest mitigation is not "we will be careful" — it is a test that breaks the build.
/// </para>
/// <para>
/// These rules operate on the <b>project files</b> rather than on compiled assemblies. That is deliberate:
/// a project reference is the thing being forbidden, it is what a developer actually adds, and checking it
/// this way needs no extra dependency and reports a path a human can go and fix.
/// </para>
/// </remarks>
public class ProjectReferenceRules
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void No_service_may_reference_another_service()
    {
        // The rule that keeps this a microservice architecture rather than a distributed monolith. Services
        // integrate through events and through explicit contracts, never by compiling against each other.
        var violations = new List<string>();

        foreach (string project in ProjectsUnder("src/services", "src/gateways"))
        {
            string owner = OwningComponent(project);

            foreach (string reference in ProjectReferencesOf(project))
            {
                if (!IsServiceOrGatewayProject(reference))
                {
                    continue;
                }

                string target = OwningComponent(reference);

                // Within one service, layering references (Api -> Application -> Domain) are correct and
                // expected. Only crossing into a *different* service is forbidden.
                if (!string.Equals(owner, target, StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add($"{Rel(project)} -> {Rel(reference)}");
                }
            }
        }

        violations.Should().BeEmpty(
            "a service must never compile against another service; integrate via events or an explicit contract "
            + "(docs/adr/0008-monorepo.md)");
    }

    [Fact]
    public void Only_the_rabbitmq_building_block_may_reference_the_rabbitmq_client()
    {
        // This is what makes IEventBus a real abstraction rather than a decorative one. If a service could
        // reference RabbitMQ.Client directly, the "we could swap the broker" claim in ADR-0016 would be
        // untrue the moment anyone took the shortcut.
        var violations = new List<string>();

        foreach (string project in ProjectsUnder("src"))
        {
            if (Path.GetFileName(project) == "ECommerce.EventBus.RabbitMQ.csproj")
            {
                continue;
            }

            if (PackageReferencesOf(project).Any(p => p.StartsWith("RabbitMQ", StringComparison.OrdinalIgnoreCase)))
            {
                violations.Add(Rel(project));
            }
        }

        violations.Should().BeEmpty(
            "only ECommerce.EventBus.RabbitMQ may know the transport; everything else depends on IEventBus "
            + "(docs/adr/0016-rabbitmq-behind-ieventbus.md)");
    }

    [Fact]
    public void Domain_projects_must_not_depend_on_infrastructure()
    {
        // "Dependencies point inward", asserted rather than merely drawn on a diagram. A domain project with
        // an empty package list is one that can be unit-tested with no framework at all - and it is why this
        // codebase's IDomainEvent deliberately does not derive from MediatR's INotification, unlike
        // eShopOnContainers. See docs/architecture.md section 4.
        string[] forbidden =
        [
            "Microsoft.EntityFrameworkCore",
            "Microsoft.AspNetCore",
            "MediatR",
            "RabbitMQ",
            "Npgsql",
            "Dapper",
            "Serilog",
            "OpenTelemetry",
            "StackExchange.Redis",
            "MongoDB",
        ];

        var violations = new List<string>();

        foreach (string project in ProjectsUnder("src").Where(p => Path.GetFileName(p).EndsWith(".Domain.csproj", StringComparison.Ordinal)))
        {
            foreach (string package in PackageReferencesOf(project))
            {
                if (forbidden.Any(f => package.StartsWith(f, StringComparison.OrdinalIgnoreCase)))
                {
                    violations.Add($"{Rel(project)} -> {package}");
                }
            }
        }

        violations.Should().BeEmpty(
            "the domain layer must depend on nothing but the BCL (docs/architecture.md section 4)");
    }

    [Fact]
    public void Every_project_uses_central_package_management()
    {
        // A Version attribute on a PackageReference silently opts that project out of the single-version
        // guarantee, and the resulting drift fails at runtime as an assembly binding error rather than at
        // build time. See docs/adr/0013-net10-target-framework.md.
        var violations = new List<string>();

        foreach (string project in ProjectsUnder("src", "tests"))
        {
            XDocument document = XDocument.Load(project);

            bool hasPinnedVersion = document
                .Descendants("PackageReference")
                .Any(element => element.Attribute("Version") is not null);

            if (hasPinnedVersion)
            {
                violations.Add(Rel(project));
            }
        }

        violations.Should().BeEmpty(
            "package versions belong in Directory.Packages.props, never on an individual PackageReference");
    }

    // ---------------------------------------------------------------------
    //  Helpers
    // ---------------------------------------------------------------------

    private static IEnumerable<string> ProjectsUnder(params string[] relativePaths) =>
        relativePaths
            .Select(relative => Path.Combine(RepoRoot, relative))
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.csproj", SearchOption.AllDirectories));

    private static IEnumerable<string> ProjectReferencesOf(string projectPath) =>
        XDocument.Load(projectPath)
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => Path.GetFullPath(
                Path.Combine(Path.GetDirectoryName(projectPath)!, include!.Replace('\\', Path.DirectorySeparatorChar))));

    private static IEnumerable<string> PackageReferencesOf(string projectPath) =>
        XDocument.Load(projectPath)
            .Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .Where(include => include.Length > 0);

    private static bool IsServiceOrGatewayProject(string projectPath)
    {
        string normalised = projectPath.Replace('\\', '/');
        return normalised.Contains("/src/services/", StringComparison.OrdinalIgnoreCase)
               || normalised.Contains("/src/gateways/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The service or gateway a project belongs to — the directory directly beneath <c>services/</c> or
    /// <c>gateways/</c>, e.g. <c>ordering</c> for every one of Ordering's four projects.
    /// </summary>
    private static string OwningComponent(string projectPath)
    {
        string[] segments = projectPath.Replace('\\', '/').Split('/');

        for (int i = 0; i < segments.Length - 1; i++)
        {
            if (segments[i] is "services" or "gateways")
            {
                return segments[i + 1];
            }
        }

        return string.Empty;
    }

    private static string Rel(string path) =>
        Path.GetRelativePath(RepoRoot, path).Replace('\\', '/');

    /// <summary>
    /// Walks up from the test assembly until the directory containing the solution file is found, so the
    /// tests work identically from the IDE, the CLI, and CI regardless of working directory.
    /// </summary>
    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ECommerce.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
               ?? throw new InvalidOperationException("Could not locate the repository root (ECommerce.slnx).");
    }
}
