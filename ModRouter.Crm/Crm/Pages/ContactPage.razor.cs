using Microsoft.AspNetCore.Components;
using ModRouter.Crm.Data;

namespace ModRouter.Crm.Crm.Pages;

/// <summary>One contact. The template contacts/{id:int} gives this page its <see cref="Id"/> as an int.</summary>
public partial class ContactPage
{
    /// <summary>The {id} of the address, already converted to an int.</summary>
    [Parameter]
    public int Id { get; set; }

    Contact? Contact => CrmData.Contact(Id);
}
