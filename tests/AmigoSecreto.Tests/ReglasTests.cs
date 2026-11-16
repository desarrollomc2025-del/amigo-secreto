using AmigoSecreto.Servicios;

namespace AmigoSecreto.Tests;

public class SorteoTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(25)]
    public void GenerarCiclo_forma_un_solo_ciclo_sin_autoasignaciones(int cantidad)
    {
        var ids = Enumerable.Range(100, cantidad).ToList();

        for (var repeticion = 0; repeticion < 200; repeticion++)
        {
            var asignaciones = Sorteo.GenerarCiclo(ids);

            Assert.Equal(cantidad, asignaciones.Count);
            Assert.All(asignaciones, a => Assert.NotEqual(a.DaId, a.RecibeId));
            Assert.Equal(ids.Order(), asignaciones.Select(a => a.DaId).Order());
            Assert.Equal(ids.Order(), asignaciones.Select(a => a.RecibeId).Order());

            // Seguir el ciclo desde cualquier persona debe recorrer a todas antes de volver.
            var siguiente = asignaciones.ToDictionary(a => a.DaId, a => a.RecibeId);
            var actual = ids[0];
            for (var paso = 1; paso < cantidad; paso++)
            {
                actual = siguiente[actual];
                Assert.NotEqual(ids[0], actual);
            }
            Assert.Equal(ids[0], siguiente[actual]);
        }
    }

    [Fact]
    public void GenerarCiclo_produce_ordenes_distintos()
    {
        var ids = Enumerable.Range(1, 6).ToList();
        var distintos = Enumerable.Range(0, 100)
            .Select(_ => string.Join(",", Sorteo.GenerarCiclo(ids).OrderBy(a => a.DaId).Select(a => a.RecibeId)))
            .Distinct()
            .Count();
        Assert.True(distintos > 10);
    }

    [Fact]
    public void GenerarCiclo_rechaza_menos_de_dos_participantes() =>
        Assert.Throws<ArgumentException>(() => Sorteo.GenerarCiclo([5, 5]));
}

public class TextoTests
{
    [Theory]
    [InlineData("José Peña", "JOSE PENA")]
    [InlineData("  maría   JOSÉ  ", "MARIA JOSE")]
    [InlineData("Ángel Müller", "ANGEL MULLER")]
    public void NormalizarNombre_ignora_mayusculas_tildes_y_espacios(string entrada, string esperado) =>
        Assert.Equal(esperado, Texto.NormalizarNombre(entrada));

    [Fact]
    public void Limpiar_colapsa_espacios() => Assert.Equal("Pie de limón", Texto.Limpiar("  Pie   de\tlimón "));
}

public class CodigoPersonalTests
{
    [Fact]
    public void Generar_usa_solo_caracteres_no_ambiguos()
    {
        for (var i = 0; i < 1000; i++)
        {
            var codigo = CodigoPersonal.Generar();
            Assert.Equal(6, codigo.Length);
            Assert.DoesNotContain(codigo, c => "0O1I".Contains(c));
            Assert.All(codigo, c => Assert.Contains(c, CodigoPersonal.Alfabeto));
        }
    }

    [Theory]
    [InlineData("abc234", "ABC234")]
    [InlineData(" ABC-234 ", "ABC234")]
    [InlineData("ABC23", null)]
    [InlineData("ABC2340", null)]
    [InlineData("ABC0O1", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Normalizar(string? entrada, string? esperado) =>
        Assert.Equal(esperado, CodigoPersonal.Normalizar(entrada));
}

public class HoraGuatemalaTests
{
    [Fact]
    public void Formatear_resta_seis_horas() =>
        Assert.StartsWith("24/12/2026 6:30", HoraGuatemala.Formatear(new DateTime(2026, 12, 25, 0, 30, 0, DateTimeKind.Utc)));
}

public class CorreoTests
{
    [Theory]
    [InlineData("maria.lopez@gmail.com")]
    [InlineData("  Juan_Perez+postres@Outlook.COM ")]
    [InlineData("a@empresa.com.gt")]
    [InlineData("o'neil@sub-dominio.org")]
    public void Acepta_correos_validos(string correo) => Assert.True(Correo.EsValido(correo));

    [Theory]
    [InlineData("")]
    [InlineData("maria")]
    [InlineData("maria@")]
    [InlineData("maria@gmail")]
    [InlineData("@gmail.com")]
    [InlineData("maria lopez@gmail.com")]
    [InlineData("maria@@gmail.com")]
    [InlineData(".maria@gmail.com")]
    [InlineData("maria.@gmail.com")]
    [InlineData("ma..ria@gmail.com")]
    [InlineData("maria@-gmail.com")]
    [InlineData("maria@gmail.c")]
    public void Rechaza_correos_invalidos(string correo) => Assert.False(Correo.EsValido(correo));

    [Fact]
    public void Normaliza_a_minusculas_sin_espacios() =>
        Assert.Equal("juan@outlook.com", Correo.Normalizar("  Juan@Outlook.COM "));

    [Fact]
    public void El_atributo_deja_el_vacio_a_Required() =>
        Assert.True(new CorreoValidoAttribute().IsValid(""));
}
