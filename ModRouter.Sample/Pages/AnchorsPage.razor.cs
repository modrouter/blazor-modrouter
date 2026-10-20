namespace ModRouter.Sample.Pages;

public partial class AnchorsPage
{
    bool _ready;

    protected override async Task OnInitializedAsync()
    {
        await Task.Delay(400);
        _ready = true;
    }
}
