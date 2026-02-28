namespace Aurum.Domain

// `open` brings a namespace into scope — like `using` in C#.
// We need System for Guid and DateTimeOffset.
open System

// ============================================================
// DISCRIMINATED UNIONS
// ============================================================
// A discriminated union (DU) is a type that can be exactly
// ONE of a fixed set of named cases.
//
// Think of it as an enum — but each case can carry its own data,
// and the compiler forces you to handle every case.
//
// In C# you'd model this as a base class + subclasses.
// In F# one `type` declaration does the same job, and the
// compiler guarantees exhaustiveness at compile time.
// ============================================================

/// The supported currencies.
/// No data attached — these are pure named cases, like an enum.
type Currency =
    | USD
    | EUR
    | UAH

/// What kind of bank account this is.
type AccountType =
    | Checking
    | Savings
    | Cash

/// What kind of financial movement this is.
///
/// Notice that each case carries *different* data:
///   Income  → a category string (e.g. "Salary")
///   Expense → a category string (e.g. "Groceries")
///   Transfer → the Guid of the destination account
///
/// This is what separates DUs from plain enums.
/// The data is baked into the type itself.
type TransactionType =
    | Income   of category: string
    | Expense  of category: string
    | Transfer of toAccountId: Guid

/// All error cases in the domain, unified into one type.
///
/// This becomes the 'E in Result<'T, DomainError>.
/// Later, pattern matching in HTTP handlers will map each case
/// to the correct HTTP status code:
///   NotFound        → 404
///   ValidationError → 400
///   Conflict        → 409
///
/// Using a DU means no stringly-typed errors, and the compiler
/// forces you to handle every error case.
type DomainError =
    | NotFound        of string
    | ValidationError of string
    | Conflict        of string


// ============================================================
// RECORD TYPES
// ============================================================
// Records are immutable, named data structures.
//
// Key properties:
// - All fields are immutable by default (no setters)
// - To "update" a record you write: { existing with Field = newValue }
//   This returns a NEW record — the original is unchanged.
// - Structural equality is generated automatically:
//   two Accounts with the same field values are equal, no
//   .Equals() override needed.
// ============================================================

/// A financial account owned by the user.
///
/// `Guid` is used for IDs — avoids integer ID collisions across systems.
/// `DateTimeOffset` stores time with timezone offset (safer than DateTime).
type Account = {
    Id          : Guid
    Name        : string
    AccountType : AccountType
    Currency    : Currency
    CreatedAt   : DateTimeOffset
}

/// A single financial movement recorded against an account.
///
/// `Description` is `string option` — it may or may not be present.
/// `option<'T>` is itself a discriminated union built into F#:
///
///   type Option<'T> =
///     | Some of 'T   // value is present
///     | None         // value is absent
///
/// This is F#'s safe alternative to null. You can never accidentally
/// treat a None as a value — the compiler forces you to unwrap it.
///
/// `Amount` is always positive. Whether it adds or subtracts
/// from the balance is determined by `TransactionType`.
type Transaction = {
    Id              : Guid
    AccountId       : Guid
    Amount          : decimal
    TransactionType : TransactionType
    Description     : string option
    OccurredAt      : DateTimeOffset
}

/// The result of aggregating transactions for a single month.
/// A pure data shape — no behaviour, just structure.
type MonthlySummary = {
    Year    : int
    Month   : int
    Income  : decimal
    Expense : decimal
    Net     : decimal   // Income - Expense; can be negative
}
