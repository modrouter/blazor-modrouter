namespace ModRouter.Crm.Data;

public enum Stage { Lead, Qualified, Proposal, Won }

public sealed record Account(int Id, string Name, string Industry, Stage Stage, string Owner, decimal Revenue, string City, int Health);

public sealed record Contact(int Id, int AccountId, string Name, string Title, string Email, string Phone);

public sealed record Activity(int AccountId, string Kind, string Text, string When);

/// <summary>The sample data. Real CRMs call an API; the loaders in this app call this class after a pretend network delay.</summary>
public static class CrmData
{
    public static readonly IReadOnlyList<Account> Accounts =
    [
        new(1, "Northwind Traders", "Retail", Stage.Won, "Ava Chen", 482_000, "Seattle", 92),
        new(2, "Contoso Health", "Healthcare", Stage.Proposal, "Marcus Webb", 310_000, "Austin", 74),
        new(3, "Fabrikam Robotics", "Manufacturing", Stage.Qualified, "Ava Chen", 205_000, "Detroit", 68),
        new(4, "Tailspin Air", "Travel", Stage.Lead, "Priya Nair", 96_000, "Denver", 51),
        new(5, "Adventure Works", "Retail", Stage.Won, "Marcus Webb", 640_000, "Portland", 88),
        new(6, "Litware Energy", "Energy", Stage.Proposal, "Priya Nair", 420_000, "Houston", 79),
        new(7, "Wingtip Toys", "Retail", Stage.Qualified, "Ava Chen", 133_000, "Chicago", 63),
        new(8, "Proseware Finance", "Finance", Stage.Lead, "Marcus Webb", 58_000, "Boston", 47),
        new(9, "Lucerne Publishing", "Media", Stage.Won, "Priya Nair", 274_000, "New York", 85),
        new(10, "Wide World Importers", "Logistics", Stage.Proposal, "Ava Chen", 361_000, "Miami", 71),
        new(11, "Alpine Ski House", "Travel", Stage.Qualified, "Marcus Webb", 112_000, "Salt Lake City", 66),
        new(12, "Coho Winery", "Food", Stage.Lead, "Priya Nair", 41_000, "Napa", 44),
    ];

    public static readonly IReadOnlyList<Contact> Contacts =
    [
        new(1, 1, "Robin Calloway", "VP Purchasing", "robin@northwind.example", "+1 206 555 0141"),
        new(2, 1, "Daniel Okafor", "Head of Ops", "daniel@northwind.example", "+1 206 555 0178"),
        new(3, 2, "Mei Tanaka", "Chief Medical Officer", "mei@contoso.example", "+1 512 555 0112"),
        new(4, 2, "Luis Ortega", "IT Director", "luis@contoso.example", "+1 512 555 0190"),
        new(5, 3, "Hannah Brandt", "Plant Manager", "hannah@fabrikam.example", "+1 313 555 0164"),
        new(6, 4, "Tom Reilly", "Fleet Planner", "tom@tailspin.example", "+1 303 555 0123"),
        new(7, 5, "Isabel Moreau", "Buyer", "isabel@adventure-works.example", "+1 503 555 0199"),
        new(8, 5, "Kofi Mensah", "Finance Lead", "kofi@adventure-works.example", "+1 503 555 0102"),
        new(9, 6, "Sara Lindqvist", "Procurement", "sara@litware.example", "+1 713 555 0136"),
        new(10, 7, "Jonas Weber", "Owner", "jonas@wingtip.example", "+1 312 555 0155"),
        new(11, 8, "Nadia Haddad", "CFO", "nadia@proseware.example", "+1 617 555 0171"),
        new(12, 9, "Oscar Lindgren", "Managing Editor", "oscar@lucerne.example", "+1 212 555 0148"),
        new(13, 10, "Camila Duarte", "Logistics Lead", "camila@wideworld.example", "+1 305 555 0187"),
        new(14, 11, "Ben Fraser", "General Manager", "ben@alpine.example", "+1 801 555 0116"),
        new(15, 12, "Lena Rossi", "Founder", "lena@coho.example", "+1 707 555 0133"),
    ];

    public static Account? Account(int id) => Accounts.FirstOrDefault(a => a.Id == id);

    public static Contact? Contact(int id) => Contacts.FirstOrDefault(c => c.Id == id);

    public static IEnumerable<Contact> ContactsOf(int accountId) => Contacts.Where(c => c.AccountId == accountId);

    public static IEnumerable<Activity> ActivityOf(int accountId)
    {
        var a = Account(accountId);

        if (a is null)
        {
            yield break;
        }

        yield return new(accountId, "Call", $"Check-in call with {ContactsOf(accountId).FirstOrDefault()?.Name ?? "the team"} about renewal scope.", "2 days ago");
        yield return new(accountId, "Email", $"Sent the {a.Stage} summary and next steps.", "5 days ago");
        yield return new(accountId, "Meeting", $"Onsite workshop in {a.City}.", "2 weeks ago");
        yield return new(accountId, "Note", $"{a.Owner} flagged a budget review for next quarter.", "3 weeks ago");
    }
}
