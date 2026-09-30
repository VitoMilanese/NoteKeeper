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
            EnsureProjectsSchema(connection);
            EnsureTimeTrackingColumns(connection);

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

                    UPDATE "Notes"
                    SET "Status" = 4
                    WHERE "Status" = 0
                      AND EXISTS (
                        SELECT 1
                        FROM "NoteTags"
                        WHERE "NoteTags"."NoteId" = "Notes"."Id"
                          AND "NoteTags"."Name" = 'selected' COLLATE NOCASE
                      );

                    UPDATE "Notes"
                    SET "Status" = 5
                    WHERE "Status" = 0
                      AND EXISTS (
                        SELECT 1
                        FROM "NoteTags"
                        WHERE "NoteTags"."NoteId" = "Notes"."Id"
                          AND "NoteTags"."Name" = 'test' COLLATE NOCASE
                      );

                    UPDATE "Notes"
                    SET "Status" = 7
                    WHERE "Status" = 0
                      AND EXISTS (
                        SELECT 1
                        FROM "NoteTags"
                        WHERE "NoteTags"."NoteId" = "Notes"."Id"
                          AND "NoteTags"."Name" = 'suspended' COLLATE NOCASE
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


    private static void EnsureTimeTrackingColumns(
        System.Data.Common.DbConnection connection)
    {
        if (!HasColumn(connection, "Notes", "EstimatedTimeMinutes"))
        {
            Execute(
                connection,
                """
                ALTER TABLE "Notes"
                ADD COLUMN "EstimatedTimeMinutes" INTEGER NULL;
                """);
        }

        if (!HasColumn(connection, "Notes", "SpentTimeMinutes"))
        {
            Execute(
                connection,
                """
                ALTER TABLE "Notes"
                ADD COLUMN "SpentTimeMinutes" INTEGER NULL;
                """);
        }
    }

    private static void EnsureProjectsSchema(
        System.Data.Common.DbConnection connection)
    {
        if (!HasTable(connection, "Projects"))
        {
            Execute(
                connection,
                """
                CREATE TABLE "Projects" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_Projects" PRIMARY KEY AUTOINCREMENT,
                    "Name" TEXT NOT NULL,
                    "CreatedAtUtc" TEXT NOT NULL,
                    "UpdatedAtUtc" TEXT NOT NULL
                );

                CREATE INDEX "IX_Projects_Name"
                    ON "Projects" ("Name");

                CREATE INDEX "IX_Projects_UpdatedAtUtc"
                    ON "Projects" ("UpdatedAtUtc");
                """);
        }

        var addedProjectId = false;
        if (!HasColumn(connection, "Notes", "ProjectId"))
        {
            Execute(
                connection,
                """
                ALTER TABLE "Notes"
                ADD COLUMN "ProjectId" INTEGER NOT NULL DEFAULT 0;
                """);
            addedProjectId = true;
        }

        Execute(
            connection,
            """
            CREATE INDEX IF NOT EXISTS "IX_Notes_ProjectId"
                ON "Notes" ("ProjectId");
            """);

        var orphanCount = ScalarInt(
            connection,
            """
            SELECT COUNT(*)
            FROM "Notes"
            WHERE "ProjectId" = 0
               OR NOT EXISTS (
                   SELECT 1
                   FROM "Projects"
                   WHERE "Projects"."Id" = "Notes"."ProjectId"
               );
            """);

        var projectCount = ScalarInt(
            connection,
            """
            SELECT COUNT(*)
            FROM "Projects";
            """);

        if (projectCount == 0 && (addedProjectId || orphanCount > 0))
        {
            Execute(
                connection,
                """
                INSERT INTO "Projects" ("Name", "CreatedAtUtc", "UpdatedAtUtc")
                VALUES (
                    'Default project',
                    COALESCE(
                        (SELECT MIN("CreatedAtUtc") FROM "Notes"),
                        CURRENT_TIMESTAMP
                    ),
                    COALESCE(
                        (SELECT MAX("UpdatedAtUtc") FROM "Notes"),
                        CURRENT_TIMESTAMP
                    )
                );
                """);
            projectCount = 1;
        }

        if (projectCount > 0 && orphanCount > 0)
        {
            Execute(
                connection,
                """
                UPDATE "Notes"
                SET "ProjectId" = (
                    SELECT "Id"
                    FROM "Projects"
                    ORDER BY "Id"
                    LIMIT 1
                )
                WHERE "ProjectId" = 0
                   OR NOT EXISTS (
                       SELECT 1
                       FROM "Projects"
                       WHERE "Projects"."Id" = "Notes"."ProjectId"
                   );
                """);
        }
    }

    private static bool HasTable(
        System.Data.Common.DbConnection connection,
        string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table'
              AND name = $name;
            """;

        var parameter = command.CreateParameter();
        parameter.ParameterName = "$name";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);

        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    private static int ScalarInt(
        System.Data.Common.DbConnection connection,
        string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(command.ExecuteScalar());
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
            SELECT "Id", 'backlog'
            FROM "Notes"
            WHERE "Status" = 0;

            INSERT OR IGNORE INTO "NoteTags" ("NoteId", "Name")
            SELECT "Id", 'selected'
            FROM "Notes"
            WHERE "Status" = 4;

            INSERT OR IGNORE INTO "NoteTags" ("NoteId", "Name")
            SELECT "Id", 'active'
            FROM "Notes"
            WHERE "Status" = 1;

            INSERT OR IGNORE INTO "NoteTags" ("NoteId", "Name")
            SELECT "Id", 'test'
            FROM "Notes"
            WHERE "Status" = 5;

            INSERT OR IGNORE INTO "NoteTags" ("NoteId", "Name")
            SELECT "Id", 'done'
            FROM "Notes"
            WHERE "Status" IN (2, 3, 6);

            INSERT OR IGNORE INTO "NoteTags" ("NoteId", "Name")
            SELECT "Id", 'released'
            FROM "Notes"
            WHERE "Status" = 3;

            INSERT OR IGNORE INTO "NoteTags" ("NoteId", "Name")
            SELECT "Id", 'suspended'
            FROM "Notes"
            WHERE "Status" = 7;
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
