using aspire_react.Server.Application.Companies.Queries;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// Task V â€” Company-scoping cho GET /companies (cÃ¹ng lá»›p lá»—i Ä‘Ã£ fix á»Ÿ Departments.GetAll Task K /
/// GetLocations Task U): user thÆ°á»�ng CHá»ˆ tháº¥y subtree cÃ´ng ty cá»§a mÃ¬nh; Superuser (hoáº·c user thÆ°á»�ng
/// khÃ´ng cÃ³ cÃ´ng ty) tháº¥y toÃ n bá»™ cÃ¢y. Verify qua controller trá»±c tiáº¿p trÃªn EF InMemory.
/// [Giai đoạn 3] Companies migrated to MediatR — tests now drive ListCompaniesQueryHandler directly
/// with FakeScope (same scope substance; the controller is a thin Send() map).
/// </summary>
public class CompanyTreeScopeTests
{
    private static AppDbContext CreateContext(string name)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options, new TestHelpers.SuperUserScope());
    }

    private static ListCompaniesQueryHandler Build(AppDbContext db, TestHelpers.FakeScope scope)
        => new(db, scope);

    private static List<string> FlattenNames(IReadOnlyList<CompanyTreeNodeDto> roots)
    {
        var names = new List<string>();
        void Walk(IEnumerable<CompanyTreeNodeDto> nodes)
        {
            foreach (var n in nodes)
            {
                names.Add(n.Name);
                Walk(n.Children);
            }
        }
        Walk(roots);
        return names;
    }

    [Fact]
    public async Task Superuser_Sees_All_Companies()
    {
        var db = CreateContext("super-sees-all");
        var parent = new Company { Name = "Parent Co" };
        db.Companies.AddRange(parent,
            new Company { Name = "Child A", ParentId = parent.Id },
            new Company { Name = "Child B", ParentId = parent.Id });
        await db.SaveChangesAsync();

        var roots = await Build(db, new TestHelpers.FakeScope { Super = true })
            .Handle(new ListCompaniesQuery(), CancellationToken.None);
        var names = FlattenNames(roots);
        Assert.Equal(3, names.Count);
        Assert.Contains("Parent Co", names);
        Assert.Contains("Child A", names);
        Assert.Contains("Child B", names);
    }

    [Fact]
    public async Task RegularUser_Sees_Only_Own_Subtree()
    {
        var db = CreateContext("reg-sees-own-subtree");
        var parent = new Company { Name = "Parent Co" };
        var childA = new Company { Name = "Child A", ParentId = parent.Id };
        var childB = new Company { Name = "Child B", ParentId = parent.Id };
        var grandchild = new Company { Name = "Grandchild A1", ParentId = childA.Id };
        db.Companies.AddRange(parent, childA, childB, grandchild);
        await db.SaveChangesAsync();

        // User belongs to Child A â†’ sees only Child A + its descendants, NOT Parent or Child B.
        var roots = await Build(db, new TestHelpers.FakeScope { Super = false, CompanyId = childA.Id })
            .Handle(new ListCompaniesQuery(), CancellationToken.None);
        var names = FlattenNames(roots);

        Assert.Equal(2, names.Count);
        Assert.Contains("Child A", names);
        Assert.Contains("Grandchild A1", names);
        Assert.DoesNotContain("Parent Co", names);
        Assert.DoesNotContain("Child B", names);

        // The scoped subtree must be rooted at Child A (its parent is outside the visible set).
        Assert.Single(roots);
    }

    [Fact]
    public async Task RegularUser_WithoutCompany_Sees_All()
    {
        var db = CreateContext("reg-nocompany-sees-all");
        var parent = new Company { Name = "Parent Co" };
        db.Companies.AddRange(parent,
            new Company { Name = "Child A", ParentId = parent.Id });
        await db.SaveChangesAsync();

        var names = FlattenNames(await Build(db, new TestHelpers.FakeScope { Super = false, CompanyId = null })
            .Handle(new ListCompaniesQuery(), CancellationToken.None));
        Assert.Equal(2, names.Count);
    }
}
