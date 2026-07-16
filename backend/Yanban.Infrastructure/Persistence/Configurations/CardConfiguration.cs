using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;
using Yanban.Domain.Entities;

namespace Yanban.Infrastructure.Persistence.Configurations;

public class CardConfiguration : IEntityTypeConfiguration<Card>
{
    /// <summary>Name of the shadow search-vector property; the search service reads it via EF.Property.</summary>
    public const string SearchVectorProperty = "SearchVector";

    /// <summary>
    /// The Postgres text-search config. A query must be parsed with the same config the vector
    /// was built with, or stemmed words match nothing.
    ///
    /// CAUTION: this const is the single source of truth only at model-build time. It is
    /// interpolated into the computed-column SQL, which is then frozen into a migration; the
    /// already-applied migration hard-codes 'russian'. The query side (CardSearchService) reads
    /// this const at runtime. So changing it alone desynchronizes the two and search quietly stops
    /// stemming, with no test failing (the tests move with the const too). Changing it means a new
    /// migration that rebuilds the column.
    /// </summary>
    public const string TextSearchConfig = "russian";

    public void Configure(EntityTypeBuilder<Card> b)
    {
        b.ToTable("cards");
        b.HasKey(x => x.Id);

        b.Property(x => x.Title).IsRequired().HasMaxLength(500);
        b.Property(x => x.Rank).IsRequired().HasMaxLength(64);
        b.HasIndex(x => new { x.ListId, x.Rank });

        b.HasOne(x => x.List)
            .WithMany(l => l.Cards)
            .HasForeignKey(x => x.ListId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Assignee)
            .WithMany()
            .HasForeignKey(x => x.AssigneeId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasOne(x => x.CreatedBy)
            .WithMany()
            .HasForeignKey(x => x.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        // Optimistic concurrency via Postgres' system column xmin. This is exactly
        // what Npgsql's UseXminAsConcurrencyToken() wraps; mapping it explicitly is
        // provider-identical and the "xid" system column is excluded from DDL.
        b.Property<uint>("xmin")
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        // Full-text search. A shadow property for the same reason xmin is one: the vector
        // is a derived index artifact, not domain state, and mapping it here keeps
        // NpgsqlTsVector (a database driver type) out of Yanban.Domain, which has no
        // package references at all.
        //
        // STORED means Postgres recomputes it on every write, so the index can never drift
        // from the row and no trigger or backfill is needed. Two details matter:
        //   - the config must be named. to_tsvector(regconfig, text) is IMMUTABLE, while
        //     the one-arg to_tsvector(text) is only STABLE, which Postgres rejects here;
        //   - 'russian' is bilingual in stock Postgres. It maps asciiword to english_stem
        //     and word to russian_stem, so one column stems both "deploying" to "deploy"
        //     and "задачи" to "задач".
        // setweight A/B is what makes a title hit outrank a body hit under ts_rank.
        b.Property<NpgsqlTsVector>(SearchVectorProperty)
            .HasColumnName("search_vector")
            .HasComputedColumnSql(
                $"setweight(to_tsvector('{TextSearchConfig}', coalesce(title, '')), 'A') || " +
                $"setweight(to_tsvector('{TextSearchConfig}', coalesce(description, '')), 'B')",
                stored: true);

        b.HasIndex(SearchVectorProperty).HasMethod("gin");
    }
}
