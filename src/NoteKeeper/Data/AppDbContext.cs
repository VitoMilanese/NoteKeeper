using Microsoft.EntityFrameworkCore;
using NoteKeeper.Models;

namespace NoteKeeper.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Note> Notes => Set<Note>();
    public DbSet<NoteBlock> NoteBlocks => Set<NoteBlock>();
    public DbSet<NoteTag> NoteTags => Set<NoteTag>();

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
        });

        modelBuilder.Entity<Note>(entity =>
        {
            entity.HasIndex(x => x.ProjectId);
            entity.HasIndex(x => x.UpdatedAtUtc);
            entity.HasMany(x => x.Blocks)
                .WithOne(x => x.Note)
                .HasForeignKey(x => x.NoteId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.Tags)
                .WithOne(x => x.Note)
                .HasForeignKey(x => x.NoteId)
                .OnDelete(DeleteBehavior.Cascade);
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
