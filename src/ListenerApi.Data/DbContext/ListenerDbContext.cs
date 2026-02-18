using ListenerApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ListenerApi.Data.DbContext;

public class ListenerDbContext : Microsoft.EntityFrameworkCore.DbContext
{
    public ListenerDbContext(DbContextOptions<ListenerDbContext> options)
        : base(options) { }

    public DbSet<EmployeeRecord> EmployeeRecords { get; set; } = null!;
    public DbSet<EmployeePayAttributes> EmployeePayAttributes { get; set; } = null!;

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

        modelBuilder.Entity<EmployeePayAttributes>(entity =>
        {
            entity.HasKey(e => e.EmployeeId);
            entity.HasOne(e => e.Employee)
                  .WithOne(e => e.PayAttributes)
                  .HasForeignKey<EmployeePayAttributes>(e => e.EmployeeId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.Property(e => e.GrossPay).HasPrecision(18, 2);
            entity.Property(e => e.FederalTax).HasPrecision(18, 2);
            entity.Property(e => e.StateTax).HasPrecision(18, 2);
            entity.Property(e => e.AdditionalFederalWithholding).HasPrecision(18, 2);
            entity.Property(e => e.AdditionalStateWithholding).HasPrecision(18, 2);
            entity.Property(e => e.TotalTax).HasPrecision(18, 2);
            entity.Property(e => e.TotalFixedDeductions).HasPrecision(18, 2);
            entity.Property(e => e.TotalPercentDeductions).HasPrecision(18, 2);
            entity.Property(e => e.TotalDeductions).HasPrecision(18, 2);
            entity.Property(e => e.NetPay).HasPrecision(18, 2);
            entity.Property(e => e.PayRate).HasPrecision(18, 2);
            entity.Property(e => e.TotalHoursWorked).HasPrecision(18, 2);
            entity.Property(e => e.PayType).IsRequired().HasMaxLength(10);
            entity.Property(e => e.PayPeriodStart).IsRequired().HasMaxLength(50);
            entity.Property(e => e.PayPeriodEnd).IsRequired().HasMaxLength(50);
        });
    }
}
