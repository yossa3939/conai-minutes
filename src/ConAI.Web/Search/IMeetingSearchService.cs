namespace ConAI.Web.Search;

public interface IMeetingSearchService
{
    Task<SearchOutcome> SearchAsync(
        string ownerId, string query, int page, CancellationToken cancellationToken);
}
