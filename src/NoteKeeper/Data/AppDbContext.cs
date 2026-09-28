using Microsoft.EntityFrameworkCore;
using NoteKeeper.Models;

namespace NoteKeeper.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Note> Notes => Set<Note>();
    public DbSet<NoteBlock> NoteBlocks => Set<NoteBlock>();
    public DbSet<NoteTag> NoteTags => Set<NoteTag>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Note>(entity =>
        {
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
