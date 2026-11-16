namespace AmigoSecreto.Data;

public sealed class Participante
{
    public int Id { get; set; }
    public required string NombreCompleto { get; set; }

    /// <summary>Nombre en mayúsculas y sin tildes; sirve para evitar inscripciones duplicadas.</summary>
    public required string NombreNormalizado { get; set; }

    public required string Postre { get; set; }
    public required string LugarCompra { get; set; }
    public bool Asistira { get; set; }

    /// <summary>Correo para enviarle su código. Nulo en inscripciones anteriores a este campo.</summary>
    public string? Correo { get; set; }

    /// <summary>Código personal de 6 caracteres (ver <see cref="Servicios.CodigoPersonal"/>).</summary>
    public required string Codigo { get; set; }

    public DateTime FechaInscripcionUtc { get; set; }
    public DateTime? FechaActualizacionUtc { get; set; }
}

/// <summary>Quién (<see cref="DaId"/>) le regala a quién (<see cref="RecibeId"/>).</summary>
public sealed class Asignacion
{
    public int DaId { get; set; }
    public int RecibeId { get; set; }

    public Participante? Da { get; set; }
    public Participante? Recibe { get; set; }
}

/// <summary>Fila única (Id = 1) con el estado del sorteo.</summary>
public sealed class EstadoSorteo
{
    public const int IdUnico = 1;

    public int Id { get; set; }
    public bool Realizado { get; set; }
    public DateTime? FechaSorteoUtc { get; set; }
}
