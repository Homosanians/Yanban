using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;
using Yanban.Domain.Entities;

namespace Yanban.Infrastructure.Persistence.Configurations;

public class ActivityLogConfiguration : IEntityTypeConfiguration<ActivityLog>
{
    public void Configure(EntityTypeBuilder<ActivityLog> b)
    {
        b.ToTable("activity_logs");
        b.HasKey(x => x.Id);

        // Postgres GENERATED ALWAYS AS IDENTITY: the monotonic ordering/outbox cursor.
        // Unique so it can key a keyset scan; the app never supplies it.
        b.Property(x => x.Sequence).UseIdentityAlwaysColumn();
        b.HasAlternateKey(x => x.Sequence);

        // Stored as the enum name (readable straight from SQL, and adding an action
        // later can't silently renumber history the way an int mapping would).
        b.Property(x => x.Action).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.EntityType).IsRequired().HasMaxLength(20);
        b.Property(x => x.Summary).HasMaxLength(500);
        b.Property(x => x.OldValue).HasMaxLength(500);
        b.Property(x => x.NewValue).HasMaxLength(500);

        // The board feed reads newest-first within a board — a descending composite
        // index serves both the filter and the ORDER BY straight from the index.
        b.HasIndex(x => new { x.BoardId, x.Sequence }).IsDescending(false, true);

        // BoardId/ActorId are intentionally unconstrained columns (see ActivityLog):
        // audit rows must survive deletion of the board or user they reference.

        // Audit search — the same machinery as card search (ADR-12), reused rather than reinvented:
        // a STORED generated tsvector, so it can never drift from the row, plus a GIN index.
        //
        // Everything the row says in words goes in: the summary, and both sides of a rename. The
        // actor's *name* deliberately does not — it lives in `users`, and a generated column may
        // only see its own row. Searching by person is a filter (actorId), not a text match, which
        // is the better answer anyway: it cannot be confused by a card that mentions someone's name.
        //
        // Weight A on the summary, B on the values: "renamed" should outrank a card that merely
        // happens to contain the word.
        b.Property<NpgsqlTsVector>(SearchVectorProperty)
            .HasColumnName("search_vector")
            .HasComputedColumnSql(
                $"setweight(to_tsvector('{CardConfiguration.TextSearchConfig}', coalesce(summary, '')), 'A') || " +
                $"setweight(to_tsvector('{CardConfiguration.TextSearchConfig}', coalesce(old_value, '')), 'B') || " +
                $"setweight(to_tsvector('{CardConfiguration.TextSearchConfig}', coalesce(new_value, '')), 'B')",
                stored: true);

        b.HasIndex(SearchVectorProperty).HasMethod("gin");
    }

    /// <summary>Shadow property: a driver type (NpgsqlTsVector) must not leak into the domain.</summary>
    public const string SearchVectorProperty = "SearchVector";
}
