using Microsoft.EntityFrameworkCore;
using PocketGoal.Models;

namespace PocketGoal.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        public DbSet<UserProfile> UserProfiles { get; set; } = null!;
        public DbSet<SavingGoal> SavingGoals { get; set; } = null!;
        public DbSet<Saving> Savings { get; set; } = null!;
        public DbSet<ExpenseCategory> ExpenseCategories { get; set; } = null!;
        public DbSet<Expense> Expenses { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // UserProfile
            modelBuilder.Entity<UserProfile>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Email);
                entity.HasIndex(e => e.PhoneNumber);
                entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
                entity.Property(e => e.Email).HasMaxLength(150).IsRequired();
                entity.Property(e => e.PhoneNumber).HasMaxLength(20).IsRequired();
            });

            // SavingGoal
            modelBuilder.Entity<SavingGoal>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.UserId);
                entity.Property(e => e.GoalName).HasMaxLength(150).IsRequired();
                entity.Property(e => e.TargetAmount).HasColumnType("decimal(18,2)");
                entity.Property(e => e.CurrentSavedAmount).HasColumnType("decimal(18,2)");

                entity.HasOne(e => e.User)
                    .WithMany(u => u.SavingGoals)
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Saving
            modelBuilder.Entity<Saving>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.SavingGoalId);
                entity.Property(e => e.Amount).HasColumnType("decimal(18,2)");

                entity.HasOne(e => e.SavingGoal)
                    .WithMany(g => g.Savings)
                    .HasForeignKey(e => e.SavingGoalId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // ExpenseCategory
            modelBuilder.Entity<ExpenseCategory>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.UserId);
                entity.Property(e => e.Name).HasMaxLength(80).IsRequired();

                entity.HasOne(e => e.User)
                    .WithMany(u => u.CustomCategories)
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Expense
            modelBuilder.Entity<Expense>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.UserId);
                entity.HasIndex(e => e.CategoryId);
                entity.Property(e => e.Amount).HasColumnType("decimal(18,2)");

                entity.HasOne(e => e.User)
                    .WithMany(u => u.Expenses)
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Category)
                    .WithMany(c => c.Expenses)
                    .HasForeignKey(e => e.CategoryId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // Seed Default Expense Categories
            SeedDefaultCategories(modelBuilder);
        }

        private static void SeedDefaultCategories(ModelBuilder modelBuilder)
        {
            var defaultCategories = new List<ExpenseCategory>
            {
                new ExpenseCategory
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111111101"),
                    UserId = null,
                    Name = "Food & Dining",
                    Icon = "bi-egg-fried",
                    ColorHex = "#f59e0b",
                    CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                },
                new ExpenseCategory
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111111102"),
                    UserId = null,
                    Name = "Travel & Commute",
                    Icon = "bi-airplane",
                    ColorHex = "#3b82f6",
                    CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                },
                new ExpenseCategory
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111111103"),
                    UserId = null,
                    Name = "Fuel & Vehicle",
                    Icon = "bi-fuel-pump",
                    ColorHex = "#ef4444",
                    CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                },
                new ExpenseCategory
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111111104"),
                    UserId = null,
                    Name = "Shopping",
                    Icon = "bi-bag-check",
                    ColorHex = "#ec4899",
                    CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                },
                new ExpenseCategory
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111111105"),
                    UserId = null,
                    Name = "Entertainment & Movies",
                    Icon = "bi-film",
                    ColorHex = "#8b5cf6",
                    CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                },
                new ExpenseCategory
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111111106"),
                    UserId = null,
                    Name = "Bills & Utilities",
                    Icon = "bi-lightning-charge",
                    ColorHex = "#10b981",
                    CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                },
                new ExpenseCategory
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111111107"),
                    UserId = null,
                    Name = "Subscriptions",
                    Icon = "bi-play-btn",
                    ColorHex = "#6366f1",
                    CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                },
                new ExpenseCategory
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111111108"),
                    UserId = null,
                    Name = "Health & Medical",
                    Icon = "bi-heart-pulse",
                    ColorHex = "#14b8a6",
                    CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                },
                new ExpenseCategory
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111111109"),
                    UserId = null,
                    Name = "Education",
                    Icon = "bi-book",
                    ColorHex = "#0284c7",
                    CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                },
                new ExpenseCategory
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111111110"),
                    UserId = null,
                    Name = "Other",
                    Icon = "bi-three-dots",
                    ColorHex = "#64748b",
                    CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                }
            };

            modelBuilder.Entity<ExpenseCategory>().HasData(defaultCategories);
        }
    }
}
