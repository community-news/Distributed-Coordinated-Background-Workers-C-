using System;

namespace Logx.WorkerCoordination.Identity
{


    /// <summary>
    /// Default implementation that reads identity from environment or machine info
    /// </summary>
    public class EnvironmentIdentityProvider : INodeIdentityProvider
    {
        private readonly string _nodeId;
        private readonly string _groupName;

        /// <summary>
        /// Creates a new instance with optional custom environment variable names
        /// </summary>
        /// 
        public EnvironmentIdentityProvider(
            string nodeIdEnvVar = "HOSTNAME",
            string groupNameEnvVar = "CLUSTER_NAME")
        {
            // Try environment variables first, then fall back to machine identifiers
            _nodeId = Environment.GetEnvironmentVariable(nodeIdEnvVar) ??  // Custom env var
                      Environment.GetEnvironmentVariable("HOSTNAME") ??     // Kubernetes/Docker
                      Environment.GetEnvironmentVariable("COMPUTERNAME") ?? // Windows
                      Environment.MachineName;                            // .NET fallback

            _groupName = Environment.GetEnvironmentVariable(groupNameEnvVar) ??   // Custom env var
                         Environment.GetEnvironmentVariable("KUBERNETES_CLUSTER") ?? // K8s
                         Environment.GetEnvironmentVariable("CLUSTER_NAME") ??    // Generic
                         "default-group";                                       // Default fallback
        }

        /// <inheritdoc/>
        public string NodeId => _nodeId;

        /// <inheritdoc/>
        public string GroupName => _groupName;
    }
}