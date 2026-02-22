using Analytics.Application.Models;

namespace Analytics.Application.Abstractions;

public interface IDatabaseProfileResolver
{
    ResolvedDatabaseProfile Resolve(DatabaseProfileId profileId);
}
