using Microsoft.EntityFrameworkCore;
using RetroArr.Core.Games;
using RetroArr.Core.Download.History;
using RetroArr.Core.Notifications;
using System.Collections.Generic;
using System.Text.Json;
using System.Linq;
using System;

namespace RetroArr.Core.Data
{
    public class RetroArrDbContext : DbContext
    {
        public RetroArrDbContext(DbContextOptions<RetroArrDbContext> options) : base(options)
        {
        }

        public DbSet<Game> Games { get; set; }
        public DbSet<Platform> Platforms { get; set; }
        public DbSet<GameFile> GameFiles { get; set; }
        
        // New entities for collections, tags, reviews
        public DbSet<Collection> Collections { get; set; }
        public DbSet<CollectionGame> CollectionGames { get; set; }
        public DbSet<Tag> Tags { get; set; }
        public DbSet<GameTag> GameTags { get; set; }
        public DbSet<GameReview> GameReviews { get; set; }
        public DbSet<Webhook> Webhooks { get; set; }

        // Download tracking
        public DbSet<DownloadHistoryEntry> DownloadHistory { get; set; }
        public DbSet<DownloadBlacklistEntry> DownloadBlacklist { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Game>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Title).IsRequired();
                
                // Owned type for Images
                entity.OwnsOne(e => e.Images, images =>
                {
                    var stringListComparer = new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<string>>(
                        (c1, c2) => c1.SequenceEqual(c2),
                        c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                        c => c.ToList());

                    images.Property(i => i.Screenshots)
                        .HasConversion(
                            v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null),
                            v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions)null) ?? new List<string>())
                        .Metadata.SetValueComparer(stringListComparer);

                    images.Property(i => i.Artworks)
                        .HasConversion(
                            v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null),
                            v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions)null) ?? new List<string>())
                        .Metadata.SetValueComparer(stringListComparer);
                });

                // Genres as JSON string with comparer
                var stringListComparer = new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<string>>(
                    (c1, c2) => c1.SequenceEqual(c2),
                    c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                    c => c.ToList());

                entity.Property(e => e.Genres)
                    .HasConversion(
                        v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null),
                        v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions)null) ?? new List<string>())
                    .Metadata.SetValueComparer(stringListComparer);

                entity.HasMany(e => e.GameFiles)
                    .WithOne(f => f.Game)
                    .HasForeignKey(f => f.GameId);

                entity.HasIndex(e => new { e.Title, e.PlatformId })
                    .IsUnique()
                    .HasDatabaseName("IX_Games_Title_PlatformId");

                entity.HasIndex(e => e.Path)
                    .IsUnique()
                    .HasFilter("[Path] IS NOT NULL")
                    .HasDatabaseName("IX_Games_Path");

                entity.HasIndex(e => new { e.IgdbId, e.PlatformId })
                    .IsUnique()
                    .HasFilter("[IgdbId] IS NOT NULL")
                    .HasDatabaseName("IX_Games_IgdbId_PlatformId");

                entity.HasIndex(e => e.PlatformId)
                    .HasDatabaseName("IX_Games_PlatformId");
            });

            modelBuilder.Entity<Platform>(entity =>
            {
                entity.HasKey(e => e.Id);
            });

            modelBuilder.Entity<GameFile>(entity =>
            {
                entity.HasKey(e => e.Id);

                var stringListComparer = new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<string>>(
                    (c1, c2) => c1.SequenceEqual(c2),
                    c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                    c => c.ToList());

                entity.Property(e => e.Languages)
                    .HasConversion(
                        v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null),
                        v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions)null) ?? new List<string>())
                    .Metadata.SetValueComparer(stringListComparer);
            });

            // Collection configuration
            modelBuilder.Entity<Collection>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
                entity.HasMany(e => e.CollectionGames)
                    .WithOne(cg => cg.Collection)
                    .HasForeignKey(cg => cg.CollectionId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<CollectionGame>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasOne(e => e.Game)
                    .WithMany()
                    .HasForeignKey(e => e.GameId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Tag configuration
            modelBuilder.Entity<Tag>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(50);
                entity.HasIndex(e => e.Name).IsUnique();
                entity.HasMany(e => e.GameTags)
                    .WithOne(gt => gt.Tag)
                    .HasForeignKey(gt => gt.TagId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<GameTag>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => new { e.GameId, e.TagId }).IsUnique();
                entity.HasOne(e => e.Game)
                    .WithMany()
                    .HasForeignKey(e => e.GameId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // GameReview configuration
            modelBuilder.Entity<GameReview>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.GameId).IsUnique();
                entity.HasOne(e => e.Game)
                    .WithMany()
                    .HasForeignKey(e => e.GameId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Webhook configuration
            modelBuilder.Entity<Webhook>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Url).IsRequired();
            });

            // Download History
            modelBuilder.Entity<DownloadHistoryEntry>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.DownloadId).IsRequired();
                entity.Property(e => e.Title).IsRequired();
                entity.Property(e => e.State)
                    .HasConversion<string>()
                    .HasMaxLength(20);
                entity.HasIndex(e => e.DownloadId)
                    .IsUnique()
                    .HasDatabaseName("IX_DownloadHistory_DownloadId");
                entity.HasIndex(e => e.State)
                    .HasDatabaseName("IX_DownloadHistory_State");
                entity.HasIndex(e => e.Platform)
                    .HasDatabaseName("IX_DownloadHistory_Platform");
                entity.HasIndex(e => e.ImportedAt)
                    .HasDatabaseName("IX_DownloadHistory_ImportedAt");
            });

            // Download Blacklist
            modelBuilder.Entity<DownloadBlacklistEntry>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Title).IsRequired();
                entity.Property(e => e.Reason).IsRequired();
                entity.HasIndex(e => e.DownloadId)
                    .HasDatabaseName("IX_DownloadBlacklist_DownloadId");
                entity.HasIndex(e => e.Title)
                    .HasDatabaseName("IX_DownloadBlacklist_Title");
            });
        }
    }
}
