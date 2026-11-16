using System.Threading.RateLimiting;
using AmigoSecreto.Components;
using AmigoSecreto.Data;
using AmigoSecreto.Servicios;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ---------- Base de datos (con reintentos: SQL Server puede tardar en arrancar junto con la app) ----------
var cadenaSql = builder.Configuration.GetConnectionString("Sql") ?? builder.Configuration["Sql"];
if (string.IsNullOrWhiteSpace(cadenaSql))
{
    throw new InvalidOperationException(
        "Falta la cadena de conexión 'Sql'. En local: dotnet user-secrets set \"ConnectionStrings:Sql\" \"...\". " +
        "En Docker: variable de entorno ConnectionStrings__Sql (ver deploy/aws/docker-compose.yml).");
}
var csb = new SqlConnectionStringBuilder(cadenaSql);
if (csb.ConnectTimeout < 60) csb.ConnectTimeout = 60; // margen amplio por si SQL Server todavía está arrancando

builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlServer(csb.ConnectionString, sql =>
    sql.EnableRetryOnFailure(maxRetryCount: 8, maxRetryDelay: TimeSpan.FromSeconds(15), errorNumbersToAdd: null)));

builder.Services.AddSingleton<InicializadorBaseDatos>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<InicializadorBaseDatos>());
builder.Services.AddScoped<IntercambioService>();
builder.Services.AddSingleton<ClaveAdministracion>();
builder.Services.AddSingleton(builder.Configuration.GetSection("Correo").Get<OpcionesCorreo>() ?? new OpcionesCorreo());
builder.Services.AddSingleton<EnviadorCorreo>();

// ---------- Llaves de Data Protection (cookies de gestión y antiforgery) ----------
// En un contenedor hay que guardarlas en un volumen; si no, se pierden al reiniciar y las cookies dejan de valer.
var rutaLlaves = builder.Configuration["DataProtection:Ruta"];
if (!string.IsNullOrWhiteSpace(rutaLlaves))
{
    builder.Services.AddDataProtection()
        .SetApplicationName("AmigoSecreto")
        .PersistKeysToFileSystem(new DirectoryInfo(rutaLlaves));
}

// ---------- Componentes: solo renderizado estático en servidor (sin WebSockets) ----------
builder.Services.AddRazorComponents();

// ---------- Autenticación del organizador ----------
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "amigo.gestion";
        o.Cookie.HttpOnly = true;
        o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.LoginPath = "/gestion/acceso";
        o.AccessDeniedPath = "/gestion/acceso";
        o.ReturnUrlParameter = "volver";
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Politicas.Organizador, p => p.RequireRole(Politicas.Organizador));

builder.Services.AddAntiforgery(o =>
{
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    o.Cookie.SameSite = SameSiteMode.Strict;
});

// App Service termina TLS en su proxy y reenvía X-Forwarded-Proto/For.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

// Límite de envíos de formularios por IP (frena adivinar códigos o la clave).
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        if (!HttpMethods.IsPost(ctx.Request.Method))
            return RateLimitPartition.GetNoLimiter("lectura");

        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "desconocida";
        var esAcceso = ctx.Request.Path.StartsWithSegments("/gestion/acceso");
        return RateLimitPartition.GetFixedWindowLimiter(
            (esAcceso ? "acceso:" : "post:") + ip,
            _ => new FixedWindowRateLimiterOptions
            {
                // Generoso para los envíos normales: en el convivio muchos comparten la misma red Wi-Fi.
                PermitLimit = esAcceso ? 10 : 60,
                Window = esAcceso ? TimeSpan.FromMinutes(5) : TimeSpan.FromMinutes(1),
            });
    });
});

var app = builder.Build();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/estado/{0}", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

// Cabeceras de seguridad y sin caché para páginas HTML (pueden mostrar códigos o asignaciones).
app.Use(async (ctx, next) =>
{
    ctx.Response.OnStarting(() =>
    {
        var h = ctx.Response.Headers;
        h.XContentTypeOptions = "nosniff";
        h.XFrameOptions = "DENY";
        h["Referrer-Policy"] = "no-referrer";
        if (ctx.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true)
            h.CacheControl = "no-store";
        return Task.CompletedTask;
    });
    await next();
});

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>();

app.Run();

public static class Politicas
{
    public const string Organizador = "Organizador";
}
