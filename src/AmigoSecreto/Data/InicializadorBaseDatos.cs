using Microsoft.EntityFrameworkCore;

namespace AmigoSecreto.Data;

/// <summary>
/// Crea las tablas al iniciar la app. Corre en segundo plano y reintenta, porque la base
/// SQL Server puede tardar unos segundos en aceptar conexiones cuando arranca junto con la app.
/// Las consultas esperan a que termine mediante <see cref="EsperarAsync"/>.
/// </summary>
public sealed class InicializadorBaseDatos(IServiceScopeFactory scopeFactory, ILogger<InicializadorBaseDatos> logger)
    : BackgroundService
{
    private static readonly TimeSpan EsperaMaxima = TimeSpan.FromSeconds(90);
    private readonly TaskCompletionSource listo = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task EsperarAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await listo.Task.WaitAsync(EsperaMaxima, cancellationToken);
        }
        catch (TimeoutException ex)
        {
            throw new InvalidOperationException("La base de datos todavía no está disponible.", ex);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        for (var intento = 1; !stoppingToken.IsCancellationRequested; intento++)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var creada = await db.Database.EnsureCreatedAsync(stoppingToken);
                // EnsureCreated no modifica tablas existentes: las columnas agregadas después se crean aquí.
                await db.Database.ExecuteSqlRawAsync(
                    "IF COL_LENGTH('Participantes', 'Correo') IS NULL ALTER TABLE Participantes ADD Correo nvarchar(254) NULL;",
                    stoppingToken);
                logger.LogInformation(creada
                    ? "Tablas de la base de datos creadas."
                    : "La base de datos ya tenía las tablas.");
                listo.TrySetResult();
                return;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                var espera = TimeSpan.FromSeconds(Math.Min(60, 5 * intento));
                logger.LogWarning(ex, "No se pudo inicializar la base de datos (intento {Intento}). Reintento en {Espera}.",
                    intento, espera);
                await Task.Delay(espera, stoppingToken);
            }
        }
    }
}
