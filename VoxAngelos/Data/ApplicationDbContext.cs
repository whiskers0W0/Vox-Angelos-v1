using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace VoxAngelos.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<UserProfile> UserProfiles { get; set; }
        public DbSet<UserIdentityDocument> UserIdentityDocuments { get; set; }
        public DbSet<UserFaceVerification> UserFaceVerifications { get; set; }
        public DbSet<UserOcrVerification> UserOcrVerifications { get; set; }
        public DbSet<AccountApproval> AccountApprovals { get; set; }
        public DbSet<UserLoginAudit> UserLoginAudits { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Unique index on phone number
            builder.Entity<ApplicationUser>()
                .HasIndex(u => u.PhoneNumber)
                .IsUnique();

            // One-to-one: ApplicationUser <-> UserProfile
            builder.Entity<UserProfile>()
                .HasOne(up => up.User)
                .WithOne(u => u.UserProfile)
                .HasForeignKey<UserProfile>(up => up.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // One-to-one: ApplicationUser <-> AccountApproval
            builder.Entity<AccountApproval>()
                .HasOne(aa => aa.User)
                .WithOne(u => u.AccountApproval)
                .HasForeignKey<AccountApproval>(aa => aa.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // One-to-many: ApplicationUser <-> UserIdentityDocument
            builder.Entity<UserIdentityDocument>()
                .HasOne(uid => uid.User)
                .WithMany(u => u.IdentityDocuments)
                .HasForeignKey(uid => uid.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // One-to-many: ApplicationUser <-> UserFaceVerification
            builder.Entity<UserFaceVerification>()
                .HasOne(ufv => ufv.User)
                .WithMany(u => u.FaceVerifications)
                .HasForeignKey(ufv => ufv.UserId)
                .OnDelete(DeleteBehavior.NoAction);

            // One-to-many: UserIdentityDocument <-> UserFaceVerification
            builder.Entity<UserFaceVerification>()
                .HasOne(ufv => ufv.IdentityDocument)
                .WithMany(uid => uid.FaceVerifications)
                .HasForeignKey(ufv => ufv.IdentityDocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            // One-to-many: ApplicationUser <-> UserOcrVerification
            builder.Entity<UserOcrVerification>()
                .HasOne(uov => uov.User)
                .WithMany(u => u.OcrVerifications)
                .HasForeignKey(uov => uov.UserId)
                .OnDelete(DeleteBehavior.NoAction);

            // One-to-many: UserIdentityDocument <-> UserOcrVerification
            builder.Entity<UserOcrVerification>()
                .HasOne(uov => uov.IdentityDocument)
                .WithMany(uid => uid.OcrVerifications)
                .HasForeignKey(uov => uov.IdentityDocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            // One-to-many: ApplicationUser <-> UserLoginAudit
            builder.Entity<UserLoginAudit>()
                .HasOne(ula => ula.User)
                .WithMany(u => u.LoginAudits)
                .HasForeignKey(ula => ula.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}