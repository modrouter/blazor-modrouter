using ModRouter.Core.Definitions;
using ModRouter.Lab.Lessons.L1;
using ModRouter.Lab.Lessons.L3;
using ModRouter.Lab.Lessons.L4;
using ModRouter.Lab.Lessons.L5;
using ModRouter.Lab.Lessons.L6;
using ModRouter.Lab.Lessons.L7;
using ModRouter.Lab.Lessons.L8;
using ModRouter.Lab.Lessons.L9;
using ModRouter.Lab.Pages;

namespace ModRouter.Lab;

/// <summary>
/// The whole route table of the lab. Each lesson adds a route subtree below <c>/lessons</c>.
/// A route has a template, the component that renders it and, usually, a name. <c>children:</c> nests routes: the child's
/// template is relative to its parent's, and the child renders where the parent places a <c>ModRouterOutlet</c>.
/// </summary>
public static class LabRoutes
{
    public static void Declare(RouteMap routes)
    {
        routes.Route("/", typeof(HomePage), name: "home", title: "Start");

        // Lesson 1: routes inside routes.
        routes.Route("/lessons/routes", typeof(Lesson1Page), name: "l1", title: "Pages inside pages", children: lesson => lesson
            .Index(typeof(L1Welcome), name: "hangar-start")
            .Route("fleet", typeof(FleetLayout), name: "fleet", title: "Fleet", children: fleet => fleet
                .Index(typeof(FleetList), name: "fleet-list")
                .Route("{id:int}", typeof(ShipPage), p => $"Ship {p["id"]}", name: "ship", children: ship => ship
                    .Index(typeof(ShipOverview), name: "ship-overview")
                    .Route("crew", typeof(ShipCrew), name: "ship-crew", title: "Crew")
                    .Route("log", typeof(ShipLog), name: "ship-log", title: "Log"))
                .Breadcrumb("Fleet")));

        // Lesson 2 is declared on its pages with @page and [RouteName]; AddPages reads those attributes (see Lessons/L2).
        routes.AddPages(typeof(Program).Assembly);

        // Lesson 3: four routes compete for /atlas/... and the router ranks them.
        routes.Route("/lessons/matching", typeof(Lesson3Page), name: "l3", title: "Which route wins?", children: lesson => lesson
            .Route("atlas/new", typeof(AtlasNew), name: "atlas-new", title: "New region")
            .Route("atlas/{id:int}", typeof(AtlasById), p => $"Region {p["id"]}", name: "atlas-id")
            .Route("atlas/{slug}", typeof(AtlasBySlug), p => $"{p["slug"]}", name: "atlas-slug")
            .Route("atlas/{*rest}", typeof(AtlasRest), name: "atlas-rest", title: "Deep address"));

        // Lesson 4: guards decide whether a navigation may go on. A redirect rule forwards one address to a named route.
        routes.Route("/lessons/guards", typeof(Lesson4Page), name: "l4", title: "Gates", children: lesson => lesson
            .Route("dock", typeof(DockPage), name: "gate-dock", title: "Dock")
            .Route("bridge", typeof(BridgePage), name: "gate-bridge", title: "Bridge").Guard<CrewOnlyGuard>()
            .Route("vault", typeof(VaultPage), name: "gate-vault", title: "Vault").Guard<CaptainOnlyGuard>()
            .Route("airlock", typeof(AirlockPage), name: "gate-airlock", title: "Airlock").Guard<SealedGuard>()
            .Redirect("old-bridge", "gate-bridge"));

        // Lesson 5: a loader fetches the data before the page shows. CacheFor keeps the answer: fresh for 8 s, then usable for 30 s more.
        // The live feed is a child with its own loader; deferred values and streams are never cached.
        routes.Route("/lessons/loaders", typeof(Lesson5Page), name: "l5", title: "Data in flight", children: lesson => lesson
            .Index(typeof(PlanetList), name: "planet-list")
            .Route("{id:int}", typeof(PlanetPage), p => $"Planet {p["id"]}", name: "planet", children: planet => planet
                .Index(typeof(PlanetSummary), name: "planet-summary")
                .Route("live", typeof(PlanetLive), name: "planet-live", title: "Live feed").Loader<LiveFeedLoader>())
                .Loader<PlanetLoader>()
                .CacheFor(PlanetCache.Fresh, PlanetCache.Stale));

        // Lesson 6: a leave guard is asked before the page on screen is left.
        routes.Route("/lessons/leave", typeof(Lesson6Page), name: "l6", title: "Are you sure?", children: lesson => lesson
            .Index(typeof(LogEditor), name: "log-editor").LeaveGuard<UnsavedDraftGuard>()
            .Route("archive", typeof(LogArchive), name: "log-archive", title: "Archive"));

        // Lesson 7: a group shares a layout without adding to the address; a slot fills the layout's named outlet.
        routes.Route("/lessons/layout", typeof(Lesson7Page), name: "l7", title: "Shared layouts and side panels", children: lesson => lesson
            .Index(typeof(L7Intro), name: "console-start")
            .Group(typeof(ConsoleLayout), console => console
                .Route("radar", typeof(RadarPage), name: "radar", title: "Radar").Slot("side", typeof(RadarInspector))
                .Route("comms", typeof(CommsPage), name: "comms", title: "Comms").Slot("side", typeof(CommsInspector))
                .Route("power", typeof(PowerPage), name: "power", title: "Power")));

        // Lesson 8: KeepAlive keeps a page, with its state, while the user is elsewhere. The observatory is not kept.
        routes.Route("/lessons/alive", typeof(Lesson8Page), name: "l8", title: "Pages that stay alive", children: lesson => lesson
            .Route("reactor", typeof(ReactorPage), name: "reactor", title: "Reactor").KeepAlive(3)
            .Route("hangar", typeof(HangarPage), name: "hangar", title: "Hangar").KeepAlive(3)
            .Route("observatory", typeof(ObservatoryPage), name: "observatory", title: "Observatory"));

        // Lesson 9: nothing to declare. The flight recorder reads what the router already keeps.
        routes.Route("/lessons/recorder", typeof(Lesson9Page), name: "l9", title: "The flight recorder");
    }
}
