using CvPlatform.Core.Entities;

namespace CvPlatform.Core.Access;

public interface IAccessRuleEngine
{
    bool CanAccess(
        Position position,
        bool isAdmin,
        IReadOnlyDictionary<Guid, TypedValue> profileValues);
}
