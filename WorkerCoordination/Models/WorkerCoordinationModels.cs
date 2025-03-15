using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Logx.WorkerCoordination.Models
{
    /// <summary>
    /// Represents a distributed lock for single-instance workers
    /// </summary>
    [Table("WorkerLocks")]
    public class WorkerLock
    {
        /// <summary>
        /// The unique name of the lock
        /// </summary>
        [Key]
        [Required]
        [MaxLength(100)]
        public string LockName { get; set; } = string.Empty;

        /// <summary>
        /// The unique ID of the worker instance that owns the lock
        /// </summary>
        [Required]
        [MaxLength(100)]
        public string OwnerId { get; set; } = string.Empty;

        /// <summary>
        /// The pod name where the lock owner is running
        /// </summary>
        [Required]
        [MaxLength(100)]
        public string OwnerPod { get; set; } = string.Empty;

        /// <summary>
        /// The Kubernetes cluster name where the lock owner is running
        /// </summary>
        [Required]
        [MaxLength(100)]
        public string ClusterName { get; set; } = string.Empty;

        /// <summary>
        /// The timestamp when the lock expires if not renewed
        /// </summary>
        [Required]
        public DateTime ExpiresAt { get; set; }

        /// <summary>
        /// The timestamp when the lock was last renewed
        /// </summary>
        [Required]
        public DateTime LastRenewed { get; set; }

        /// <summary>
        /// The timestamp when the lock was first acquired
        /// </summary>
        [Required]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Represents a registered worker in the distributed system
    /// </summary>
    [Table("WorkerRegistrations")]
    public class WorkerRegistration
    {
        /// <summary>
        /// The unique ID of the worker instance
        /// </summary>
        [Key]
        [Required]
        [MaxLength(100)]
        public string WorkerId { get; set; } = string.Empty;

        /// <summary>
        /// The type of worker (used for grouping workers of the same type)
        /// </summary>
        [Required]
        [MaxLength(100)]
        public string WorkerType { get; set; } = string.Empty;

        /// <summary>
        /// The pod name where the worker is running
        /// </summary>
        [Required]
        [MaxLength(100)]
        public string PodName { get; set; } = string.Empty;

        /// <summary>
        /// The Kubernetes cluster name where the worker is running
        /// </summary>
        [Required]
        [MaxLength(100)]
        public string ClusterName { get; set; } = string.Empty;

        /// <summary>
        /// The timestamp when the worker was first registered
        /// </summary>
        [Required]
        public DateTime RegisteredAt { get; set; }

        /// <summary>
        /// The timestamp when the worker last sent a heartbeat
        /// </summary>
        [Required]
        public DateTime LastHeartbeat { get; set; }
    }
}