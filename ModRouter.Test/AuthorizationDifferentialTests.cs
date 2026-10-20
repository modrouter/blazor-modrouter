using System.Reflection;
using Bunit;
using ModRouter.Authorization;
using ModRouter.Core.Guards;
using ModRouter.Test.AuthorizationPages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace ModRouter.Test;

/// <summary>
/// The guard against the built-in <c>AuthorizeRouteView</c>: twenty-five page classes (every attribute shape) by
/// thirteen users, with and without a fallback policy, 650 cells. The built-in is rendered in bUnit with the real
/// authorization services and its decision is whether the page or the not-authorized marker rendered (an exception is a
/// cell too); the guard decides the same page with the same services. Every cell must agree, exceptions included.
/// </summary>
[TestFixture]
public sealed class AuthorizationDifferentialTests
{
    static readonly Type[] Pages = typeof(PageBase).Assembly.GetTypes()
        .Where(t => t.Namespace == typeof(PageBase).Namespace && !t.IsAbstract && t.Name.Length > 2 && t.Name[0] == 'P' && char.IsDigit(t.Name[1]))
        .OrderBy(t => t.Name, StringComparer.Ordinal)
        .ToArray();

    static string Classify(string markup) => markup.Contains("AUTHORIZING") ? "AUTHORIZING" : markup.Contains("DENIED") ? "D" : markup.Contains("PAGE") ? "A" : "?" + markup;

    static RenderFragment BuiltInTree(Type page) => builder =>
    {
        builder.OpenComponent<CascadingAuthenticationState>(0);
        builder.AddComponentParameter(1, nameof(CascadingAuthenticationState.ChildContent), (RenderFragment)(inner =>
        {
            inner.OpenComponent<AuthorizeRouteView>(0);
            inner.AddComponentParameter(1, nameof(AuthorizeRouteView.RouteData), new RouteData(page, new Dictionary<string, object?>()));
            inner.AddComponentParameter(2, nameof(AuthorizeRouteView.NotAuthorized), (RenderFragment<AuthenticationState>)(_ => b => b.AddMarkupContent(0, "<p>DENIED</p>")));
            inner.AddComponentParameter(3, nameof(AuthorizeRouteView.Authorizing), (RenderFragment)(b => b.AddMarkupContent(0, "<p>AUTHORIZING</p>")));
            inner.CloseComponent();
        }));
        builder.CloseComponent();
    };

    static string Describe(Exception exception)
    {
        while (exception.InnerException is { } inner && (exception is AggregateException or TargetInvocationException || exception.GetType().Name.Contains("Unhandled", StringComparison.Ordinal)))
        {
            exception = inner;
        }

        return $"X:{exception.GetType().Name}:{exception.Message}";
    }

    /// <summary>"A" (the page rendered), "D" (the not-authorized content rendered) or "X:&lt;type&gt;:&lt;message&gt;".</summary>
    static async Task<string> BuiltInAsync(Type page, FakeAuthenticationStateProvider provider, bool fallbackPolicy)
    {
        using var ctx = new BunitContext();
        ctx.Services.AddTestAuthorization(provider, fallbackPolicy);

        try
        {
            var cut = ctx.Render(BuiltInTree(page));

            for (var i = 0; i < 100 && (cut.Markup.Contains("AUTHORIZING") || cut.Markup.Trim().Length == 0); i++)
            {
                await Task.Delay(20);
            }

            return Classify(cut.Markup);
        }
        catch (Exception exception)
        {
            return Describe(exception);
        }
    }

    static async Task<string> GuardAsync(Type page, IServiceProvider services)
    {
        try
        {
            var result = await new AuthorizeAttributeGuard().CanActivateAsync(AuthorizationKit.GuardContext(page, services));

            return result.Outcome == RouteGuardOutcome.Allow ? "A" : "D";
        }
        catch (Exception exception)
        {
            return Describe(exception);
        }
    }

    [Test]
    public void The_matrix_covers_the_spikes_twenty_five_page_classes()
        => Assert.That(Pages.Select(p => p.Name[..3]), Is.EqualTo(Enumerable.Range(1, 25).Select(n => $"P{n:00}")));

    [Test]
    public async Task The_guard_decides_every_cell_like_the_built_in_AuthorizeRouteView()
    {
        var cells = 0;
        var disagreements = new List<string>();
        var decisions = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var fallbackPolicy in new[] { false, true })
        {
            foreach (var (name, user) in Users.All)
            {
                var provider = new FakeAuthenticationStateProvider(user);
                await using var services = new ServiceCollection().AddTestAuthorization(provider, fallbackPolicy).BuildServiceProvider();

                foreach (var page in Pages)
                {
                    cells++;
                    var builtIn = await BuiltInAsync(page, provider, fallbackPolicy);
                    var guard = await GuardAsync(page, services);
                    decisions[$"{page.Name}/{name}/fallback={fallbackPolicy}"] = builtIn;

                    if (!string.Equals(builtIn, guard, StringComparison.Ordinal))
                    {
                        disagreements.Add($"{page.Name} user={name} fallback={fallbackPolicy}: built-in {builtIn}, guard {guard}");
                    }
                }
            }
        }

        Assert.That(cells, Is.EqualTo(650));
        Assert.That(disagreements, Is.Empty, string.Join(Environment.NewLine, disagreements));

        // The cells the spike singled out, so the matrix is known to exercise them (and the harness to see exceptions).
        Assert.That(decisions["P20_SchemesOnly/Auth/fallback=False"], Is.EqualTo("X:NotSupportedException:The authorization data specifies an authentication scheme with value 'Cookies'. Authentication schemes cannot be specified for components."));
        Assert.That(decisions["P14_MissingPolicy/A/fallback=False"], Is.EqualTo("X:InvalidOperationException:The AuthorizationPolicy named: 'Missing' was not found."));
        Assert.That(decisions["P23_RolesEmptyString/A/fallback=True"], Is.EqualTo("X:InvalidOperationException:At least one role must be specified."));
        Assert.That(decisions["P01_NoAttrs/Anon/fallback=True"], Is.EqualTo("A"), "no fallback policy applies to a page without attributes");
        Assert.That(decisions["P16_BaseAnon_DerivedAuth/Anon/fallback=False"], Is.EqualTo("A"), "a base [AllowAnonymous] wins over the derived [Authorize]");
        Assert.That(decisions["P03_RolesA/UnauthRoleA/fallback=False"], Is.EqualTo("A"), "a roles-only [Authorize] admits an unauthenticated identity with the role claim");
        Assert.That(decisions["P18_ResourceSensitive/A/fallback=False"], Is.EqualTo("D"), "the resource is null, so the handler never succeeds");
        Assert.That(decisions.Values.Count(d => d == "A"), Is.EqualTo(2 * 125), "the spike's matrix: 125 allowed cells per fallback setting");
        Assert.That(decisions.Values.Count(d => d.StartsWith("X:", StringComparison.Ordinal)), Is.EqualTo(2 * 3 * 13), "P14, P20 and P23 throw for every user");
    }
}
