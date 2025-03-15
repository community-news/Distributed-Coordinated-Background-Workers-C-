using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Logx.WorkerCoordination.Models;

namespace Logx.WorkerCoordination.Extensions
{
    /// <summary>
    /// Extension methods for configuring worker coordination services
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds worker coordination services to the specified IServiceCollection
        /// </summary>
        /// <param name="services">The service collection to add services to</param>
        /// <param name="dbContextOptionsAction">Action to configure the DbContext options</param>
        /// <returns>The updated service collection</returns>
        public static IServiceCollection AddWorkerCoordination(
            this IServiceCollection services,
            Action<DbContextOptionsBuilder> dbContextOptionsAction)
        {
            services.AddDbContext<WorkerCoordinationDbContext>(dbContextOptionsAction);
            return services;
        }
    }
}