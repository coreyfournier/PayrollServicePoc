using ListenerApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ListenerApi.Data.DbContext;

public class ListenerDbContext : Microsoft.EntityFrameworkCore.DbContext
{
    public ListenerDbContext(DbContextOptions<ListenerDbContext> options)
        : base(options) { }

    public DbSet<EmployeeRecord> EmployeeRecords { get; set; } = null!;
    public DbSet<EmployeePayAttributes> EmployeePayAttributes { get; set; } = null!;
    public DbSet<TransferRecord> TransferRecords { get; set; } = null!;
    public DbSet<OutboxMessage> OutboxMessages { get; set; } = null!;
    public DbSet<EmployeeTransferStatus> EmployeeTransferStatuses { get; set; } = null!;

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
            // No FK constraint — this is an eventually-consistent read model where
            // net pay events may arrive before employee events (different Kafka partitions).
            // Navigation property is kept for GraphQL query convenience only.
            entity.HasOne(e => e.Employee)
                  .WithOne(e => e.PayAttributes)
                  .HasForeignKey<EmployeePayAttributes>(e => e.EmployeeId)
                  .OnDelete(DeleteBehavior.NoAction);
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
            entity.Property(e => e.TransferTotalAmount).HasPrecision(18, 2);
        });

        modelBuilder.Entity<TransferRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            // No FK constraint — transfer events may arrive before employee events.
            entity.HasOne(e => e.Employee)
                  .WithMany()
                  .HasForeignKey(e => e.EmployeeId)
                  .OnDelete(DeleteBehavior.NoAction);
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(20);
            entity.Property(e => e.FailureReason).HasMaxLength(500);
            entity.Property(e => e.CurrentBalance).HasPrecision(18, 2);
            entity.Property(e => e.ExternalReferenceId).HasMaxLength(100);
            entity.Property(e => e.WorkflowStepsJson).HasColumnType("json");
            entity.Ignore(e => e.WorkflowSteps);
            entity.HasIndex(e => new { e.EmployeeId, e.PayPeriodNumber });
            entity.HasIndex(e => new { e.EmployeeId, e.InitiatedAt });
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.AggregateId).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Topic).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Payload).IsRequired().HasColumnType("json");
            entity.HasIndex(e => e.CreatedAt);
        });

        modelBuilder.Entity<EmployeeTransferStatus>(entity =>
        {
            entity.HasKey(e => e.EmployeeId);
            entity.HasOne(e => e.Employee)
                  .WithOne()
                  .HasForeignKey<EmployeeTransferStatus>(e => e.EmployeeId)
                  .OnDelete(DeleteBehavior.NoAction);
            entity.Property(e => e.TotalAmountTransferred).HasPrecision(18, 2);
            entity.Property(e => e.PeriodAmountLimit).HasPrecision(18, 2);
        });
    }
}
