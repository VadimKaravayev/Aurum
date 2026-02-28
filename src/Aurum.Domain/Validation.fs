namespace Aurum.Domain

open System

// ============================================================
// RESULT TYPE — Railway-Oriented Programming
// ============================================================
// Result<'T, 'E> is a built-in F# discriminated union:
//
//   type Result<'T, 'E> =
//     | Ok    of 'T   // success — carries the validated value
//     | Error of 'E   // failure — carries the error
//
// Every validator in this module returns Result<something, DomainError>.
//
// This is "railway-oriented programming": imagine two parallel tracks —
// the happy path (Ok) and the error path (Error). A function either
// keeps you on the happy track or switches you to the error track.
// Once you're on the error track you stay there — no exceptions thrown.
//
// Compared to throwing exceptions:
//   - Errors are part of the function signature — callers can't ignore them
//   - No try/catch noise at the call site
//   - Composable: you can chain validators with Result.bind
// ============================================================

module Validation =

    // ============================================================
    // ACTIVE PATTERNS
    // ============================================================
    // An active pattern is a named pattern you define yourself
    // and can use inside `match` expressions.
    //
    // Syntax: let (|PatternName|_|) input = ...
    //
    // The (|...|_|) syntax means it's a PARTIAL active pattern:
    //   - Returns `Some value` if the input matches
    //   - Returns `None` if it doesn't
    //
    // The `_` in (|Name|_|) means "this might not match".
    // A total active pattern (|A|B|) would cover all cases.
    //
    // Why use them? They make `match` expressions read like prose:
    //
    //   match s with
    //   | PositiveDecimal v -> ...   ← reads: "if s is a positive decimal, call it v"
    //   | _                 -> ...   ← reads: "otherwise..."
    //
    // vs the alternative:
    //   let parsed, v = Decimal.TryParse(s)
    //   if parsed && v > 0m then ...
    // ============================================================

    /// Matches a string that parses as a decimal greater than zero.
    /// The `when` clause is a guard — an extra condition on top of the match.
    let (|PositiveDecimal|_|) (s: string) =
        match Decimal.TryParse(s) with
        | true, v when v > 0m -> Some v   // guard: v must be positive
        | _                   -> None

    /// Matches a string that parses as a valid Guid.
    let (|ValidGuid|_|) (s: string) =
        match Guid.TryParse(s) with
        | true, g -> Some g
        | _       -> None


    // ============================================================
    // VALIDATORS
    // ============================================================
    // Each validator follows the same pattern:
    //   - Takes raw/untrusted input
    //   - Returns Result<ValidatedValue, DomainError>
    //   - Never throws exceptions
    //   - Is a pure function (no side effects, no I/O)
    //
    // Type annotations are optional in F# (inference is very strong),
    // but we write them here explicitly for clarity.
    //
    // Function signature syntax:
    //   let functionName (param: InputType) : ReturnType = body
    // ============================================================

    /// Validates that a name is non-empty after trimming whitespace.
    let validateName (name: string) : Result<string, DomainError> =
        if String.IsNullOrWhiteSpace(name) then
            Error (ValidationError "Name cannot be empty")
        else
            Ok (name.Trim())

    /// Validates that a decimal amount is positive.
    /// `0m` — the `m` suffix marks a decimal literal in F#.
    let validateAmount (amount: decimal) : Result<decimal, DomainError> =
        if amount <= 0m then
            Error (ValidationError "Amount must be greater than zero")
        else
            Ok amount

    /// Validates that a category string is non-empty.
    let validateCategory (category: string) : Result<string, DomainError> =
        if String.IsNullOrWhiteSpace(category) then
            Error (ValidationError "Category cannot be empty")
        else
            Ok (category.Trim())

    /// Validates a raw string amount coming from HTTP input.
    /// Uses our active pattern — the match reads almost like English.
    ///
    /// $"..." is an F# interpolated string — like $"" in C#.
    let validateAmountString (s: string) : Result<decimal, DomainError> =
        match s with
        | PositiveDecimal v -> Ok v
        | _                 -> Error (ValidationError $"'{s}' is not a valid positive amount")

    /// Validates a raw string ID (e.g. from a URL path segment).
    /// Uses our active pattern.
    let validateGuid (s: string) : Result<Guid, DomainError> =
        match s with
        | ValidGuid g -> Ok g
        | _           -> Error (ValidationError $"'{s}' is not a valid ID")

    /// Validates a year is plausible (not in the distant past/future).
    let validateYear (year: int) : Result<int, DomainError> =
        let current = DateTimeOffset.UtcNow.Year
        if year < 2000 || year > current + 1 then
            Error (ValidationError $"Year {year} is out of range")
        else
            Ok year

    /// Validates a month is 1–12.
    let validateMonth (month: int) : Result<int, DomainError> =
        if month < 1 || month > 12 then
            Error (ValidationError $"Month {month} must be between 1 and 12")
        else
            Ok month
