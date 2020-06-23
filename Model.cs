namespace BucketMonitor
{
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;

    public class BucketMonitorContext : DbContext
    {
        public BucketMonitorContext(DbContextOptions<BucketMonitorContext> options)
            : base(options)
        {
        }

        public DbSet<Bucket> Bucket { get; set; }

        public DbSet<ImageEntry> Image { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<Bucket>(entity => {
                entity.Property(e => e.Name)
                    .HasColumnType("varchar(190)");
                entity.HasIndex(e => e.Name)
                    .IsUnique();
            });

            builder.Entity<ImageEntry>(entity => {
                entity.Property(e => e.Status)
                    .HasConversion(new EnumToNumberConverter<ImageStatus, int>());
                entity.Property(e => e.Key)
                    .HasColumnType("varchar(190)");
                entity.HasIndex(e => e.Key)
                    .IsUnique();
            });
        }
    }

    public class Bucket
    {
        public int Id { get; set; }

        [Required]
        public string Name { get; set; }

        public List<ImageEntry> Images { get; } = new List<ImageEntry>();
    }

    public class ImageEntry
    {
        public int Id { get; set; }

        [Required]
        public int BucketId { get; set; }

        [Required]
        public Bucket Bucket { get; set; }

        [Required]
        public string Key { get; set; }

        [Required]
        public DateTime LastModified { get; set; }

        [Required]
        public long FileSize { get; set; }

        [Required]
        public ImageStatus Status { get; set; }
    }
}
