using Microsoft.EntityFrameworkCore;

namespace AmigoSecreto.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Participante> Participantes => Set<Participante>();
    public DbSet<Asignacion> Asignaciones => Set<Asignacion>();
    public DbSet<EstadoSorteo> EstadosSorteo => Set<EstadoSorteo>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Participante>(e =>
        {
            e.ToTable("Participantes");
            e.Property(p => p.NombreCompleto).HasMaxLength(100);
            e.Property(p => p.NombreNormalizado).HasMaxLength(100);
            e.Property(p => p.Postre).HasMaxLength(150);
            e.Property(p => p.LugarCompra).HasMaxLength(150);
            e.Property(p => p.Correo).HasMaxLength(Servicios.Correo.LongitudMaxima);
            e.Property(p => p.Codigo).HasMaxLength(6).IsFixedLength().IsUnicode(false);
            e.HasIndex(p => p.NombreNormalizado).IsUnique();
            e.HasIndex(p => p.Codigo).IsUnique();
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

        modelBuilder.Entity<EstadoSorteo>(e =>
        {
            e.ToTable("EstadoSorteo");
            e.Property(s => s.Id).ValueGeneratedNever();
            e.HasData(new EstadoSorteo { Id = EstadoSorteo.IdUnico, Realizado = false });
        });
    }
}
