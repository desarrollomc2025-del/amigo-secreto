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

/// <summary>Validación y normalización de correos electrónicos.</summary>
public static partial class Correo
{
    public const int LongitudMaxima = 254;

    // usuario@dominio.tld: sin espacios, un solo @, dominio con al menos un punto y TLD de 2+ letras.
    [GeneratedRegex(@"^[A-Za-z0-9._%+'-]+@(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\.)+[A-Za-z]{2,24}$")]
    private static partial Regex Formato();

    public static string Normalizar(string? valor) => (valor ?? string.Empty).Trim().ToLowerInvariant();

    public static bool EsValido(string? valor)
    {
        var correo = Normalizar(valor);
        if (correo.Length is 0 or > LongitudMaxima) return false;
        if (!Formato().IsMatch(correo)) return false;
        var usuario = correo[..correo.IndexOf('@')];
        // Sin puntos al inicio/fin ni puntos seguidos en la parte local.
        return usuario.Length <= 64 && !usuario.StartsWith('.') && !usuario.EndsWith('.') && !usuario.Contains("..");
    }
}


/// <summary>Identificador de empresa en la URL: /{slug}/inscripcion.</summary>
public static partial class Slug
{
    public const int LongitudMaxima = 40;

    // Minúsculas, números y guiones; sin guion al inicio ni al final. Sin puntos: no choca con archivos (app.css).
    [GeneratedRegex(@"^[a-z0-9](?:[a-z0-9-]{0,38}[a-z0-9])?$")]
    private static partial Regex Formato();

    /// <summary>Primeros segmentos de rutas de la app: una empresa no puede llamarse así.</summary>
    public static readonly IReadOnlySet<string> Reservados = new HashSet<string>(StringComparer.Ordinal)
    {
        "admin", "gestion", "consulta", "inscripcion", "estado", "error", "not-found", "e", "api",
    };

    public static bool EsValido(string? valor) =>
        valor is not null && Formato().IsMatch(valor) && !valor.Contains("--") && !Reservados.Contains(valor);

    /// <summary>Sugerencia a partir del nombre: "Café Ñandú S.A." → "cafe-nandu-s-a".</summary>
    public static string Sugerir(string? nombre)
    {
        var sb = new StringBuilder();
        foreach (var c in Texto.NormalizarNombre(nombre).ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c)) sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        if (slug.Length > LongitudMaxima) slug = slug[..LongitudMaxima].TrimEnd('-');
        return slug;
    }
}

/// <summary>Hash de las claves de organizador de cada empresa (PBKDF2-SHA256 con sal).</summary>
public static class HashClave
{
    public const int LongitudMinima = 10;
    private const int Iteraciones = 100_000;
    private const int LongitudSal = 16;
    private const int LongitudHash = 32;

    /// <summary>Formato: pbkdf2-sha256$iteraciones$sal(base64)$hash(base64).</summary>
    public static string Crear(string clave)
    {
        var sal = RandomNumberGenerator.GetBytes(LongitudSal);
        var hash = Rfc2898DeriveBytes.Pbkdf2(clave, sal, Iteraciones, HashAlgorithmName.SHA256, LongitudHash);
        return $"pbkdf2-sha256${Iteraciones}${Convert.ToBase64String(sal)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verificar(string? clave, string? almacenado)
    {
        if (string.IsNullOrEmpty(clave) || string.IsNullOrEmpty(almacenado)) return false;
        var partes = almacenado.Split('$');
        if (partes is not ["pbkdf2-sha256", var iter, var salB64, var hashB64] || !int.TryParse(iter, out var iteraciones))
            return false;
        try
        {
            var esperado = Convert.FromBase64String(hashB64);
            var calculado = Rfc2898DeriveBytes.Pbkdf2(clave, Convert.FromBase64String(salB64), iteraciones,
                HashAlgorithmName.SHA256, esperado.Length);
            return CryptographicOperations.FixedTimeEquals(calculado, esperado);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
