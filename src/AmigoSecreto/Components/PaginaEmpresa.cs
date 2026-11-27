using AmigoSecreto.Data;
using AmigoSecreto.Servicios;
using Microsoft.AspNetCore.Components;

namespace AmigoSecreto.Components;

/// <summary>
/// Base de las páginas de una empresa. Cada página declara dos rutas: la de la empresa
/// (/{EmpresaSlug}/...) y la antigua sin empresa (/...), que usa la empresa predeterminada.
/// </summary>
public abstract class PaginaEmpresa : ComponentBase
{
    /// <summary>Segmento de la URL; lo lee <see cref="EmpresaActual"/> (aquí solo se declara para que la ruta sea válida).</summary>
    [Parameter]
    public string? EmpresaSlug { get; set; }

    [CascadingParameter]
    protected HttpContext HttpContext { get; set; } = default!;

    [Inject] protected EmpresaActual EmpresaActual { get; set; } = default!;
    [Inject] protected NavigationManager Navegacion { get; set; } = default!;

    /// <summary>La empresa de la página; null si no existe (ya se respondió 404).</summary>
    protected Empresa? Emp { get; private set; }

    protected string NombreEmpresa => Emp?.Nombre ?? "Amigo secreto";

    /// <summary>Carga la empresa; si no existe responde 404 y devuelve false.</summary>
    protected async Task<bool> CargarEmpresaAsync()
    {
        Emp = await EmpresaActual.ObtenerAsync();
        if (Emp is null) Navegacion.NotFound();
        return Emp is not null;
    }

    /// <summary>Carga la empresa sin responder 404 si no hay (para la portada «/»).</summary>
    protected async Task CargarEmpresaOpcionalAsync() => Emp = await EmpresaActual.ObtenerAsync();

    /// <summary>
    /// Para páginas de gestión: además exige haber entrado como organizador de esta empresa
    /// (o con ADMIN_KEY); si no, redirige al acceso de la empresa.
    /// </summary>
    protected async Task<bool> CargarGestionAsync(string subruta)
    {
        if (!await CargarEmpresaAsync()) return false;
        if (Politicas.PuedeGestionar(HttpContext.User, Emp!.Id)) return true;
        Navegacion.NavigateTo($"{Ruta("gestion/acceso")}?volver={Uri.EscapeDataString(subruta)}");
        return false;
    }

    /// <summary>Ruta relativa dentro de la empresa: Ruta("consulta") → "acme/consulta".</summary>
    protected string Ruta(string subruta = "") => EmpresaActual.Ruta(Emp!, subruta);
}
