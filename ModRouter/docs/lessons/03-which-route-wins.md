# Lesson 3: Which route wins?

Type any address and watch every candidate route get ranked. The router never guesses. Back to the [guided lessons](../lessons.md).

**Live demo:** [open lesson 3 in the lab](https://modrouter.github.io/blazor-modrouter/lab/lessons/matching)

## See it

1. Press `new`. A route made only of fixed text beats every route with a placeholder.
2. Press `42`. Both `{id:int}` and `{slug}` fit, and the one with a rule wins.
3. Press `andromeda`. The `int` rule fails, so the plain placeholder takes it.
4. Press `42/notes/7`. Only the catch-all is long enough.
5. Type your own address and press **Go there** to see the page the winner renders.

## Read it

Four routes compete for the addresses under `/atlas/`:

```csharp
// ModRouter.Lab/LabRoutes.cs
routes.Route("/lessons/matching", typeof(Lesson3Page), name: "l3", title: "Which route wins?", children: lesson => lesson
    .Route("atlas/new", typeof(AtlasNew), name: "atlas-new", title: "New region")
    .Route("atlas/{id:int}", typeof(AtlasById), p => $"Region {p["id"]}", name: "atlas-id")
    .Route("atlas/{slug}", typeof(AtlasBySlug), p => $"{p["slug"]}", name: "atlas-slug")
    .Route("atlas/{*rest}", typeof(AtlasRest), name: "atlas-rest", title: "Deep address"));
```

When several routes fit one address, the router prefers them in this order, one segment of the address at a time:

| Rank | Kind of segment | Example |
|---|---|---|
| 1 | Fixed text | `new` |
| 2 | Placeholder with a rule | `{id:int}` |
| 3 | Placeholder without a rule | `{slug}` |
| 4 | Catch-all | `{*rest}` |

The order never depends on the order you declared the routes in. Move the lines around and nothing changes.

### Asking the router to explain

`Explain` evaluates an address against the whole route table and returns a verdict for every candidate. It does not navigate and does not render anything:

```csharp
// ModRouter.Lab/Lessons/L3/Lesson3Page.razor.cs
var explanation = Holder.Table.Explain($"{Prefix}/{Rest.TrimStart('/')}");
```

Each candidate carries one of four verdicts, which the lab draws as the ladder:

| Verdict | Meaning | In the lab |
|---|---|---|
| `Matched`, and selected | The winner | Amber, first |
| `LostOnPrecedence` | Matches, but a better route wins, with the reason | Violet |
| `ConstraintFailed` | The shape fits but a rule on a value failed | Rose |
| `SegmentMismatch` | The address does not fit this shape | Dim |

Read the reasons. For `42` the router says the winner "has a constrained parameter, which ranks before a parameter without a constraint". That is the table above in a sentence.

`Explain` takes an address relative to the app, without the base path, query or fragment. See [Routing](../routing.md) for the details.

### Rules, called constraints

A rule after the colon limits what a placeholder accepts: `{id:int}`, `{year:int}`, `{name:minlength(3)}`, `{when:datetime}`. The built-in set is listed in [Routing](../routing.md). A value that breaks a rule does not match the route, which is why `andromeda` falls through to `{slug}`.

### Ambiguity is a startup error

If two routes have the same shape, nothing could rank them. ModRouter does not pick one. It stops the app at startup with a code and a message. That is better than discovering it when a visitor opens the address.

## Your turn

1. Add a route that has the same shape as the `int` one:

   ```csharp
   .Route("atlas/{code:int}", typeof(AtlasById), name: "atlas-code")
   ```

2. Start the lab. It fails with `RT0012`, naming both templates. Remove the line.
3. Now add `atlas/{year:int}/{month:int?}` and press `2024` and `2024/5`. This table compiles, because the router ranks the two routes: `2024` goes to `{id:int}`, where the value is plain, and `2024/5` goes to the year and month route. The ladder shows the year route losing on precedence for `2024`. Read its reason.

## What you learned

- The router ranks routes by how specific each segment is, never by declaration order.
- `Explain` shows every candidate and why each lost.
- Rules on placeholders decide whether a route fits.
- Routes that cannot be ranked stop the app at startup.

Next: [Lesson 4, Gates](04-gates.md).
