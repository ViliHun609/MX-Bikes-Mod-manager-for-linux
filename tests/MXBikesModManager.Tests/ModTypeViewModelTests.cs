using MXBikesModManager.Models;
using MXBikesModManager.ViewModels;
using Xunit;

namespace MXBikesModManager.Tests;

public class ModTypeViewModelTests
{
    private static ModTypeConfig Type() => new() { Key = "bikes", Folder = "bikes", Title = "Bikes" };

    private static ModEntry Entry(ModTypeConfig type, string baseName, string category, long size = 0) => new()
    {
        Name = baseName,
        BaseName = baseName,
        Type = type,
        Kind = ModKind.PkzArchive,
        Category = category,
        Path = Path.Combine("/tmp", baseName + ".pkz"),
        SizeBytes = size
    };

    private static ModTypeViewModel ViewModel(ModTypeConfig type) =>
        new(type, (_, _) => true, _ => true, _ => { }, _ => { }, _ => { });

    [Fact]
    public void Categories_are_sorted_by_count_and_filter_the_grid()
    {
        var type = Type();
        var vm = ViewModel(type);

        vm.Load(new[]
        {
            Entry(type, "mx1_a", "MX1", 10),
            Entry(type, "mx1_b", "MX1", 20),
            Entry(type, "eighty_five", "85cc", 30),
            Entry(type, "other", "Other", 5)
        });

        // "All" + three categories, biggest first.
        Assert.Equal(4, vm.Categories.Count);
        Assert.Equal("All", vm.Categories[0].Name);
        Assert.Equal(4, vm.Categories[0].Count);
        Assert.Equal("MX1", vm.Categories[1].Name);
        Assert.Equal(2, vm.Categories[1].Count);
        Assert.True(vm.HasCategories);

        vm.SelectedCategory = vm.Categories[1];
        Assert.Equal(2, vm.FilteredItems.Count);
        Assert.All(vm.FilteredItems, i => Assert.Equal("MX1", i.Category));
    }

    [Fact]
    public void Category_sort_groups_mods_by_category()
    {
        var type = Type();
        var vm = ViewModel(type);

        vm.Load(new[]
        {
            Entry(type, "z_mx1", "MX1", 10),
            Entry(type, "a_other", "Other", 20),
            Entry(type, "m_85cc", "85cc", 30)
        });

        vm.SortMode = "Category";

        Assert.Equal(
            new[] { "85cc", "MX1", "Other" },
            vm.FilteredItems.Select(i => i.Category));
    }

    [Fact]
    public void Uncategorised_mods_do_not_produce_a_chip()
    {
        var type = Type();
        var vm = ViewModel(type);

        vm.Load(new[] { Entry(type, "plain", string.Empty) });

        Assert.Single(vm.Categories);
        Assert.False(vm.HasCategories);
        Assert.Single(vm.FilteredItems);
    }
}
