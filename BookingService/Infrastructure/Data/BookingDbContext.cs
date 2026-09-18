using BookingService.Entities;
using Microsoft.EntityFrameworkCore;

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
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var entries = ChangeTracker.Entries<Booking>();

        foreach (var entry in entries)
        {
            if (entry.State != EntityState.Modified)
                continue;

            var statusProperty = entry.Properties.FirstOrDefault(p =>
                p.Metadata.Name == nameof(Booking.Status) && p.IsModified
            );

            if (statusProperty == null)
                continue;

            BookingStatus? oldStatus = (BookingStatus)(
                statusProperty.OriginalValue ?? throw new InvalidOperationException()
            );

            var newStatus = (BookingStatus)(
                statusProperty.CurrentValue ?? throw new InvalidOperationException()
            );

            var id = (long)(
                entry.Property(nameof(Booking.Id)).CurrentValue
                ?? throw new InvalidOperationException()
            );

            Entities.BookingStatusHistory.Create(id, oldStatus, newStatus);
        }

        return await base.SaveChangesAsync(cancellationToken);
    }
}
