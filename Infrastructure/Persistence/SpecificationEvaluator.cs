using Domain.Common;
using Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;
public class SpecificationEvaluator<TEntiy> where TEntiy : BaseEntity
{
    public static IQueryable<TEntiy> GetQuery(IQueryable<TEntiy> inputQuery, ISpecification<TEntiy> spec)
    {
        var query = inputQuery;

        if (spec.Criteria != null) query = query.Where(spec.Criteria);

        if (spec.OrderBy != null) query = query.OrderBy(spec.OrderBy);

        if (spec.OrderByDesc != null) query = query.OrderByDescending(spec.OrderByDesc);

        query = spec.Includes.Aggregate(query, (current, include) => current.Include(include));

        return query;
    }
}
