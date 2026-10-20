using System.Reflection;
using ModRouter.Core.Compilation;

namespace ModRouter.Test;

[TestFixture]
public sealed class ArchitectureTests
{
    static readonly string[] ForbiddenNamespaces = ["Microsoft.AspNetCore.Components", "Microsoft.JSInterop"];

    const BindingFlags AllMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    [Test]
    public void Core_does_not_expose_or_hold_Blazor_or_JS_interop_types()
    {
        var coreTypes = typeof(CompiledRouteTable).Assembly
            .GetTypes()
            .Where(t => t.Namespace is not null && t.Namespace.StartsWith("ModRouter.Core", StringComparison.Ordinal))
            .ToList();

        Assert.That(coreTypes, Is.Not.Empty);

        var violations = coreTypes
            .SelectMany(type => ReferencedTypes(type).Select(referenced => (type, referenced)))
            .Where(pair => IsForbidden(pair.referenced))
            .Select(pair => $"{pair.type.FullName} references {pair.referenced.FullName}")
            .Distinct()
            .ToList();

        Assert.That(violations, Is.Empty, string.Join(Environment.NewLine, violations));
    }

    // Plumbing that slice 3 may still reshape is internal (the test project sees it through InternalsVisibleTo); the
    // seam, the stage contract, the state and the components stay public.
    static readonly Type[] InternalPlumbing =
    [
        typeof(Navigation.NavigationCoordinator),
        typeof(Navigation.BlazorNavigationAdapter),
        typeof(Navigation.BaseUriHelper),
        typeof(Components.RouteComponentBinder),
        typeof(Components.ValueConverter),
        typeof(Navigation.RedirectStage),
        typeof(Navigation.GuardStage),
        typeof(Navigation.LoaderStage),
        typeof(Components.RouteErrorContent),
        typeof(Components.RouteErrorFragments),
        typeof(Core.Loading.IRouteStream),
        typeof(Discovery.PageDiscovery),
        typeof(Components.RouteEntryCollector),
        typeof(Components.RouteEntryNode),
        typeof(Authorization.AuthorizationRouteContributor),
        typeof(Authorization.AuthorizationRequirements),
        typeof(Authorization.AuthenticationChangeSubscription),
        typeof(Compatibility.NavigationContextFactory),
        typeof(Compatibility.AssemblySnapshot),
        typeof(Diagnostics.RouterLog),
        typeof(Diagnostics.NavigationTrace),
        typeof(Components.RouteLinkEvaluator),
        typeof(Navigation.RouteStatus),
        typeof(Navigation.LoaderCache),
        typeof(Navigation.LoaderCacheState),
        typeof(Components.RouteComponentRegistry),
        typeof(Navigation.LeaveGuardStage),
        typeof(Navigation.NavigationCompletion),
        typeof(Navigation.LoaderStatePersistence),
        typeof(Navigation.IRouteDataPersistence),
        typeof(Navigation.RouteDataPersistence<>),
        typeof(Core.Definitions.RoutePersistedDataMetadata),
        typeof(Components.RouteCrumbs),
        typeof(Navigation.NavigationService),
        typeof(Navigation.NavigationEffects),
        typeof(Navigation.NavigationEffectsPlanner),
        typeof(Navigation.FocusEffect),
        typeof(Navigation.ScrollEffect),
        typeof(Navigation.ScrollMode),
        typeof(Authorization.AuthorizeAttributeGuard),
        typeof(Navigation.NavigationOutcome),
        typeof(Core.Definitions.RedirectOnly),
    ];

    static readonly Type[] PublicSurface =
    [
        typeof(Navigation.INavigationPlatformAdapter),
        typeof(Navigation.LocationChangingRequest),
        typeof(Navigation.LocationChangedNotification),
        typeof(Navigation.LocationChangingDecision),
        typeof(Navigation.INavigationStage),
        typeof(Navigation.StageResult),
        typeof(Navigation.RouteNavigationContext),
        typeof(Navigation.INavigationService),
        typeof(Navigation.RouteState),
        typeof(Navigation.RouteStateStore),
        typeof(Navigation.BaseRelativeLocation),
        typeof(Navigation.NavigationTrigger),
        typeof(Navigation.RouteFailure),
        typeof(Components.RouteBindingValidationException),
        typeof(Components.RouteOutletContext),
        typeof(Components.ModRouter),
        typeof(Components.ModRouterOutlet),
        typeof(Components.IKeepAliveRoute),
        typeof(ModRouterOptions),
        typeof(ModRouterServiceCollectionExtensions),
        typeof(Core.Guards.IRouteGuard),
        typeof(Core.Guards.RouteGuardContext),
        typeof(Core.Guards.RouteGuardResult),
        typeof(Core.Guards.RouteGuardOutcome),
        typeof(Core.Guards.IRouteLeaveGuard),
        typeof(Core.Guards.IRouteNavigationLock),
        typeof(Core.Guards.RouteLeaveContext),
        typeof(Core.Guards.RouteLeaveResult),
        typeof(Core.Guards.RouteLeaveOutcome),
        typeof(Core.Loading.IRouteLoader),
        typeof(Core.Loading.RouteLoadContext),
        typeof(Core.Loading.RouteLoadResult),
        typeof(Core.Loading.RouteReloadPolicy),
        typeof(Core.Loading.DeferredValue<>),
        typeof(Core.Loading.RouteStream<>),
        typeof(Core.Loading.DeferredStatus),
        typeof(Core.Compilation.RouteTableHolder),
        typeof(Core.Definitions.RouteGuardMetadata),
        typeof(Core.Definitions.RouteLeaveGuardMetadata),
        typeof(Core.Definitions.RouteLoaderCacheMetadata),
        typeof(Core.Definitions.RouteLoaderMetadata),
        typeof(Core.Definitions.RouteSlotMetadata),
        typeof(Core.Definitions.RouteKeepAliveMetadata),
        typeof(Core.Definitions.RouteRedirectMetadata),
        typeof(Core.Definitions.RouteAliasMetadata),
        typeof(Core.Definitions.AliasBehavior),
        typeof(Navigation.RouteError),
        typeof(Navigation.RouteErrorKind),
        typeof(Navigation.RouteLevelData),
        typeof(Components.ModRouteData),
        typeof(Components.ModDeferred<>),
        typeof(Components.ModStream<>),
        typeof(Components.ModNavigationIndicator),
        typeof(Components.RouteLink),
        typeof(Components.RouteLinkMatch),
        typeof(Components.RouteBreadcrumb),
        typeof(Core.Breadcrumbs.BreadcrumbTrail),
        typeof(Core.Breadcrumbs.BreadcrumbEntry),
        typeof(Core.Titles.RouteTitleFormatter),
        typeof(Core.Definitions.RouteBreadcrumbMetadata),
        typeof(Core.Definitions.RouteFocusMetadata),
        typeof(Core.Definitions.RouteAnnounceMetadata),
        typeof(Navigation.ModNavigationOptions),
        typeof(TitleOptions),
        typeof(AnnounceOptions),
        typeof(FocusOptions),
        typeof(ScrollOptions),
        typeof(ViewTransitionOptions),
        typeof(Discovery.RouteNameAttribute),
        typeof(Discovery.RouteTitleAttribute),
        typeof(Discovery.RouteBreadcrumbAttribute),
        typeof(Discovery.RouteGuardAttribute),
        typeof(Discovery.RouteGuardAttribute<>),
        typeof(Discovery.RouteLoaderCacheAttribute),
        typeof(Discovery.RouteSlotAttribute),
        typeof(Discovery.RouteKeepAliveAttribute),
        typeof(Discovery.RouteLeaveGuardAttribute),
        typeof(Discovery.RouteLeaveGuardAttribute<>),
        typeof(Discovery.RouteLoaderAttribute),
        typeof(Discovery.RouteLoaderAttribute<>),
        typeof(Discovery.RouteFocusAttribute),
        typeof(Discovery.RouteNoAnnounceAttribute),
        typeof(Discovery.RouteIndexAttribute),
        typeof(Discovery.RouteCanonicalAliasAttribute),
        typeof(Discovery.RouteParentAttribute),
        typeof(Discovery.RoutePersistLoaderDataAttribute),
        typeof(Discovery.RoutePersistLoaderDataAttribute<>),
        typeof(RouteMapPagesExtensions),
        typeof(RouteMapPersistenceExtensions),
        typeof(Navigation.IRouteStatusSink),
        typeof(Navigation.RouteStatusKind),
        typeof(Components.RoutePreload),
        typeof(LoaderCacheOptions),
        typeof(Navigation.NavigationResult),
        typeof(Navigation.NavigationStatus),
        typeof(Discovery.PageAssemblyCollection),
        typeof(Discovery.PageAssembly),
        typeof(Components.ModRouteEntry),
        typeof(Components.ModRoutePages),
        typeof(Authorization.IAuthorizationRouteGuard),
        typeof(Authorization.RouteAuthorizeMetadata),
        typeof(Authorization.RouteAllowAnonymousMetadata),
        typeof(RouteMapAuthorizationExtensions),
        typeof(Compatibility.Router),
        typeof(Compatibility.RouteView),
        typeof(Compatibility.AuthorizeRouteView),
        typeof(Core.Matching.RouteCandidate),
        typeof(Core.Matching.RouteCandidateVerdict),
        typeof(Diagnostics.DiagnosticsOptions),
        typeof(Diagnostics.RouterDiagnostics),
        typeof(Diagnostics.NavigationRecord),
        typeof(Diagnostics.NavigationRecordOutcome),
        typeof(Diagnostics.StageRecord),
        typeof(Diagnostics.StageOutcome),
        typeof(Diagnostics.RouterTelemetry),
    ];

    const string RouterNamespace = "ModRouter.";

    // The reviewed public surface: every exported type of the library, sorted ordinally, without the common namespace prefix.
    // A nested public type would appear with a '+'; there are none.
    static readonly string[] ReviewedPublicTypes =
    [
        "AnnounceOptions",
        "Authorization.IAuthorizationRouteGuard",
        "Authorization.RouteAllowAnonymousMetadata",
        "Authorization.RouteAuthorizeMetadata",
        "Compatibility.AuthorizeRouteView",
        "Compatibility.RouteView",
        "Compatibility.Router",
        "Components.IKeepAliveRoute",
        "Components.ModDeferred`1",
        "Components.ModNavigationIndicator",
        "Components.ModRouteData",
        "Components.ModRouteEntry",
        "Components.ModRoutePages",
        "Components.ModRouter",
        "Components.ModRouterOutlet",
        "Components.ModStream`1",
        "Components.RouteBindingValidationException",
        "Components.RouteBreadcrumb",
        "Components.RouteLink",
        "Components.RouteLinkMatch",
        "Components.RouteOutletContext",
        "Components.RoutePreload",
        "Core.Breadcrumbs.BreadcrumbEntry",
        "Core.Breadcrumbs.BreadcrumbTrail",
        "Core.Compilation.CompiledRoute",
        "Core.Compilation.CompiledRouteTable",
        "Core.Compilation.RouteCompilationError",
        "Core.Compilation.RouteCompilationException",
        "Core.Compilation.RouteCompiler",
        "Core.Compilation.RouteTableHolder",
        "Core.Constraints.ConstraintRegistry",
        "Core.Constraints.IConstraintRegistry",
        "Core.Constraints.IRefinementConstraint",
        "Core.Constraints.IRouteConstraint",
        "Core.Definitions.AliasBehavior",
        "Core.Definitions.RouteAliasMetadata",
        "Core.Definitions.RouteAnnounceMetadata",
        "Core.Definitions.RouteBreadcrumbMetadata",
        "Core.Definitions.RouteComponentReuse",
        "Core.Definitions.RouteDefinition",
        "Core.Definitions.RouteFocusMetadata",
        "Core.Definitions.RouteGuardMetadata",
        "Core.Definitions.RouteKeepAliveMetadata",
        "Core.Definitions.RouteLeaveGuardMetadata",
        "Core.Definitions.RouteLoaderCacheMetadata",
        "Core.Definitions.RouteLoaderMetadata",
        "Core.Definitions.RouteMap",
        "Core.Definitions.RouteMetadataCollection",
        "Core.Definitions.RouteRedirectMetadata",
        "Core.Definitions.RouteSlotMetadata",
        "Core.Generation.INamedRouteRegistry",
        "Core.Generation.RouteUrlGenerationError",
        "Core.Generation.RouteUrlGenerationException",
        "Core.Generation.RouteValues",
        "Core.Generation.UrlGenerationErrorCode",
        "Core.Guards.IRouteGuard",
        "Core.Guards.IRouteLeaveGuard",
        "Core.Guards.IRouteNavigationLock",
        "Core.Guards.RouteGuardContext",
        "Core.Guards.RouteGuardOutcome",
        "Core.Guards.RouteGuardResult",
        "Core.Guards.RouteLeaveContext",
        "Core.Guards.RouteLeaveOutcome",
        "Core.Guards.RouteLeaveResult",
        "Core.Loading.DeferredStatus",
        "Core.Loading.DeferredValue`1",
        "Core.Loading.IRouteLoader",
        "Core.Loading.RouteLoadContext",
        "Core.Loading.RouteLoadResult",
        "Core.Loading.RouteReloadPolicy",
        "Core.Loading.RouteStream`1",
        "Core.Matching.RouteCandidate",
        "Core.Matching.RouteCandidateVerdict",
        "Core.Matching.RouteContext",
        "Core.Matching.RouteExplanation",
        "Core.Matching.RouteMatchChain",
        "Core.Matching.RouteMatchFailure",
        "Core.Matching.RouteMatchResult",
        "Core.Matching.RouteParameters",
        "Core.Syntax.ConstraintSpec",
        "Core.Syntax.TemplateSegment",
        "Core.Syntax.TemplateSegmentKind",
        "Core.Titles.RouteTitleFormatter",
        "Core.Titles.RouteTitles",
        "Diagnostics.DiagnosticsOptions",
        "Diagnostics.NavigationRecord",
        "Diagnostics.NavigationRecordOutcome",
        "Diagnostics.RouterDiagnostics",
        "Diagnostics.RouterTelemetry",
        "Diagnostics.StageOutcome",
        "Diagnostics.StageRecord",
        "Discovery.PageAssembly",
        "Discovery.PageAssemblyCollection",
        "Discovery.RouteBreadcrumbAttribute",
        "Discovery.RouteCanonicalAliasAttribute",
        "Discovery.RouteFocusAttribute",
        "Discovery.RouteGuardAttribute",
        "Discovery.RouteGuardAttribute`1",
        "Discovery.RouteIndexAttribute",
        "Discovery.RouteKeepAliveAttribute",
        "Discovery.RouteLeaveGuardAttribute",
        "Discovery.RouteLeaveGuardAttribute`1",
        "Discovery.RouteLoaderAttribute",
        "Discovery.RouteLoaderAttribute`1",
        "Discovery.RouteLoaderCacheAttribute",
        "Discovery.RouteNameAttribute",
        "Discovery.RouteNoAnnounceAttribute",
        "Discovery.RouteParentAttribute",
        "Discovery.RoutePersistLoaderDataAttribute",
        "Discovery.RoutePersistLoaderDataAttribute`1",
        "Discovery.RouteSlotAttribute",
        "Discovery.RouteTitleAttribute",
        "FocusOptions",
        "LoaderCacheOptions",
        "ModRouterOptions",
        "ModRouterServiceCollectionExtensions",
        "Navigation.BaseRelativeLocation",
        "Navigation.INavigationPlatformAdapter",
        "Navigation.INavigationService",
        "Navigation.INavigationStage",
        "Navigation.IRouteStatusSink",
        "Navigation.LocationChangedNotification",
        "Navigation.LocationChangingDecision",
        "Navigation.LocationChangingRequest",
        "Navigation.ModNavigationOptions",
        "Navigation.NavigationResult",
        "Navigation.NavigationStatus",
        "Navigation.NavigationTrigger",
        "Navigation.RouteError",
        "Navigation.RouteErrorKind",
        "Navigation.RouteFailure",
        "Navigation.RouteLevelData",
        "Navigation.RouteNavigationContext",
        "Navigation.RouteState",
        "Navigation.RouteStateStore",
        "Navigation.RouteStatusKind",
        "Navigation.StageResult",
        "RouteMapAuthorizationExtensions",
        "RouteMapPagesExtensions",
        "RouteMapPersistenceExtensions",
        "ScrollOptions",
        "TitleOptions",
        "ViewTransitionOptions",
    ];

    /// <summary>
    /// The public type list is pinned: a type cannot become public (or stop being public) by accident. The per-namespace tests
    /// above cover Compatibility, Authorization and Discovery only; this one covers the whole assembly.
    /// </summary>
    [Test]
    public void The_public_surface_is_exactly_the_reviewed_list()
    {
        var actual = typeof(CompiledRouteTable).Assembly.GetExportedTypes()
            .Select(type => type.FullName!)
            .Order(StringComparer.Ordinal)
            .ToList();
        var expected = ReviewedPublicTypes.Select(name => RouterNamespace + name).ToList();
        var added = actual.Except(expected, StringComparer.Ordinal).ToList();
        var removed = expected.Except(actual, StringComparer.Ordinal).ToList();

        Assert.That(expected, Is.Ordered.Using<string>(StringComparer.Ordinal), "keep ReviewedPublicTypes sorted ordinally");
        Assert.That(added.Concat(removed), Is.Empty,
            $"A public type was added or removed. Update ReviewedPublicTypes in {nameof(ArchitectureTests)} deliberately (and document the new type), "
            + $"or make the type internal. Added: [{string.Join(", ", added)}]. Removed: [{string.Join(", ", removed)}].");
        Assert.That(actual, Is.EqualTo(expected));
    }

    /// <summary>The ASP.NET Core adapter exports one extension class; the sink itself stays internal.</summary>
    [Test]
    public void The_AspNetCore_adapter_exposes_only_its_registration_extension()
    {
        var adapter = typeof(ModRouterAspNetCoreExtensions).Assembly;

        Assert.That(adapter, Is.Not.SameAs(typeof(CompiledRouteTable).Assembly));
        Assert.That(adapter.GetExportedTypes().Select(type => type.FullName), Is.EqualTo(new[] { "ModRouter.ModRouterAspNetCoreExtensions" }));
        Assert.That(typeof(CompiledRouteTable).Assembly.GetReferencedAssemblies().Select(reference => reference.Name), Does.Not.Contain("Microsoft.AspNetCore.Http.Abstractions"),
            "the router package itself knows nothing about HttpContext");
    }

    /// <summary>The compatibility layer is the three built-in look-alikes and nothing else public.</summary>
    [Test]
    public void Compatibility_exposes_only_the_three_built_in_look_alikes()
    {
        var exposed = typeof(CompiledRouteTable).Assembly.GetExportedTypes()
            .Where(t => t.Namespace == typeof(Compatibility.Router).Namespace)
            .Select(t => t.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.That(exposed, Is.EqualTo(new[] { "AuthorizeRouteView", "RouteView", "Router" }));
    }

    static readonly string[] AuthorizationNamespaces = ["Microsoft.AspNetCore.Authorization", "Microsoft.AspNetCore.Components.Authorization"];

    static bool IsAuthorizationLayer(Type type)
        => type.Namespace is { } ns
            && (ns.StartsWith("ModRouter.Authorization", StringComparison.Ordinal)
                || ns.StartsWith("ModRouter.Compatibility", StringComparison.Ordinal));

    static bool IsAuthorizationPackage(Type type)
        => type.Namespace is not null && AuthorizationNamespaces.Any(ns => type.Namespace.StartsWith(ns, StringComparison.Ordinal));

    /// <summary>
    /// Only Authorization/ and Compatibility/ touch Microsoft.AspNetCore.Authorization and Microsoft.AspNetCore.Components.Authorization,
    /// in their members and in the methods they call: the coordinator's re-check reaches the authentication provider through
    /// Authorization/, so an app that never authorizes pays nothing for the package.
    /// </summary>
    [Test]
    public void Only_Authorization_and_Compatibility_use_the_authorization_packages()
    {
        var outside = typeof(CompiledRouteTable).Assembly.GetTypes().Where(t => !IsAuthorizationLayer(t)).ToList();
        var violations = outside
            .SelectMany(type => ReferencedTypes(type).Select(referenced => (type, referenced))
                .Concat(type.GetMethods(AllMembers).Cast<MethodBase>().Concat(type.GetConstructors(AllMembers))
                    .SelectMany(method => CalledMethods(method).Select(called => (type, referenced: called.DeclaringType!)))))
            .Where(pair => IsAuthorizationPackage(pair.referenced))
            .Select(pair => $"{pair.type.FullName} references {pair.referenced.FullName}")
            .Distinct()
            .ToList();

        Assert.That(outside, Is.Not.Empty);
        Assert.That(violations, Is.Empty, string.Join(Environment.NewLine, violations));
        Assert.That(ReferencedTypes(typeof(Authorization.AuthorizeAttributeGuard)).Concat(typeof(Authorization.AuthorizeAttributeGuard).GetMethods(AllMembers).SelectMany(m => CalledMethods(m).Select(c => c.DeclaringType!))).Any(IsAuthorizationPackage), Is.True,
            "the check would not notice a violation if the guard itself did not register as using the packages");
    }

    /// <summary>The public types slice 4b's authorization adds (the fluent extensions live in the root namespace, next to AddModRouter).</summary>
    [Test]
    public void Authorization_exposes_only_the_guard_its_marker_and_the_metadata()
    {
        var exposed = typeof(CompiledRouteTable).Assembly.GetExportedTypes()
            .Where(t => t.Namespace == typeof(Authorization.AuthorizeAttributeGuard).Namespace)
            .Select(t => t.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.That(exposed, Is.EqualTo(new[] { "IAuthorizationRouteGuard", "RouteAllowAnonymousMetadata", "RouteAuthorizeMetadata" }));
        Assert.That(typeof(RouteMapAuthorizationExtensions).Namespace, Is.EqualTo(typeof(ModRouterServiceCollectionExtensions).Namespace), "Authorize() and AllowAnonymous() are reachable from the same using as AddModRouter");
    }

    /// <summary>The public types slice 4a added (AddPages lives in the root namespace); everything else public in the Discovery namespace would be a surface creep.</summary>
    [Test]
    public void Discovery_exposes_only_the_attributes_and_the_page_assembly_collection()
    {
        var exposed = typeof(CompiledRouteTable).Assembly.GetExportedTypes()
            .Where(t => t.Namespace == typeof(Discovery.RouteNameAttribute).Namespace)
            .Select(t => t.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.That(exposed, Is.EqualTo(PublicSurface
            .Where(t => t.Namespace == typeof(Discovery.RouteNameAttribute).Namespace)
            .Select(t => t.Name)
            .Order(StringComparer.Ordinal)));
    }

    /// <summary>
    /// The router's own navigation context is <c>RouteNavigationContext</c>: a public type named <c>NavigationContext</c> is
    /// CS0104-ambiguous with <c>Microsoft.AspNetCore.Components.Routing.NavigationContext</c> in any file that imports both
    /// namespaces (the compatibility layer and every app's <c>App.razor.cs</c> do).
    /// </summary>
    [Test]
    public void The_router_exposes_no_public_type_named_NavigationContext()
    {
        var exported = typeof(CompiledRouteTable).Assembly.GetExportedTypes().Select(t => t.Name).ToList();

        Assert.That(exported, Does.Not.Contain("NavigationContext"));
        Assert.That(exported, Does.Contain(nameof(Navigation.RouteNavigationContext)));
    }

    /// <summary>Every public member of the library is documented; the build enforces it (CS1591) as long as nothing suppresses the warning.</summary>
    [TestCase("ModRouter")]
    [TestCase("ModRouter.AspNetCore")]
    public void The_library_does_not_suppress_missing_XML_comments(string projectName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, projectName, projectName + ".csproj")))
        {
            directory = directory.Parent;
        }

        Assert.That(directory, Is.Not.Null, projectName + ".csproj was not found above the test output directory.");

        var project = File.ReadAllText(Path.Combine(directory!.FullName, projectName, projectName + ".csproj"));

        Assert.That(project, Does.Not.Contain("CS1591"));
        Assert.That(project, Does.Contain("<GenerateDocumentationFile>true</GenerateDocumentationFile>"));
    }

    /// <summary>Only Discovery/ reflects over an assembly's types; nothing else in the library enumerates them.</summary>
    [Test]
    public void Only_Discovery_enumerates_assembly_types()
    {
        var enumerators = new HashSet<string>(StringComparer.Ordinal) { "GetTypes", "GetExportedTypes", "get_ExportedTypes", "get_DefinedTypes", "GetForwardedTypes" };
        var callers = typeof(CompiledRouteTable).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(AllMembers).Cast<MethodBase>().Concat(type.GetConstructors(AllMembers)))
            .Where(method => CalledMethods(method).Any(called => typeof(Assembly).IsAssignableFrom(called.DeclaringType) && enumerators.Contains(called.Name)))
            .Select(method => $"{method.DeclaringType!.FullName}.{method.Name}")
            .Distinct()
            .ToList();

        Assert.That(callers, Is.Not.Empty, "the check would not notice a violation if it saw no caller at all");
        Assert.That(callers, Is.All.StartsWith("ModRouter.Discovery."), string.Join(Environment.NewLine, callers));
    }

    /// <summary>A call that writes a log entry: ILogger(.Log), the LoggerExtensions helpers, or a hand-made LoggerMessage.Define.</summary>
    static bool IsLogWrite(MethodBase called)
        => called.DeclaringType is { Namespace: "Microsoft.Extensions.Logging" } declaring
            && (declaring.Name is "LoggerExtensions" or "LoggerMessage"
                || (declaring.Name is "ILogger" or "ILogger`1" && called.Name is "Log" or "BeginScope"));

    static bool IsRouterLog(Type type)
        => type == typeof(Diagnostics.RouterLog) || (type.DeclaringType is { } outer && IsRouterLog(outer));

    /// <summary>
    /// Every log entry goes through <see cref="Diagnostics.RouterLog"/> (stable ids, fixed levels, no ad hoc message): no
    /// other method of the library calls <c>ILogger.Log</c>, a <c>LoggerExtensions</c> helper or <c>LoggerMessage.Define</c>.
    /// </summary>
    [Test]
    public void Only_RouterLog_writes_log_entries()
    {
        static IEnumerable<string> Writers(IEnumerable<Type> types)
            => types
                .SelectMany(type => type.GetMethods(AllMembers).Cast<MethodBase>().Concat(type.GetConstructors(AllMembers)))
                .Where(method => CalledMethods(method).Any(IsLogWrite))
                .Select(method => $"{method.DeclaringType!.FullName}.{method.Name}")
                .Distinct();

        var types = typeof(CompiledRouteTable).Assembly.GetTypes();
        var outside = Writers(types.Where(t => !IsRouterLog(t))).ToList();

        Assert.That(Writers(types.Where(IsRouterLog)), Is.Not.Empty, "the check would not notice a violation if RouterLog itself did not register as writing");
        Assert.That(outside, Is.Empty, string.Join(Environment.NewLine, outside));
    }

    static readonly Dictionary<short, System.Reflection.Emit.OpCode> OpCodeTable = typeof(System.Reflection.Emit.OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (System.Reflection.Emit.OpCode)field.GetValue(null)!)
        .ToDictionary(code => code.Value);

    /// <summary>The methods <paramref name="method"/> calls directly, read from its IL (compiler-generated bodies included).</summary>
    static IEnumerable<MethodBase> CalledMethods(MethodBase method)
    {
        if (method.GetMethodBody()?.GetILAsByteArray() is not { } il)
        {
            yield break;
        }

        var module = method.Module;
        var typeArguments = method.DeclaringType is { IsGenericType: true } declaring ? declaring.GetGenericArguments() : null;
        var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
        var position = 0;

        while (position < il.Length)
        {
            short value = il[position++];

            if (value == 0xFE)
            {
                value = (short)(0xFE00 | il[position++]);
            }

            var opCode = OpCodeTable[value];
            MethodBase? called = null;

            if (opCode.OperandType is System.Reflection.Emit.OperandType.InlineMethod)
            {
                var token = BitConverter.ToInt32(il, position);

                try
                {
                    called = module.ResolveMethod(token, typeArguments, methodArguments);
                }
                catch (Exception exception) when (exception is ArgumentException or BadImageFormatException)
                {
                    // A token this walker cannot resolve (an open generic in a nested context) is not an assembly enumerator.
                }
            }

            position += opCode.OperandType switch
            {
                System.Reflection.Emit.OperandType.InlineNone => 0,
                System.Reflection.Emit.OperandType.ShortInlineBrTarget or System.Reflection.Emit.OperandType.ShortInlineI or System.Reflection.Emit.OperandType.ShortInlineVar => 1,
                System.Reflection.Emit.OperandType.InlineVar => 2,
                System.Reflection.Emit.OperandType.InlineI8 or System.Reflection.Emit.OperandType.InlineR => 8,
                System.Reflection.Emit.OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, position),
                _ => 4,
            };

            if (called is not null)
            {
                yield return called;
            }
        }
    }

    [Test]
    public void Navigation_plumbing_is_internal_and_the_contract_types_are_public()
    {
        Assert.Multiple(() =>
        {
            foreach (var type in InternalPlumbing)
            {
                Assert.That(type.IsPublic, Is.False, $"{type.Name} should be internal");
            }

            foreach (var type in PublicSurface)
            {
                Assert.That(type.IsPublic, Is.True, $"{type.Name} should be public");
            }

            Assert.That(typeof(Components.RouteOutletContext).GetConstructors(), Is.Empty, "RouteOutletContext is created by ModRouter only; its constructor is internal");
            Assert.That(typeof(Navigation.RouteStateStore).GetMethod("Commit"), Is.Null, "RouteStateStore.Commit stays internal");
            Assert.That(typeof(Navigation.RouteStateStore).GetMethod("SetNavigating"), Is.Null, "RouteStateStore.SetNavigating stays internal");
            Assert.That(typeof(Navigation.RouteError).GetConstructors(), Is.Empty, "RouteError is created by the router only");
            Assert.That(typeof(Core.Compilation.RouteTableHolder).GetConstructors(), Is.Empty, "RouteTableHolder is created by AddModRouter only");
            Assert.That(typeof(Core.Compilation.RouteTableHolder).GetMethod("Freeze"), Is.Null, "only ModRouter freezes the holder");
            Assert.That(typeof(Core.Compilation.RouteTableHolder).GetProperty("StartupDefinitions"), Is.Null, "the startup definitions are ModRouter's input to the markup compile");
            Assert.That(typeof(Core.Compilation.RouteTableHolder).GetProperty("FrozenSignature"), Is.Null, "the signature is the freeze's own bookkeeping");
            Assert.That(typeof(RouteMapPagesExtensions).Namespace, Is.EqualTo(typeof(ModRouterServiceCollectionExtensions).Namespace), "AddPages is reachable from the same using as AddModRouter");
        });
    }

    static bool IsForbidden(Type type)
        => type.Namespace is not null && ForbiddenNamespaces.Any(f => type.Namespace.StartsWith(f, StringComparison.Ordinal));

    static IEnumerable<Type> ReferencedTypes(Type type)
    {
        var found = new List<Type>();

        if (type.BaseType is not null)
        {
            Expand(type.BaseType, found);
        }

        foreach (var contract in type.GetInterfaces())
        {
            Expand(contract, found);
        }

        foreach (var field in type.GetFields(AllMembers))
        {
            Expand(field.FieldType, found);
        }

        foreach (var property in type.GetProperties(AllMembers))
        {
            Expand(property.PropertyType, found);
        }

        foreach (var method in type.GetMethods(AllMembers))
        {
            Expand(method.ReturnType, found);

            foreach (var parameter in method.GetParameters())
            {
                Expand(parameter.ParameterType, found);
            }
        }

        foreach (var constructor in type.GetConstructors(AllMembers))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                Expand(parameter.ParameterType, found);
            }
        }

        return found;
    }

    static void Expand(Type type, List<Type> found)
    {
        found.Add(type);

        if (type.HasElementType && type.GetElementType() is { } element)
        {
            Expand(element, found);
        }

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                Expand(argument, found);
            }
        }
    }
}
