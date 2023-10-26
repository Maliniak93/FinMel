﻿using Application.Common;
using Domain.Common;
using Domain.Interfaces;
using Infrastructure;
using Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace FinMel.Infrastructure.Persistence;
public class ApplicationDbContext : DbContext, IApplicationDbContext
{
    private readonly IMediator _mediator;
    private readonly AuditableEntitySaveChangesInterceptor _auditableEntitySaveChangesInterceptor;

    public ApplicationDbContext(
        IMediator mediator,
        AuditableEntitySaveChangesInterceptor auditableEntitySaveChangesInterceptor,
        DbContextOptions<ApplicationDbContext> options) : base(options)
    {
        _mediator = mediator;
        _auditableEntitySaveChangesInterceptor = auditableEntitySaveChangesInterceptor;
    }


    public new DbSet<TEntity> Set<TEntity>()
        where TEntity : BaseEntity =>
        base.Set<TEntity>();

    public async Task<TEntity> GetByIdAsync<TEntity>(int id)
        where TEntity : BaseEntity =>
        await Set<TEntity>().FirstOrDefaultAsync(e => e.Id == id);

    public void Insert<TEntity>(TEntity entity)
        where TEntity : BaseEntity =>
        Set<TEntity>().Add(entity);

    public new void Remove<TEntity>(TEntity entity)
        where TEntity : BaseEntity =>
        Set<TEntity>().Remove(entity);

    public new void Update<TEntity>(TEntity entity)
        where TEntity : BaseEntity =>
        Set<TEntity>().Update(entity);

    public IQueryable<TEntity> GetEntityWithSpec<TEntity>(ISpecification<TEntity> spec)
        where TEntity : BaseEntity =>
        SpecificationEvaluator<TEntity>.GetQuery(Set<TEntity>(), spec);

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        base.OnModelCreating(builder);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.AddInterceptors(_auditableEntitySaveChangesInterceptor);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await _mediator.DispatchDomainEvents(this);

        return await base.SaveChangesAsync(cancellationToken);
    }


    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<decimal>()
            .HavePrecision(18, 6);
    }

    public void Clear()
    {
        base.ChangeTracker.Clear();
    }
}

