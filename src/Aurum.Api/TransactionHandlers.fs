namespace Aurum.Api

open System
open System.Data
open Giraffe
open Aurum.Domain
open Aurum.Infrastructure

module TransactionHandlers =

    // ============================================================
    // REQUEST / RESPONSE TYPES
    // ============================================================

    [<CLIMutable>]
    type CreateTransactionDto = {
        AccountId   : string
        Amount      : decimal
        Type        : string   // "Income" | "Expense" | "Transfer"
        Category    : string   // required for Income/Expense, null otherwise
        ToAccountId : string   // required for Transfer, null otherwise
        Description : string   // optional
    }

    type TransactionResponse = {
        Id          : string
        AccountId   : string
        Amount      : decimal
        Type        : string
        Category    : string   // null for Transfer
        ToAccountId : string   // null for Income/Expense
        Description : string   // null if not provided
        OccurredAt  : string
    }

    type MonthlySummaryResponse = {
        Year    : int
        Month   : int
        Income  : decimal
        Expense : decimal
        Net     : decimal
    }


    // ============================================================
    // PRIVATE HELPERS
    // ============================================================

    let private toResponse (t: Transaction) : TransactionResponse =
        // Deconstruct TransactionType into plain strings for the response.
        // Null is used here intentionally — it serializes as JSON null.
        let typeName, category, toAccountId =
            match t.TransactionType with
            | Income  cat  -> "Income",   cat,         null
            | Expense cat  -> "Expense",  cat,         null
            | Transfer tid -> "Transfer", null,        string tid

        { Id          = string t.Id
          AccountId   = string t.AccountId
          Amount      = t.Amount
          Type        = typeName
          Category    = category
          ToAccountId = toAccountId
          Description = t.Description |> Option.defaultValue null
          OccurredAt  = t.OccurredAt.ToString("O") }

    /// Validates the DTO and builds a TransactionType DU.
    ///
    /// `Result.map` transforms the Ok value:
    ///   - validateCategory returns Result<string, DomainError>
    ///   - Result.map Income turns it into Result<TransactionType, DomainError>
    let private parseTransactionType (dto: CreateTransactionDto) : Result<TransactionType, DomainError> =
        match dto.Type with
        | "Income"   -> Validation.validateCategory dto.Category  |> Result.map Income
        | "Expense"  -> Validation.validateCategory dto.Category  |> Result.map Expense
        | "Transfer" -> Validation.validateGuid dto.ToAccountId   |> Result.map Transfer
        | s          -> Error (ValidationError $"Unknown transaction type: '{s}'")

    let private validateAndBuild (dto: CreateTransactionDto) : Result<Transaction, DomainError> =
        Validation.validateGuid dto.AccountId
        |> Result.bind (fun accountId ->
            Validation.validateAmount dto.Amount
            |> Result.bind (fun amount ->
                parseTransactionType dto
                |> Result.map (fun txType ->
                    { Id              = Guid.NewGuid()
                      AccountId       = accountId
                      Amount          = amount
                      TransactionType = txType
                      Description     = Option.ofObj dto.Description
                      OccurredAt      = DateTimeOffset.UtcNow })))

    /// Parses a query string value as int, returns a Result.
    let private parseIntParam (name: string) (value: string option) : Result<int, DomainError> =
        match value with
        | None -> Error (ValidationError $"Missing required query parameter: '{name}'")
        | Some s ->
            match System.Int32.TryParse(s) with
            | true, n -> Ok n
            | _       -> Error (ValidationError $"'{s}' is not a valid integer for '{name}'")

    /// Tries to parse a query string value as DateTimeOffset.
    /// Returns None if the param is absent (it's optional).
    let private parseDateParam (value: string option) : Result<DateTimeOffset option, DomainError> =
        match value with
        | None -> Ok None
        | Some s ->
            match DateTimeOffset.TryParse(s) with
            | true, d -> Ok (Some d)
            | _       -> Error (ValidationError $"'{s}' is not a valid date")


    // ============================================================
    // HANDLERS
    // ============================================================

    /// POST /transactions
    let createTransaction (conn: IDbConnection) : HttpHandler =
        fun next ctx ->
            task {
                let! dto = ctx.BindJsonAsync<CreateTransactionDto>()

                match validateAndBuild dto with
                | Error e -> return! Helpers.mapError e next ctx
                | Ok tx ->
                    let! result = TransactionRepo.insert conn tx |> Async.StartAsTask
                    match result with
                    | Ok ()   -> return! (setStatusCode 201 >=> json (toResponse tx)) next ctx
                    | Error e -> return! Helpers.mapError e next ctx
            }

    /// GET /transactions?accountId=&from=&to=&category=
    ///
    /// `ctx.TryGetQueryStringValue "key"` returns string option.
    /// All filters except accountId are optional.
    let listTransactions (conn: IDbConnection) : HttpHandler =
        fun next ctx ->
            task {
                let rawAccountId = ctx.TryGetQueryStringValue "accountId"
                let rawFrom      = ctx.TryGetQueryStringValue "from"
                let rawTo        = ctx.TryGetQueryStringValue "to"
                let category     = ctx.TryGetQueryStringValue "category"

                // Validate required param
                match Validation.validateGuid (Option.defaultValue "" rawAccountId) with
                | Error e -> return! Helpers.mapError e next ctx
                | Ok accountId ->

                    // Validate optional date params
                    match parseDateParam rawFrom, parseDateParam rawTo with
                    | Error e, _ | _, Error e -> return! Helpers.mapError e next ctx
                    | Ok from, Ok to_ ->

                        let! result = TransactionRepo.query conn accountId from to_ category |> Async.StartAsTask
                        match result with
                        | Error e  -> return! Helpers.mapError e next ctx
                        | Ok txs   -> return! json (txs |> List.map toResponse) next ctx
            }

    /// GET /summary/monthly?accountId=&year=&month=
    let getMonthlySummary (conn: IDbConnection) : HttpHandler =
        fun next ctx ->
            task {
                let rawAccountId = ctx.TryGetQueryStringValue "accountId"
                let rawYear      = ctx.TryGetQueryStringValue "year"
                let rawMonth     = ctx.TryGetQueryStringValue "month"

                let validationResult =
                    Validation.validateGuid (Option.defaultValue "" rawAccountId)
                    |> Result.bind (fun accountId ->
                        parseIntParam "year" rawYear
                        |> Result.bind (fun year ->
                            Validation.validateYear year
                            |> Result.bind (fun year ->
                                parseIntParam "month" rawMonth
                                |> Result.bind (fun month ->
                                    Validation.validateMonth month
                                    |> Result.map (fun month -> accountId, year, month)))))

                match validationResult with
                | Error e -> return! Helpers.mapError e next ctx
                | Ok (accountId, year, month) ->
                    let! result = TransactionRepo.query conn accountId None None None |> Async.StartAsTask
                    match result with
                    | Error e -> return! Helpers.mapError e next ctx
                    | Ok transactions ->
                        let summary = Logic.getMonthlySummary year month transactions
                        return! json {
                            Year    = summary.Year
                            Month   = summary.Month
                            Income  = summary.Income
                            Expense = summary.Expense
                            Net     = summary.Net
                        } next ctx
            }
