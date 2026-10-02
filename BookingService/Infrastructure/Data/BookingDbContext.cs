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

    public DbSet<OutboxMessage> OutboxMessages
    {
        get => Set<OutboxMessage>();
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
                .HasIndex(booking => booking.CatalogRequestId)
                .HasDatabaseName("idx_bookings_catalog_request_id")
                .IsUnique();

            entity
                .HasIndex(booking => new { booking.UserId, booking.Status })
                .HasDatabaseName("idx_bookings_user_id_status");

            entity
                .HasIndex(booking => new
                {
                    booking.ResourceId,
                    booking.BookedFrom,
                    booking.BookedTo,
                })
                .HasDatabaseName("idx_bookings_resource_id_dates");

            entity
                .HasIndex(booking => booking.CreatedAt)
                .IsDescending()
                .HasDatabaseName("idx_bookings_created_at");

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

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("outbox_messages").HasKey(m => m.Id);

            entity
                .Property(m => m.Id)
                .HasColumnName("id")
                .UseIdentityByDefaultColumn()
                .HasColumnType("bigint");

            entity
                .Property(m => m.MessageType)
                .HasColumnName("message_type")
                .IsRequired()
                .HasColumnType("text");

            entity
                .Property(m => m.CreatedAt)
                .HasColumnName("created_at")
                .HasColumnType("timestamp with time zone");

            entity.Property(m => m.Payload).HasColumnName("payload").HasColumnType("jsonb");

            entity
                .Property(m => m.ProcessedAt)
                .HasColumnName("processed_at")
                .HasColumnType("timestamp with time zone")
                .IsRequired(false);

            entity.Property(m => m.RetryCount).HasColumnName("retry_count");

            entity
                .Property(m => m.FailedAt)
                .HasColumnName("failed_at")
                .HasColumnType("timestamp with time zone")
                .IsRequired(false);

            entity
                .HasIndex(m => m.ProcessedAt, "idx_outbox_messages_processed_at")
                .HasFilter("processed_at IS NULL and failed_at IS NULL");
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
            var results = await base.SaveChangesAsync(cancellationToken);

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
                return results;

            Set<BookingStatusHistory>().AddRange(histories);
            await base.SaveChangesAsync(cancellationToken);

            return results;
        }
    }
}
