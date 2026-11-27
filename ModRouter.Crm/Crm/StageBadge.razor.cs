using Microsoft.AspNetCore.Components;
using ModRouter.Crm.Data;
using ModRouter.Crm.Ui;

namespace ModRouter.Crm.Crm;

/// <summary>A deal stage as a coloured badge.</summary>
public partial class StageBadge
{
    /// <summary>The stage to show.</summary>
    [Parameter]
    public Stage Stage { get; set; }

    Color ColorOf => Stage switch { Stage.Won => Color.Success, Stage.Proposal => Color.Warning, Stage.Qualified => Color.Brand, _ => Color.Info };
}
