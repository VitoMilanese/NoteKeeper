using Microsoft.EntityFrameworkCore;
using NoteKeeper.Models;

namespace NoteKeeper.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Note> Notes => Set<Note>();
    public DbSet<NoteBlock> NoteBlocks => Set<NoteBlock>();
    public DbSet<NoteTag> NoteTags => Set<NoteTag>();
    public DbSet<TimeManagementDay> TimeManagementDays => Set<TimeManagementDay>();
    public DbSet<TimeManagementEntry> TimeManagementEntries => Set<TimeManagementEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Project>(entity =>
        {
            entity.HasIndex(x => x.Name);
            entity.HasIndex(x => x.UpdatedAtUtc);
            entity.HasMany(x => x.Notes)
                .WithOne(x => x.Project)
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.TimeManagementDays)
                .WithOne(x => x.Project)
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Note>(entity =>
        {
            entity.HasIndex(x => x.ProjectId);
            entity.HasIndex(x => new { x.ProjectId, x.IsTimeManagementPinned });
            entity.HasIndex(x => x.UpdatedAtUtc);
            entity.HasMany(x => x.Blocks)
                .WithOne(x => x.Note)
                .HasForeignKey(x => x.NoteId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.Tags)
                .WithOne(x => x.Note)
                .HasForeignKey(x => x.NoteId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.TimeEntries)
                .WithOne(x => x.Note)
                .HasForeignKey(x => x.NoteId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<TimeManagementDay>(entity =>
        {
            entity.HasIndex(x => new { x.ProjectId, x.Date }).IsUnique();
            entity.HasMany(x => x.Entries)
                .WithOne(x => x.Day)
                .HasForeignKey(x => x.TimeManagementDayId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TimeManagementEntry>(entity =>
        {
            entity.HasIndex(x => x.TimeManagementDayId);
            entity.HasIndex(x => x.NoteId);
        });

        modelBuilder.Entity<NoteBlock>(entity =>
        {
            entity.HasIndex(x => new { x.NoteId, x.SortOrder });
        });

        modelBuilder.Entity<NoteTag>(entity =>
        {
            entity.HasIndex(x => x.Name);
            entity.HasIndex(x => new { x.NoteId, x.Name }).IsUnique();
        });
    }
}
