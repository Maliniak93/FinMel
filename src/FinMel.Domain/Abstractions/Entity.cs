namespace FinMel.Domain.Abstractions;

/// <summary>
/// Base class for all entities with a strongly-typed identifier.
/// Provides identity-based equality comparison and proper hash code implementation.
/// </summary>
/// <typeparam name="TId">The type of the entity identifier</typeparam>
public abstract class Entity<TId> : Entity, IEquatable<Entity<TId>>
    where TId : notnull
{
    /// <summary>
    /// Initializes a new instance of the Entity class with the specified identifier.
    /// </summary>
    /// <param name="id">The entity identifier</param>
    protected Entity(TId id)
    {
        if (EqualityComparer<TId>.Default.Equals(id, default!))
        {
            throw new ArgumentException("Entity Id cannot be default value.", nameof(id));
        }
        
        Id = id;
    }

    /// <summary>
    /// Parameterless constructor for ORM frameworks.
    /// Should not be used directly in application code.
    /// </summary>
    protected Entity()
    {
    }

    public TId Id { get; protected init; } = default!;

    /// <summary>
    /// Gets the entity's identifier as an object.
    /// </summary>
    public override object GetId() => Id;

    /// <summary>
    /// Determines whether two entity instances are equal based on their identifiers.
    /// </summary>
    public static bool operator ==(Entity<TId>? left, Entity<TId>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return left.Equals(right);
    }

    /// <summary>
    /// Determines whether two entity instances are not equal based on their identifiers.
    /// </summary>
    public static bool operator !=(Entity<TId>? left, Entity<TId>? right)
    {
        return !(left == right);
    }

    /// <summary>
    /// Determines whether the current entity is equal to another entity of the same type.
    /// </summary>
    public bool Equals(Entity<TId>? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (GetType() != other.GetType())
        {
            return false;
        }

        // If both entities have default Id values, they are not equal
        if (EqualityComparer<TId>.Default.Equals(Id, default!) || 
            EqualityComparer<TId>.Default.Equals(other.Id, default!))
        {
            return false;
        }

        return EqualityComparer<TId>.Default.Equals(Id, other.Id);
    }

    /// <summary>
    /// Determines whether the current entity is equal to the specified object.
    /// </summary>
    public override bool Equals(object? obj)
    {
        if (obj is null)
        {
            return false;
        }

        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        if (GetType() != obj.GetType())
        {
            return false;
        }

        if (obj is not Entity<TId> entity)
        {
            return false;
        }

        return Equals(entity);
    }

    /// <summary>
    /// Returns the hash code for this entity based on its identifier.
    /// </summary>
    public override int GetHashCode()
    {
        if (EqualityComparer<TId>.Default.Equals(Id, default!))
        {
            return base.GetHashCode();
        }

        return HashCode.Combine(GetType(), Id);
    }
}

/// <summary>
/// Non-generic base class for all entities.
/// Provides common functionality that doesn't depend on the identifier type.
/// </summary>
public abstract class Entity
{
    /// <summary>
    /// Gets the entity's identifier as an object.
    /// </summary>
    public abstract object GetId();
}
