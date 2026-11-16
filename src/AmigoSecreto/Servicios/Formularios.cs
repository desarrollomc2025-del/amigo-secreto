using System.ComponentModel.DataAnnotations;
using AmigoSecreto.Data;

namespace AmigoSecreto.Servicios;

public sealed class ParticipanteForm
{
    [Required(ErrorMessage = "Escribe tu nombre completo.")]
    [StringLength(100, MinimumLength = 3, ErrorMessage = "El nombre debe tener entre 3 y 100 caracteres.")]
    public string? NombreCompleto { get; set; }

    [Required(ErrorMessage = "Indica qué postre te gustaría recibir.")]
    [StringLength(150, MinimumLength = 2, ErrorMessage = "El postre debe tener entre 2 y 150 caracteres.")]
    public string? Postre { get; set; }

    [Required(ErrorMessage = "Indica dónde se puede comprar el postre.")]
    [StringLength(150, MinimumLength = 2, ErrorMessage = "El lugar de compra debe tener entre 2 y 150 caracteres.")]
    public string? LugarCompra { get; set; }

    [Required(ErrorMessage = "Escribe tu correo electrónico.")]
    [CorreoValido]
    public string? Correo { get; set; }

    [Required(ErrorMessage = "Indica si asistirás al convivio.")]
    public bool? Asistira { get; set; }

    /// <summary>Solo se usa al editar (campo oculto).</summary>
    public string? Codigo { get; set; }

    public static ParticipanteForm Desde(Participante p) => new()
    {
        NombreCompleto = p.NombreCompleto,
        Postre = p.Postre,
        LugarCompra = p.LugarCompra,
        Asistira = p.Asistira,
        Correo = p.Correo,
        Codigo = p.Codigo,
    };
}

/// <summary>Valida el formato del correo con <see cref="Correo.EsValido"/>.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class CorreoValidoAttribute : ValidationAttribute
{
    public CorreoValidoAttribute() : base("Escribe un correo electrónico válido, por ejemplo nombre@gmail.com.") { }

    // El campo vacío lo resuelve [Required].
    public override bool IsValid(object? value) =>
        value is not string s || string.IsNullOrWhiteSpace(s) || Correo.EsValido(s);
}

public sealed class BusquedaForm
{
    [Required(ErrorMessage = "Escribe tu código personal.")]
    public string? Codigo { get; set; }
}

public sealed class AccesoForm
{
    [Required(ErrorMessage = "Escribe la clave de administración.")]
    public string? Clave { get; set; }
}
