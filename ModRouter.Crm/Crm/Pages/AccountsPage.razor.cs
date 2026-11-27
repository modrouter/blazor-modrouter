using Microsoft.AspNetCore.Components;
using ModRouter.Crm.Guide;
using ModRouter.Crm.Data;
using ModRouter.Navigation;

namespace ModRouter.Crm.Crm.Pages;

/// <summary>
/// The account list. Its search, stage filter and sort are read from the query string, so the list is a function of the address.
/// </summary>
public partial class AccountsPage
{
    /// <summary>Free-text search over name, industry and owner.</summary>
    [Parameter, SupplyParameterFromQuery(Name = "q")]
    public string? Q { get; set; }

    /// <summary>A stage name to filter by.</summary>
    [Parameter, SupplyParameterFromQuery(Name = "stage")]
    public string? Stage { get; set; }

    /// <summary>"revenue" sorts by revenue; anything else sorts by name.</summary>
    [Parameter, SupplyParameterFromQuery(Name = "sort")]
    public string? Sort { get; set; }

    [Inject]
    Tour Tour { get; set; } = default!;

    [Inject]
    INavigationService Navigation { get; set; } = default!;

    IReadOnlyList<Account> Rows
    {
        get
        {
            var rows = CrmData.Accounts.AsEnumerable();

            if (Tour.Has(4))
            {
                if (!string.IsNullOrWhiteSpace(Q))
                {
                    rows = rows.Where(a => $"{a.Name} {a.Industry} {a.Owner}".Contains(Q, StringComparison.OrdinalIgnoreCase));
                }

                if (Enum.TryParse<Stage>(Stage, true, out var stage))
                {
                    rows = rows.Where(a => a.Stage == stage);
                }

                rows = Sort == "revenue" ? rows.OrderByDescending(a => a.Revenue) : rows.OrderBy(a => a.Name);
            }

            return rows.ToList();
        }
    }

    static string Chip(bool on) => "rounded-circular border px-3 py-1 text-sm font-medium no-underline transition-colors "
        + (on ? "border-brandStroke bg-brandBackground/10 text-brandForegroundLink" : "border-stroke2 bg-background text-foreground2 hover:bg-subtleBackgroundHover");

    // The query of a link: the value for one key changed and the current values of the others kept. A null value drops the key.
    Dictionary<string, object?> WithStage(string? stage) => new() { ["q"] = Q, ["stage"] = stage, ["sort"] = Sort };

    Dictionary<string, object?> WithSort(string? sort) => new() { ["q"] = Q, ["stage"] = Stage, ["sort"] = sort };

    void Search(ChangeEventArgs e)
    {
        var text = e.Value?.ToString();
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(text))
        {
            parts.Add($"q={Uri.EscapeDataString(text.Trim())}");
        }

        if (Stage is not null)
        {
            parts.Add($"stage={Uri.EscapeDataString(Stage)}");
        }

        if (Sort is not null)
        {
            parts.Add($"sort={Uri.EscapeDataString(Sort)}");
        }

        Navigation.NavigateTo("accounts" + (parts.Count > 0 ? "?" + string.Join('&', parts) : string.Empty));
    }
}
