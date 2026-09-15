namespace EmmaServer.Services.Anomalie;

/// <summary>
/// Mediana, MAD e z robusto (Iglewicz-Hoaglin).
/// Mai media e deviazione standard: una sola riga letta male da 10.000 EUR sposterebbe il centro
/// e nasconderebbe per sempre gli scostamenti veri di quell'articolo.
/// </summary>
public static class StatisticaRobusta
{
    /// <summary>Rende il MAD confrontabile con una deviazione standard su dati normali.</summary>
    public const double CostanteMad = 0.6745;

    public static double Mediana(IReadOnlyCollection<double> valori)
    {
        if (valori.Count == 0) throw new ArgumentException("Serve almeno un valore.", nameof(valori));

        var ordinati = valori.ToArray();
        Array.Sort(ordinati);

        int meta = ordinati.Length / 2;
        return ordinati.Length % 2 == 1
            ? ordinati[meta]
            : (ordinati[meta - 1] + ordinati[meta]) / 2.0;
    }

    /// <summary>Median Absolute Deviation: mediana di |x - mediana|.</summary>
    public static double Mad(IReadOnlyCollection<double> valori, double mediana)
        => Mediana(valori.Select(v => Math.Abs(v - mediana)).ToArray());

    public static double ZRobusto(double x, double mediana, double mad)
        => CostanteMad * (x - mediana) / mad;

    /// <summary>
    /// Un MAD sotto questa soglia (in proporzione alla mediana) si tratta come zero: prezzo di listino fisso.
    /// Evita z enormi per differenze di arrotondamento al centesimo.
    /// </summary>
    public static bool MadNullo(double mad, double mediana)
        => mad <= Math.Abs(mediana) * 1e-9 || mad == 0;
}
