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

public class SlugTests
{
    [Theory]
    [InlineData("acme")]
    [InlineData("red-cmr")]
    [InlineData("a1")]
    [InlineData("x")]
    public void Acepta_slugs_validos(string slug) => Assert.True(Slug.EsValido(slug));

    [Theory]
    [InlineData("")]
    [InlineData("-acme")]
    [InlineData("acme-")]
    [InlineData("ac--me")]
    [InlineData("Acme")]
    [InlineData("acmé")]
    [InlineData("app.css")]
    [InlineData("gestion")]
    [InlineData("admin")]
    [InlineData("consulta")]
    [InlineData(null)]
    public void Rechaza_slugs_invalidos_o_reservados(string? slug) => Assert.False(Slug.EsValido(slug));

    [Theory]
    [InlineData("Café Ñandú S.A.", "cafe-nandu-s-a")]
    [InlineData("  Red Cetino-Monteagudo-Rivera ", "red-cetino-monteagudo-rivera")]
    [InlineData("¡Hola!", "hola")]
    public void Sugerir(string nombre, string esperado) => Assert.Equal(esperado, Slug.Sugerir(nombre));

    [Fact]
    public void Sugerir_respeta_la_longitud_maxima() =>
        Assert.True(Slug.EsValido(Slug.Sugerir(new string('a', 30) + " " + new string('b', 30))));
}

public class HashClaveTests
{
    [Fact]
    public void Verifica_la_clave_correcta_y_rechaza_otras()
    {
        var hash = HashClave.Crear("clave-de-prueba");
        Assert.True(HashClave.Verificar("clave-de-prueba", hash));
        Assert.False(HashClave.Verificar("clave-de-prueba ", hash));
        Assert.False(HashClave.Verificar("", hash));
        Assert.False(HashClave.Verificar(null, hash));
    }

    [Fact]
    public void Usa_sal_distinta_cada_vez() => Assert.NotEqual(HashClave.Crear("igual-igual"), HashClave.Crear("igual-igual"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("texto-plano")]
    [InlineData("pbkdf2-sha256$x$abc$def")]
    [InlineData("pbkdf2-sha256$1000$no-base64$no-base64")]
    public void Rechaza_hashes_invalidos_sin_lanzar(string? almacenado) =>
        Assert.False(HashClave.Verificar("cualquier-clave", almacenado));
}

public class AccesoEmpresaTests
{
    [Theory]
    [InlineData("/acme/gestion", "acme/gestion/acceso?volver=gestion")]
    [InlineData("/acme/gestion/parejas", "acme/gestion/acceso?volver=gestion%2Fparejas")]
    [InlineData("/gestion/reporte", "gestion/acceso?volver=gestion%2Freporte")]
    [InlineData("/gestion", "gestion/acceso?volver=gestion")]
    [InlineData("/admin", null)]
    [InlineData("https://otro.sitio/gestion", null)]
    [InlineData("//otro.sitio/gestion", null)]
    [InlineData(null, null)]
    public void Redirige_al_acceso_de_la_empresa(string? volver, string? esperado) =>
        Assert.Equal(esperado, AccesoEmpresa.Desde(volver));
}
