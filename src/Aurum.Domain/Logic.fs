namespace Aurum.Domain

// ============================================================
// PURE FUNCTIONS
// ============================================================
// This module contains only pure functions:
//   - No database access
//   - No HTTP
//   - No side effects
//   - Same inputs always produce same outputs
//
// Because they're pure, these functions are trivially unit-testable.
// You just call them with data and check the result — no mocks needed.
// ============================================================

module Logic =

    /// Calculates the net balance from a list of transactions.
    ///
    /// Income adds to the balance.
    /// Expense and Transfer subtract from it.
    /// Returns 0m for an empty list (List.sumBy default).
    ///
    /// Pipeline operator |>:
    ///   `transactions |> List.sumBy f`  is the same as  `List.sumBy f transactions`
    ///   Reads left-to-right: "take transactions, then sum them by this rule"
    let calculateBalance (transactions: Transaction list) : decimal =
        transactions
        |> List.sumBy (fun t ->
            match t.TransactionType with
            | Income _   ->  t.Amount   // positive: money in
            | Expense _  -> -t.Amount   // negative: money out
            | Transfer _ -> -t.Amount)  // negative: money leaves this account

    // `private` — visible only within this module, not part of the public API
    let private isIncome (t: Transaction) =
        match t.TransactionType with
        | Income _ -> true
        | _        -> false

    let private isExpense (t: Transaction) =
        match t.TransactionType with
        | Expense _ -> true
        | _         -> false

    /// Aggregates transactions for a specific year and month into a MonthlySummary.
    ///
    /// Note the three separate argument lists: (year) (month) (transactions)
    /// This is currying — each argument is applied one at a time.
    /// You can partially apply:
    ///   let summarise2025 = getMonthlySummary 2025
    ///   let summarise2025 : int -> Transaction list -> MonthlySummary
    let getMonthlySummary (year: int) (month: int) (transactions: Transaction list) : MonthlySummary =

        // `let` inside a function is a local immutable binding — like `val` in Kotlin
        let inMonth =
            transactions
            |> List.filter (fun t ->
                t.OccurredAt.Year  = year &&
                t.OccurredAt.Month = month)

        let totalIncome =
            inMonth
            |> List.filter isIncome
            |> List.sumBy (fun t -> t.Amount)

        let totalExpense =
            inMonth
            |> List.filter isExpense
            |> List.sumBy (fun t -> t.Amount)

        // Record construction — every field must be provided.
        // The compiler will error if you miss one.
        { Year    = year
          Month   = month
          Income  = totalIncome
          Expense = totalExpense
          Net     = totalIncome - totalExpense }

    /// Groups transactions by category and sums amounts per category.
    /// Transfers are excluded — they have no category.
    /// Returns a list of (categoryName, totalAmount) tuples.
    ///
    /// List.choose = map + filter in one step.
    ///   Return Some to include the value, None to skip it.
    let summariseByCategory (transactions: Transaction list) : (string * decimal) list =
        transactions
        |> List.choose (fun t ->
            match t.TransactionType with
            | Income  cat -> Some (cat, t.Amount)
            | Expense cat -> Some (cat, t.Amount)
            | Transfer _  -> None)                  // skip — no category
        |> List.groupBy fst                         // group by category name (first of tuple)
        |> List.map (fun (cat, pairs) ->
            let total = pairs |> List.sumBy snd     // sum amounts (second of tuple)
            cat, total)
