using System;

namespace Logx.WorkerCoordination.Identity
{
    /// <summary>
    /// Explicit implementation that allows setting custom node/group names
    /// </summary>
    /// <remarks>
    /// This is useful for testing or when environment variables are not available
    /// </remarks>
    public class ExplicitNodeIdentityProvider : INodeIdentityProvider
    {
        private readonly string _nodeId;
        private readonly string _groupName;

        /// <summary>
        /// Creates a new instance with explicit node and group names
        /// </summary>
        /// <param name="nodeId">The unique identifier for the node</param>
        /// <param name="groupName">The name of the logical group/cluster</param>
        public ExplicitNodeIdentityProvider(string nodeId, string groupName)
        {
            _nodeId = nodeId;
            _groupName = groupName;
        }

        /// <inheritdoc/>
        public string NodeId => _nodeId;

        /// <inheritdoc/>
        public string GroupName => _groupName;
    }
}