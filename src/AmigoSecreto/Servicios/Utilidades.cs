using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AmigoSecreto.Servicios;

public static partial class Texto
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex Espacios();

    /// <summary>Quita espacios al inicio y al final y colapsa los espacios repetidos.</summary>
    public static string Limpiar(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? string.Empty : Espacios().Replace(valor.Trim(), " ");

    /// <summary>Mayúsculas y sin tildes/diacríticos: "José  Peña" → "JOSE PENA".</summary>
    public static string NormalizarNombre(string? valor)
    {
        var descompuesto = Limpiar(valor).Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(descompuesto.Length);
        foreach (var c in descompuesto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC).ToUpperInvariant();
    }
}

public static class CodigoPersonal
{
    public const int Longitud = 6;

    /// <summary>Sin caracteres ambiguos: no incluye 0, O, 1 ni I.</summary>
    public const string Alfabeto = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string Generar() => RandomNumberGenerator.GetString(Alfabeto, Longitud);

    /// <summary>Normaliza lo que escribe el usuario (espacios, guiones, minúsculas). Devuelve null si no es válido.</summary>
    public static string? Normalizar(string? entrada)
    {
        if (string.IsNullOrWhiteSpace(entrada)) return null;
        var limpio = new string(entrada.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray()).ToUpperInvariant();
        return limpio.Length == Longitud && limpio.All(Alfabeto.Contains) ? limpio : null;
    }
}

public static class Sorteo
{
    /// <summary>
    /// Baraja a los participantes con un generador criptográfico y forma un solo ciclo
    /// A→B→C→…→A. Nadie se toca a sí mismo y funciona con cualquier cantidad ≥ 2 (par o impar).
    /// </summary>
    public static IReadOnlyList<(int DaId, int RecibeId)> GenerarCiclo(IEnumerable<int> ids)
    {
        var orden = ids.Distinct().ToArray();
        if (orden.Length < 2)
            throw new ArgumentException("Se necesitan al menos 2 participantes distintos.", nameof(ids));

        RandomNumberGenerator.Shuffle(orden.AsSpan());
        return orden.Select((id, i) => (id, orden[(i + 1) % orden.Length])).ToList();
    }
}

public static class HoraGuatemala
{
    // Guatemala usa UTC-6 todo el año (sin horario de verano).
    private static readonly TimeSpan Desfase = TimeSpan.FromHours(-6);
    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-GT");

    public static string Formatear(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).Add(Desfase).ToString("dd/MM/yyyy h:mm tt", Cultura);
}
