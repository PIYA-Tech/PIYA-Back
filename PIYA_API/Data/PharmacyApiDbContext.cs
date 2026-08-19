using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Data
{
    /// <summary>
    /// Converts DateTime values to/from UTC so they are stored as <c>timestamptz</c>
    /// and always read back with <c>DateTimeKind.Utc</c>.
    /// </summary>
    internal sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
    {
        public UtcDateTimeConverter() : base(
            v => v.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v, DateTimeKind.Utc),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc)) { }
    }

    internal sealed class UtcNullableDateTimeConverter : ValueConverter<DateTime?, DateTime?>
    {
        public UtcNullableDateTimeConverter() : base(
            v => v.HasValue
                ? (DateTime?)(v.Value.Kind == DateTimeKind.Utc ? v.Value : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc))
                : null,
            v => v.HasValue ? (DateTime?)DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null) { }
    }

    public class PharmacyApiDbContext(DbContextOptions<PharmacyApiDbContext> options) : DbContext(options)
    {
        public DbSet<Pharmacy> Pharmacies { get; set; }
        public DbSet<PharmacyCompany> PharmacyCompanies { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Token> Tokens { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<TwoFactorAuth> TwoFactorAuths { get; set; }
        public DbSet<EmailVerificationToken> EmailVerificationTokens { get; set; }
        public DbSet<PasswordResetToken> PasswordResetTokens { get; set; }
        public DbSet<MedicalDocument> MedicalDocuments { get; set; }
        public DbSet<DeviceToken> DeviceTokens { get; set; }
        
        // Healthcare entities
        public DbSet<Hospital> Hospitals { get; set; }
        public DbSet<DoctorProfile> DoctorProfiles { get; set; }
        public DbSet<PharmacistProfile> PharmacistProfiles { get; set; }
        public DbSet<Appointment> Appointments { get; set; }
        public DbSet<Prescription> Prescriptions { get; set; }
        public DbSet<PrescriptionItem> PrescriptionItems { get; set; }
        public DbSet<Medication> Medications { get; set; }
        public DbSet<PharmacyInventory> PharmacyInventories { get; set; }
        public DbSet<InventoryBatch> InventoryBatches { get; set; }
        public DbSet<InventoryHistory> InventoryHistories { get; set; }
        public DbSet<DoctorNote> DoctorNotes { get; set; }
        public DbSet<QRToken> QRTokens { get; set; }
        public DbSet<RevokedToken> RevokedTokens { get; set; }
        
        // Pharmacy Staff & Permissions
        public DbSet<PharmacyStaff> PharmacyStaff { get; set; }
        public DbSet<UserPermission> UserPermissions { get; set; }
        
        // Ratings, Search, and Reminders
        public DbSet<PharmacyRating> PharmacyRatings { get; set; }
        public DbSet<SearchHistory> SearchHistories { get; set; }
        public DbSet<AppointmentReminder> AppointmentReminders { get; set; }
        public DbSet<PrescriptionRefillReminder> PrescriptionRefillReminders { get; set; }

        // Referrals and Medical Tests
        public DbSet<Referral> Referrals { get; set; }
        public DbSet<MedicalTest> MedicalTests { get; set; }

        // Auth security
        public DbSet<UsedRefreshToken> UsedRefreshTokens { get; set; }
        public DbSet<WebhookSubscription> WebhookSubscriptions { get; set; }
        public DbSet<WebhookDelivery> WebhookDeliveries { get; set; }
        public DbSet<UserConsent> UserConsents { get; set; }
        public DbSet<IntegrationSyncState> IntegrationSyncStates { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // User - TwoFactorAuth (One-to-One)
            modelBuilder.Entity<User>()
                .HasOne(u => u.TwoFactorAuth)
                .WithOne(t => t.User)
                .HasForeignKey<TwoFactorAuth>(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Index on Token.RefreshToken for fast refresh lookups
            modelBuilder.Entity<Token>()
                .HasIndex(t => t.RefreshToken);

            // Index on Token.UserId so per-user purge on login is O(log n) not a full-table scan
            modelBuilder.Entity<Token>()
                .HasIndex(t => t.UserId);

            // RevokedToken: Jti is the PK; index on ExpiresAt for cleanup queries
            modelBuilder.Entity<RevokedToken>()
                .HasKey(r => r.Jti);
            modelBuilder.Entity<RevokedToken>()
                .HasIndex(r => r.ExpiresAt);

            // UsedRefreshToken: TokenHash is the PK; indexes for reuse-detection lookups
            modelBuilder.Entity<UsedRefreshToken>()
                .HasKey(u => u.TokenHash);
            modelBuilder.Entity<UsedRefreshToken>()
                .HasIndex(u => u.Family);
            modelBuilder.Entity<UsedRefreshToken>()
                .HasIndex(u => u.ExpiresAt); // for cleanup
            modelBuilder.Entity<UsedRefreshToken>()
                .HasIndex(u => u.UserId);

            var webhookEventsProperty = modelBuilder.Entity<WebhookSubscription>()
                .Property(item => item.Events)
                .HasConversion(
                    events => System.Text.Json.JsonSerializer.Serialize(events, (System.Text.Json.JsonSerializerOptions?)null),
                    json => System.Text.Json.JsonSerializer.Deserialize<List<WebhookEventType>>(json, (System.Text.Json.JsonSerializerOptions?)null) ?? new());
            webhookEventsProperty.Metadata.SetValueComparer(
                new ValueComparer<List<WebhookEventType>>(
                    (left, right) => left != null && right != null && left.SequenceEqual(right),
                    events => events.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
                    events => events.ToList()));
            modelBuilder.Entity<WebhookSubscription>().HasIndex(item => item.IsActive);
            modelBuilder.Entity<WebhookSubscription>().Property(item => item.Url).HasMaxLength(2048);
            modelBuilder.Entity<WebhookSubscription>().Property(item => item.Secret).HasMaxLength(4096);
            modelBuilder.Entity<WebhookDelivery>().HasIndex(item => new { item.Success, item.NextAttemptAt, item.LockedUntil });
            modelBuilder.Entity<WebhookDelivery>().HasIndex(item => item.WebhookId);
            modelBuilder.Entity<WebhookDelivery>().Property(item => item.Response).HasMaxLength(4096);
            modelBuilder.Entity<WebhookDelivery>()
                .HasOne<WebhookSubscription>()
                .WithMany()
                .HasForeignKey(item => item.WebhookId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<UserConsent>().HasIndex(item => new { item.UserId, item.Purpose, item.GrantedAt });
            modelBuilder.Entity<UserConsent>().Property(item => item.Purpose).HasMaxLength(200);
            modelBuilder.Entity<UserConsent>().Property(item => item.IpAddress).HasMaxLength(64);
            modelBuilder.Entity<UserConsent>().Property(item => item.UserAgent).HasMaxLength(1024);
            modelBuilder.Entity<IntegrationSyncState>().HasKey(item => item.Key);
            modelBuilder.Entity<IntegrationSyncState>().Property(item => item.Key).HasMaxLength(200);

            // Token.Family — index for fast family-revocation queries
            modelBuilder.Entity<Token>()
                .HasIndex(t => t.Family);

            // User - DoctorProfile (One-to-One)
            modelBuilder.Entity<DoctorProfile>()
                .HasOne(dp => dp.User)
                .WithOne()
                .HasForeignKey<DoctorProfile>(dp => dp.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // AuditLog - User (Many-to-One, nullable)
            modelBuilder.Entity<AuditLog>()
                .HasOne(a => a.User)
                .WithMany()
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.SetNull);

            // Indexes for performance
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique();

            modelBuilder.Entity<User>()
                .HasIndex(u => u.Username)
                .IsUnique();

            modelBuilder.Entity<AuditLog>()
                .HasIndex(a => a.CreatedAt);

            modelBuilder.Entity<AuditLog>()
                .HasIndex(a => a.UserId);

            modelBuilder.Entity<AuditLog>()
                .HasIndex(a => a.Action);
            
            // PharmacyStaff - Pharmacy relationship
            modelBuilder.Entity<PharmacyStaff>()
                .HasOne(ps => ps.Pharmacy)
                .WithMany()
                .HasForeignKey(ps => ps.PharmacyId)
                .OnDelete(DeleteBehavior.Cascade);
            
            // PharmacyStaff - User relationship
            modelBuilder.Entity<PharmacyStaff>()
                .HasOne(ps => ps.User)
                .WithMany()
                .HasForeignKey(ps => ps.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            
            // UserPermission - User relationship
            modelBuilder.Entity<UserPermission>()
                .HasOne(up => up.User)
                .WithMany()
                .HasForeignKey(up => up.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            
            // UserPermission - GrantedBy relationship
            modelBuilder.Entity<UserPermission>()
                .HasOne(up => up.GrantedBy)
                .WithMany()
                .HasForeignKey(up => up.GrantedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
            
            // Indexes for PharmacyStaff
            modelBuilder.Entity<PharmacyStaff>()
                .HasIndex(ps => ps.PharmacyId);
            
            modelBuilder.Entity<PharmacyStaff>()
                .HasIndex(ps => ps.UserId);
            
            modelBuilder.Entity<PharmacyStaff>()
                .HasIndex(ps => new { ps.PharmacyId, ps.UserId });
            
            // Indexes for UserPermission
            modelBuilder.Entity<UserPermission>()
                .HasIndex(up => up.UserId);
            
            modelBuilder.Entity<UserPermission>()
                .HasIndex(up => up.Permission);
            
            modelBuilder.Entity<UserPermission>()
                .HasIndex(up => new { up.UserId, up.Permission });
            
            // EmailVerificationToken - User relationship
            modelBuilder.Entity<EmailVerificationToken>()
                .HasOne(evt => evt.User)
                .WithMany()
                .HasForeignKey(evt => evt.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            
            modelBuilder.Entity<EmailVerificationToken>()
                .HasIndex(evt => evt.TokenHash);
            
            modelBuilder.Entity<EmailVerificationToken>()
                .HasIndex(evt => evt.Email);
            
            // PasswordResetToken - User relationship
            modelBuilder.Entity<PasswordResetToken>()
                .HasOne(prt => prt.User)
                .WithMany()
                .HasForeignKey(prt => prt.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            
            modelBuilder.Entity<PasswordResetToken>()
                .HasIndex(prt => prt.TokenHash);
            
            modelBuilder.Entity<PasswordResetToken>()
                .HasIndex(prt => prt.Email);
            
            // MedicalDocument - User relationship
            modelBuilder.Entity<MedicalDocument>()
                .HasOne(md => md.User)
                .WithMany()
                .HasForeignKey(md => md.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            
            modelBuilder.Entity<MedicalDocument>()
                .HasOne(md => md.UploadedBy)
                .WithMany()
                .HasForeignKey(md => md.UploadedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            
            modelBuilder.Entity<MedicalDocument>()
                .HasOne(md => md.VerifiedBy)
                .WithMany()
                .HasForeignKey(md => md.VerifiedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            
            modelBuilder.Entity<MedicalDocument>()
                .HasOne(md => md.Appointment)
                .WithMany()
                .HasForeignKey(md => md.AppointmentId)
                .OnDelete(DeleteBehavior.SetNull);
            
            modelBuilder.Entity<MedicalDocument>()
                .HasOne(md => md.Prescription)
                .WithMany()
                .HasForeignKey(md => md.PrescriptionId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<MedicalDocument>()
                .HasOne(md => md.MedicalTest)
                .WithMany(mt => mt.Documents)
                .HasForeignKey(md => md.MedicalTestId)
                .OnDelete(DeleteBehavior.SetNull);
            
            modelBuilder.Entity<MedicalDocument>()
                .HasIndex(md => md.UserId);
            
            modelBuilder.Entity<MedicalDocument>()
                .HasIndex(md => md.DocumentType);
            
            modelBuilder.Entity<MedicalDocument>()
                .HasIndex(md => md.FileHash);
            
            // DeviceToken - User relationship
            modelBuilder.Entity<DeviceToken>()
                .HasOne(dt => dt.User)
                .WithMany()
                .HasForeignKey(dt => dt.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            
            modelBuilder.Entity<DeviceToken>()
                .HasIndex(dt => dt.Token)
                .IsUnique();
            
            modelBuilder.Entity<DeviceToken>()
                .HasIndex(dt => dt.UserId);
            
            // PharmacyRating relationships
            modelBuilder.Entity<PharmacyRating>()
                .HasOne(pr => pr.Pharmacy)
                .WithMany(p => p.Ratings)
                .HasForeignKey(pr => pr.PharmacyId)
                .OnDelete(DeleteBehavior.Cascade);
            
            modelBuilder.Entity<PharmacyRating>()
                .HasOne(pr => pr.User)
                .WithMany()
                .HasForeignKey(pr => pr.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            
            modelBuilder.Entity<PharmacyRating>()
                .HasOne(pr => pr.Prescription)
                .WithMany()
                .HasForeignKey(pr => pr.PrescriptionId)
                .OnDelete(DeleteBehavior.SetNull);
            
            modelBuilder.Entity<PharmacyRating>()
                .HasIndex(pr => pr.PharmacyId);
            
            modelBuilder.Entity<PharmacyRating>()
                .HasIndex(pr => pr.UserId);
            
            modelBuilder.Entity<PharmacyRating>()
                .HasIndex(pr => new { pr.PharmacyId, pr.UserId });
            
            // PharmacyRatingCategories owned entity (stored as JSON)
            modelBuilder.Entity<PharmacyRating>()
                .OwnsOne(pr => pr.Categories, categories =>
                {
                    categories.ToJson();
                });
            
            // SearchHistory relationships
            modelBuilder.Entity<SearchHistory>()
                .HasOne(sh => sh.User)
                .WithMany()
                .HasForeignKey(sh => sh.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            
            modelBuilder.Entity<SearchHistory>()
                .HasOne(sh => sh.Coordinates)
                .WithMany()
                .HasForeignKey(sh => sh.CoordinatesId)
                .OnDelete(DeleteBehavior.SetNull);
            
            modelBuilder.Entity<SearchHistory>()
                .HasIndex(sh => sh.UserId);
            
            modelBuilder.Entity<SearchHistory>()
                .HasIndex(sh => sh.SearchedAt);
            
            modelBuilder.Entity<SearchHistory>()
                .HasIndex(sh => sh.SearchType);
            
            // AppointmentReminder relationships
            modelBuilder.Entity<AppointmentReminder>()
                .HasOne(ar => ar.Appointment)
                .WithMany()
                .HasForeignKey(ar => ar.AppointmentId)
                .OnDelete(DeleteBehavior.Cascade);
            
            modelBuilder.Entity<AppointmentReminder>()
                .HasOne(ar => ar.User)
                .WithMany()
                .HasForeignKey(ar => ar.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            
            modelBuilder.Entity<AppointmentReminder>()
                .HasIndex(ar => ar.AppointmentId);
            
            modelBuilder.Entity<AppointmentReminder>()
                .HasIndex(ar => ar.ReminderTime);
            
            modelBuilder.Entity<AppointmentReminder>()
                .HasIndex(ar => ar.IsSent);
            
            // PrescriptionRefillReminder relationships
            modelBuilder.Entity<PrescriptionRefillReminder>()
                .HasOne(prr => prr.Prescription)
                .WithMany()
                .HasForeignKey(prr => prr.PrescriptionId)
                .OnDelete(DeleteBehavior.Cascade);
            
            modelBuilder.Entity<PrescriptionRefillReminder>()
                .HasOne(prr => prr.Patient)
                .WithMany()
                .HasForeignKey(prr => prr.PatientId)
                .OnDelete(DeleteBehavior.Cascade);
            
            modelBuilder.Entity<PrescriptionRefillReminder>()
                .HasIndex(prr => prr.PrescriptionId);
            
            modelBuilder.Entity<PrescriptionRefillReminder>()
                .HasIndex(prr => prr.PatientId);
            
            modelBuilder.Entity<PrescriptionRefillReminder>()
                .HasIndex(prr => prr.ReminderDate);
            
            modelBuilder.Entity<PrescriptionRefillReminder>()
                .HasIndex(prr => prr.IsSent);

            // ── Referral ─────────────────────────────────────────────────────────────

            modelBuilder.Entity<Referral>()
                .HasOne(r => r.ReferringDoctor)
                .WithMany()
                .HasForeignKey(r => r.ReferringDoctorId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Referral>()
                .HasOne(r => r.Patient)
                .WithMany()
                .HasForeignKey(r => r.PatientId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Referral>()
                .HasOne(r => r.ReferredToDoctor)
                .WithMany()
                .HasForeignKey(r => r.ReferredToDoctorId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<Referral>()
                .HasOne(r => r.SourceAppointment)
                .WithMany()
                .HasForeignKey(r => r.SourceAppointmentId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            // ResultAppointment: the generated appointment has a back-reference to this Referral
            modelBuilder.Entity<Referral>()
                .HasOne(r => r.ResultAppointment)
                .WithOne(a => a.Referral)
                .HasForeignKey<Referral>(r => r.ResultAppointmentId)
                .OnDelete(DeleteBehavior.SetNull);

            // Self-referential chain: child referral → parent referral
            modelBuilder.Entity<Referral>()
                .HasOne(r => r.ParentReferral)
                .WithMany(r => r.ChildReferrals)
                .HasForeignKey(r => r.ParentReferralId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Referral>()
                .HasIndex(r => r.PatientId);

            modelBuilder.Entity<Referral>()
                .HasIndex(r => r.ReferringDoctorId);

            modelBuilder.Entity<Referral>()
                .HasIndex(r => r.ReferredToDoctorId);

            modelBuilder.Entity<Referral>()
                .HasIndex(r => r.ParentReferralId);

            modelBuilder.Entity<Referral>()
                .HasIndex(r => r.Status);

            modelBuilder.Entity<Referral>()
                .HasIndex(r => r.CreatedAt);

            // ── MedicalTest ───────────────────────────────────────────────────────────

            // ReferralId is now nullable — SetNull so deleting a referral doesn't cascade-delete tests
            modelBuilder.Entity<MedicalTest>()
                .HasOne(mt => mt.Referral)
                .WithMany(r => r.Tests)
                .HasForeignKey(mt => mt.ReferralId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.SetNull);

            // Direct patient link — always populated, even for emergency/walk-in tests
            modelBuilder.Entity<MedicalTest>()
                .HasOne(mt => mt.Patient)
                .WithMany()
                .HasForeignKey(mt => mt.PatientId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<MedicalTest>()
                .HasOne(mt => mt.Appointment)
                .WithMany()
                .HasForeignKey(mt => mt.AppointmentId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<MedicalTest>()
                .HasOne(mt => mt.OrderedByDoctor)
                .WithMany()
                .HasForeignKey(mt => mt.OrderedByDoctorId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<MedicalTest>()
                .HasOne(mt => mt.PerformedByDoctor)
                .WithMany()
                .HasForeignKey(mt => mt.PerformedByDoctorId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<MedicalTest>()
                .HasIndex(mt => mt.PatientId);

            modelBuilder.Entity<MedicalTest>()
                .HasIndex(mt => mt.ReferralId);

            modelBuilder.Entity<MedicalTest>()
                .HasIndex(mt => mt.Status);

            // ── Hospital Director ─────────────────────────────────────────────────────

            // Hospital.DirectorId — optional FK to the User with HospitalDirector role.
            // Restrict prevents accidental deletion of a user who is still a director.
            modelBuilder.Entity<Hospital>()
                .HasOne(h => h.Director)
                .WithMany()
                .HasForeignKey(h => h.DirectorId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Hospital>()
                .HasIndex(h => h.DirectorId);

            // ── Pharmacy Network Owner ────────────────────────────────────────────────

            // PharmacyCompany.OwnerId — optional FK to the User with PharmacyNetworkOwner role.
            modelBuilder.Entity<PharmacyCompany>()
                .HasOne(pc => pc.Owner)
                .WithMany()
                .HasForeignKey(pc => pc.OwnerId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PharmacyCompany>()
                .HasIndex(pc => pc.OwnerId);

            // ── UTC DateTime converters ───────────────────────────────────────────────
            // Applied last so they don't interfere with property-level configuration above.
            // All DateTime/DateTime? columns become `timestamptz` in PostgreSQL and are always
            // read back with DateTimeKind.Utc — no more DateTimeKind.Unspecified surprises.
            var utcConverter         = new UtcDateTimeConverter();
            var utcNullableConverter = new UtcNullableDateTimeConverter();

            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entityType.GetProperties())
                {
                    if (property.ClrType == typeof(DateTime))
                        property.SetValueConverter(utcConverter);
                    else if (property.ClrType == typeof(DateTime?))
                        property.SetValueConverter(utcNullableConverter);
                }
            }
        }
    }
}
