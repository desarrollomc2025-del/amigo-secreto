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

    /// <summary>
    /// Pasos idempotentes para bases creadas con versiones anteriores (en una base nueva no hacen nada).
    /// Se usa EXEC(...) cuando el paso menciona columnas que quizá aún no existen, porque SQL Server
    /// valida los nombres de columna al compilar el lote.
    /// </summary>
    private static readonly string[] Migraciones =
    [
        // v2: correo del participante.
        "IF COL_LENGTH('Participantes', 'Correo') IS NULL ALTER TABLE Participantes ADD Correo nvarchar(254) NULL;",

        // v3: varias empresas. Los datos que ya existían pasan a la empresa «red-cmr» (predeterminada).
        """
        IF OBJECT_ID(N'Empresas') IS NULL
        CREATE TABLE Empresas (
            Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Empresas PRIMARY KEY,
            Slug varchar(40) NOT NULL,
            Nombre nvarchar(100) NOT NULL,
            Subtitulo nvarchar(100) NOT NULL,
            FechaSorteo nvarchar(150) NOT NULL,
            ClaveHash varchar(200) NULL,
            Predeterminada bit NOT NULL,
            SorteoRealizado bit NOT NULL,
            FechaSorteoUtc datetime2 NULL,
            FechaCreacionUtc datetime2 NOT NULL,
            CONSTRAINT IX_Empresas_Slug UNIQUE (Slug)
        );
        """,
        """
        IF OBJECT_ID(N'EstadoSorteo') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM Empresas)
        EXEC(N'INSERT INTO Empresas (Slug, Nombre, Subtitulo, FechaSorteo, ClaveHash, Predeterminada, SorteoRealizado, FechaSorteoUtc, FechaCreacionUtc)
               SELECT ''red-cmr'', N''Red Cetino-Monteagudo-Rivera'', N''Intercambio de postres'',
                      N''el sábado 21 de noviembre a las 9:00 p. m.'', NULL, 1,
                      ISNULL((SELECT Realizado FROM EstadoSorteo WHERE Id = 1), 0),
                      (SELECT FechaSorteoUtc FROM EstadoSorteo WHERE Id = 1), SYSUTCDATETIME();');
        """,
        """
        IF COL_LENGTH('Participantes', 'EmpresaId') IS NULL
        BEGIN
            ALTER TABLE Participantes ADD EmpresaId int NULL;
            EXEC(N'UPDATE Participantes SET EmpresaId = (SELECT MIN(Id) FROM Empresas) WHERE EmpresaId IS NULL;');
            EXEC(N'ALTER TABLE Participantes ALTER COLUMN EmpresaId int NOT NULL;');
        END
        """,
        """
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Participantes_NombreNormalizado' AND object_id = OBJECT_ID(N'Participantes'))
            DROP INDEX IX_Participantes_NombreNormalizado ON Participantes;
        """,
        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Participantes_EmpresaId_NombreNormalizado' AND object_id = OBJECT_ID(N'Participantes'))
            EXEC(N'CREATE UNIQUE INDEX IX_Participantes_EmpresaId_NombreNormalizado ON Participantes (EmpresaId, NombreNormalizado);');
        """,
        """
        IF OBJECT_ID(N'FK_Participantes_Empresas_EmpresaId') IS NULL
            EXEC(N'ALTER TABLE Participantes ADD CONSTRAINT FK_Participantes_Empresas_EmpresaId FOREIGN KEY (EmpresaId) REFERENCES Empresas (Id);');
        """,
        // El estado del sorteo ahora vive en cada empresa.
        "IF OBJECT_ID(N'EstadoSorteo') IS NOT NULL AND EXISTS (SELECT 1 FROM Empresas) DROP TABLE EstadoSorteo;",
    ];
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
                // EnsureCreated no modifica tablas existentes: los cambios posteriores se aplican aquí.
                foreach (var paso in Migraciones)
                    await db.Database.ExecuteSqlRawAsync(paso, stoppingToken);
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
