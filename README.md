# 🎁 Red Cetino-Monteagudo-Rivera · Intercambio de postres

Aplicación web (amigo secreto) para organizar el intercambio de postres del convivio de la Red Cetino-Monteagudo-Rivera.

- **Participantes** (sin cuenta): se inscriben (nombre, correo, postre, lugar de compra y asistencia; todo obligatorio), reciben un código personal de 6 caracteres en pantalla y por correo, pueden editar su inscripción hasta el sorteo y después abren un regalo animado que revela a su amigo secreto.
- **Organizador** (`/gestion`, protegido con `ADMIN_KEY`): ve totales y la lista de inscritos, realiza/rehace/reinicia el sorteo, elimina inscripciones, recupera códigos perdidos y puede **limpiar todo** (borrar inscripciones y sorteo) para empezar de nuevo. **Nunca** ve las asignaciones.

📖 **[Manual de usuario](docs/MANUAL-USUARIO.md)** para participantes y organizador.

## Tecnología

| Pieza | Detalle |
|---|---|
| Web | Blazor Web App, .NET 10, **solo renderizado estático en servidor** (sin WebSockets ni `blazor.web.js`) |
| Datos | Entity Framework Core 10 + SQL Server 2022 Express (contenedor) con reintentos de conexión |
| Hosting | AWS EC2 (Amazon Linux 2023) con Docker Compose y Caddy (HTTPS automático) |
| Correo | SMTP con MailKit (p. ej. Gmail con contraseña de aplicación) |
| CI | GitHub Actions (`.github/workflows/ci.yml`): compila y prueba |

### Estructura

```
src/AmigoSecreto/
  Program.cs                  Configuración: BD, cookie de gestión, límites de envíos, cabeceras
  Data/                       Entidades, DbContext e inicializador (crea las tablas al iniciar)
  Servicios/                  Lógica del intercambio, sorteo, códigos, correo, clave de administración
  Components/Pages/           Inicio, Inscripción, Consulta (mi código), Gestión, errores
  wwwroot/app.css, app.js     Estilo navideño y mejoras (copiar código, regalo, confeti)
tests/AmigoSecreto.Tests/     Pruebas del sorteo, nombres, códigos y validación de correo
deploy/aws/                   docker-compose, Caddyfile, .env.example y user-data de la EC2
Dockerfile                    Imagen de la app
```

## Reglas principales

- **Código personal**: 6 caracteres del alfabeto `ABCDEFGHJKLMNPQRSTUVWXYZ23456789` (sin 0/O ni 1/I), generado con `RandomNumberGenerator`. Se guarda en `localStorage` para rellenarlo automáticamente.
- **Nombres duplicados**: se comparan sin distinguir mayúsculas, tildes ni espacios extra (`José  Peña` = `jose pena`). Hay un índice único en la base como respaldo.
- **Sorteo**: solo participan quienes asistirán (mínimo 3). Se barajan con generador criptográfico y se forma **un solo ciclo** A→B→C→…→A: nadie se toca a sí mismo y funciona con números impares. Al sortear se cierran las inscripciones.
- **Privacidad**: las asignaciones solo se envían al navegador de cada participante cuando ingresa **su propio** código. El panel de gestión solo muestra la cantidad.
- **Seguridad**: antiforgery en todos los formularios; validación en cliente (HTML5) y servidor (DataAnnotations); clave comparada en tiempo constante (hash SHA-256 + `FixedTimeEquals`); cookie de gestión HttpOnly, Secure, SameSite=Strict y firmada/cifrada por Data Protection; límite de envíos por IP (10 intentos de acceso cada 5 min, 60 envíos por minuto); `Cache-Control: no-store` en páginas.
- **Fechas**: se guardan en UTC y se muestran en hora de Guatemala (UTC-6). La fecha del sorteo que se anuncia está en `Evento.FechaSorteo` (`Servicios/Utilidades.cs`).
- **Correo**: obligatorio y validado (formato `usuario@dominio.tld`); se guarda en minúsculas. Las bases creadas antes de este campo reciben la columna al iniciar la app.

## Ejecutar en local

Requisitos: [.NET 10 SDK](https://dotnet.microsoft.com/download) y SQL Server LocalDB (viene con Visual Studio) u otro SQL Server.

```powershell
cd src/AmigoSecreto

# Configuración local con user-secrets (no se guarda en el repositorio)
dotnet user-secrets set "ConnectionStrings:Sql" "Server=(localdb)\MSSQLLocalDB;Database=AmigoSecreto;Trusted_Connection=True;TrustServerCertificate=True"
dotnet user-secrets set "ADMIN_KEY" "una-clave-larga-y-secreta"

dotnet run --launch-profile https
```

Abre https://localhost:7248. Las tablas se crean solas al iniciar. Para el panel del organizador entra a https://localhost:7248/gestion.

> Si el navegador no confía en el certificado de desarrollo: `dotnet dev-certs https --trust`.

Pruebas:

```powershell
dotnet test
```

## Publicar en AWS (EC2 + Docker)

Todo corre en una sola instancia EC2 con Docker Compose (`deploy/aws/docker-compose.yml`):

| Contenedor | Función |
|---|---|
| `app` | La aplicación (imagen construida con el `Dockerfile` de la raíz). Las llaves de Data Protection se guardan en el volumen `llaves`. |
| `sql` | SQL Server 2022 **Express** con los datos en el volumen `sqldata`. |
| `caddy` | Proxy inverso con **HTTPS automático** (Let's Encrypt) en los puertos 80/443. |

### 1. Crear la instancia

- **AMI**: Amazon Linux 2023 (x86_64). **Tipo**: al menos 4 GB de RAM (SQL Server no arranca con menos de ~2 GB), p. ej. `c7i-flex.large`. Disco gp3 de 20 GB.
- **Security group**: 80 y 443 abiertos a todos; 22 (SSH) solo a tu IP.
- **User data**: el contenido de `deploy/aws/user-data.sh` (instala Docker y Compose, crea 2 GB de swap y clona este repositorio en `~/amigo-secreto`).
- Asocia una **Elastic IP** para que la dirección no cambie.

### 2. Dominio

Crea un registro **A** del dominio apuntando a la Elastic IP (en Cloudflare, con la nube **gris / DNS only**: el certificado lo saca Caddy y el límite de envíos por IP necesita la IP real del visitante). Sin dominio propio puedes usar `<ip-con-guiones>.sslip.io`, p. ej. `3-12-95-79.sslip.io`.

### 3. Configurar y levantar

```bash
ssh -i amigosecreto-key.pem ec2-user@<ip>
cd ~/amigo-secreto/deploy/aws
cp .env.example .env && chmod 600 .env
nano .env          # DOMINIO, SQL_PASSWORD, ADMIN_KEY y CORREO_*
docker compose up -d --build
```

`DOMINIO` acepta varios nombres separados por comas (p. ej. `midominio.org, www.midominio.org`). Las tablas se crean solas al iniciar.

### 4. Envío del código por correo

La app envía a cada inscrito un correo con su código por SMTP (MailKit). Con Gmail:

1. Activa la **verificación en 2 pasos** de la cuenta y crea una **contraseña de aplicación** en <https://myaccount.google.com/apppasswords>.
2. En `.env`: `CORREO_HOST=smtp.gmail.com`, `CORREO_PUERTO=587`, `CORREO_USUARIO` y `CORREO_REMITENTE` = la cuenta, `CORREO_CLAVE` = la contraseña de aplicación (16 letras, sin espacios).
3. `docker compose up -d app` para aplicar.

Si el correo no está configurado la app funciona igual y le pide al participante que anote su código.

### Actualizar a una nueva versión

```bash
cd ~/amigo-secreto && git pull && cd deploy/aws && docker compose up -d --build
```

### Comandos útiles

```bash
docker compose ps                 # estado de los contenedores
docker compose logs -f app        # registros de la app
docker compose restart caddy      # reintentar el certificado tras cambiar el DNS
```

> Los datos viven en los volúmenes de Docker de la instancia. Para respaldarlos: `docker compose exec sql /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$SQL_PASSWORD" -Q "BACKUP DATABASE AmigoSecreto TO DISK='/var/opt/mssql/respaldo.bak'"`.

## Integración continua

`.github/workflows/ci.yml` compila y ejecuta las pruebas en cada push y pull request a `main`.

## Uso el día del convivio

1. Comparte la URL para que todos se inscriban (si hiciste pruebas antes, usa **Limpiar todo** en `/gestion` primero).
2. Revisa en `/gestion` que todos los asistentes estén inscritos.
3. Pulsa **Realizar sorteo**: se cierran las inscripciones.
4. Cada quien entra con su código y abre su regalo 🎁.
5. Si alguien perdió su código, recupéralo desde la lista de participantes y compártelo **solo con esa persona**.
