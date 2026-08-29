namespace EmmaServer.Tests.Infrastructure;

/// <summary>
/// Come <see cref="FactAttribute"/>, ma il test viene saltato (non fallito) quando il database
/// dei test non e' configurato o non risponde. Utile in CI e sulle macchine senza accesso al DB.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute()
    {
        if (TestDatabase.MotivoSkip is { } motivo)
        {
            Skip = motivo;
        }
    }
}

/// <summary>Versione parametrizzata di <see cref="IntegrationFactAttribute"/>.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class IntegrationTheoryAttribute : TheoryAttribute
{
    public IntegrationTheoryAttribute()
    {
        if (TestDatabase.MotivoSkip is { } motivo)
        {
            Skip = motivo;
        }
    }
}
