using ModRouter.Core.Titles;

namespace ModRouter.Test;

[TestFixture]
public sealed class RouteTitleFormatterTests
{
    [Test]
    public void A_titled_route_is_formatted_with_the_title_as_the_first_argument()
    {
        Assert.That(RouteTitleFormatter.Resolve("Customers", "{0} - Acme", null), Is.EqualTo("Customers - Acme"));
        Assert.That(RouteTitleFormatter.Resolve("Customers", RouteTitleFormatter.DefaultFormat, null), Is.EqualTo("Customers"));
        Assert.That(RouteTitleFormatter.Resolve("Customers", "Acme | {0} | {0}", null), Is.EqualTo("Acme | Customers | Customers"));
    }

    [Test]
    public void A_format_without_the_placeholder_is_allowed_and_ignores_the_title()
    {
        Assert.That(RouteTitleFormatter.Resolve("Customers", "Acme", null), Is.EqualTo("Acme"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void An_untitled_route_uses_the_default_title_verbatim(string? untitled)
    {
        Assert.That(RouteTitleFormatter.Resolve(untitled, "{0} - Acme", "Acme"), Is.EqualTo("Acme"), "the format is not applied to the default title");
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    public void An_untitled_route_without_a_default_leaves_the_document_title_alone(string? defaultTitle)
    {
        Assert.That(RouteTitleFormatter.Resolve(null, "{0} - Acme", defaultTitle), Is.Null);
    }

    [Test]
    public void Braces_in_the_title_are_data_not_format_syntax()
    {
        Assert.That(RouteTitleFormatter.Resolve("Order {42}", "{0} - Acme", null), Is.EqualTo("Order {42} - Acme"));
    }

    [Test]
    public void A_null_format_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => RouteTitleFormatter.Resolve("x", null!, null));
        Assert.Throws<ArgumentNullException>(() => RouteTitleFormatter.Validate(null!));
    }

    [TestCase("{0}")]
    [TestCase("{0} - Acme")]
    [TestCase("Acme")]
    public void Valid_formats_pass_validation(string format)
    {
        Assert.DoesNotThrow(() => RouteTitleFormatter.Validate(format));
    }

    [TestCase("{0")]
    [TestCase("{1}")]
    [TestCase("{abc}")]
    public void Invalid_formats_fail_validation_with_a_helpful_message(string format)
    {
        var exception = Assert.Throws<ArgumentException>(() => RouteTitleFormatter.Validate(format))!;

        Assert.That(exception.Message, Does.Contain(format).And.Contain("{0}"));
    }
}
