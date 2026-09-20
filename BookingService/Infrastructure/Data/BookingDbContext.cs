using BookingService.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace BookingService.Infrastructure.Data;

public class BookingDbContext : DbContext
{
    public DbSet<Booking> Bookings
    {
        get => Set<Booking>();
    }

    public DbSet<BookingStatusHistory> BookingStatusHistory
    {
        get => Set<BookingStatusHistory>();
    }

    public BookingDbContext(DbContextOptions<BookingDbContext> options)
        : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Booking>(entity =>
        {
            entity.ToTable("bookings");

            entity.HasKey(b => b.Id);

            entity.Property(b => b.Id).HasColumnName("id").UseIdentityByDefaultColumn();

            entity
                .Property(b => b.Status)
                .HasColumnName("status")
                .HasConversion<int>()
                .IsRequired();

            entity.Property(b => b.UserId).HasColumnName("user_id").IsRequired();

            entity.Property(b => b.ResourceId).HasColumnName("resource_id").IsRequired();

            entity
                .Property(b => b.BookedFrom)
                .HasColumnName("booked_from")
                .HasColumnType("date")
                .IsRequired();

            entity
                .Property(b => b.BookedTo)
                .HasColumnName("booked_to")
                .HasColumnType("date")
                .IsRequired();

            entity
                .Property(b => b.CreatedAt)
                .HasColumnName("created_at")
                .HasColumnType("timestamp with time zone")
                .IsRequired();

            entity
                .Property(b => b.CatalogRequestId)
                .HasColumnName("catalog_request_id")
                .HasColumnType("uuid");

            entity
                .Property(b => b.CancellationRequestedAt)
                .HasColumnName("cancellation_requested_at");

            entity.HasIndex(b => b.Status, "idx_bookings_status");

            entity
                .HasIndex(b => b.Status, "idx_bookings_cancellation_pending")
                .HasFilter("status = 4");

            entity.HasIndex(b => b.UserId, "idx_bookings_user_id");

            entity.HasIndex(b => b.ResourceId).HasDatabaseName("idx_bookings_resource_id");

            entity
                .Property(b => b.Version)
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();
        });

        modelBuilder.Entity<BookingStatusHistory>(entity =>
        {
            entity.ToTable("booking_status_history");

            entity
                .HasOne<Booking>()
                .WithMany()
                .HasForeignKey(b => b.BookingId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasKey(b => b.Id);

            entity.Property(b => b.Id).HasColumnName("id").UseIdentityByDefaultColumn();

            entity.Property(b => b.BookingId).HasColumnName("booking_id").IsRequired();

            entity
                .Property(b => b.ChangedAt)
                .HasColumnName("changed_at")
                .HasColumnType("timestamp with time zone")
                .IsRequired();

            entity.Property(b => b.StatusFrom).HasColumnName("status_from").IsRequired(false);

            entity.Property(b => b.StatusTo).HasColumnName("status_to").IsRequired();

            entity.HasIndex(b => b.BookingId, "idx_booking_status_history_booking_id");
        });
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var entries = ChangeTracker.Entries<Booking>().ToList();
        var pendingAdded = new List<(EntityEntry<Booking> Entry, BookingStatus NewStatus)>();

        foreach (var entry in entries)
        {
            var statusProperty = entry.Property(b => b.Status);

            switch (entry.State)
            {
                case EntityState.Modified when !statusProperty.IsModified:
                    continue;
                case EntityState.Modified:
                {
                    BookingStatus? oldStatus = statusProperty.OriginalValue;
                    var newStatus = statusProperty.CurrentValue;

                    var statusHistory = Entities.BookingStatusHistory.Create(
                        entry.Entity.Id,
                        oldStatus,
                        newStatus
                    );

                    BookingStatusHistory.Add(statusHistory);
                    break;
                }
                case EntityState.Added:
                    pendingAdded.Add((entry, statusProperty.CurrentValue));
                    break;
                case EntityState.Detached:
                case EntityState.Unchanged:
                case EntityState.Deleted:
                default:
                    break;
            }
        }

        if (Database.CurrentTransaction is not null)
            return await base.SaveChangesAsync(cancellationToken);

        {
            await using var tx = await Database.BeginTransactionAsync(cancellationToken);
            var result = await base.SaveChangesAsync(cancellationToken);

            if (pendingAdded.Count <= 0)
            {
                await tx.CommitAsync(cancellationToken);
                return result;
            }

            foreach (var (entry, newStatus) in pendingAdded)
            {
                var statusHistory = Entities.BookingStatusHistory.Create(
                    entry.Entity.Id,
                    null,
                    newStatus
                );

                BookingStatusHistory.Add(statusHistory);
            }

            await base.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            return result;
        }
    }
}
