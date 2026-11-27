using Microsoft.EntityFrameworkCore;

namespace AmigoSecreto.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Empresa> Empresas => Set<Empresa>();
    public DbSet<Participante> Participantes => Set<Participante>();
    public DbSet<Asignacion> Asignaciones => Set<Asignacion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Si cambias algo aquí, actualiza también la migración manual en InicializadorBaseDatos.
        modelBuilder.Entity<Empresa>(e =>
        {
            e.ToTable("Empresas");
            e.Property(x => x.Slug).HasMaxLength(Servicios.Slug.LongitudMaxima).IsUnicode(false);
            e.Property(x => x.Nombre).HasMaxLength(100);
            e.Property(x => x.Subtitulo).HasMaxLength(100);
            e.Property(x => x.FechaSorteo).HasMaxLength(150);
            e.Property(x => x.ClaveHash).HasMaxLength(200).IsUnicode(false);
            e.HasIndex(x => x.Slug).IsUnique();
        });

        modelBuilder.Entity<Participante>(e =>
        {
            e.ToTable("Participantes");
            e.Property(p => p.NombreCompleto).HasMaxLength(100);
            e.Property(p => p.NombreNormalizado).HasMaxLength(100);
            e.Property(p => p.Postre).HasMaxLength(150);
            e.Property(p => p.LugarCompra).HasMaxLength(150);
            e.Property(p => p.Correo).HasMaxLength(Servicios.Correo.LongitudMaxima);
            e.Property(p => p.Codigo).HasMaxLength(6).IsFixedLength().IsUnicode(false);
            e.HasIndex(p => new { p.EmpresaId, p.NombreNormalizado }).IsUnique();
            e.HasIndex(p => p.Codigo).IsUnique();
            // Sin cascada: una empresa solo se puede eliminar cuando ya no tiene inscritos.
            e.HasOne(p => p.Empresa).WithMany().HasForeignKey(p => p.EmpresaId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Asignacion>(e =>
        {
            e.ToTable("Asignaciones");
            e.HasKey(a => a.DaId);
            e.HasIndex(a => a.RecibeId).IsUnique();
            // Sin cascada: las asignaciones se borran explícitamente al reiniciar/rehacer
            // y no se permite eliminar participantes con sorteo realizado.
            e.HasOne(a => a.Da).WithMany().HasForeignKey(a => a.DaId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(a => a.Recibe).WithMany().HasForeignKey(a => a.RecibeId).OnDelete(DeleteBehavior.NoAction);
        });
    }
}
