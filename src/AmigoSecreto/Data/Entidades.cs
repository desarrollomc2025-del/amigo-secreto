namespace AmigoSecreto.Data;

/// <summary>
/// Empresa u organización que hace su propio intercambio: tiene sus inscritos, su sorteo, su clave
/// de organizador y su dirección propia (/{Slug}). Las empresas no ven los datos de las demás.
/// </summary>
public sealed class Empresa
{
    public int Id { get; set; }

    /// <summary>Identificador en la URL: minúsculas, números y guiones (ver <see cref="Servicios.Slug"/>).</summary>
    public required string Slug { get; set; }

    public required string Nombre { get; set; }

    /// <summary>Texto bajo el nombre, p. ej. «Intercambio de postres».</summary>
    public required string Subtitulo { get; set; }

    /// <summary>Cuándo se hará el sorteo, en texto libre: «el sábado 21 de noviembre a las 9:00 p. m.». Vacío = sin anunciar.</summary>
    public string FechaSorteo { get; set; } = "";

    /// <summary>Hash de la clave del organizador de esta empresa (<see cref="Servicios.HashClave"/>). Nulo = solo con ADMIN_KEY.</summary>
    public string? ClaveHash { get; set; }

    /// <summary>La que se muestra en la página principal (/) y en las direcciones sin empresa. Como máximo una.</summary>
    public bool Predeterminada { get; set; }

    public bool SorteoRealizado { get; set; }
    public DateTime? FechaSorteoUtc { get; set; }
    public DateTime FechaCreacionUtc { get; set; }
}

public sealed class Participante
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }
    public Empresa? Empresa { get; set; }

    public required string NombreCompleto { get; set; }

    /// <summary>Nombre en mayúsculas y sin tildes; sirve para evitar inscripciones duplicadas dentro de la empresa.</summary>
    public required string NombreNormalizado { get; set; }

    public required string Postre { get; set; }
    public required string LugarCompra { get; set; }
    public bool Asistira { get; set; }

    /// <summary>Correo para enviarle su código. Nulo en inscripciones anteriores a este campo.</summary>
    public string? Correo { get; set; }

    /// <summary>Código personal de 6 caracteres (ver <see cref="Servicios.CodigoPersonal"/>). Único en toda la base.</summary>
    public required string Codigo { get; set; }

    public DateTime FechaInscripcionUtc { get; set; }
    public DateTime? FechaActualizacionUtc { get; set; }
}

/// <summary>Quién (<see cref="DaId"/>) le regala a quién (<see cref="RecibeId"/>). Ambos son de la misma empresa.</summary>
public sealed class Asignacion
{
    public int DaId { get; set; }
    public int RecibeId { get; set; }

    public Participante? Da { get; set; }
    public Participante? Recibe { get; set; }
}
