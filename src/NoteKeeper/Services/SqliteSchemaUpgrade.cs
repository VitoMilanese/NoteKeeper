using System.Data;
using Microsoft.EntityFrameworkCore;
using NoteKeeper.Data;

namespace NoteKeeper.Services;

public static class SqliteSchemaUpgrade
{
    public static void Apply(AppDbContext db)
    {
        db.Database.EnsureCreated();

        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        if (shouldClose)
        {
            connection.Open();
        }

        try
        {
            if (!HasColumn(connection, "Notes", "Status"))
            {
                Execute(
                    connection,
                    """
                    ALTER TABLE "Notes"
                    ADD COLUMN "Status" INTEGER NOT NULL DEFAULT 0;
                    """);

                Execute(
                    connection,
                    """
                    UPDATE "Notes"
                    SET "Status" = 3
                    WHERE EXISTS (
                        SELECT 1
                        FROM "NoteTags"
                        WHERE "NoteTags"."NoteId" = "Notes"."Id"
                          AND "NoteTags"."Name" = 'released' COLLATE NOCASE
                    );

                    UPDATE "Notes"
                    SET "Status" = 2
                    WHERE "Status" = 0
                      AND EXISTS (
                        SELECT 1
                        FROM "NoteTags"
                        WHERE "NoteTags"."NoteId" = "Notes"."Id"
                          AND "NoteTags"."Name" = 'done' COLLATE NOCASE
                      );

                    UPDATE "Notes"
                    SET "Status" = 1
                    WHERE "Status" = 0
                      AND EXISTS (
                        SELECT 1
                        FROM "NoteTags"
                        WHERE "NoteTags"."NoteId" = "Notes"."Id"
                          AND "NoteTags"."Name" = 'active' COLLATE NOCASE
                      );
                    """);
            }

            EnsureImplicitTags(connection);
        }
        finally
        {
            if (shouldClose)
            {
                connection.Close();
            }
        }
    }

    private static bool HasColumn(
        System.Data.Common.DbConnection connection,
        string tableName,
        string columnName)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            $"PRAGMA table_info(\"{tableName.Replace("\"", "\"\"")}\");";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(
                    reader.GetString(1),
                    columnName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void EnsureImplicitTags(
        System.Data.Common.DbConnection connection)
    {
        Execute(
            connection,
            """
            INSERT OR IGNORE INTO "NoteTags" ("NoteId", "Name")
            SELECT "Id", 'active'
            FROM "Notes"
            WHERE "Status" = 1;

            INSERT OR IGNORE INTO "NoteTags" ("NoteId", "Name")
            SELECT "Id", 'done'
            FROM "Notes"
            WHERE "Status" IN (2, 3);

            INSERT OR IGNORE INTO "NoteTags" ("NoteId", "Name")
            SELECT "Id", 'released'
            FROM "Notes"
            WHERE "Status" = 3;
            """);
    }

    private static void Execute(
        System.Data.Common.DbConnection connection,
        string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
