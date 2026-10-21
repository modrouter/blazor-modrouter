namespace ModRouter.Lab.Services;

/// <summary>Which lessons the visitor has opened, so the lesson line can light the stations already passed.</summary>
public sealed class LessonProgress
{
    readonly HashSet<int> _visited = [];

    public bool Visited(int number) => _visited.Contains(number);

    public event Action? Changed;

    public void Visit(int number)
    {
        if (_visited.Add(number))
        {
            Changed?.Invoke();
        }
    }
}
