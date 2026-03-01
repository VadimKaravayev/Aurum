namespace Aurum.Api

open System
open System.Data
open Giraffe
open Aurum.Domain
open Aurum.Infrastructure

// ============================================================
// ACCOUNT HTTP HANDLERS
// ============================================================
// Each handler follows the same pipeline:
//
//   1. Bind  — deserialize JSON body or parse URL/query params
//   2. Validate — run domain validators, get Result
//   3. Persist — call the repo, get Async<Result>
//   4. Respond — map Ok to 2xx JSON, map Error to mapError
//
// Handlers are `HttpHandler` — a function:
//   HttpFunc -> HttpContext -> Task<HttpContext option>
//
// We write them as: fun next ctx -> task { ... }
// `next` is the next handler in the pipeline (Giraffe plumbing).
// `ctx` is the current HTTP context (request + response).
// ============================================================

module AccountHandlers =

    // ============================================================
    // REQUEST / RESPONSE TYPES
    // ============================================================
    // `[<CLIMutable>]` adds a parameterless constructor so that
    // System.Text.Json can deserialize into F# records.
    // Without it, JSON binding would fail — F# records are immutable
    // by default and have no setter-based constructor.
    // ============================================================

    [<CLIMutable>]
    type CreateAccountDto = {
        Name        : string
        AccountType : string   // "Checking" | "Savings" | "Cash"
        Currency    : string   // "USD" | "EUR" | "UAH"
    }

    type AccountResponse = {
        Id          : string
        Name        : string
        AccountType : string
        Currency    : string
        CreatedAt   : string
    }

    type BalanceResponse = {
        AccountId : string
        Balance   : decimal
    }


    // ============================================================
    // PRIVATE HELPERS
    // ============================================================

    /// Maps an Account domain record to a JSON-safe response type.
    /// `string x` calls .ToString() — works on DU cases like `USD` → "USD".
    let private toResponse (a: Account) : AccountResponse = {
        Id          = string a.Id
        Name        = a.Name
        AccountType = string a.AccountType
        Currency    = string a.Currency
        CreatedAt   = a.CreatedAt.ToString("O")
    }

    let private parseCurrency = function
        | "USD" -> Ok USD | "EUR" -> Ok EUR | "UAH" -> Ok UAH
        | s     -> Error (ValidationError $"Unknown currency: '{s}'")

    let private parseAccountType = function
        | "Checking" -> Ok Checking | "Savings" -> Ok Savings | "Cash" -> Ok Cash
        | s          -> Error (ValidationError $"Unknown account type: '{s}'")

    /// Validates the DTO and builds a domain Account record.
    ///
    /// `Result.bind` passes the Ok value to the next function,
    /// or short-circuits on Error — railway-oriented programming.
    ///
    /// `Result.map` transforms the Ok value without changing the shape.
    let private validateAndBuild (dto: CreateAccountDto) : Result<Account, DomainError> =
        Validation.validateName dto.Name
        |> Result.bind (fun name ->
            parseCurrency dto.Currency
            |> Result.bind (fun currency ->
                parseAccountType dto.AccountType
                |> Result.map (fun accountType ->
                    { Id          = Guid.NewGuid()
                      Name        = name
                      AccountType = accountType
                      Currency    = currency
                      CreatedAt   = DateTimeOffset.UtcNow })))


    // ============================================================
    // HANDLERS
    // ============================================================

    /// POST /accounts
    /// Deserializes body → validates → inserts → 201 with account JSON.
    ///
    /// `ctx.BindJsonAsync<T>()` deserializes the request body.
    /// `Async.StartAsTask` converts F# Async to .NET Task so we can
    /// use `let!` inside a `task { }` computation expression.
    let createAccount (conn: IDbConnection) : HttpHandler =
        fun next ctx ->
            task {
                let! dto = ctx.BindJsonAsync<CreateAccountDto>()

                match validateAndBuild dto with
                | Error e -> return! Helpers.mapError e next ctx
                | Ok account ->
                    let! result = AccountRepo.insert conn account |> Async.StartAsTask
                    match result with
                    | Ok ()   -> return! (setStatusCode 201 >=> json (toResponse account)) next ctx
                    | Error e -> return! Helpers.mapError e next ctx
            }

    /// GET /accounts/{id}
    /// Parses the id string → looks up in DB → 200 with account JSON.
    ///
    /// `id` comes from the URL — Giraffe extracts it via `routef`.
    let getAccount (conn: IDbConnection) (id: string) : HttpHandler =
        fun next ctx ->
            task {
                match Validation.validateGuid id with
                | Error e -> return! Helpers.mapError e next ctx
                | Ok guid ->
                    let! result = AccountRepo.getById conn guid |> Async.StartAsTask
                    match result with
                    | Ok account -> return! json (toResponse account) next ctx
                    | Error e    -> return! Helpers.mapError e next ctx
            }

    /// GET /accounts/{id}/balance
    /// Loads all transactions for the account and calculates the balance.
    let getBalance (conn: IDbConnection) (id: string) : HttpHandler =
        fun next ctx ->
            task {
                match Validation.validateGuid id with
                | Error e -> return! Helpers.mapError e next ctx
                | Ok guid ->
                    let! result = TransactionRepo.query conn guid None None None |> Async.StartAsTask
                    match result with
                    | Error e            -> return! Helpers.mapError e next ctx
                    | Ok transactions    ->
                        let balance = Logic.calculateBalance transactions
                        return! json { AccountId = string guid; Balance = balance } next ctx
            }
