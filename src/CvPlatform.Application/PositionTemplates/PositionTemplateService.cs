using CvPlatform.Application.Authorization;
using CvPlatform.Application.Attributes;
using CvPlatform.Application.Common;
using CvPlatform.Application.Positions;
using CvPlatform.Core.Data;
using CvPlatform.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace CvPlatform.Application.PositionTemplates;

public sealed class PositionTemplateService(IAppDbContextFactory factory) : IPositionTemplateService
{
    public async Task<Result<PagedResult<PositionTemplateDto>>> ListAsync(
        ActorContext actor, PageRequest page, string? search = null, CancellationToken ct = default)
    {
        if (!actor.IsPrivileged)
            return Result<PagedResult<PositionTemplateDto>>.Failure(
                ErrorCodes.Forbidden, "Only recruiters can manage position templates.");

        await using var db = factory.CreateDbContext();
        var query = db.PositionTemplates.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(t => t.Name.Contains(search) || t.Description.Contains(search));
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(t => t.Name).ThenBy(t => t.Id)
            .Skip(page.Skip).Take(page.PageSize)
            .Select(ToDtoExpression())
            .ToListAsync(ct);
        return Result<PagedResult<PositionTemplateDto>>.Success(
            new PagedResult<PositionTemplateDto>(items, total, page.Page, page.PageSize));
    }

    public async Task<Result<PositionTemplateDetailDto>> GetAsync(
        ActorContext actor, Guid templateId, CancellationToken ct = default)
    {
        if (!actor.IsPrivileged)
            return Result<PositionTemplateDetailDto>.Failure(
                ErrorCodes.Forbidden, "Only recruiters can manage position templates.");

        await using var db = factory.CreateDbContext();
        var template = await db.PositionTemplates.AsNoTracking()
            .SingleOrDefaultAsync(t => t.Id == templateId, ct);
        if (template is null)
            return Result<PositionTemplateDetailDto>.Failure(ErrorCodes.NotFound, "Template was not found.");

        var attributes = await db.PositionTemplateAttributes.AsNoTracking()
            .Where(a => a.PositionTemplateId == templateId)
            .OrderBy(a => a.SortOrder).ThenBy(a => a.AttributeDefinitionId)
            .Select(a => new PositionTemplateAttributeDto(
                a.AttributeDefinitionId, a.AttributeDefinition.Name,
                a.AttributeDefinition.DataType, a.IsRequired, a.SortOrder))
            .ToListAsync(ct);
        var rules = await db.PositionTemplateAccessRules.AsNoTracking()
            .Where(r => r.PositionTemplateId == templateId)
            .OrderBy(r => r.AttributeDefinition.Name).ThenBy(r => r.Id)
            .Select(r => new PositionTemplateAccessRuleDto(
                r.Id, r.AttributeDefinitionId, r.AttributeDefinition.Name,
                r.DataType, r.Operator, r.ComparisonValue))
            .ToListAsync(ct);

        return Result<PositionTemplateDetailDto>.Success(
            new PositionTemplateDetailDto(ToDto(template), attributes, rules));
    }

    public async Task<Result<PositionTemplateDto>> CreateAsync(
        ActorContext actor, PositionTemplateInput input, CancellationToken ct = default)
    {
        if (!actor.IsPrivileged)
            return Result<PositionTemplateDto>.Failure(
                ErrorCodes.Forbidden, "Only recruiters can create position templates.");
        var validation = Validate(input);
        if (validation is not null)
            return Result<PositionTemplateDto>.Failure(ErrorCodes.ValidationFailed, validation);
        var attributeValidation = AttributeRequirementValidation.Validate(input.Attributes);
        if (attributeValidation is not null)
            return Result<PositionTemplateDto>.Failure(ErrorCodes.ValidationFailed, attributeValidation);

        await using var db = factory.CreateDbContext();
        if (input.Attributes is { Count: > 0 })
        {
            var definitionIds = input.Attributes.Select(a => a.AttributeDefinitionId).ToArray();
            var definitionCount = await db.AttributeDefinitions
                .CountAsync(d => definitionIds.Contains(d.Id), ct);
            if (definitionCount != definitionIds.Length)
                return Result<PositionTemplateDto>.Failure(
                    ErrorCodes.NotFound, "One or more attribute definitions were not found.");
        }
        var now = DateTime.UtcNow;
        var template = new PositionTemplate
        {
            Id = Guid.NewGuid(),
            CreatedById = actor.UserId,
            Name = input.Name.Trim(),
            Description = input.Description.Trim(),
            Company = input.Company?.Trim(),
            Level = input.Level?.Trim(),
            MaxProjects = input.MaxProjects,
            IsActive = input.IsActive,
            CreatedAt = now,
            UpdatedAt = now,
            Attributes = input.Attributes?.Select(a => new PositionTemplateAttribute
            {
                AttributeDefinitionId = a.AttributeDefinitionId,
                IsRequired = a.IsRequired,
                SortOrder = a.SortOrder,
            }).ToList() ?? [],
        };
        db.PositionTemplates.Add(template);
        await db.SaveChangesAsync(ct);
        return Result<PositionTemplateDto>.Success(ToDto(template));
    }

    public async Task<Result<PositionTemplateDto>> UpdateAsync(
        ActorContext actor, Guid templateId, PositionTemplateInput input, CancellationToken ct = default)
    {
        if (!actor.IsPrivileged)
            return Result<PositionTemplateDto>.Failure(
                ErrorCodes.Forbidden, "Only recruiters can edit position templates.");
        var validation = Validate(input);
        if (validation is not null)
            return Result<PositionTemplateDto>.Failure(ErrorCodes.ValidationFailed, validation);
        var attributeValidation = AttributeRequirementValidation.Validate(input.Attributes);
        if (attributeValidation is not null)
            return Result<PositionTemplateDto>.Failure(ErrorCodes.ValidationFailed, attributeValidation);

        await using var db = factory.CreateDbContext();
        var template = await db.PositionTemplates.Include(t => t.Attributes)
            .SingleOrDefaultAsync(t => t.Id == templateId, ct);
        if (template is null)
            return Result<PositionTemplateDto>.Failure(ErrorCodes.NotFound, "Template was not found.");
        if (input.ExpectedVersion is null || input.ExpectedVersion != template.Version)
            return Result<PositionTemplateDto>.Failure(
                ErrorCodes.ConcurrencyConflict, "The template was modified by someone else.");

        template.Name = input.Name.Trim();
        template.Description = input.Description.Trim();
        template.Company = input.Company?.Trim();
        template.Level = input.Level?.Trim();
        template.MaxProjects = input.MaxProjects;
        template.IsActive = input.IsActive;
        template.UpdatedAt = DateTime.UtcNow;
        if (input.Attributes is not null)
        {
            var definitionIds = input.Attributes.Select(a => a.AttributeDefinitionId).ToArray();
            var definitionCount = await db.AttributeDefinitions
                .CountAsync(d => definitionIds.Contains(d.Id), ct);
            if (definitionCount != definitionIds.Length)
                return Result<PositionTemplateDto>.Failure(
                    ErrorCodes.NotFound, "One or more attribute definitions were not found.");

            var desiredAttributes = input.Attributes.ToDictionary(a => a.AttributeDefinitionId);
            foreach (var existing in template.Attributes.Where(a => !desiredAttributes.ContainsKey(a.AttributeDefinitionId)).ToList())
            {
                db.PositionTemplateAttributes.Remove(existing);
                template.Attributes.Remove(existing);
            }

            foreach (var desired in desiredAttributes.Values)
            {
                var existing = template.Attributes.SingleOrDefault(
                    a => a.AttributeDefinitionId == desired.AttributeDefinitionId);
                if (existing is null)
                {
                    template.Attributes.Add(new PositionTemplateAttribute
                    {
                        PositionTemplateId = templateId,
                        AttributeDefinitionId = desired.AttributeDefinitionId,
                        IsRequired = desired.IsRequired,
                        SortOrder = desired.SortOrder,
                    });
                }
                else
                {
                    existing.IsRequired = desired.IsRequired;
                    existing.SortOrder = desired.SortOrder;
                }
            }
        }
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<PositionTemplateDto>.Failure(
                ErrorCodes.ConcurrencyConflict, "The template was modified by someone else.");
        }
        return Result<PositionTemplateDto>.Success(ToDto(template));
    }

    public async Task<Result> DeleteAsync(
        ActorContext actor, Guid templateId, long expectedVersion, CancellationToken ct = default)
        => await DeleteManyAsync(actor, [new PositionTemplateDeleteInput(templateId, expectedVersion)], ct);

    public async Task<Result> DeleteManyAsync(
        ActorContext actor, IReadOnlyList<PositionTemplateDeleteInput> templates, CancellationToken ct = default)
    {
        if (!actor.IsPrivileged)
            return Result.Failure(ErrorCodes.Forbidden, "Only recruiters can delete position templates.");
        if (templates.Count == 0)
            return Result.Failure(ErrorCodes.ValidationFailed, "At least one template must be selected.");
        if (templates.Select(t => t.Id).Distinct().Count() != templates.Count)
            return Result.Failure(ErrorCodes.ValidationFailed, "A template cannot be selected more than once.");

        await using var db = factory.CreateDbContext();
        var ids = templates.Select(t => t.Id).ToArray();
        var found = await db.PositionTemplates
            .Where(t => ids.Contains(t.Id))
            .ToListAsync(ct);
        if (found.Count != templates.Count)
            return Result.Failure(ErrorCodes.NotFound, "One or more templates were not found.");

        var expectedVersions = templates.ToDictionary(t => t.Id, t => t.ExpectedVersion);
        if (found.Any(template => template.Version != expectedVersions[template.Id]))
            return Result.Failure(ErrorCodes.ConcurrencyConflict, "One or more templates were modified by someone else.");

        db.PositionTemplates.RemoveRange(found);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(ErrorCodes.ConcurrencyConflict, "One or more templates were modified by someone else.");
        }
        return Result.Success();
    }

    public async Task<Result<PositionTemplateDto>> SaveFromPositionAsync(
        ActorContext actor, Guid positionId, string name, CancellationToken ct = default)
    {
        if (!actor.IsPrivileged)
            return Result<PositionTemplateDto>.Failure(
                ErrorCodes.Forbidden, "Only recruiters can create position templates.");
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
            return Result<PositionTemplateDto>.Failure(ErrorCodes.ValidationFailed, "Template name is invalid.");

        await using var db = factory.CreateDbContext();
        var source = await db.Positions.AsNoTracking().AsSplitQuery()
            .Include(p => p.Attributes)
            .Include(p => p.AccessRules)
            .SingleOrDefaultAsync(p => p.Id == positionId, ct);
        if (source is null)
            return Result<PositionTemplateDto>.Failure(ErrorCodes.NotFound, "Position was not found.");

        var now = DateTime.UtcNow;
        var templateId = Guid.NewGuid();
        var template = new PositionTemplate
        {
            Id = templateId,
            CreatedById = actor.UserId,
            Name = name.Trim(),
            Description = source.ShortDescription,
            Company = source.Company,
            Level = source.Level,
            MaxProjects = source.MaxProjects,
            CreatedAt = now,
            UpdatedAt = now,
            Attributes = source.Attributes.Select(a => new PositionTemplateAttribute
            {
                PositionTemplateId = templateId,
                AttributeDefinitionId = a.AttributeDefinitionId,
                IsRequired = a.IsRequired,
                SortOrder = a.SortOrder,
            }).ToList(),
            AccessRules = source.AccessRules.Select(r => new PositionTemplateAccessRule
            {
                Id = Guid.NewGuid(),
                PositionTemplateId = templateId,
                AttributeDefinitionId = r.AttributeDefinitionId,
                DataType = r.DataType,
                Operator = r.Operator,
                ComparisonValue = r.ComparisonValue,
                NumericComparison = r.NumericComparison,
                DateComparison = r.DateComparison,
            }).ToList(),
        };
        db.PositionTemplates.Add(template);
        await db.SaveChangesAsync(ct);
        return Result<PositionTemplateDto>.Success(ToDto(template));
    }

    public async Task<Result<PositionDto>> CreatePositionAsync(
        ActorContext actor, Guid templateId, CancellationToken ct = default)
    {
        if (!actor.IsPrivileged)
            return Result<PositionDto>.Failure(
                ErrorCodes.Forbidden, "Only recruiters can create positions from templates.");

        await using var db = factory.CreateDbContext();
        var template = await db.PositionTemplates.AsNoTracking().AsSplitQuery()
            .Include(t => t.Attributes)
            .Include(t => t.AccessRules)
            .SingleOrDefaultAsync(t => t.Id == templateId && t.IsActive, ct);
        if (template is null)
            return Result<PositionDto>.Failure(ErrorCodes.NotFound, "Active template was not found.");

        var positionId = Guid.NewGuid();
        var position = new Position
        {
            Id = positionId,
            OwnerId = actor.UserId,
            Title = template.Name,
            ShortDescription = template.Description,
            Company = template.Company,
            Level = template.Level,
            IsPublic = false,
            MaxProjects = template.MaxProjects,
            Attributes = template.Attributes.Select(a => new PositionAttribute
            {
                PositionId = positionId,
                AttributeDefinitionId = a.AttributeDefinitionId,
                IsRequired = a.IsRequired,
                SortOrder = a.SortOrder,
            }).ToList(),
            AccessRules = template.AccessRules.Select(r => new AccessRule
            {
                PositionId = positionId,
                AttributeDefinitionId = r.AttributeDefinitionId,
                DataType = r.DataType,
                Operator = r.Operator,
                ComparisonValue = r.ComparisonValue,
                NumericComparison = r.NumericComparison,
                DateComparison = r.DateComparison,
            }).ToList(),
        };
        db.Positions.Add(position);
        await db.SaveChangesAsync(ct);
        return Result<PositionDto>.Success(new PositionDto(
            position.Id, position.OwnerId, position.Title, position.ShortDescription,
            position.Company, position.Level, position.IsPublic, position.MaxProjects, position.Version));
    }

    private static string? Validate(PositionTemplateInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 200)
            return "Template name is required and must not exceed 200 characters.";
        if (input.Description.Length > 2000)
            return "Template description must not exceed 2000 characters.";
        if (input.MaxProjects is < 0 or > 20)
            return "Maximum projects must be between 0 and 20.";
        return null;
    }

    private static System.Linq.Expressions.Expression<Func<PositionTemplate, PositionTemplateDto>> ToDtoExpression() =>
        t => new PositionTemplateDto(
            t.Id, t.CreatedById, t.Name, t.Description, t.Company, t.Level,
            t.MaxProjects, t.IsActive, t.Version, t.Attributes.Count, t.AccessRules.Count);

    private static PositionTemplateDto ToDto(PositionTemplate template) => new(
        template.Id, template.CreatedById, template.Name, template.Description,
        template.Company, template.Level, template.MaxProjects, template.IsActive,
        template.Version, template.Attributes.Count, template.AccessRules.Count);
}
