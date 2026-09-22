using BookingService.Dto.Request;
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

    public DbSet<ProcessedEvent> ProcessedEvents
    {
        get => Set<ProcessedEvent>();
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

        modelBuilder.Entity<ProcessedEvent>(entity =>
        {
            entity.ToTable("processed_events").HasKey(e => e.EventId);

            entity
                .Property(e => e.EventId)
                .HasColumnName("event_id")
                .HasColumnType("uuid")
                .IsRequired();

            entity
                .Property(e => e.ProcessedAt)
                .HasColumnName("processed_at")
                .HasColumnType("timestamp with time zone");
        });
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var entries = ChangeTracker.Entries<Booking>().ToList();
        var modifiedEntries = new List<EntityEntry<Booking>>();
        var addedEntries = new List<EntityEntry<Booking>>();

        foreach (var entry in entries)
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    addedEntries.Add(entry);
                    break;
                case EntityState.Modified when entry.Property(b => b.Status).IsModified:
                    modifiedEntries.Add(entry);
                    break;
                case EntityState.Detached:
                case EntityState.Unchanged:
                case EntityState.Deleted:
                default:
                    break;
            }
        }

        if (Database.CurrentTransaction is not null)
            return await SaveCoreAsync();

        await using var tx = await Database.BeginTransactionAsync(cancellationToken);
        var result = await SaveCoreAsync();
        await tx.CommitAsync(cancellationToken);
        return result;

        async Task<int> SaveCoreAsync()
        {
            var result = await base.SaveChangesAsync(cancellationToken);

            var histories = (
                from entry in modifiedEntries
                let statusProperty = entry.Property(b => b.Status)
                select Entities.BookingStatusHistory.Create(
                    entry.Entity.Id,
                    statusProperty.OriginalValue,
                    statusProperty.CurrentValue
                )
            ).ToList();

            histories.AddRange(
                addedEntries.Select(entry =>
                    Entities.BookingStatusHistory.Create(entry.Entity.Id, null, entry.Entity.Status)
                )
            );

            if (histories.Count <= 0)
                return result;

            Set<BookingStatusHistory>().AddRange(histories);
            await base.SaveChangesAsync(cancellationToken);

            return result;
        }
    }
}
