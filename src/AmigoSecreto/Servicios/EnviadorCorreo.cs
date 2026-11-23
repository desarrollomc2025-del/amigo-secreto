using System.Net;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace AmigoSecreto.Servicios;

/// <summary>Configuración SMTP (sección "Correo"; en variables de entorno: Correo__Host, Correo__Clave, ...).</summary>
public sealed class OpcionesCorreo
{
    public string? Host { get; set; }
    public int Puerto { get; set; } = 587;
    public string? Usuario { get; set; }
    public string? Clave { get; set; }
    public string? Remitente { get; set; }
    public string NombreRemitente { get; set; } = "Red Cetino-Monteagudo-Rivera";

    public bool Configurado => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(Remitente);
}

/// <summary>Envía por correo el código personal de cada inscrito.</summary>
public sealed class EnviadorCorreo(OpcionesCorreo opciones, ILogger<EnviadorCorreo> logger)
{
    public bool Configurado => opciones.Configurado;

    /// <summary>Devuelve true si el correo se entregó al servidor SMTP. Nunca lanza excepción.</summary>
    public async Task<bool> EnviarCodigoAsync(string destinatario, string nombre, string codigo, string urlConsulta,
        CancellationToken cancellationToken = default)
    {
        if (!opciones.Configurado)
        {
            logger.LogWarning("Correo no configurado (Correo:Host / Correo:Remitente); no se envió el código.");
            return false;
        }

        var mensaje = new MimeMessage();
        mensaje.From.Add(new MailboxAddress(opciones.NombreRemitente, opciones.Remitente));
        mensaje.To.Add(new MailboxAddress(nombre, destinatario));
        mensaje.Subject = "Tu código del intercambio de postres 🎁";

        var nombreHtml = WebUtility.HtmlEncode(nombre);
        var urlHtml = WebUtility.HtmlEncode(urlConsulta);
        mensaje.Body = new BodyBuilder
        {
            TextBody =
                $"""
                Hola, {nombre}:

                ¡Ya estás inscrito en el intercambio de postres de la Red Cetino-Monteagudo-Rivera!

                Tu código personal es: {codigo}

                📅 El sorteo del amigo secreto se hará {Evento.FechaSorteo}

                Con él puedes ver o editar tu inscripción y, después del sorteo, descubrir a tu amigo secreto:
                {urlConsulta}

                Guárdalo y no lo compartas.
                """,
            HtmlBody =
                $"""
                <div style="font-family:Arial,Helvetica,sans-serif;max-width:480px;margin:auto;color:#222">
                  <h2 style="color:#b3202b">🎁 Red Cetino-Monteagudo-Rivera</h2>
                  <p>Hola, <strong>{nombreHtml}</strong>:</p>
                  <p>¡Ya estás inscrito en el <strong>intercambio de postres</strong>!</p>
                  <p>Tu código personal es:</p>
                  <p style="font-size:32px;font-weight:bold;letter-spacing:6px;text-align:center;background:#fff4d6;
                            border:2px dashed #d4a017;border-radius:8px;padding:12px">{codigo}</p>
                  <p style="background:#eef7ee;border-left:4px solid #2e7d32;padding:10px 12px;border-radius:4px">
                    📅 El sorteo del amigo secreto se hará <strong>{Evento.FechaSorteo}</strong></p>
                  <p>Con él puedes ver o editar tu inscripción y, después del sorteo, descubrir a tu amigo secreto.</p>
                  <p style="text-align:center"><a href="{urlHtml}" style="background:#b3202b;color:#fff;padding:10px 18px;
                     border-radius:6px;text-decoration:none">Ver mi inscripción</a></p>
                  <p style="font-size:12px;color:#777">Guarda este código y no lo compartas.</p>
                </div>
                """,
        }.ToMessageBody();

        try
        {
            using var cliente = new SmtpClient { Timeout = 20_000 };
            await ConectarAsync(cliente, cancellationToken);
            await cliente.SendAsync(new MailKit.SendRequest(mensaje), cancellationToken);
            await cliente.DisconnectAsync(true, cancellationToken);
            logger.LogInformation("Código enviado por correo a la inscripción de {Nombre}.", nombre);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "No se pudo enviar el correo con el código de {Nombre}.", nombre);
            return false;
        }
    }

    /// <summary>
    /// Envía a cada participante (quien da) el nombre de su amigo secreto, el postre y dónde comprarlo.
    /// Usa una sola conexión SMTP y se reconecta si se cae. Nunca lanza excepción por un correo fallido.
    /// </summary>
    public async Task<ResultadoEnvioMasivo> EnviarAmigosSecretosAsync(IReadOnlyList<Pareja> parejas, string urlConsulta,
        CancellationToken cancellationToken = default)
    {
        var sinCorreo = parejas.Where(p => string.IsNullOrWhiteSpace(p.Da.Correo))
            .Select(p => p.Da.NombreCompleto).Order().ToList();
        var fallidos = new List<string>();
        var enviados = 0;

        if (!opciones.Configurado)
        {
            logger.LogWarning("Correo no configurado (Correo:Host / Correo:Remitente); no se enviaron los amigos secretos.");
            return new ResultadoEnvioMasivo(0, sinCorreo, [], Configurado: false);
        }

        using var cliente = new SmtpClient { Timeout = 20_000 };
        foreach (var pareja in parejas.Where(p => !string.IsNullOrWhiteSpace(p.Da.Correo)))
        {
            try
            {
                if (!cliente.IsConnected)
                    await ConectarAsync(cliente, cancellationToken);
                await cliente.SendAsync(new MailKit.SendRequest(MensajeAmigoSecreto(pareja, urlConsulta)), cancellationToken);
                enviados++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "No se pudo enviar el amigo secreto a {Nombre}.", pareja.Da.NombreCompleto);
                fallidos.Add(pareja.Da.NombreCompleto);
                try { if (cliente.IsConnected) await cliente.DisconnectAsync(true, cancellationToken); } catch { /* se reconecta */ }
            }
        }

        try { if (cliente.IsConnected) await cliente.DisconnectAsync(true, cancellationToken); } catch { /* sin importancia */ }
        logger.LogInformation("Amigos secretos enviados por correo: {Enviados} enviados, {Fallidos} fallidos, {SinCorreo} sin correo.",
            enviados, fallidos.Count, sinCorreo.Count);
        return new ResultadoEnvioMasivo(enviados, sinCorreo, fallidos, Configurado: true);
    }

    private MimeMessage MensajeAmigoSecreto(Pareja pareja, string urlConsulta)
    {
        var da = pareja.Da;
        var amigo = pareja.Recibe;
        var mensaje = new MimeMessage();
        mensaje.From.Add(new MailboxAddress(opciones.NombreRemitente, opciones.Remitente));
        mensaje.To.Add(new MailboxAddress(da.NombreCompleto, da.Correo!));
        mensaje.Subject = "🎁 ¡Ya tienes a tu amigo secreto!";

        static string h(string texto) => WebUtility.HtmlEncode(texto);
        mensaje.Body = new BodyBuilder
        {
            TextBody =
                $"""
                Hola, {da.NombreCompleto}:

                ¡Ya se realizó el sorteo del intercambio de postres de la Red Cetino-Monteagudo-Rivera!

                Tu amigo secreto es: {amigo.NombreCompleto}

                🍰 Postre que le gustaría: {amigo.Postre}
                📍 Dónde comprarlo: {amigo.LugarCompra}

                También puedes verlo con tu código en:
                {urlConsulta}

                🤫 ¡Shhh! Que sea secreto hasta el convivio.
                """,
            HtmlBody =
                $"""
                <div style="font-family:Arial,Helvetica,sans-serif;max-width:480px;margin:auto;color:#222">
                  <h2 style="color:#b3202b">🎁 Red Cetino-Monteagudo-Rivera</h2>
                  <p>Hola, <strong>{h(da.NombreCompleto)}</strong>:</p>
                  <p>¡Ya se realizó el sorteo del <strong>intercambio de postres</strong>!</p>
                  <p style="text-align:center;margin:18px 0 4px;color:#777;text-transform:uppercase;letter-spacing:2px;font-size:13px">
                    Tu amigo secreto es…</p>
                  <p style="font-size:28px;font-weight:bold;text-align:center;color:#b3202b;margin:0 0 16px">{h(amigo.NombreCompleto)}</p>
                  <table role="presentation" style="width:100%;border-collapse:separate;border-spacing:0 8px">
                    <tr><td style="background:#fff4d6;border:2px solid #f0dca8;border-radius:8px;padding:10px 12px">
                      <div style="font-size:13px;color:#777;font-weight:bold">🍰 Postre que le gustaría</div>
                      <div style="font-size:17px;font-weight:bold">{h(amigo.Postre)}</div></td></tr>
                    <tr><td style="background:#fff4d6;border:2px solid #f0dca8;border-radius:8px;padding:10px 12px">
                      <div style="font-size:13px;color:#777;font-weight:bold">📍 Dónde comprarlo</div>
                      <div style="font-size:17px;font-weight:bold">{h(amigo.LugarCompra)}</div></td></tr>
                  </table>
                  <p style="text-align:center"><a href="{h(urlConsulta)}" style="background:#b3202b;color:#fff;padding:10px 18px;
                     border-radius:6px;text-decoration:none">Verlo en la página</a></p>
                  <p style="font-size:13px;color:#777;text-align:center">🤫 ¡Shhh! Que sea secreto hasta el convivio.</p>
                </div>
                """,
        }.ToMessageBody();
        return mensaje;
    }

    private async Task ConectarAsync(SmtpClient cliente, CancellationToken cancellationToken)
    {
        var seguridad = opciones.Puerto == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
        await cliente.ConnectAsync(opciones.Host!, opciones.Puerto, seguridad, cancellationToken);
        if (!string.IsNullOrEmpty(opciones.Usuario))
            await cliente.AuthenticateAsync(opciones.Usuario, opciones.Clave ?? string.Empty, cancellationToken);
    }
}

public sealed record ResultadoEnvioMasivo(int Enviados, IReadOnlyList<string> SinCorreo, IReadOnlyList<string> Fallidos, bool Configurado);
