namespace Aurum.Infrastructure

open System
open System.Data
open Donald
open Aurum.Domain

// ============================================================
// ACCOUNT REPOSITORY
// ============================================================
// Each function:
//   - Takes an IDbConnection (injected from outside)
//   - Returns Async<Result<'T, DomainError>>
//
// Async     — the operation may do I/O
// Result    — the operation may fail in an expected way
// DomainError — the failure is a known domain case
//
// The caller decides when to run the async and how to handle
// the result. This function just describes what to do.
// ============================================================

module AccountRepo =

    // ============================================================
    // PRIVATE HELPERS — serialization / deserialization
    // ============================================================
    // These are private because they're implementation details.
    // The outside world doesn't need to know how we serialize DUs.
    // ============================================================

    /// Converts a string from the DB back into a Currency DU case.
    ///
    /// `function` is shorthand for `fun x -> match x with`.
    /// It's used when the function body is just a single match.
    let private parseCurrency = function
        | "USD" -> USD
        | "EUR" -> EUR
        | "UAH" -> UAH
        | s     -> failwithf "Unknown currency in DB: '%s'" s

    let private parseAccountType = function
        | "Checking" -> Checking
        | "Savings"  -> Savings
        | "Cash"     -> Cash
        | s          -> failwithf "Unknown account type in DB: '%s'" s

    /// Maps a database row to an Account record.
    ///
    /// `IDataReader` is the standard .NET interface for reading rows.
    /// Donald extends it with typed Read* methods so you don't have
    /// to cast from `obj` manually.
    ///
    /// `rd.ReadString "col"` reads a TEXT column as string.
    /// `Guid.Parse` converts the string back to a Guid.
    /// `DateTimeOffset.Parse` converts ISO 8601 string back to DateTimeOffset.
    let private ofDataReader (rd: IDataReader) : Account = {
        Id          = rd.ReadString "id"           |> Guid.Parse
        Name        = rd.ReadString "name"
        AccountType = rd.ReadString "account_type" |> parseAccountType
        Currency    = rd.ReadString "currency"     |> parseCurrency
        CreatedAt   = rd.ReadString "created_at"   |> DateTimeOffset.Parse
    }


    // ============================================================
    // ASYNC COMPUTATION EXPRESSION
    // ============================================================
    // `async { }` is a computation expression — a special block
    // that builds up an Async<'T> value without running it immediately.
    //
    // `return` inside async { } wraps a value in Async.
    // `let! x = asyncOp` runs an async operation and binds the result.
    // `do! asyncOp` runs an async operation for its side effect.
    //
    // It's like `async/await` in C#, but the whole thing is a value
    // you can pass around, compose, and run when you choose.
    // ============================================================

    /// Looks up an account by ID.
    /// Returns Ok Account if found, Error (NotFound ...) if not.
    let getById (conn: IDbConnection) (id: Guid) : Async<Result<Account, DomainError>> =
        async {
            let result =
                conn
                |> Db.newCommand "SELECT id, name, account_type, currency, created_at FROM accounts WHERE id = @id"
                |> Db.setParams [ "id", SqlType.String (string id) ]
                |> Db.querySingle ofDataReader  // returns Account option

            // Pattern match on Option to produce a Result
            return
                match result with
                | Some account -> Ok account
                | None         -> Error (NotFound $"Account '{id}' not found")
        }

    /// Inserts a new account into the database.
    ///
    /// `string account.AccountType` calls .ToString() on a DU case.
    /// For a case with no data like `Checking`, this returns "Checking".
    ///
    /// `try / with` handles unexpected DB exceptions (e.g. duplicate ID).
    /// Expected failures use Result; unexpected ones use exceptions.
    let insert (conn: IDbConnection) (account: Account) : Async<Result<unit, DomainError>> =
        async {
            try
                conn
                |> Db.newCommand
                    "INSERT INTO accounts (id, name, account_type, currency, created_at)
                     VALUES (@id, @name, @account_type, @currency, @created_at)"
                |> Db.setParams [
                    "id",           SqlType.String (string account.Id)
                    "name",         SqlType.String account.Name
                    "account_type", SqlType.String (string account.AccountType)
                    "currency",     SqlType.String (string account.Currency)
                    // "O" format: ISO 8601 round-trip, e.g. "2025-01-15T10:30:00+00:00"
                    "created_at",   SqlType.String (account.CreatedAt.ToString("O"))
                ]
                |> Db.exec

                return Ok ()

            with ex ->
                // Duplicate primary key or constraint violation
                return Error (Conflict ex.Message)
        }
