namespace ModRouter.Core.Definitions;

/// <summary>
/// Marks the loader of a route level as persisting its critical data across the prerender to interactive handover
/// (<c>PersistLoaderData&lt;T&gt;()</c> or <c>[RoutePersistLoaderData&lt;T&gt;]</c>). <see cref="Persistence"/> is the typed
/// codec the navigation layer reads and writes; the definitions layer only needs to know that the level opted in, and with
/// which type.
/// </summary>
/// <param name="DataType">The type of the data the loader returns.</param>
/// <param name="Persistence">The codec of <paramref name="DataType"/> (opaque here, so this layer stays Blazor-free).</param>
internal sealed record RoutePersistedDataMetadata(Type DataType, object Persistence);
