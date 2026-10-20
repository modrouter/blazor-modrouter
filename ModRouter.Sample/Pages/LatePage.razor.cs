namespace ModRouter.Sample.Pages;

public partial class LatePage
{
    bool _ready;

    protected override async Task OnInitializedAsync()
    {
        await Task.Delay(500);
        _ready = true;
    }
}
