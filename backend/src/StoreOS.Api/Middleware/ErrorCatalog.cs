using System.Net;

namespace StoreOS.Api.Middleware;

public static class ErrorCatalog
{
    private static readonly Dictionary<string, HttpStatusCode> Explicit = new()
    {
        // 401 - the client must (re)authenticate; the Angular interceptor refreshes on 401 only
        ["unauthenticated"] = HttpStatusCode.Unauthorized,
        ["invalid_credentials"] = HttpStatusCode.Unauthorized,
        ["token_version_stale"] = HttpStatusCode.Unauthorized,
        ["invalid_refresh_token"] = HttpStatusCode.Unauthorized,
        ["refresh_token_expired"] = HttpStatusCode.Unauthorized,
        ["refresh_token_reuse_detected"] = HttpStatusCode.Unauthorized,
        ["tenant_required"] = HttpStatusCode.Unauthorized,
        // 403 - authenticated but not allowed
        ["forbidden"] = HttpStatusCode.Forbidden,
        ["tenant_inactive"] = HttpStatusCode.Forbidden,
        // 400 - malformed request context
        ["branch_context_required"] = HttpStatusCode.BadRequest,
        ["idempotency_key_required"] = HttpStatusCode.BadRequest,
        ["idempotency_key_invalid"] = HttpStatusCode.BadRequest,
        ["invalid_permission_code"] = HttpStatusCode.BadRequest,
        // 404 - not found
        ["sale_not_found"] = HttpStatusCode.NotFound,
        // 409 - state conflicts
        ["branch_code_taken"] = HttpStatusCode.Conflict,
        ["imei_already_active"] = HttpStatusCode.Conflict,
        ["idempotency_key_reuse"] = HttpStatusCode.Conflict,
        ["idempotency_in_progress"] = HttpStatusCode.Conflict,
        ["serialization_conflict"] = HttpStatusCode.Conflict,
        // 422 - business-rule violation
        ["current_password_incorrect"] = HttpStatusCode.UnprocessableEntity,
        ["sale_not_active"] = HttpStatusCode.UnprocessableEntity,
        ["payment_exceeds_balance"] = HttpStatusCode.UnprocessableEntity,
        ["invalid_refund_amount"] = HttpStatusCode.UnprocessableEntity,
        ["no_lines_to_refund"] = HttpStatusCode.UnprocessableEntity,
    };

    public static HttpStatusCode StatusFor(string code)
    {
        if (Explicit.TryGetValue(code, out var s)) return s;
        if (code.EndsWith("_not_found", StringComparison.Ordinal)) return HttpStatusCode.NotFound;
        return HttpStatusCode.UnprocessableEntity; // every other business-rule violation
    }
}
