using System.Data;
using AmigoSecreto.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AmigoSecreto.Servicios;

public sealed record Resultado(bool Ok, string? Error = null, string? Codigo = null)
{
    public static Resultado Exito(string? codigo = null) => new(true, Codigo: codigo);
    public static Resultado Falla(string error) => new(false, error);
}

public sealed record VistaParticipante(Participante Participante, bool SorteoRealizado, Participante? Amigo);

public sealed record Pareja(Participante Da, Participante Recibe);

public sealed record ResumenGestion(
    EstadoSorteo Estado,
    IReadOnlyList<Participante> Participantes,
    int TotalAsignaciones)
{
    public int Asistentes => Participantes.Count(p => p.Asistira);
    public int NoAsistentes => Participantes.Count - Asistentes;
}

/// <summary>
/// Toda la lógica del intercambio. Las operaciones que cambian datos usan transacciones
/// serializables dentro de la estrategia de reintentos (necesario con EnableRetryOnFailure).
/// </summary>
public sealed class IntercambioService(AppDbContext db, InicializadorBaseDatos inicializador, ILogger<IntercambioService> logger)
{
    public const int MinimoParticipantes = 3;
    private const string NombreDuplicado = "Ya existe una inscripción con ese nombre. Si es la tuya, usa tu código para editarla.";

    // ---------- Participantes ----------

    public async Task<bool> SorteoRealizadoAsync()
    {
        await inicializador.EsperarAsync();
        return await db.EstadosSorteo.AsNoTracking()
            .Where(e => e.Id == EstadoSorteo.IdUnico)
            .Select(e => e.Realizado)
            .SingleAsync();
    }

    public async Task<Resultado> InscribirAsync(ParticipanteForm form)
    {
        await inicializador.EsperarAsync();
        var nombre = Texto.Limpiar(form.NombreCompleto);
        var normalizado = Texto.NormalizarNombre(nombre);

        try
        {
            return await EnTransaccionAsync(async () =>
            {
                if (await EstadoAsync() is { Realizado: true })
                    return Resultado.Falla("Las inscripciones están cerradas porque el sorteo ya se realizó.");

                if (await db.Participantes.AnyAsync(p => p.NombreNormalizado == normalizado))
                    return Resultado.Falla(NombreDuplicado);

                string codigo;
                do codigo = CodigoPersonal.Generar();
                while (await db.Participantes.AnyAsync(p => p.Codigo == codigo));

                db.Participantes.Add(new Participante
                {
                    NombreCompleto = nombre,
                    NombreNormalizado = normalizado,
                    Postre = Texto.Limpiar(form.Postre),
                    LugarCompra = Texto.Limpiar(form.LugarCompra),
                    Asistira = form.Asistira == true,
                    Correo = Correo.Normalizar(form.Correo),
                    Codigo = codigo,
                    FechaInscripcionUtc = DateTime.UtcNow,
                });
                await db.SaveChangesAsync();
                return Resultado.Exito(codigo);
            });
        }
        catch (DbUpdateException ex) when (EsViolacionUnica(ex))
        {
            return Resultado.Falla(NombreDuplicado);
        }
    }

    public async Task<VistaParticipante?> ConsultarAsync(string codigo)
    {
        await inicializador.EsperarAsync();
        var participante = await db.Participantes.AsNoTracking().SingleOrDefaultAsync(p => p.Codigo == codigo);
        if (participante is null) return null;

        var realizado = (await EstadoAsync(seguimiento: false)).Realizado;
        Participante? amigo = null;
        if (realizado && participante.Asistira)
        {
            amigo = await db.Asignaciones.AsNoTracking()
                .Where(a => a.DaId == participante.Id)
                .Select(a => a.Recibe)
                .SingleOrDefaultAsync();
        }
        return new VistaParticipante(participante, realizado, amigo);
    }

    public async Task<Resultado> ActualizarAsync(string codigo, ParticipanteForm form)
    {
        await inicializador.EsperarAsync();
        var nombre = Texto.Limpiar(form.NombreCompleto);
        var normalizado = Texto.NormalizarNombre(nombre);

        try
        {
            return await EnTransaccionAsync(async () =>
            {
                if (await EstadoAsync() is { Realizado: true })
                    return Resultado.Falla("El sorteo ya se realizó; tu inscripción ya no se puede modificar.");

                var p = await db.Participantes.SingleOrDefaultAsync(x => x.Codigo == codigo);
                if (p is null)
                    return Resultado.Falla("No encontramos una inscripción con ese código.");

                if (await db.Participantes.AnyAsync(x => x.NombreNormalizado == normalizado && x.Id != p.Id))
                    return Resultado.Falla("Ya existe otra inscripción con ese nombre.");

                p.NombreCompleto = nombre;
                p.NombreNormalizado = normalizado;
                p.Postre = Texto.Limpiar(form.Postre);
                p.LugarCompra = Texto.Limpiar(form.LugarCompra);
                p.Asistira = form.Asistira == true;
                p.Correo = Correo.Normalizar(form.Correo);
                p.FechaActualizacionUtc = DateTime.UtcNow;
                await db.SaveChangesAsync();
                return Resultado.Exito(codigo);
            });
        }
        catch (DbUpdateException ex) when (EsViolacionUnica(ex))
        {
            return Resultado.Falla("Ya existe otra inscripción con ese nombre.");
        }
    }

    // ---------- Gestión ----------

    public async Task<ResumenGestion> ResumenAsync()
    {
        await inicializador.EsperarAsync();
        var estado = await EstadoAsync(seguimiento: false);
        var participantes = await db.Participantes.AsNoTracking()
            .OrderBy(p => p.FechaInscripcionUtc)
            .ToListAsync();
        // Solo se cuenta: las parejas se ven aparte, en /gestion/parejas, para no revelarlas por accidente.
        var asignaciones = await db.Asignaciones.CountAsync();
        return new ResumenGestion(estado, participantes, asignaciones);
    }

    /// <param name="rehacer">false = primer sorteo; true = generar una nueva asignación.</param>
    public async Task<Resultado> SortearAsync(bool rehacer)
    {
        await inicializador.EsperarAsync();
        return await EnTransaccionAsync(async () =>
        {
            var estado = await EstadoAsync();
            if (!rehacer && estado.Realizado)
                return Resultado.Falla("El sorteo ya se realizó. Usa «Rehacer sorteo» si necesitas una nueva asignación.");
            if (rehacer && !estado.Realizado)
                return Resultado.Falla("Todavía no se ha realizado el sorteo.");

            var ids = await db.Participantes.Where(p => p.Asistira).Select(p => p.Id).ToListAsync();
            if (ids.Count < MinimoParticipantes)
                return Resultado.Falla($"Se necesitan al menos {MinimoParticipantes} personas que asistan (hay {ids.Count}).");

            await db.Asignaciones.ExecuteDeleteAsync();
            db.Asignaciones.AddRange(Sorteo.GenerarCiclo(ids)
                .Select(x => new Asignacion { DaId = x.DaId, RecibeId = x.RecibeId }));
            estado.Realizado = true;
            estado.FechaSorteoUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();

            logger.LogInformation("Sorteo {Tipo} con {Cantidad} participantes.", rehacer ? "rehecho" : "realizado", ids.Count);
            return Resultado.Exito();
        });
    }

    public async Task<Resultado> ReiniciarAsync()
    {
        await inicializador.EsperarAsync();
        return await EnTransaccionAsync(async () =>
        {
            var estado = await EstadoAsync();
            await db.Asignaciones.ExecuteDeleteAsync();
            estado.Realizado = false;
            estado.FechaSorteoUtc = null;
            await db.SaveChangesAsync();
            logger.LogInformation("Sorteo reiniciado; inscripciones abiertas.");
            return Resultado.Exito();
        });
    }

    /// <summary>Borra todas las inscripciones y asignaciones y reabre las inscripciones (para un intercambio nuevo).</summary>
    public async Task<Resultado> LimpiarTodoAsync()
    {
        await inicializador.EsperarAsync();
        return await EnTransaccionAsync(async () =>
        {
            var estado = await EstadoAsync();
            await db.Asignaciones.ExecuteDeleteAsync();
            var borrados = await db.Participantes.ExecuteDeleteAsync();
            estado.Realizado = false;
            estado.FechaSorteoUtc = null;
            await db.SaveChangesAsync();
            logger.LogWarning("Datos limpiados: se borraron {Cantidad} inscripciones y todas las asignaciones.", borrados);
            return Resultado.Exito();
        });
    }

    public async Task<Resultado> EliminarAsync(int id)
    {
        await inicializador.EsperarAsync();
        return await EnTransaccionAsync(async () =>
        {
            if (await EstadoAsync() is { Realizado: true })
                return Resultado.Falla("No se pueden eliminar inscripciones después del sorteo. Reinicia el sorteo primero.");

            var borrados = await db.Participantes.Where(p => p.Id == id).ExecuteDeleteAsync();
            return borrados > 0 ? Resultado.Exito() : Resultado.Falla("La inscripción ya no existe.");
        });
    }

    /// <summary>
    /// Parejas del sorteo (quién le regala a quién), ordenadas siguiendo la cadena para que se lean
    /// como A → B → C → … Solo para la página protegida /gestion/parejas.
    /// </summary>
    public async Task<IReadOnlyList<Pareja>> ParejasAsync()
    {
        await inicializador.EsperarAsync();
        var asignaciones = await db.Asignaciones.AsNoTracking()
            .Select(a => new Pareja(a.Da!, a.Recibe!))
            .ToListAsync();
        if (asignaciones.Count == 0) return [];

        var porDa = asignaciones.ToDictionary(p => p.Da.Id);
        var ordenadas = new List<Pareja>(asignaciones.Count);
        var visitados = new HashSet<int>();
        // Recorre cada ciclo empezando por el primero en inscribirse (normalmente hay uno solo).
        foreach (var inicio in asignaciones.OrderBy(p => p.Da.FechaInscripcionUtc))
        {
            Pareja? actual = inicio;
            while (actual is not null && visitados.Add(actual.Da.Id))
            {
                ordenadas.Add(actual);
                actual = porDa.GetValueOrDefault(actual.Recibe.Id);
            }
        }
        return ordenadas;
    }

    public async Task<Participante?> ObtenerParticipanteAsync(int id)
    {
        await inicializador.EsperarAsync();
        return await db.Participantes.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id);
    }

    // ---------- Auxiliares ----------

    private Task<EstadoSorteo> EstadoAsync(bool seguimiento = true)
    {
        var consulta = seguimiento ? db.EstadosSorteo : db.EstadosSorteo.AsNoTracking();
        return consulta.SingleAsync(e => e.Id == EstadoSorteo.IdUnico);
    }

    private Task<Resultado> EnTransaccionAsync(Func<Task<Resultado>> operacion)
    {
        var estrategia = db.Database.CreateExecutionStrategy();
        return estrategia.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear(); // por si es un reintento
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var resultado = await operacion();
            if (resultado.Ok) await tx.CommitAsync();
            else await tx.RollbackAsync();
            return resultado;
        });
    }

    private static bool EsViolacionUnica(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };
}
