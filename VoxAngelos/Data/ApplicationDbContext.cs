using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace VoxAngelos.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>, IDataProtectionKeyContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // Data Protection keys (antiforgery tokens, auth cookies) live here instead of
        // local disk so they survive Render's container respins/redeploys — the app's
        // ephemeral filesystem otherwise regenerates them and invalidates any token
        // already embedded in a page a user has open (surfaces as submissions/logins
        // silently failing, or "the antiforgery token could not be decrypted").
        public DbSet<Microsoft.AspNetCore.DataProtection.EntityFrameworkCore.DataProtectionKey> DataProtectionKeys { get; set; } = null!;

        public DbSet<UserProfile> UserProfiles { get; set; }
        public DbSet<UserIdentityDocument> UserIdentityDocuments { get; set; }
        public DbSet<UserFaceVerification> UserFaceVerifications { get; set; }
        public DbSet<UserOcrVerification> UserOcrVerifications { get; set; }
        public DbSet<AccountApproval> AccountApprovals { get; set; }
        public DbSet<UserLoginAudit> UserLoginAudits { get; set; }
        public DbSet<Concern> Concerns { get; set; }
        public DbSet<ConcernAttachment> ConcernAttachments { get; set; }
        public DbSet<ConcernTimelineEvent> ConcernTimelineEvents { get; set; }
        public DbSet<UserNotification> UserNotifications { get; set; }
        public DbSet<Recommendation> Recommendations { get; set; }
        public DbSet<RecommendationRating> RecommendationRatings { get; set; }
        public DbSet<RecommendationAttachment> RecommendationAttachments { get; set; }
        public DbSet<ClassificationCorrection> ClassificationCorrections { get; set; }
        public DbSet<LearnedKeyword> LearnedKeywords { get; set; }

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

            builder.Entity<Recommendation>()
                .HasOne(r => r.Citizen)
                .WithMany()
                .HasForeignKey(r => r.CitizenId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<RecommendationRating>()
                .HasOne(r => r.Recommendation)
                .WithMany(rec => rec.Ratings)
                .HasForeignKey(r => r.RecommendationId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<RecommendationRating>()
                .HasOne(r => r.Citizen)
                .WithMany()
                .HasForeignKey(r => r.CitizenId)
                .OnDelete(DeleteBehavior.Restrict);

            // One rating per citizen per recommendation — re-rating upserts this row
            // instead of accumulating duplicates.
            builder.Entity<RecommendationRating>()
                .HasIndex(r => new { r.RecommendationId, r.CitizenId })
                .IsUnique();

            // The leaderboard reads sorted by this column directly (see
            // RecommendationRatingService) — index it so that stays cheap even
            // without the in-memory cache layer.
            builder.Entity<Recommendation>()
                .HasIndex(r => r.CompositeScore);

            builder.Entity<RecommendationAttachment>()
                .HasOne(a => a.Recommendation)
                .WithMany(r => r.Attachments)
                .HasForeignKey(a => a.RecommendationId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<ClassificationCorrection>()
                .HasOne(cc => cc.Concern)
                .WithMany()
                .HasForeignKey(cc => cc.ConcernId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<ClassificationCorrection>()
                .HasOne(cc => cc.ReviewedBy)
                .WithMany()
                .HasForeignKey(cc => cc.ReviewedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            // A concern can only ever be reviewed once — enforced at the DB level so a
            // race between two LGU reviewers can't produce two conflicting verdicts.
            builder.Entity<ClassificationCorrection>()
                .HasIndex(cc => cc.ConcernId)
                .IsUnique();

            builder.Entity<LearnedKeyword>()
                .HasIndex(lk => new { lk.Word, lk.Department })
                .IsUnique();

            // Location Density Score (Urgency Algorithm): geography point + GIST index so
            // ST_DWithin distance lookups in UrgencyScoreService stay index-backed instead
            // of scanning every concern's coordinates on every submission.
            builder.Entity<Concern>()
                .Property(c => c.Location)
                .HasColumnType("geography (Point, 4326)");

            builder.Entity<Concern>()
                .HasIndex(c => c.Location)
                .HasMethod("GIST");

            builder.Entity<ConcernTimelineEvent>()
                .HasOne(e => e.Concern)
                .WithMany(c => c.TimelineEvents)
                .HasForeignKey(e => e.ConcernId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<ConcernTimelineEvent>()
                .HasIndex(e => new { e.ConcernId, e.CreatedAt });

            builder.Entity<UserNotification>()
                .HasOne(n => n.RecipientUser)
                .WithMany()
                .HasForeignKey(n => n.RecipientUserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<UserNotification>()
                .HasIndex(n => new { n.RecipientUserId, n.IsRead, n.CreatedAt });
        }
    }
}
