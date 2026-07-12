using GymTrackPro.API.Data;
using GymTrackPro.API.Services;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GymTrackPro.Tests;

public sealed class ProjectionVersionProviderTests
{
    [Fact]
    public async Task Read_and_increment_use_durable_member_row()
    {
        await using var context = CreateContext();
        context.Members.Add(CreateMember(7));
        context.MemberProjectionVersions.Add(new MemberProjectionVersion
        {
            MemberID = 7,
            Version = 41
        });
        await context.SaveChangesAsync();
        var provider = new ProjectionVersionProvider(context);

        Assert.Equal(41, await provider.GetMutationVersionForMemberAsync(7));
        Assert.Equal(42, await provider.IncrementMutationVersionForMemberAsync(7));
        Assert.Equal(42, await context.MemberProjectionVersions
            .AsNoTracking()
            .Where(item => item.MemberID == 7)
            .Select(item => item.Version)
            .SingleAsync());
    }

    [Fact]
    public async Task Missing_row_fails_predictably_and_get_never_allocates()
    {
        await using var context = CreateContext();
        context.Members.Add(CreateMember(8));
        await context.SaveChangesAsync();
        var provider = new ProjectionVersionProvider(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GetMutationVersionForMemberAsync(8));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.IncrementMutationVersionForMemberAsync(8));
        Assert.Empty(context.MemberProjectionVersions);
    }

    [Fact]
    public async Task Maximum_version_rejects_increment_without_wraparound()
    {
        await using var context = CreateContext();
        context.Members.Add(CreateMember(9));
        context.MemberProjectionVersions.Add(new MemberProjectionVersion
        {
            MemberID = 9,
            Version = MemberProjectionVersion.MaximumVersion
        });
        await context.SaveChangesAsync();
        var provider = new ProjectionVersionProvider(context);

        await Assert.ThrowsAsync<OverflowException>(() =>
            provider.IncrementMutationVersionForMemberAsync(9));

        Assert.Equal(
            MemberProjectionVersion.MaximumVersion,
            await provider.GetMutationVersionForMemberAsync(9));
    }

    [Fact]
    public void Sql_increment_is_one_atomic_guarded_output_statement()
    {
        var sql = ProjectionVersionProvider.AtomicIncrementSql;

        Assert.Contains("UPDATE [MemberProjectionVersions]", sql, StringComparison.Ordinal);
        Assert.Contains("OUTPUT INSERTED.[Version]", sql, StringComparison.Ordinal);
        Assert.Contains("[Version] < @maximumVersion", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(';', sql);
    }

    [Fact]
    public async Task Relational_increment_without_domain_transaction_fails_before_connecting()
    {
        var options = new DbContextOptionsBuilder<GymDbContext>()
            .UseSqlServer(
                "Server=unreachable.invalid;Database=NeverConnect;User Id=none;Password=none;" +
                "Encrypt=True;TrustServerCertificate=False")
            .Options;
        await using var context = new GymDbContext(options);
        var provider = new ProjectionVersionProvider(context);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.IncrementMutationVersionForMemberAsync(1));

        Assert.Contains("active domain transaction", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Atomic_path_detaches_clean_state_and_rejects_pending_stale_overwrite()
    {
        using var context = CreateContext();
        var provider = new ProjectionVersionProvider(context);
        var clean = new MemberProjectionVersion { MemberID = 20, Version = 4 };
        context.Attach(clean);

        provider.PrepareTrackedVersionStateForAtomicIncrement(20);

        Assert.Equal(EntityState.Detached, context.Entry(clean).State);

        var pending = new MemberProjectionVersion { MemberID = 20, Version = 5 };
        context.Attach(pending);
        pending.Version = 6;
        context.Entry(pending).State = EntityState.Modified;
        Assert.Throws<InvalidOperationException>(() =>
            provider.PrepareTrackedVersionStateForAtomicIncrement(20));
        Assert.Equal(EntityState.Modified, context.Entry(pending).State);
    }

    [Fact]
    public void Registration_is_scoped_and_shares_the_scoped_dbcontext()
    {
        var services = new ServiceCollection();
        services.AddDbContext<GymDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddProjectionVersionInfrastructure();
        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        using var firstScope = provider.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<IProjectionVersionProvider>();
        var repeated = firstScope.ServiceProvider.GetRequiredService<IProjectionVersionProvider>();
        using var secondScope = provider.CreateScope();
        var second = secondScope.ServiceProvider.GetRequiredService<IProjectionVersionProvider>();

        Assert.Same(first, repeated);
        Assert.NotSame(first, second);
    }

    private static GymDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<GymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new GymDbContext(options);
    }

    private static Member CreateMember(int memberId) => new()
    {
        MemberID = memberId,
        FirstName = "Projection",
        LastName = "Member",
        Gender = "Other",
        BirthDate = new DateTime(1990, 1, 1),
        PhoneNumber = $"555{memberId:D7}",
        EmergencyContact = "Contact",
        QRCode = $"QR-{memberId}",
        Status = "Active",
        DateRegistered = DateTime.UtcNow,
        LastModified = DateTime.UtcNow
    };
}
