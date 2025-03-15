using Microsoft.EntityFrameworkCore;

namespace Logx.WorkerCoordination.Models
{
    /// <summary>
    /// Database context for worker coordination
    /// </summary>
    public class WorkerCoordinationDbContext : DbContext
    {
        /// <summary>
        /// Initializes a new instance of the WorkerCoordinationDbContext
        /// </summary>
        public WorkerCoordinationDbContext(DbContextOptions<WorkerCoordinationDbContext> options)
            : base(options)
        {
        }

        /// <summary>
        /// Database set for worker locks
        /// </summary>
        public DbSet<WorkerLock> WorkerLocks { get; set; } = null!;

        /// <summary>
        /// Database set for worker registrations
        /// </summary>
        public DbSet<WorkerRegistration> WorkerRegistrations { get; set; } = null!;

        /// <summary>
        /// Configures the model and indexes
        /// </summary>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure indexes for WorkerLock
            modelBuilder.Entity<WorkerLock>(entity =>
            {
                entity.HasKey(e => e.LockName);
                entity.HasIndex(e => e.OwnerId);
                entity.HasIndex(e => e.ExpiresAt);
            });

            // Configure indexes for WorkerRegistration
            modelBuilder.Entity<WorkerRegistration>(entity =>
            {
                entity.HasKey(e => e.WorkerId);

                // Index for finding workers by type
                entity.HasIndex(e => e.WorkerType);

                // Index for heartbeat expiration cleanup
                entity.HasIndex(e => e.LastHeartbeat);

                // Compound index for worker type + cluster name
                entity.HasIndex(e => new { e.WorkerType, e.ClusterName });
            });
        }
    }
}