namespace Aurum.Api

open Giraffe
open Aurum.Domain

// ============================================================
// SHARED HTTP HELPERS
// ============================================================
// One place to map domain errors to HTTP responses.
// Both handler modules use this so we define it once.
// ============================================================

module Helpers =

    /// Maps a DomainError to the appropriate HTTP response.
    ///
    /// `setStatusCode` sets the HTTP status code.
    /// `text` writes a plain string body.
    /// `>=>` composes two HttpHandlers into one.
    ///
    /// This is the single boundary where railway errors become HTTP responses.
    let mapError (error: DomainError) : HttpHandler =
        match error with
        | NotFound        msg -> setStatusCode 404 >=> text msg
        | ValidationError msg -> setStatusCode 400 >=> text msg
        | Conflict        msg -> setStatusCode 409 >=> text msg
