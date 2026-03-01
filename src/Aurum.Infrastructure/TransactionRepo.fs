namespace Aurum.Infrastructure

open System
open System.Data
open Donald
open Aurum.Domain

// ============================================================
// TRANSACTION REPOSITORY
// ============================================================
// TransactionType is a discriminated union — it can't be stored
// in a single DB column as-is. We split it across three columns:
//
//   transaction_type  TEXT    "Income" | "Expense" | "Transfer"
//   category          TEXT    populated for Income/Expense, NULL for Transfer
//   to_account_id     TEXT    populated for Transfer, NULL for others
//
// When reading back, we reconstruct the DU from those columns.
// ============================================================

module TransactionRepo =

    // ============================================================
    // PRIVATE — serialization helpers
    // ============================================================

    /// Reconstructs a TransactionType DU from three DB columns.
    /// `string option` — the column may be NULL, Donald returns None.
    /// `Option.defaultValue` unwraps Some "x" → "x", None → fallback.
    let private parseTransactionType
        (txType: string)
        (category: string option)
        (toAccountId: string option)
        : TransactionType =
        match txType with
        | "Income"   -> Income  (Option.defaultValue "" category)
        | "Expense"  -> Expense (Option.defaultValue "" category)
        | "Transfer" ->
            let targetId =
                toAccountId
                |> Option.map Guid.Parse
                |> Option.defaultValue Guid.Empty
            Transfer targetId
        | s -> failwithf "Unknown transaction type in DB: '%s'" s

    /// Maps a database row to a Transaction record.
    /// `rd.ReadStringOption` reads a nullable TEXT column as string option.
    /// Donald converts .NET nulls into F# option values — no null checks needed.
    let private ofDataReader (rd: IDataReader) : Transaction =
        let txType   = rd.ReadString "transaction_type"
        let category = rd.ReadStringOption "category"
        let toAccId  = rd.ReadStringOption "to_account_id"

        { Id              = rd.ReadString "id"         |> Guid.Parse
          AccountId       = rd.ReadString "account_id" |> Guid.Parse
          Amount          = rd.ReadDecimal "amount"
          TransactionType = parseTransactionType txType category toAccId
          Description     = rd.ReadStringOption "description"
          OccurredAt      = rd.ReadString "occurred_at" |> DateTimeOffset.Parse }

    /// Deconstructs a TransactionType into three column values.
    /// Returns a tuple: (typeName, category option, toAccountId option)
    ///
    /// Tuples in F#: `(a, b, c)` — a lightweight group of values.
    /// Each element can be a different type.
    let private toColumns (txType: TransactionType) : string * string option * string option =
        match txType with
        | Income  cat  -> "Income",   Some cat,         None
        | Expense cat  -> "Expense",  Some cat,         None
        | Transfer tid -> "Transfer", None,             Some (string tid)

    /// Converts a string option to a SqlType for a nullable DB column.
    let private optionalString = function
        | Some s -> SqlType.String s
        | None   -> SqlType.Null


    // ============================================================
    // PUBLIC API
    // ============================================================

    /// Records a new transaction.
    ///
    /// Tuple destructuring — unpack a tuple directly in a let binding:
    ///   let (a, b, c) = someFunction ()
    let insert (conn: IDbConnection) (tx: Transaction) : Async<Result<unit, DomainError>> =
        async {
            try
                let (typeName, category, toAccId) = toColumns tx.TransactionType

                conn
                |> Db.newCommand
                    "INSERT INTO transactions
                        (id, account_id, amount, transaction_type, category, to_account_id, description, occurred_at)
                     VALUES
                        (@id, @account_id, @amount, @transaction_type, @category, @to_account_id, @description, @occurred_at)"
                |> Db.setParams [
                    "id",               SqlType.String  (string tx.Id)
                    "account_id",       SqlType.String  (string tx.AccountId)
                    "amount",           SqlType.Decimal tx.Amount
                    "transaction_type", SqlType.String  typeName
                    "category",         optionalString  category
                    "to_account_id",    optionalString  toAccId
                    "description",      optionalString  tx.Description
                    "occurred_at",      SqlType.String  (tx.OccurredAt.ToString("O"))
                ]
                |> Db.exec

                return Ok ()

            with ex ->
                return Error (Conflict ex.Message)
        }

    /// Queries transactions for an account with optional date and category filters.
    ///
    /// All filters are `option` — None means "don't filter by this".
    /// We build the WHERE clause dynamically from whichever filters are Some.
    ///
    /// `@` concatenates two lists: [1; 2] @ [3; 4] = [1; 2; 3; 4]
    let query
        (conn: IDbConnection)
        (accountId: Guid)
        (from: DateTimeOffset option)
        (to_: DateTimeOffset option)
        (category: string option)
        : Async<Result<Transaction list, DomainError>> =
        async {
            // Build condition strings for active filters
            let conditions = [
                yield "account_id = @account_id"
                if from.IsSome     then yield "occurred_at >= @from"
                if to_.IsSome      then yield "occurred_at <= @to"
                if category.IsSome then yield "category = @category"
            ]

            let whereClause = String.concat " AND " conditions
            let sql = $"SELECT * FROM transactions WHERE {whereClause} ORDER BY occurred_at DESC"

            // Build params — only include params for active filters
            let allParams = [
                yield "account_id", SqlType.String (string accountId)
                match from     with Some d -> yield "from",     SqlType.String (d.ToString("O")) | None -> ()
                match to_      with Some d -> yield "to",       SqlType.String (d.ToString("O")) | None -> ()
                match category with Some c -> yield "category", SqlType.String c                 | None -> ()
            ]

            let results =
                conn
                |> Db.newCommand sql
                |> Db.setParams allParams
                |> Db.query ofDataReader   // returns Transaction list

            return Ok results
        }
