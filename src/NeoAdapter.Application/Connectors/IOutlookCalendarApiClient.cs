using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NeoAdapter.Application.Connectors;

public interface IOutlookCalendarApiClient
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> GetCalendarsAsync(string microsoftUserId, string accessToken, CancellationToken cancellationToken);
    Task CreateEventAsync(string microsoftUserId, string? calendarName, string accessToken, object eventPayload, CancellationToken cancellationToken);
}
