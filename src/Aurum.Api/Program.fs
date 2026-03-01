module Program

open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Giraffe
open Aurum.Api
open Aurum.Infrastructure

// ============================================================
// STARTUP
// ============================================================
// `[<EntryPoint>]` marks this function as the program entry point.
// It must take `string array` and return `int` (the exit code).
//
// ASP.NET Core startup follows two phases:
//   1. Build — register services, configure options
//   2. Run   — wire up middleware, start the server
// ============================================================

[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder(args)

    // Register Giraffe as the HTTP handler framework
    builder.Services.AddGiraffe() |> ignore

    let app = builder.Build()

    // Read connection string from appsettings.json
    let connStr = app.Configuration.GetConnectionString("Default")

    // Create and open the SQLite connection
    let conn = Db.connect connStr

    // Run schema migrations — creates tables if they don't exist
    Db.migrate conn

    // Wire up Giraffe with our route table
    app.UseGiraffe(Router.routes conn)

    app.Run()

    0
