using ModRouter.Core.Constraints;
using ModRouter.Core.Definitions;
using ModRouter.Core.Matching;
using ModRouter.Core.Syntax;

namespace ModRouter.Core.Compilation;

/// <summary>Flattens, validates and indexes route definitions. One instance per compilation.</summary>
internal sealed class RouteTableBuilder(IConstraintRegistry registry)
{
    readonly List<RouteCompilationError> _errors = [];
    readonly HashSet<RouteDefinition> _withErrors = [];
    readonly List<CompiledRoute> _entries = [];
    readonly Dictionary<string, CompiledRoute> _named = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, IRouteConstraint> _constraints = new(StringComparer.Ordinal);

    sealed record ParsedTemplate(IReadOnlyList<TemplateSegment> Segments, IReadOnlyList<IRouteConstraint?> Constraints);

    sealed record OwnVariant(ParsedTemplate Parsed, bool IsAlias);

    /// <summary>A flattened path: the segments so far, the matching constraints, the canonical form and each level's end.</summary>
    sealed record Variant(
        IReadOnlyList<TemplateSegment> Segments,
        IReadOnlyList<IRouteConstraint?> Constraints,
        IReadOnlyList<TemplateSegment> Canonical,
        IReadOnlyList<int> LevelEnds,
        bool IsAlias)
    {
        public static Variant Root { get; } = new([], [], [], [], false);
    }

    public CompiledRouteTable Build(IReadOnlyList<RouteDefinition> roots)
    {
        foreach (var root in roots)
        {
            Visit(root, [], null);
        }

        foreach (var entry in _entries.Where(e => !e.IsAlias && e.Definition.Metadata.Contains<RouteRedirectMetadata>()))
        {
            ValidateRedirect(entry);
        }

        // The trie is always built so every ambiguity is reported together with the other problems. Entries of
        // definitions (or descendants of definitions) that already have an error are left out: they would only
        // produce misleading follow-on ambiguities.
        var trie = new TrieBuilder();

        foreach (var entry in _entries.Where(e => e.IsMatchable && !e.Chain.Any(_withErrors.Contains)))
        {
            trie.Insert(entry);
        }

        trie.Complete();

        foreach (var (first, second) in trie.FindAmbiguities())
        {
            AddError(
                "RT0012",
                $"The routes '{first.Template}' ({RouteDescriptions.Describe(first.Definition)}) and '{second.Template}' can match the same URL with equal precedence. Change one template, add a constraint that tells them apart, or remove one of them.",
                second.Definition,
                other: RouteDescriptions.Describe(first.Definition));
        }

        if (_errors.Count > 0)
        {
            throw new RouteCompilationException(_errors);
        }

        return new CompiledRouteTable(_entries, _named, trie.Complete());
    }

    void Visit(RouteDefinition definition, IReadOnlyList<RouteDefinition> parentChain, IReadOnlyList<Variant>? parentVariants)
    {
        var isRoot = parentVariants is null;
        IReadOnlyList<Variant> parents = parentVariants ?? new[] { Variant.Root };
        IReadOnlyList<RouteDefinition> chain = [.. parentChain, definition];

        if (definition.Name is not null && string.IsNullOrWhiteSpace(definition.Name))
        {
            AddError("RT0013", "The route name is empty or whitespace; use null for an unnamed route or give it a real name.", definition);
        }

        if (definition.Metadata.GetAll<RouteLoaderMetadata>().Skip(1).Any())
        {
            AddError("RT0016", "A route level can have only one loader. Merge the loaders into one, or move one to a child or parent route.", definition);
        }

        if (definition.Metadata.Contains<RoutePersistedDataMetadata>() && !definition.Metadata.Contains<RouteLoaderMetadata>())
        {
            AddError("RT0025", "PersistLoaderData carries the data of the route's loader across the prerender, so the route needs a loader. Add one with Loader<T>() or [RouteLoader<T>], or remove PersistLoaderData.", definition);
        }

        if (definition.Metadata.GetAll<RoutePersistedDataMetadata>().Skip(1).Any())
        {
            AddError("RT0025", "A route level persists the data of its loader only once; remove the extra PersistLoaderData.", definition);
        }

        if (definition.Metadata.Contains<RouteLoaderCacheMetadata>() && !definition.Metadata.Contains<RouteLoaderMetadata>())
        {
            AddError("RT0026", "CacheFor caches the data of the route's loader, so the route needs a loader. Add one with Loader<T>() or [RouteLoader<T>], or remove CacheFor.", definition);
        }

        if (definition.Metadata.GetAll<RouteLoaderCacheMetadata>().Skip(1).Any())
        {
            AddError("RT0026", "A route level caches its loader once; remove the extra CacheFor.", definition);
        }

        ValidateExperienceMetadata(definition);
        ValidateSlots(definition);
        ValidateKeepAlive(definition);

        if (definition.IsIndex)
        {
            VisitIndex(definition, chain, parents, isRoot);

            return;
        }

        if (definition.IsGroup && !ValidateGroup(definition))
        {
            return;
        }

        // A group adds no segment: its one variant is empty, so its children's paths are the parent's plus their own.
        var own = definition.IsGroup ? [new OwnVariant(new ParsedTemplate([], []), false)] : ParseOwnVariants(definition, isRoot);

        if (own is null)
        {
            return;
        }

        ValidateCanonicalAlias(definition, own);

        var combined = Combine(parents, own);

        if (!ValidateCombined(definition, combined))
        {
            return;
        }

        if (definition.Children.Count > 0)
        {
            // Every variant (canonical and alias) is checked: a trailing optional or catch-all in an alias would
            // otherwise register a terminal shorter than a non-leaf level and break matching.
            foreach (var variant in own)
            {
                var last = variant.Parsed.Segments.LastOrDefault();

                if (last is { Kind: TemplateSegmentKind.OptionalParameter or TemplateSegmentKind.CatchAll })
                {
                    AddError("RT0007", $"A route whose template or alias ends with an optional or catch-all parameter ('{last}') cannot have child routes, because that parameter could swallow the child's segments.", definition);
                }
            }

            foreach (var extra in definition.Children.Where(c => c.IsIndex).Skip(1))
            {
                AddError("RT0008", $"The route {RouteDescriptions.Describe(definition)} has more than one index child. Only one index route is allowed per parent; remove or merge {RouteDescriptions.Describe(extra)}.", extra);
            }
        }

        // A default page declared inside a pathless group is still this route's default page.
        var hasIndexChild = definition.Children.Any(ProvidesIndex);

        foreach (var variant in combined)
        {
            AddEntry(definition, chain, variant, matchable: !hasIndexChild && !definition.IsGroup);
        }

        var indexSeen = false;

        foreach (var child in definition.Children)
        {
            if (child.IsIndex)
            {
                if (indexSeen)
                {
                    continue;
                }

                indexSeen = true;
            }

            Visit(child, chain, combined);
        }
    }

    void ValidateKeepAlive(RouteDefinition definition)
    {
        var keeps = definition.Metadata.GetAll<RouteKeepAliveMetadata>().ToList();

        if (keeps.Count > 1)
        {
            AddError("RT0029", "A route keeps its page alive once; remove the extra KeepAlive.", definition);
        }

        if (keeps.Any(k => k.Max < 1))
        {
            AddError("RT0029", "KeepAlive needs a Max of at least 1.", definition);
        }

        if (keeps.Count > 0 && (definition.IsGroup || definition.Metadata.Contains<RouteRedirectMetadata>()))
        {
            AddError("RT0029", "KeepAlive keeps the page of a route alive; a group is only a layout and a redirect renders nothing. Put it on the routes inside the group.", definition);
        }
    }

    void ValidateSlots(RouteDefinition definition)
    {
        var slots = definition.Metadata.GetAll<RouteSlotMetadata>().ToList();

        foreach (var duplicate in slots.GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            AddError("RT0028", $"A route level fills the outlet '{duplicate.Key}' only once; remove the extra Slot.", definition);
        }

        foreach (var slot in slots.Where(s => string.IsNullOrWhiteSpace(s.Name) || !typeof(Microsoft.AspNetCore.Components.IComponent).IsAssignableFrom(s.Component)))
        {
            AddError("RT0028", $"The slot '{slot.Name}' needs a name and a component type that implements IComponent ({slot.Component.FullName} does not).", definition);
        }
    }

    static bool ProvidesIndex(RouteDefinition child) => child.IsIndex || (child.IsGroup && child.Children.Any(ProvidesIndex));

    bool ValidateGroup(RouteDefinition group)
    {
        var valid = true;

        void Fail(string message)
        {
            AddError("RT0027", message, group);
            valid = false;
        }

        if (group.Template.Length != 0)
        {
            Fail($"A group has no path of its own, but its template is '{group.Template}'. Use RouteMap.Group, which leaves it empty.");
        }

        if (group.Children.Count == 0)
        {
            Fail("A group needs at least one child route: it only wraps its children in a layout.");
        }

        if (group.Name is not null || group.Aliases.Count > 0 || group.StaticTitle is not null || group.Title is not null)
        {
            Fail("A group has no URL of its own, so it cannot have a name, aliases or a title; give them to its child routes.");
        }

        if (group.Metadata.Contains<RouteRedirectMetadata>())
        {
            Fail("A group cannot be a redirect; declare the redirect as a route of its own.");
        }

        return valid;
    }

    void ValidateExperienceMetadata(RouteDefinition definition)
    {
        var duplicates = new[]
        {
            (".Breadcrumb(...)", definition.Metadata.GetAll<RouteBreadcrumbMetadata>().Skip(1).Any()),
            (".Focus(...)", definition.Metadata.GetAll<RouteFocusMetadata>().Skip(1).Any()),
            (".NoAnnounce()", definition.Metadata.GetAll<RouteAnnounceMetadata>().Skip(1).Any()),
        };

        foreach (var (call, _) in duplicates.Where(d => d.Item2))
        {
            AddError("RT0018", $"A route can declare {call} only once; the second call would silently replace the first. Remove one of them.", definition);
        }
    }

    void ValidateCanonicalAlias(RouteDefinition definition, List<OwnVariant> own)
    {
        if (definition.Metadata.Get<RouteAliasMetadata>() is not { Behavior: AliasBehavior.RedirectToCanonical })
        {
            return;
        }

        var canonical = ParameterNames(own[0].Parsed.Segments);

        foreach (var variant in own.Skip(1))
        {
            if (!canonical.SetEquals(ParameterNames(variant.Parsed.Segments)))
            {
                AddError(
                    "RT0015",
                    $"The alias '{TemplateText.From(variant.Parsed.Segments)}' must declare exactly the parameters of the canonical template '{TemplateText.From(own[0].Parsed.Segments)}' ({string.Join(", ", canonical.Order())}), because a canonical alias redirects by forwarding them.",
                    definition,
                    template: TemplateText.From(variant.Parsed.Segments));
            }
        }
    }

    void ValidateRedirect(CompiledRoute entry)
    {
        var redirect = entry.Definition.Metadata.GetRequired<RouteRedirectMetadata>();
        var definition = entry.Definition;

        if (definition.Children.Count > 0 || definition.Aliases.Count > 0 || definition.Metadata.Contains<RouteGuardMetadata>() || definition.Metadata.Contains<RouteLoaderMetadata>())
        {
            AddError("RT0017", "A redirect route only forwards to another route: it cannot have children, aliases, a guard or a loader.", definition);

            return;
        }

        if (!_named.TryGetValue(redirect.ToRoute, out var target))
        {
            AddError("RT0017", $"The redirect to the route '{redirect.ToRoute}' has no target: no route has that name. Name the target route, or fix the name.", definition);

            return;
        }

        if (target.Definition.Metadata.Contains<RouteRedirectMetadata>())
        {
            AddError("RT0017", $"The redirect to '{redirect.ToRoute}' targets another redirect. Point it at the final route.", definition);

            return;
        }

        var provided = ParameterNames(entry.Segments);
        var missing = target.Segments
            .Where(s => s.Kind == TemplateSegmentKind.Parameter && !provided.Contains(s.Text))
            .Select(s => s.Text)
            .ToArray();

        if (missing.Length > 0)
        {
            AddError("RT0017", $"The redirect from '{entry.Template}' cannot reach '{redirect.ToRoute}': the required parameter(s) {string.Join(", ", missing.Select(m => $"'{m}'"))} are not in the redirect's template. Add them to the template with the same names.", definition);
        }
    }

    static HashSet<string> ParameterNames(IEnumerable<TemplateSegment> segments)
        => segments.Where(s => s.IsParameter).Select(s => s.Text).ToHashSet(StringComparer.OrdinalIgnoreCase);

    void VisitIndex(RouteDefinition index, IReadOnlyList<RouteDefinition> chain, IReadOnlyList<Variant> parents, bool isRoot)
    {
        if (isRoot)
        {
            AddError("RT0009", "An index route must be a child of another route; a top-level route should use a normal template such as '/'.", index);

            return;
        }

        var valid = true;

        if (index.Template.Length != 0)
        {
            AddError("RT0009", $"An index route has no path of its own, but its template is '{index.Template}'. Leave the template empty (use RouteMap.Index).", index);
            valid = false;
        }

        if (index.Children.Count > 0)
        {
            AddError("RT0009", "An index route cannot have child routes.", index);
            valid = false;
        }

        if (index.Aliases.Count > 0)
        {
            AddError("RT0009", "An index route cannot have aliases; alias its parent instead.", index);
            valid = false;
        }

        if (!valid)
        {
            return;
        }

        foreach (var parent in parents)
        {
            var variant = new Variant(parent.Segments, parent.Constraints, parent.Canonical, [.. parent.LevelEnds, parent.Segments.Count], parent.IsAlias);
            AddEntry(index, chain, variant, matchable: true);
        }
    }

    List<OwnVariant>? ParseOwnVariants(RouteDefinition definition, bool isRoot)
    {
        var result = new List<OwnVariant>();
        var valid = true;

        var canonical = ParseTemplate(definition, definition.Template, isRoot, "template");

        if (canonical is null)
        {
            valid = false;
        }
        else
        {
            result.Add(new OwnVariant(canonical, false));
        }

        foreach (var alias in definition.Aliases)
        {
            var parsed = ParseTemplate(definition, alias, isRoot, "alias");

            if (parsed is null)
            {
                valid = false;
            }
            else
            {
                result.Add(new OwnVariant(parsed, true));
            }
        }

        return valid ? result : null;
    }

    ParsedTemplate? ParseTemplate(RouteDefinition definition, string text, bool isRoot, string what)
    {
        var parsed = TemplateParser.Parse(text);
        var valid = true;

        foreach (var error in parsed.Errors)
        {
            AddError("RT0001", $"The {what} '{text}' is not valid at position {error.Position}: {error.Message}", definition, position: error.Position, template: text);
            valid = false;
        }

        if (!isRoot && parsed.HasLeadingSlash)
        {
            AddError("RT0002", $"The {what} '{text}' of a child route starts with '/'. Child templates are relative to their parent; remove the leading '/'.", definition, template: text);
            valid = false;
        }

        if (isRoot && !parsed.HasLeadingSlash)
        {
            AddError("RT0014", $"The {what} '{text}' of a top-level route must start with '/' (use '/' for the root path). Only child templates are relative.", definition, template: text);
            valid = false;
        }

        if (!isRoot && parsed.Errors.Count == 0 && parsed.Segments.Count == 0)
        {
            AddError("RT0003", $"The {what} '{text}' of a child route is empty, so it would have the same path as its parent. Use RouteMap.Index for a default child.", definition, template: text);
            valid = false;
        }

        if (!valid)
        {
            return null;
        }

        var segments = parsed.Segments;

        for (var i = 0; i < segments.Count; i++)
        {
            if (segments[i].Kind == TemplateSegmentKind.CatchAll && i != segments.Count - 1)
            {
                AddError("RT0005", $"The catch-all parameter '{segments[i]}' must be the last segment of the {what} '{text}'.", definition, template: text);
                valid = false;
            }

            if (segments[i].Kind == TemplateSegmentKind.OptionalParameter && segments.Skip(i + 1).Any(s => s.Kind != TemplateSegmentKind.OptionalParameter))
            {
                AddError("RT0006", $"The optional parameter '{segments[i]}' in the {what} '{text}' is followed by a required segment or a catch-all. Only optional parameters may follow an optional parameter.", definition, template: text);
                valid = false;
            }
        }

        var constraints = new List<IRouteConstraint?>(segments.Count);

        foreach (var segment in segments)
        {
            constraints.Add(segment.Constraints.Count == 0 ? null : ResolveConstraint(definition, text, segment, ref valid));
        }

        return valid ? new ParsedTemplate(segments, constraints) : null;
    }

    IRouteConstraint? ResolveConstraint(RouteDefinition definition, string text, TemplateSegment segment, ref bool valid)
    {
        var key = segment.ConstraintKey;

        if (_constraints.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var parts = new List<IRouteConstraint>();

        foreach (var spec in segment.Constraints)
        {
            if (!registry.TryCreate(spec.ToString(), out var created, out var error))
            {
                AddError("RT0004", $"The constraint '{spec}' on parameter '{segment.Text}' in '{text}' is not valid: {error}", definition, template: text);
                valid = false;

                return null;
            }

            parts.Add(created);
        }

        IRouteConstraint constraint = parts.Count == 1 ? parts[0] : new CompositeConstraint(parts);
        _constraints[key] = constraint;

        return constraint;
    }

    static List<Variant> Combine(IReadOnlyList<Variant> parents, List<OwnVariant> own)
    {
        var combined = new List<Variant>();

        foreach (var parent in parents)
        {
            foreach (var variant in own)
            {
                var segments = new List<TemplateSegment>(parent.Segments);
                segments.AddRange(variant.Parsed.Segments);

                var constraints = new List<IRouteConstraint?>(parent.Constraints);
                constraints.AddRange(variant.Parsed.Constraints);

                var canonical = new List<TemplateSegment>(parent.Canonical);
                canonical.AddRange(own[0].Parsed.Segments);

                combined.Add(new Variant(segments, constraints, canonical, [.. parent.LevelEnds, segments.Count], parent.IsAlias || variant.IsAlias));
            }
        }

        return combined;
    }

    bool ValidateCombined(RouteDefinition definition, List<Variant> combined)
    {
        foreach (var variant in combined)
        {
            var duplicate = variant.Segments
                .Where(s => s.IsParameter)
                .GroupBy(s => s.Text, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(g => g.Count() > 1);

            if (duplicate is null)
            {
                continue;
            }

            AddError("RT0011", $"The parameter '{duplicate.Key}' appears more than once in the full template '{TemplateText.From(variant.Segments)}'. A child route cannot redeclare a parameter of its ancestors; rename one of them.", definition);

            return false;
        }

        return true;
    }

    void AddEntry(RouteDefinition definition, IReadOnlyList<RouteDefinition> chain, Variant variant, bool matchable)
    {
        var levelTemplates = variant.LevelEnds.Select(end => TemplateText.From(variant.Segments.Take(end))).ToArray();
        var entry = new CompiledRoute(
            _entries.Count,
            definition,
            chain,
            variant.Segments,
            variant.Constraints,
            variant.LevelEnds,
            levelTemplates,
            TemplateText.From(variant.Canonical),
            variant.IsAlias,
            matchable);

        _entries.Add(entry);

        if (!variant.IsAlias && definition.Name is not null && !string.IsNullOrWhiteSpace(definition.Name))
        {
            RegisterName(definition, entry);
        }
    }

    void RegisterName(RouteDefinition definition, CompiledRoute entry)
    {
        if (_named.TryGetValue(definition.Name!, out var existing))
        {
            AddError(
                "RT0010",
                $"The route name '{definition.Name}' is already used by {RouteDescriptions.Describe(existing.Definition)}. Route names must be unique (case-insensitive).",
                definition,
                other: RouteDescriptions.Describe(existing.Definition));

            return;
        }

        _named[definition.Name!] = entry;
    }

    void AddError(string code, string message, RouteDefinition definition, string? other = null, int? position = null, string? template = null)
    {
        _withErrors.Add(definition);
        _errors.Add(new RouteCompilationError(
            code,
            $"{message} Route: {RouteDescriptions.Describe(definition)}.",
            definition.Name,
            template ?? definition.Template,
            definition.Source,
            definition.Component,
            other,
            position));
    }
}
