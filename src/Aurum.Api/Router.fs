namespace Aurum.Api

open System.Data
open Giraffe

// ============================================================
// ROUTE TABLE
// ============================================================
// `choose` tries each handler in order — first match wins.
// `GET >=> route "/path"` means: match GET requests at this path.
// `routef "/path/%s" f` extracts a string from the URL and passes
//   it to the handler function f.
//
// `>=>` is the Kleisli fish operator — it composes two HttpHandlers
// into one. Think of it as "and then": match GET and then this route.
//
// IMPORTANT: more specific routes must come before generic ones.
//   /accounts/%s/balance must be before /accounts/%s,
//   otherwise /accounts/123/balance would match /accounts/%s
//   with id = "123/balance".
// ============================================================

module Router =

    let routes (conn: IDbConnection) : HttpHandler =
        choose [
            // Accounts
            POST >=> route  "/accounts"              >=> AccountHandlers.createAccount conn
            GET  >=> routef "/accounts/%s/balance"   (AccountHandlers.getBalance conn)   // more specific first
            GET  >=> routef "/accounts/%s"           (AccountHandlers.getAccount conn)

            // Transactions
            POST >=> route "/transactions"           >=> TransactionHandlers.createTransaction conn
            GET  >=> route "/transactions"           >=> TransactionHandlers.listTransactions conn

            // Summary
            GET  >=> route "/summary/monthly"        >=> TransactionHandlers.getMonthlySummary conn

            // Fallback
            setStatusCode 404 >=> text "Not found"
        ]
