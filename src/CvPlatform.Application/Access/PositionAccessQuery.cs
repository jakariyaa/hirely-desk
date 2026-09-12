using CvPlatform.Application.Authorization;
using CvPlatform.Core.Data;
using CvPlatform.Core.Entities;
using CvPlatform.Core.Enums;

namespace CvPlatform.Application.Access;

internal static class PositionAccessQuery
{
    public static IQueryable<Position> WhereAccessibleToCandidate(
        IQueryable<Position> query,
        IAppDbContext db,
        ActorContext actor)
    {
        if (actor.IsPrivileged)
            return query;

        var candidateUserId = actor.UserId;
        return query.Where(p =>
            p.IsPublic ||
            p.OwnerId == actor.UserId ||
            (p.AccessRules.Any() &&
                !p.AccessRules.Any(r =>
                    !db.ProfileAttributeValues.Any(v =>
                        v.Profile.UserId == candidateUserId &&
                        v.AttributeDefinitionId == r.AttributeDefinitionId &&
                        v.AttributeDefinition.DataType == r.DataType &&
                        ((r.Operator == RuleOperator.Equals &&
                            ((r.DataType == AttributeDataType.String && v.StringValue != null && v.StringValue == r.ComparisonValue) ||
                            (r.DataType == AttributeDataType.Text && v.TextValue != null && v.TextValue == r.ComparisonValue) ||
                            (r.DataType == AttributeDataType.Numeric && v.NumericValue.HasValue && r.NumericComparison.HasValue && v.NumericValue.Value == r.NumericComparison.Value) ||
                            (r.DataType == AttributeDataType.Date && v.DateValue.HasValue && r.DateComparison.HasValue && v.DateValue.Value == r.DateComparison.Value) ||
                            (r.DataType == AttributeDataType.Dropdown && v.DropdownOption != null && v.DropdownOption == r.ComparisonValue))) ||
                        (r.Operator == RuleOperator.NotEquals &&
                            ((r.DataType == AttributeDataType.String && v.StringValue != null && v.StringValue != r.ComparisonValue) ||
                            (r.DataType == AttributeDataType.Text && v.TextValue != null && v.TextValue != r.ComparisonValue))) ||
                        (r.Operator == RuleOperator.Contains &&
                            ((r.DataType == AttributeDataType.String && v.StringValue != null && r.ComparisonValue != null && v.StringValue.ToLower().Contains(r.ComparisonValue.ToLower())) ||
                            (r.DataType == AttributeDataType.Text && v.TextValue != null && r.ComparisonValue != null && v.TextValue.ToLower().Contains(r.ComparisonValue.ToLower())))) ||
                        (r.Operator == RuleOperator.GreaterThan &&
                            r.DataType == AttributeDataType.Numeric && v.NumericValue.HasValue && r.NumericComparison.HasValue && v.NumericValue.Value > r.NumericComparison.Value) ||
                        (r.Operator == RuleOperator.LessThan &&
                            r.DataType == AttributeDataType.Numeric && v.NumericValue.HasValue && r.NumericComparison.HasValue && v.NumericValue.Value < r.NumericComparison.Value) ||
                        (r.Operator == RuleOperator.On &&
                            r.DataType == AttributeDataType.Date && v.DateValue.HasValue && r.DateComparison.HasValue && v.DateValue.Value == r.DateComparison.Value) ||
                        (r.Operator == RuleOperator.Before &&
                            r.DataType == AttributeDataType.Date && v.DateValue.HasValue && r.DateComparison.HasValue && v.DateValue.Value < r.DateComparison.Value) ||
                        (r.Operator == RuleOperator.After &&
                            r.DataType == AttributeDataType.Date && v.DateValue.HasValue && r.DateComparison.HasValue && v.DateValue.Value > r.DateComparison.Value) ||
                        (r.Operator == RuleOperator.StartedBefore &&
                            r.DataType == AttributeDataType.Period && v.PeriodStart.HasValue && r.DateComparison.HasValue && v.PeriodStart.Value < r.DateComparison.Value) ||
                        (r.Operator == RuleOperator.StartedAfter &&
                            r.DataType == AttributeDataType.Period && v.PeriodStart.HasValue && r.DateComparison.HasValue && v.PeriodStart.Value > r.DateComparison.Value) ||
                        (r.Operator == RuleOperator.IsTrue &&
                            r.DataType == AttributeDataType.Boolean && v.BooleanValue == true) ||
                        (r.Operator == RuleOperator.IsFalse &&
                            r.DataType == AttributeDataType.Boolean && v.BooleanValue == false))))));
    }

    public static IQueryable<Cv> WhereCvAccessible(
        IQueryable<Cv> query,
        IAppDbContext db,
        ActorContext actor)
    {
        if (actor.IsPrivileged)
            return query;

        return query.Where(c =>
            c.Profile.UserId == actor.UserId ||
            c.Position.IsPublic ||
            (c.Position.AccessRules.Any() &&
                !c.Position.AccessRules.Any(r =>
                    !db.ProfileAttributeValues.Any(v =>
                        v.Profile.UserId == c.Profile.UserId &&
                        v.AttributeDefinitionId == r.AttributeDefinitionId &&
                        v.AttributeDefinition.DataType == r.DataType &&
                        ((r.Operator == RuleOperator.Equals &&
                            ((r.DataType == AttributeDataType.String && v.StringValue != null && v.StringValue == r.ComparisonValue) ||
                            (r.DataType == AttributeDataType.Text && v.TextValue != null && v.TextValue == r.ComparisonValue) ||
                            (r.DataType == AttributeDataType.Numeric && v.NumericValue.HasValue && r.NumericComparison.HasValue && v.NumericValue.Value == r.NumericComparison.Value) ||
                            (r.DataType == AttributeDataType.Date && v.DateValue.HasValue && r.DateComparison.HasValue && v.DateValue.Value == r.DateComparison.Value) ||
                            (r.DataType == AttributeDataType.Dropdown && v.DropdownOption != null && v.DropdownOption == r.ComparisonValue))) ||
                        (r.Operator == RuleOperator.NotEquals &&
                            ((r.DataType == AttributeDataType.String && v.StringValue != null && v.StringValue != r.ComparisonValue) ||
                            (r.DataType == AttributeDataType.Text && v.TextValue != null && v.TextValue != r.ComparisonValue))) ||
                        (r.Operator == RuleOperator.Contains &&
                            ((r.DataType == AttributeDataType.String && v.StringValue != null && r.ComparisonValue != null && v.StringValue.ToLower().Contains(r.ComparisonValue.ToLower())) ||
                            (r.DataType == AttributeDataType.Text && v.TextValue != null && r.ComparisonValue != null && v.TextValue.ToLower().Contains(r.ComparisonValue.ToLower())))) ||
                        (r.Operator == RuleOperator.GreaterThan &&
                            r.DataType == AttributeDataType.Numeric && v.NumericValue.HasValue && r.NumericComparison.HasValue && v.NumericValue.Value > r.NumericComparison.Value) ||
                        (r.Operator == RuleOperator.LessThan &&
                            r.DataType == AttributeDataType.Numeric && v.NumericValue.HasValue && r.NumericComparison.HasValue && v.NumericValue.Value < r.NumericComparison.Value) ||
                        (r.Operator == RuleOperator.On &&
                            r.DataType == AttributeDataType.Date && v.DateValue.HasValue && r.DateComparison.HasValue && v.DateValue.Value == r.DateComparison.Value) ||
                        (r.Operator == RuleOperator.Before &&
                            r.DataType == AttributeDataType.Date && v.DateValue.HasValue && r.DateComparison.HasValue && v.DateValue.Value < r.DateComparison.Value) ||
                        (r.Operator == RuleOperator.After &&
                            r.DataType == AttributeDataType.Date && v.DateValue.HasValue && r.DateComparison.HasValue && v.DateValue.Value > r.DateComparison.Value) ||
                        (r.Operator == RuleOperator.StartedBefore &&
                            r.DataType == AttributeDataType.Period && v.PeriodStart.HasValue && r.DateComparison.HasValue && v.PeriodStart.Value < r.DateComparison.Value) ||
                        (r.Operator == RuleOperator.StartedAfter &&
                            r.DataType == AttributeDataType.Period && v.PeriodStart.HasValue && r.DateComparison.HasValue && v.PeriodStart.Value > r.DateComparison.Value) ||
                        (r.Operator == RuleOperator.IsTrue &&
                            r.DataType == AttributeDataType.Boolean && v.BooleanValue == true) ||
                        (r.Operator == RuleOperator.IsFalse &&
                            r.DataType == AttributeDataType.Boolean && v.BooleanValue == false))))));
    }
}
