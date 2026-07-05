namespace SharedKernel;

public readonly record struct CaseId(Guid Value)
{
    public static CaseId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("N");
}

public readonly record struct ProvisionRef(string Law, string Paragraph)
{
    public override string ToString() => $"{Law} § {Paragraph}";
}

public sealed record Result<T>(bool Ok, T? Value, string? Error)
{
    public static Result<T> Success(T v) => new(true, v, null);
    public static Result<T> Failure(string e) => new(false, default, e);
}

public interface IDomainEvent { CaseId CaseId { get; } DateTimeOffset OccurredAt { get; } }

public interface IRegistry { void Register(string key, Func<object> factory); object Resolve(string key); }

public sealed class Registry : IRegistry
{
    private readonly Dictionary<string, Func<object>> _map = new();
    public void Register(string key, Func<object> factory) => _map[key] = factory;
    public object Resolve(string key) => _map[key]();
}
