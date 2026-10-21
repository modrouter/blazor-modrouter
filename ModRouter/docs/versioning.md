# Versioning and releases

How ModRouter is versioned, what a breaking change is, and how a release is cut. Back to the [README](../README.md).

## Packages and version

`ModRouter` and `ModRouter.AspNetCore` are released together under one version (`Version` in `ModRouter.Package.props`). The typed-route source
generator ships inside the `ModRouter` package.

The version follows [Semantic Versioning 2.0.0](https://semver.org). Until 1.0.0 the line is a preview: a minor version may break the public API (the
changelog says so under "Changed" or "Removed"), a patch version only fixes. From 1.0.0 a breaking change needs a major version. Pre-releases use
`-preview.N`, then `-rc.N`, then the stable version.

## What is the public API

- **Types and members.** Every public type is pinned by `ArchitectureTests`: the test fails when a type becomes public or stops being public until
  `ReviewedPublicTypes` is edited on purpose, so each addition or removal is a reviewed decision. Removing or renaming a public member, changing its
  signature, or changing what it does in a way the [compatibility matrix](compatibility.md) or the documentation promises is a breaking change.
  Internal types are not API (`InternalsVisibleTo` is for the test project only).
- **Diagnostics.** The `RT####` and `MRG###` codes are stable and never reused for another problem; their message text is not API. Log event ids and the
  `ActivitySource` name in [Observability](observability.md) are stable; adding an event is not breaking.
- **Browser contract.** The asset path `_content/ModRouter/mod-router.js`, the `data-mod-*` attributes the router renders (`data-mod-router-announcer`,
  `data-mod-route-error-host`, `data-mod-keep-alive`) and the CSS class `mod-route-link-active` are stable. The module's exported functions are not API.
- **Behavior defaults.** Changing a default option value (focus, scroll, announcements, cache sizes) is a breaking change after 1.0.

## Deprecation

A member or type is first marked `[Obsolete]` with a message that names the replacement (and a diagnostic id for a type that has call sites to find),
and keeps working for at least one minor version; it is removed in the next major (in a preview line, the next minor). The changelog lists it under "Deprecated".

## Supported frameworks

The packages target every .NET release that is in Microsoft's support window when the version ships: now `net8.0`, `net9.0` and `net10.0`, each built
against the first release of its own ASP.NET Core major so a consumer is asked for no more than its major needs. The suite runs on all three. .NET 8 reaches
end of support in November 2026; a TFM is dropped in a minor version of the preview line (a major after 1.0), announced in the changelog one release earlier.
Behavior that differs by framework is listed in the README's requirements.

## Cutting a release

1. Update `CHANGELOG.md`: move "Unreleased" under the new version with its date.
2. Set `Version` in `ModRouter.Package.props`.
3. Build and test on all three frameworks (`.github/workflows/ci.yml` runs exactly this) and pack both packages in Release.
4. Check the packed `ModRouter` package: `lib/` for the three frameworks, `analyzers/dotnet/cs/ModRouter.Generators.dll`, `staticwebassets/mod-router.js`,
   `README.md`, `icon.png`, and no dependency on anything but the ASP.NET Core Components packages.
5. Tag the commit `modrouter-v{version}` and push the tag. `.github/workflows/release.yml` then builds, tests, packs and checks the packages again, pushes
   the `.nupkg` and `.snupkg` files to nuget.org and creates a GitHub release whose notes are the changelog section. The workflow refuses a tag that does
   not match `Version` or a version without a changelog section. A pre-release version (one with a `-`) is marked as a pre-release.

To rehearse a release without publishing, run the **Release** workflow by hand from the Actions tab: it does everything except the push and the release.

### One-time setup

The release workflow publishes with [NuGet trusted publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing), so no API key is stored anywhere.

1. Create the account at nuget.org. Check that the package ids `ModRouter` and `ModRouter.AspNetCore` are free before the first release. A published version
   cannot be deleted or reused (it can be unlisted), so check the version too.
2. At nuget.org, open your profile name, then **Trusted Publishing**, and add a policy for the owner of the packages (you, or an organization):
   repository owner `modrouter`, repository `blazor-modrouter`, workflow file `release.yml` (the file name only), no environment, and the scopes
   to push new packages and new package versions for the glob `ModRouter*`.
3. Store your nuget.org **profile name** (not your email address) as the repository secret `NUGET_USER` (Settings, Secrets and variables, Actions).

When the workflow runs for a tag, GitHub issues it a short-lived OIDC token, nuget.org checks it against the policy and returns an API key that is valid for
one hour. The workflow asks for it right before the push. A policy for a private repository starts as active for 7 days and becomes permanent after its first
successful publish; this repository is public, so that does not apply.
