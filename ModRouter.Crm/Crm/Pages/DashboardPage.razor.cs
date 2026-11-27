using ModRouter.Crm.Ui;
using Microsoft.AspNetCore.Components;
using ModRouter.Crm.Data;

namespace ModRouter.Crm.Crm.Pages;

/// <summary>The home page of the CRM: the index route of the CRM layout, at the address "/".</summary>
public partial class DashboardPage : IDisposable
{
    static readonly decimal Max = Enum.GetValues<Stage>().Max(s => CrmData.Accounts.Where(a => a.Stage == s).Sum(a => a.Revenue));

    [Inject]
    Session Session { get; set; } = default!;

    protected override void OnInitialized() => Session.Changed += OnChanged;

    public void Dispose() => Session.Changed -= OnChanged;

    void OnChanged() => _ = InvokeAsync(StateHasChanged);

    static IEnumerable<Account> Top => CrmData.Accounts.OrderByDescending(a => a.Revenue).Take(5);

    static IReadOnlyList<KpiItem> Kpis =>
    [
        new() { Value = Compact(CrmData.Accounts.Where(a => a.Stage is Stage.Proposal or Stage.Qualified).Sum(a => a.Revenue)), Label = "Open pipeline", Icon = Icons.Outlined.ArrowTrending, State = KpiState.Highlight },
        new() { Value = Compact(CrmData.Accounts.Where(a => a.Stage == Stage.Won).Sum(a => a.Revenue)), Label = "Won this year", Icon = Icons.Outlined.Trophy, State = KpiState.Positive },
        new() { Value = CrmData.Accounts.Count.ToString(), Label = "Accounts", Icon = Icons.Outlined.Building },
        new() { Value = CrmData.Contacts.Count.ToString(), Label = "Contacts", Icon = Icons.Outlined.People },
    ];

    static string Compact(decimal value) => value >= 1_000_000 ? $"${value / 1_000_000:0.##}M" : $"${value / 1_000:0}K";

    static int Percent(decimal sum) => (int)(sum / Max * 100);
}
