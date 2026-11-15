# 🎁 Amigo Secreto · Intercambio de postres

Aplicación web para organizar el intercambio de postres de un convivio entre amigos.

- **Participantes** (sin cuenta): se inscriben, reciben un código personal de 6 caracteres, pueden editar su inscripción hasta el sorteo y después abren un regalo animado que revela a su amigo secreto.
- **Organizador** (`/gestion`, protegido con `ADMIN_KEY`): ve totales y la lista de inscritos, realiza/rehace/reinicia el sorteo, elimina inscripciones y recupera códigos perdidos. **Nunca** ve las asignaciones.

## Tecnología

| Pieza | Detalle |
|---|---|
| Web | Blazor Web App, .NET 10, **solo renderizado estático en servidor** (sin WebSockets ni `blazor.web.js`) |
| Datos | Entity Framework Core 10 + Azure SQL Database (serverless, oferta gratuita) con reintentos de conexión |
| Hosting | Azure App Service Linux, plan **F1** (gratuito) |
| CI/CD | GitHub Actions (`.github/workflows/azure-app-service.yml`) |

### Estructura

```
src/AmigoSecreto/
  Program.cs                  Configuración: BD, cookie de gestión, límites de envíos, cabeceras
  Data/                       Entidades, DbContext e inicializador (crea las tablas al iniciar)
  Servicios/                  Lógica del intercambio, sorteo, códigos, clave de administración
  Components/Pages/           Inicio, Inscripción, Consulta (mi código), Gestión, errores
  wwwroot/app.css, app.js     Estilo navideño y mejoras (copiar código, regalo, confeti)
tests/AmigoSecreto.Tests/     Pruebas del sorteo, normalización de nombres y códigos
```

## Reglas principales

- **Código personal**: 6 caracteres del alfabeto `ABCDEFGHJKLMNPQRSTUVWXYZ23456789` (sin 0/O ni 1/I), generado con `RandomNumberGenerator`. Se guarda en `localStorage` para rellenarlo automáticamente.
- **Nombres duplicados**: se comparan sin distinguir mayúsculas, tildes ni espacios extra (`José  Peña` = `jose pena`). Hay un índice único en la base como respaldo.
- **Sorteo**: solo participan quienes asistirán (mínimo 3). Se barajan con generador criptográfico y se forma **un solo ciclo** A→B→C→…→A: nadie se toca a sí mismo y funciona con números impares. Al sortear se cierran las inscripciones.
- **Privacidad**: las asignaciones solo se envían al navegador de cada participante cuando ingresa **su propio** código. El panel de gestión solo muestra la cantidad.
- **Seguridad**: antiforgery en todos los formularios; validación en cliente (HTML5) y servidor (DataAnnotations); clave comparada en tiempo constante (hash SHA-256 + `FixedTimeEquals`); cookie de gestión HttpOnly, Secure, SameSite=Strict y firmada/cifrada por Data Protection; límite de envíos por IP (10 intentos de acceso cada 5 min, 60 envíos por minuto); `Cache-Control: no-store` en páginas.
- **Fechas**: se guardan en UTC y se muestran en hora de Guatemala (UTC-6).

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

## Publicar en Azure

### 1. Base de datos: Azure SQL (oferta gratuita)

1. En el portal de Azure, ve a **Crear un recurso → SQL Database**.
2. En la parte superior, pulsa **Aplicar oferta** (*Free offer*).
3. Crea un servidor nuevo con **autenticación SQL** (usuario y contraseña) y guarda los datos.
4. En *Comportamiento cuando se alcanza el límite gratuito*, elige **Pausar automáticamente la base de datos hasta el próximo mes** para no generar cobros.
5. En **Redes**, activa **Permitir que los servicios y recursos de Azure accedan a este servidor**.
6. Cuando se cree, ve a **Cadenas de conexión → ADO.NET (autenticación SQL)** y copia la cadena (reemplaza `{your_password}`).

> La base serverless se pausa cuando no se usa. La primera solicitud puede tardar hasta ~1 minuto mientras se reanuda: la app usa reintentos y un tiempo de conexión de 60 s.

### 2. Aplicación: Azure App Service (Linux, F1)

1. **Crear un recurso → Aplicación web**.
   - Publicar: **Código** · Pila del entorno de ejecución: **.NET 10** · Sistema operativo: **Linux**
   - Plan de precios: **Gratis F1**
2. En la aplicación web, ve a **Configuración → Variables de entorno**:
   - Pestaña **Cadenas de conexión**: nombre `Sql`, valor la cadena del paso anterior, tipo `SQLAzure`.
   - Pestaña **Configuración de la aplicación**: nombre `ADMIN_KEY`, valor una clave de **al menos 12 caracteres**.
3. En **Configuración → General**, deja **Solo HTTPS** activado.

Notas del plan F1: no tiene *Always On*, así que la app se duerme tras unos 20 minutos sin uso y la primera visita tarda unos segundos. Tiene 60 minutos de CPU al día, suficiente para un convivio.

### 3. Publicación automática desde GitHub

El flujo `.github/workflows/azure-app-service.yml` compila y prueba cada push y PR. Con cada push a `main` además publica la app, siempre que esté configurado:

1. En App Service, **Configuración → General → Credenciales de publicación básicas de SCM: Activado** (es necesario para el perfil de publicación).
2. En **Información general → Descargar perfil de publicación**.
3. En GitHub, ve a **Settings → Secrets and variables → Actions**:
   - **Secrets** → `AZURE_WEBAPP_PUBLISH_PROFILE` = contenido completo del archivo descargado.
   - **Variables** → `AZURE_WEBAPP_NAME` = nombre de tu aplicación web.
4. Haz push a `main`, o ejecuta el flujo a mano desde la pestaña **Actions**.

### Alternativa: publicación manual

```powershell
dotnet publish src/AmigoSecreto -c Release -o publish
Compress-Archive -Path publish/* -DestinationPath app.zip -Force
az webapp deploy --resource-group <grupo> --name <app> --src-path app.zip --type zip
```

## Uso el día del convivio

1. Comparte la URL para que todos se inscriban.
2. Revisa en `/gestion` que todos los asistentes estén inscritos.
3. Pulsa **Realizar sorteo**: se cierran las inscripciones.
4. Cada quien entra con su código y abre su regalo 🎁.
5. Si alguien perdió su código, recupéralo desde la lista de participantes y compártelo **solo con esa persona**.
