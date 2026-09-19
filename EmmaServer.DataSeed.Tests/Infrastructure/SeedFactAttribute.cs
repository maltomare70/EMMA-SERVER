namespace EmmaServer.DataSeed.Tests.Infrastructure;

/// <summary>
/// Come <c>[Fact]</c>, ma si salta da solo se il database non e' configurato o non risponde.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SeedFactAttribute : FactAttribute
{
    public SeedFactAttribute()
    {
        if (SeedDatabase.MotivoSkip is { } motivo)
        {
            Skip = motivo;
        }
    }
}
