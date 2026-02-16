using ListenerApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ListenerApi.Data.DbContext;

public class ListenerDbContext : Microsoft.EntityFrameworkCore.DbContext
{
    public ListenerDbContext(DbContextOptions<ListenerDbContext> options)
        : base(options) { }

    public DbSet<EmployeeRecord> EmployeeRecords { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EmployeeRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FirstName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.LastName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(200);
            entity.Property(e => e.PayRate).HasPrecision(18, 2);
            entity.Property(e => e.PayPeriodHours).HasPrecision(18, 2).HasDefaultValue(40m);
            entity.HasIndex(e => e.LastEventId);
            entity.HasIndex(e => e.LastEventTimestamp);
        });
    }
}
