namespace aspire_react.Server.Application.Common.Interfaces;

/// <summary>
/// [Giai đoạn 1.5 — M1 sibling] Opt-in marker for commands whose output-cache tags are evicted
/// automatically by <see cref="Common.Behaviors.CacheInvalidationBehavior{TRequest,TResponse}"/>
/// after the handler succeeds — replaces the manual <c>_cacheInvalidator.Invalidate*Async()</c>
/// calls currently sprinkled through AdminController/CompaniesController.
/// </para><para>
/// Same opt-in principle as <see cref="ILoggableCommand{TResponse}"/>: commands that do not
/// implement this interface pass through the behavior untouched.
/// </para><para>
/// Tags reference <see cref="CacheTags"/> constants and MUST match the Tags on the cached GET
/// endpoints. Placement: Application/Common/Interfaces (same rationale as ILoggableCommand —
/// an application-pipeline contract; generic response parameter keeps the soft-fail gate typed).
/// </summary>
/// <typeparam name="TResponse">The handler's response type (same as the command's IRequest&lt;TResponse&gt;).</typeparam>
public interface ICacheInvalidatingCommand<TResponse>
{
    /// <summary>Output-cache tags to evict after the handler succeeded (e.g. CacheTags.Categories).</summary>
    IEnumerable<string> CacheTagsToInvalidate { get; }

    /// <summary>
    /// Soft-fail gate (default: always invalidate). Override to return <c>false</c> for responses
    /// that represent a no-op (e.g. ErrorCode COMPANY_MISMATCH) so the cache is not evicted
    /// pointlessly — mirroring the old early-return-before-invalidate behavior in the controllers.
    /// Called only AFTER the handler returned successfully; never called when the handler throws.
    /// </summary>
    bool ShouldInvalidateCache(TResponse response) => true;
}
