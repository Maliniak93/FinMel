using Domain.Interfaces;
using System.Linq.Expressions;

namespace Domain.Common;
public class BaseSpecification<TEntity> : ISpecification<TEntity>
{
    public BaseSpecification()
    {
    }

    public BaseSpecification(Expression<Func<TEntity, bool>> criteria)
    {
        Criteria = criteria;
    }
    public Expression<Func<TEntity, bool>> Criteria { get; }

    public List<Expression<Func<TEntity, object>>> Includes { get; } = new List<Expression<Func<TEntity, object>>>();

    public Expression<Func<TEntity, object>> OrderBy { get; private set; }

    public Expression<Func<TEntity, object>> OrderByDesc { get; private set; }

    protected void AddOrderBy(Expression<Func<TEntity, object>> orderByExpression)
    {
        OrderBy = orderByExpression;
    }
    protected void AddOrderByDesc(Expression<Func<TEntity, object>> orderByDescExpression)
    {
        OrderByDesc = orderByDescExpression;
    }
}
