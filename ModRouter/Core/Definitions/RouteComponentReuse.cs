namespace ModRouter.Core.Definitions;

/// <summary>How an outlet treats the component of a route when navigation keeps that route level. Applied in slice 2.</summary>
public enum RouteComponentReuse
{
    /// <summary>Keep the instance while the component type is unchanged; update parameters in place.</summary>
    ReuseWhenComponentTypeMatches,

    /// <summary>Create a new instance whenever the route level is matched again after any URL change.</summary>
    RecreateWhenRouteChanges,

    /// <summary>Create a new instance whenever this level's parameter values change.</summary>
    RecreateWhenParametersChange,

    /// <summary>Always create a new instance on every navigation.</summary>
    NeverReuse,
}
