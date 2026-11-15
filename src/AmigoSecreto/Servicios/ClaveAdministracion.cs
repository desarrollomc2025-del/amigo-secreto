using System.Security.Cryptography;
using System.Text;

namespace AmigoSecreto.Servicios;

/// <summary>Valida la clave ADMIN_KEY en tiempo constante.</summary>
public sealed class ClaveAdministracion
{
    public const int LongitudMinima = 12;
    private readonly byte[]? hashClave;

    public ClaveAdministracion(IConfiguration configuracion, ILogger<ClaveAdministracion> logger)
    {
        var clave = configuracion["ADMIN_KEY"];
        if (string.IsNullOrEmpty(clave) || clave.Length < LongitudMinima)
        {
            logger.LogError("ADMIN_KEY no está configurada o tiene menos de {Minimo} caracteres; la gestión queda deshabilitada.",
                LongitudMinima);
            return;
        }
        hashClave = SHA256.HashData(Encoding.UTF8.GetBytes(clave));
    }

    public bool Configurada => hashClave is not null;

    public bool Verificar(string? intento)
    {
        if (hashClave is null) return false;
        // Se comparan hashes de longitud fija para que el tiempo no dependa del largo ni del contenido.
        var hashIntento = SHA256.HashData(Encoding.UTF8.GetBytes(intento ?? string.Empty));
        return CryptographicOperations.FixedTimeEquals(hashIntento, hashClave);
    }
}
