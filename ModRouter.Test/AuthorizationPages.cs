// The page classes of the differential authorization matrix (the spike's twenty-five), one per attribute shape the real
// apps use or could use. They have no [Route]: discovery never sees them.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace ModRouter.Test.AuthorizationPages;

public abstract class PageBase : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "p");
        builder.AddContent(1, "PAGE");
        builder.CloseElement();
    }
}

public sealed class P01_NoAttrs : PageBase;

[Authorize]
public sealed class P02_Authorize : PageBase;

[Authorize(Roles = "A")]
public sealed class P03_RolesA : PageBase;

[Authorize(Roles = "B")]
public sealed class P04_RolesB : PageBase;

[Authorize(Roles = "A,B")]
public sealed class P05_RolesAcommaB : PageBase;

[Authorize(Roles = "IT Admin, Capital Markets")]
public sealed class P06_RolesSpaces : PageBase;

[Authorize(Policy = "NeedsX")]
public sealed class P07_PolicyX : PageBase;

[Authorize(Roles = "A", Policy = "NeedsX")]
public sealed class P08_RolesAPlusPolicyX : PageBase;

[Authorize(Roles = "A")]
[Authorize(Roles = "B")]
public sealed class P09_TwoAttrsAandB : PageBase;

[Authorize]
[AllowAnonymous]
public sealed class P10_AuthorizePlusAllowAnon : PageBase;

[AllowAnonymous]
public sealed class P11_AllowAnonOnly : PageBase;

[Authorize(Roles = "A")]
public abstract class BaseA : PageBase;

public sealed class P12_InheritsBaseA : BaseA;

[Authorize(Roles = "B")]
public sealed class P13_DerivedB_BaseA : BaseA;

[Authorize(Policy = "Missing")]
public sealed class P14_MissingPolicy : PageBase;

[Authorize(Roles = "A")]
public abstract class BaseAuthA2 : PageBase;

[AllowAnonymous]
public sealed class P15_BaseAuth_DerivedAllowAnon : BaseAuthA2;

[AllowAnonymous]
public abstract class BaseAnon : PageBase;

[Authorize(Roles = "A")]
public sealed class P16_BaseAnon_DerivedAuth : BaseAnon;

[Authorize(Roles = "Employees")]
public sealed class P17_Employees : PageBase;

[Authorize(Policy = "ResourceIsRouteData")]
public sealed class P18_ResourceSensitive : PageBase;

/// <summary>An IAuthorizeData that is not AuthorizeAttribute.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class CustomDataAttribute : Attribute, IAuthorizeData
{
    public string? Policy { get; set; }

    public string? Roles { get; set; } = "A";

    public string? AuthenticationSchemes { get; set; }
}

[CustomData]
public sealed class P19_CustomIAuthorizeData : PageBase;

[Authorize(AuthenticationSchemes = "Cookies")]
public sealed class P20_SchemesOnly : PageBase;

[Authorize(Roles = " ,A")]
public sealed class P21_RolesEmptyEntry : PageBase;

[Authorize(Roles = "a")]
public sealed class P22_RolesLowercase : PageBase;

[Authorize(Roles = "")]
public sealed class P23_RolesEmptyString : PageBase;

[Authorize(Roles = "A")]
[AllowAnonymous]
public sealed class P24_RolesPlusAllowAnon : PageBase;

[Authorize(Policy = "NeedsX")]
[Authorize(Roles = "B")]
public sealed class P25_PolicyXandRolesB_TwoAttrs : PageBase;
