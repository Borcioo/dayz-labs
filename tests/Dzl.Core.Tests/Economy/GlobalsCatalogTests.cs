using Dzl.Core.Economy;
using FluentAssertions;

/// <summary>The globals.xml engine vocabulary catalog: well-formed entries + case-insensitive lookup.</summary>
public class GlobalsCatalogTests
{
    [Fact]
    public void Catalog_covers_the_vanilla_set_with_defaults()
    {
        GlobalsCatalog.All.Should().HaveCountGreaterThan(25);
        GlobalsCatalog.All.Should().OnlyContain(d => d.Name.Length > 0 && d.Default.Length > 0);
        GlobalsCatalog.All.Select(d => d.Name).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Find_is_case_insensitive_and_carries_type_and_default()
    {
        var d = GlobalsCatalog.Find("animalmaxcount");
        d.Should().NotBeNull();
        d!.Default.Should().Be("200");
        d.Type.Should().Be(0, "AnimalMaxCount is an integer");

        GlobalsCatalog.Find("LootDamageMax")!.Type.Should().Be(1, "LootDamageMax is a float");
    }

    [Fact]
    public void Unknown_name_is_not_in_the_catalog()
    {
        GlobalsCatalog.Find("MyCustomTweak").Should().BeNull();
        GlobalsCatalog.IsKnown("ZombieMaxCount").Should().BeTrue();
        GlobalsCatalog.IsKnown("Nonsense").Should().BeFalse();
    }
}
