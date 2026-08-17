using MeDan.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace MeDan.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<StudentProfile> StudentProfiles => Set<StudentProfile>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<CompanyMember> CompanyMembers => Set<CompanyMember>();
    public DbSet<Campus> Campuses => Set<Campus>();
    public DbSet<Hostel> Hostels => Set<Hostel>();
    public DbSet<HostelPhoto> HostelPhotos => Set<HostelPhoto>();
    public DbSet<Amenity> Amenities => Set<Amenity>();
    public DbSet<HostelAmenity> HostelAmenities => Set<HostelAmenity>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Bed> Beds => Set<Bed>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Payout> Payouts => Set<Payout>();
    public DbSet<Favorite> Favorites => Set<Favorite>();
    public DbSet<Referral> Referrals => Set<Referral>();
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();
    public DbSet<CampusEvent> Events => Set<CampusEvent>();
    public DbSet<UserNotification> Notifications => Set<UserNotification>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // ---------- AppUser ----------
        b.Entity<AppUser>(e =>
        {
            // Filtered: staff accounts have no Firebase UID and must not collide on NULL.
            e.HasIndex(u => u.FirebaseUid).IsUnique().HasFilter("[FirebaseUid] IS NOT NULL");
            e.HasIndex(u => u.Email).IsUnique();
            // Filtered: users without a code yet must not collide on NULL.
            e.HasIndex(u => u.ReferralCode).IsUnique().HasFilter("[ReferralCode] IS NOT NULL");
            e.Property(u => u.Role).HasConversion<string>();
        });

        // ---------- StudentProfile (1:1 AppUser) ----------
        b.Entity<StudentProfile>(e =>
        {
            e.HasIndex(s => s.UserId).IsUnique();
            e.HasOne(s => s.User)
                .WithOne(u => u.StudentProfile)
                .HasForeignKey<StudentProfile>(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(s => s.Campus)
                .WithMany()
                .HasForeignKey(s => s.CampusCode)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ---------- Company ----------
        b.Entity<Company>(e =>
        {
            e.Property(c => c.Tier).HasConversion<string>();
            e.Property(c => c.CommissionRate).HasPrecision(5, 4);
            e.HasOne(c => c.Owner)
                .WithMany()
                .HasForeignKey(c => c.OwnerUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ---------- CompanyMember ----------
        b.Entity<CompanyMember>(e =>
        {
            e.HasIndex(m => new { m.CompanyId, m.UserId }).IsUnique();
            e.Property(m => m.Role).HasConversion<string>();
            e.HasOne(m => m.Company)
                .WithMany(c => c.Members)
                .HasForeignKey(m => m.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(m => m.User)
                .WithMany(u => u.CompanyMemberships)
                .HasForeignKey(m => m.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ---------- Campus ----------
        b.Entity<Campus>(e => e.HasKey(c => c.Code));

        // ---------- Hostel ----------
        b.Entity<Hostel>(e =>
        {
            e.Property(h => h.PropertyType).HasConversion<string>();
            e.HasIndex(h => new { h.CampusCode, h.MinPrice, h.IsVerified });
            e.HasOne(h => h.Company)
                .WithMany(c => c.Hostels)
                .HasForeignKey(h => h.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(h => h.Campus)
                .WithMany(c => c.Hostels)
                .HasForeignKey(h => h.CampusCode)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(h => h.PostedBy)
                .WithMany()
                .HasForeignKey(h => h.PostedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ---------- HostelPhoto ----------
        b.Entity<HostelPhoto>(e =>
            e.HasOne(p => p.Hostel)
                .WithMany(h => h.Photos)
                .HasForeignKey(p => p.HostelId)
                .OnDelete(DeleteBehavior.Cascade));

        // ---------- Amenity / HostelAmenity (M:N) ----------
        b.Entity<Amenity>(e => e.HasIndex(a => a.Name).IsUnique());
        b.Entity<HostelAmenity>(e =>
        {
            e.HasKey(ha => new { ha.HostelId, ha.AmenityId });
            e.HasOne(ha => ha.Hostel)
                .WithMany(h => h.Amenities)
                .HasForeignKey(ha => ha.HostelId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(ha => ha.Amenity)
                .WithMany(a => a.Hostels)
                .HasForeignKey(ha => ha.AmenityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ---------- Room ----------
        b.Entity<Room>(e =>
        {
            e.Property(r => r.RoomType).HasConversion<string>();
            e.Property(r => r.Status).HasConversion<string>();
            e.Property(r => r.Gender).HasConversion<string>();
            e.HasOne(r => r.Hostel)
                .WithMany(h => h.Rooms)
                .HasForeignKey(r => r.HostelId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ---------- Bed ----------
        b.Entity<Bed>(e =>
        {
            e.Property(bd => bd.Status).HasConversion<string>();
            e.HasOne(bd => bd.Room)
                .WithMany(r => r.Beds)
                .HasForeignKey(bd => bd.RoomId)
                .OnDelete(DeleteBehavior.Cascade);
            // A bed optionally points at its current booking; no cascade to avoid cycles.
            e.HasOne(bd => bd.CurrentBooking)
                .WithMany()
                .HasForeignKey(bd => bd.CurrentBookingId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ---------- Booking ----------
        b.Entity<Booking>(e =>
        {
            e.Property(bk => bk.Status).HasConversion<string>();
            e.HasIndex(bk => new { bk.StudentUserId, bk.CreatedAt });
            e.HasIndex(bk => new { bk.CompanyId, bk.CreatedAt });
            e.HasOne(bk => bk.Student)
                .WithMany(u => u.Bookings)
                .HasForeignKey(bk => bk.StudentUserId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(bk => bk.Hostel)
                .WithMany()
                .HasForeignKey(bk => bk.HostelId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(bk => bk.Room)
                .WithMany()
                .HasForeignKey(bk => bk.RoomId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(bk => bk.Bed)
                .WithMany()
                .HasForeignKey(bk => bk.BedId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ---------- Payment (1:1 Booking) ----------
        b.Entity<Payment>(e =>
        {
            e.HasKey(p => p.Reference);
            e.Property(p => p.Channel).HasConversion<string>();
            e.Property(p => p.Status).HasConversion<string>();
            e.HasOne(p => p.Booking)
                .WithOne(bk => bk.Payment)
                .HasForeignKey<Payment>(p => p.BookingId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ---------- Payout ----------
        b.Entity<Payout>(e =>
        {
            e.HasOne(p => p.Booking)
                .WithMany()
                .HasForeignKey(p => p.BookingId)
                .OnDelete(DeleteBehavior.Restrict);   // payouts are financial records

            e.Property(p => p.Reference).HasMaxLength(100).IsRequired();
            e.Property(p => p.ProviderReference).HasMaxLength(100);
            e.Property(p => p.FailureReason).HasMaxLength(500);

            // The idempotency guarantee: one release and one refund per booking,
            // enforced by the database rather than by application logic alone.
            e.HasIndex(p => new { p.BookingId, p.Kind }).IsUnique();
            e.HasIndex(p => p.Reference).IsUnique();
            e.HasIndex(p => p.Status);
        });

        // ---------- UserNotification ----------
        b.Entity<UserNotification>(e =>
        {
            e.Property(n => n.Title).HasMaxLength(150).IsRequired();
            e.Property(n => n.Body).HasMaxLength(1000).IsRequired();
            e.Property(n => n.Type).HasMaxLength(30);
            e.Property(n => n.Route).HasMaxLength(200);
            e.Property(n => n.ImageUrl).HasMaxLength(500);

            e.HasOne(n => n.User)
                .WithMany()
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // The feed reads "mine, newest first"; the badge counts unread.
            e.HasIndex(n => new { n.UserId, n.CreatedAt });
            e.HasIndex(n => new { n.UserId, n.ReadAt });
        });

        // ---------- CampusEvent ----------
        b.Entity<CampusEvent>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(150).IsRequired();
            e.Property(x => x.Venue).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.ImageUrl).HasMaxLength(500);

            e.HasOne(x => x.Campus)
                .WithMany()
                .HasForeignKey(x => x.CampusCode)
                .OnDelete(DeleteBehavior.SetNull);

            // The feed reads "upcoming, for my campus" — index for exactly that.
            e.HasIndex(x => new { x.CampusCode, x.StartsAt });
        });

        // ---------- DeviceToken ----------
        b.Entity<DeviceToken>(e =>
        {
            e.HasKey(d => d.Token);
            // 450 is the ceiling for an nvarchar key on SQL Server (900 bytes
            // at 2 bytes per char). FCM tokens run ~150-300 chars, so this has
            // room without pushing the index over the limit.
            e.Property(d => d.Token).HasMaxLength(450);

            // Signing out and deleting an account should take the device's
            // subscriptions with it — these are not financial records.
            e.HasOne(d => d.User)
                .WithMany()
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Every send starts by loading a user's live tokens.
            e.HasIndex(d => new { d.UserId, d.DisabledAt });
        });

        // ---------- Review ----------
        b.Entity<Review>(e =>
        {
            e.HasIndex(r => new { r.HostelId, r.StudentUserId }).IsUnique();
            e.HasOne(r => r.Hostel)
                .WithMany(h => h.Reviews)
                .HasForeignKey(r => r.HostelId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.Student)
                .WithMany()
                .HasForeignKey(r => r.StudentUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ---------- Favorite ----------
        b.Entity<Favorite>(e =>
        {
            e.HasIndex(f => new { f.StudentUserId, f.HostelId }).IsUnique();
            e.HasOne(f => f.Student)
                .WithMany()
                .HasForeignKey(f => f.StudentUserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(f => f.Hostel)
                .WithMany()
                .HasForeignKey(f => f.HostelId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ---------- Referral ----------
        b.Entity<Referral>(e =>
        {
            e.Property(r => r.Status).HasConversion<string>();
            e.HasIndex(r => r.Code);
            e.HasIndex(r => new { r.ReferrerUserId, r.CreatedAt });
            // A user can only ever be referred once.
            e.HasIndex(r => r.RefereeUserId).IsUnique().HasFilter("[RefereeUserId] IS NOT NULL");
            e.HasOne(r => r.Referrer)
                .WithMany()
                .HasForeignKey(r => r.ReferrerUserId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(r => r.Referee)
                .WithMany()
                .HasForeignKey(r => r.RefereeUserId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        // ---------- Seed: campuses + common amenities ----------
        // Codes/names match the app's Campus enum (lib/.../entities/campus.dart).
        b.Entity<Campus>().HasData(
            new Campus { Code = "UENR", FullName = "University of Energy and Natural Resources", City = "Sunyani", Latitude = 7.3349, Longitude = -2.3123 },
            new Campus { Code = "USTED", FullName = "AAMUSTED", City = "Kumasi-Tanoso", Latitude = 6.6985, Longitude = -1.6244 }
        );

        b.Entity<Amenity>().HasData(
            new Amenity { Id = 1, Name = "WiFi", IconKey = "wifi" },
            new Amenity { Id = 2, Name = "Air Conditioning", IconKey = "ac" },
            new Amenity { Id = 3, Name = "Ensuite Bathroom", IconKey = "ensuite" },
            new Amenity { Id = 4, Name = "Standby Generator", IconKey = "generator" },
            new Amenity { Id = 5, Name = "Water", IconKey = "water" },
            new Amenity { Id = 6, Name = "Security", IconKey = "security" },
            new Amenity { Id = 7, Name = "Kitchen", IconKey = "kitchen" },
            new Amenity { Id = 8, Name = "Reading Room", IconKey = "study" }
        );
    }
}
