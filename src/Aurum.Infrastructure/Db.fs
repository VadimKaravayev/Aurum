namespace Aurum.Infrastructure

open System.Data
open Microsoft.Data.Sqlite
open Donald

// ============================================================
// CONNECTION + SCHEMA
// ============================================================
// This module owns two responsibilities:
//   1. Creating and opening a database connection
//   2. Running schema migrations (CREATE TABLE IF NOT EXISTS)
//
// The rest of the infrastructure layer just receives an
// IDbConnection — it doesn't care how it was created.
// This makes it easy to swap SQLite for Postgres later.
// ============================================================

module Db =

    /// Creates an open SQLite connection.
    ///
    /// Returns IDbConnection (the abstract interface) rather than
    /// SqliteConnection (the concrete type). This means the repos
    /// only depend on the interface — they're not tied to SQLite.
    ///
    /// `:>` is the F# upcast operator — "treat this as its base type".
    /// Like casting to an interface in C#: `(IDbConnection)conn`
    let connect (connectionString: string) : IDbConnection =
        let conn = new SqliteConnection(connectionString)
        conn.Open()
        conn :> IDbConnection   // upcast to the interface

    /// Runs schema migrations — creates tables if they don't exist.
    /// Safe to call on every startup (IF NOT EXISTS).
    ///
    /// Donald pipeline:
    ///   conn |> Db.newCommand sql   → creates a DbCommand
    ///   |> Db.exec                  → runs it, returns unit
    ///
    /// SQLite types used:
    ///   TEXT    — strings, GUIDs (stored as "xxxxxxxx-xxxx-..."), dates (ISO 8601)
    ///   REAL    — decimal amounts (SQLite has no decimal type)
    let migrate (conn: IDbConnection) : unit =
        conn
        |> Db.newCommand """
            CREATE TABLE IF NOT EXISTS accounts (
                id           TEXT NOT NULL PRIMARY KEY,
                name         TEXT NOT NULL,
                account_type TEXT NOT NULL,
                currency     TEXT NOT NULL,
                created_at   TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS transactions (
                id               TEXT NOT NULL PRIMARY KEY,
                account_id       TEXT NOT NULL,
                amount           REAL NOT NULL,
                transaction_type TEXT NOT NULL,
                category         TEXT,
                to_account_id    TEXT,
                description      TEXT,
                occurred_at      TEXT NOT NULL,
                FOREIGN KEY (account_id) REFERENCES accounts(id)
            );
        """
        |> Db.exec
