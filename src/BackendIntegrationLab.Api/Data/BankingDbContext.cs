using BackendIntegrationLab.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BackendIntegrationLab.Api.Data;

public sealed class BankingDbContext(DbContextOptions<BankingDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Transfer> Transfers => Set<Transfer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var account = modelBuilder.Entity<Account>();
        account.Property(a => a.OwnerName).HasMaxLength(100).IsRequired();
        // SQLite has no native decimal type. Store exact integer cents, while all
        // application code and JSON contracts continue to use decimal money.
        account.Property(a => a.Balance).HasConversion(
            amount => (long)(amount * 100m), cents => cents / 100m);
        account.ToTable("Accounts", table => table.HasCheckConstraint("CK_Accounts_Balance", "Balance >= 0"));

        var transfer = modelBuilder.Entity<Transfer>();
        transfer.Property(t => t.Amount).HasConversion(
            amount => (long)(amount * 100m), cents => cents / 100m);
        transfer.HasOne<Account>().WithMany().HasForeignKey(t => t.SourceAccountId).OnDelete(DeleteBehavior.Restrict);
        transfer.HasOne<Account>().WithMany().HasForeignKey(t => t.DestinationAccountId).OnDelete(DeleteBehavior.Restrict);
        transfer.ToTable("Transfers", table => table.HasCheckConstraint("CK_Transfers_Amount", "Amount > 0"));
    }
}
