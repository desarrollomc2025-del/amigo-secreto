using System.ComponentModel.DataAnnotations;
using AmigoSecreto.Data;
using Microsoft.EntityFrameworkCore;

namespace AmigoSecreto.Servicios;

public sealed record EmpresaResumen(Empresa Empresa, int Inscritos);

/// <summary>Alta y mantenimiento de empresas (solo para quien entra con ADMIN_KEY, en /admin).</summary>
public sealed class EmpresasService(AppDbContext db, InicializadorBaseDatos inicializador, ILogger<EmpresasService> logger)
{
    public async Task<IReadOnlyList<EmpresaResumen>> ListarAsync()
    {
        await inicializador.EsperarAsync();
        return await db.Empresas.AsNoTracking()
            .OrderBy(e => e.Nombre)
            .Select(e => new EmpresaResumen(e, db.Participantes.Count(p => p.EmpresaId == e.Id)))
            .ToListAsync();
    }

    public async Task<Empresa?> ObtenerAsync(int id)
    {
        await inicializador.EsperarAsync();
        return await db.Empresas.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id);
    }

    public async Task<Resultado> CrearAsync(EmpresaForm form)
    {
        await inicializador.EsperarAsync();
        var slug = (form.Slug ?? "").Trim().ToLowerInvariant();
        if (!Slug.EsValido(slug))
            return Resultado.Falla(Slug.Reservados.Contains(slug) ? $"«{slug}» está reservado; elige otra dirección." : EmpresaForm.MensajeSlug);
        if (string.IsNullOrWhiteSpace(form.Clave) || form.Clave.Length < HashClave.LongitudMinima)
            return Resultado.Falla($"La clave del organizador debe tener al menos {HashClave.LongitudMinima} caracteres.");
        if (await db.Empresas.AnyAsync(e => e.Slug == slug))
            return Resultado.Falla($"Ya existe una empresa con la dirección «{slug}».");

        var esPrimera = !await db.Empresas.AnyAsync();
        var empresa = new Empresa
        {
            Slug = slug,
            Nombre = Texto.Limpiar(form.Nombre),
            Subtitulo = Texto.Limpiar(form.Subtitulo),
            FechaSorteo = Texto.Limpiar(form.FechaSorteo),
            ClaveHash = HashClave.Crear(form.Clave),
            Predeterminada = esPrimera, // la primera queda en la página principal
            FechaCreacionUtc = DateTime.UtcNow,
        };
        db.Empresas.Add(empresa);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IntercambioService.EsViolacionUnica(ex))
        {
            return Resultado.Falla($"Ya existe una empresa con la dirección «{slug}».");
        }
        logger.LogInformation("Empresa creada: {Slug}.", slug);
        return Resultado.Exito(slug);
    }

    /// <summary>Actualiza nombre, subtítulo y fecha; la clave solo si se escribió una nueva. La dirección no cambia.</summary>
    public async Task<Resultado> ActualizarAsync(int id, EmpresaForm form)
    {
        await inicializador.EsperarAsync();
        var empresa = await db.Empresas.SingleOrDefaultAsync(e => e.Id == id);
        if (empresa is null) return Resultado.Falla("La empresa ya no existe.");
        if (!string.IsNullOrEmpty(form.Clave) && form.Clave.Length < HashClave.LongitudMinima)
            return Resultado.Falla($"La clave del organizador debe tener al menos {HashClave.LongitudMinima} caracteres.");

        empresa.Nombre = Texto.Limpiar(form.Nombre);
        empresa.Subtitulo = Texto.Limpiar(form.Subtitulo);
        empresa.FechaSorteo = Texto.Limpiar(form.FechaSorteo);
        if (!string.IsNullOrEmpty(form.Clave))
            empresa.ClaveHash = HashClave.Crear(form.Clave);
        await db.SaveChangesAsync();
        return Resultado.Exito(empresa.Slug);
    }

    /// <summary>La empresa pasa a mostrarse en «/» y en las direcciones sin empresa; las demás dejan de serlo.</summary>
    public async Task<Resultado> HacerPredeterminadaAsync(int id)
    {
        await inicializador.EsperarAsync();
        if (!await db.Empresas.AnyAsync(e => e.Id == id)) return Resultado.Falla("La empresa ya no existe.");
        await db.Empresas.ExecuteUpdateAsync(s => s.SetProperty(e => e.Predeterminada, e => e.Id == id));
        return Resultado.Exito();
    }

    /// <summary>Solo se puede eliminar sin inscritos (se vacía antes con «Limpiar todo» en su panel).</summary>
    public async Task<Resultado> EliminarAsync(int id)
    {
        await inicializador.EsperarAsync();
        if (await db.Participantes.AnyAsync(p => p.EmpresaId == id))
            return Resultado.Falla("La empresa todavía tiene inscritos. Usa «Limpiar todo» en su panel antes de eliminarla.");
        var borradas = await db.Empresas.Where(e => e.Id == id).ExecuteDeleteAsync();
        if (borradas == 0) return Resultado.Falla("La empresa ya no existe.");
        logger.LogWarning("Empresa {Id} eliminada.", id);
        return Resultado.Exito();
    }
}

public sealed class EmpresaForm
{
    public const string MensajeSlug =
        "La dirección solo admite minúsculas sin tildes, números y guiones (sin guion al inicio o al final), máximo 40 caracteres.";

    [Required(ErrorMessage = "Escribe el nombre de la empresa.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "El nombre debe tener entre 2 y 100 caracteres.")]
    public string? Nombre { get; set; }

    /// <summary>Solo al crear: luego no cambia para no romper los enlaces ya compartidos.</summary>
    [StringLength(Servicios.Slug.LongitudMaxima, ErrorMessage = MensajeSlug)]
    public string? Slug { get; set; }

    [Required(ErrorMessage = "Escribe el subtítulo, p. ej. «Intercambio de postres».")]
    [StringLength(100, ErrorMessage = "El subtítulo admite hasta 100 caracteres.")]
    public string? Subtitulo { get; set; } = "Intercambio de postres";

    [StringLength(150, ErrorMessage = "La fecha admite hasta 150 caracteres.")]
    public string? FechaSorteo { get; set; }

    /// <summary>Al crear es obligatoria; al editar, vacía = no cambiarla.</summary>
    [StringLength(200, ErrorMessage = "La clave admite hasta 200 caracteres.")]
    public string? Clave { get; set; }

    public static EmpresaForm Desde(Empresa e) => new()
    {
        Nombre = e.Nombre,
        Slug = e.Slug,
        Subtitulo = e.Subtitulo,
        FechaSorteo = e.FechaSorteo,
    };
}
