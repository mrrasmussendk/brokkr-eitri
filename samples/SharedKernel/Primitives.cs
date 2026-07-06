namespace SharedKernel;

public readonly record struct StaveId(Guid Value)
{
    public static StaveId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("N");
}

public readonly record struct RuneRef(string Row, string Mark)
{
    public override string ToString() => $"{Row} · {Mark}";
}

public sealed record Result<T>(bool Ok, T? Value, string? Error)
{
    public static Result<T> Success(T v) => new(true, v, null);
    public static Result<T> Failure(string e) => new(false, default, e);
}

public interface IDomainEvent { StaveId StaveId { get; } DateTimeOffset OccurredAt { get; } }

public interface IRegistry { void Register(string key, Func<object> factory); object Resolve(string key); }

public sealed class Registry : IRegistry
{
    private readonly Dictionary<string, Func<object>> _map = new();
    public void Register(string key, Func<object> factory) => _map[key] = factory;
    public object Resolve(string key) => _map[key]();
}
