using System.Security.Claims;
using AmigoSecreto.Data;
using Microsoft.EntityFrameworkCore;

namespace AmigoSecreto.Servicios;

/// <summary>
/// Empresa de la petición actual: la del segmento /{Empresa}/... de la URL o, en las direcciones
/// sin empresa (/, /inscripcion, /gestion...), la marcada como predeterminada. Se consulta una sola vez por petición
/// y la comparten el encabezado (layout) y la página.
/// </summary>
public sealed class EmpresaActual(IHttpContextAccessor accesor, AppDbContext db, InicializadorBaseDatos inicializador)
{
    public const string ParametroRuta = "EmpresaSlug";
    private Task<Empresa?>? carga;

    /// <summary>Slug tal como vino en la URL, o null si la dirección no lleva empresa.</summary>
    public string? SlugEnRuta => accesor.HttpContext?.GetRouteValue(ParametroRuta) as string;

    public Task<Empresa?> ObtenerAsync() => carga ??= CargarAsync();

    private async Task<Empresa?> CargarAsync()
    {
        await inicializador.EsperarAsync();
        var consulta = db.Empresas.AsNoTracking();
        var slug = SlugEnRuta?.ToLowerInvariant();
        if (slug is null)
            return await consulta.Where(e => e.Predeterminada).OrderBy(e => e.Id).FirstOrDefaultAsync();
        return Slug.EsValido(slug) ? await consulta.SingleOrDefaultAsync(e => e.Slug == slug) : null;
    }

    /// <summary>Ruta relativa (a &lt;base href="/"&gt;) dentro de la empresa: Ruta(e, "consulta") → "acme/consulta".</summary>
    public static string Ruta(Empresa empresa, string subruta = "") =>
        subruta.Length == 0 ? empresa.Slug : $"{empresa.Slug}/{subruta}";
}

public static class Politicas
{
    /// <summary>Organizador de una empresa (claim <see cref="ClaimEmpresa"/> con su Id).</summary>
    public const string Organizador = "Organizador";

    /// <summary>Entró con ADMIN_KEY: gestiona todas las empresas y crea nuevas en /admin.</summary>
    public const string Administrador = "Administrador";

    public const string ClaimEmpresa = "empresa";

    /// <summary>Para [Authorize(Roles = ...)]: cualquiera que haya entrado; cada página comprueba además su empresa.</summary>
    public const string RolesGestion = Organizador + "," + Administrador;

    public static bool EsAdministrador(ClaimsPrincipal usuario) => usuario.IsInRole(Administrador);

    public static bool PuedeGestionar(ClaimsPrincipal usuario, int empresaId) =>
        usuario.IsInRole(Administrador) ||
        (usuario.IsInRole(Organizador) && usuario.FindFirstValue(ClaimEmpresa) == empresaId.ToString());
}

/// <summary>Convierte la página de gestión que se pidió sin acceso en la página de acceso de su empresa.</summary>
public static partial class AccesoEmpresa
{
    [System.Text.RegularExpressions.GeneratedRegex(@"^/(?:(?<slug>[a-z0-9-]+)/)?gestion(?:/(?<sub>parejas|reporte))?/?$")]
    private static partial System.Text.RegularExpressions.Regex RutaGestion();

    /// <summary>"/acme/gestion/parejas" → "acme/gestion/acceso?volver=gestion/parejas"; null si no es una ruta de gestión.</summary>
    public static string? Desde(string? volver)
    {
        if (string.IsNullOrEmpty(volver)) return null;
        var m = RutaGestion().Match(volver.ToLowerInvariant());
        if (!m.Success) return null;
        var prefijo = m.Groups["slug"].Success ? m.Groups["slug"].Value + "/" : "";
        var sub = m.Groups["sub"].Success ? "gestion/" + m.Groups["sub"].Value : "gestion";
        return $"{prefijo}gestion/acceso?volver={Uri.EscapeDataString(sub)}";
    }
}
