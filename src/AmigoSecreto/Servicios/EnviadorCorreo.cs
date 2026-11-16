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
            var seguridad = opciones.Puerto == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
            await cliente.ConnectAsync(opciones.Host, opciones.Puerto, seguridad, cancellationToken);
            if (!string.IsNullOrEmpty(opciones.Usuario))
                await cliente.AuthenticateAsync(opciones.Usuario, opciones.Clave ?? string.Empty, cancellationToken);
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
}
