using System;

namespace Logx.WorkerCoordination.Identity
{
    /// <summary>
    /// Provides identity information for the current node in a distributed system
    /// </summary>
    public interface INodeIdentityProvider
    {
        /// <summary>
        /// Gets a unique identifier for the current machine/node
        /// </summary>
        string NodeId { get; }

        /// <summary>
        /// Gets the name of the logical group/cluster this node belongs to
        /// </summary>
        string GroupName { get; }
    }
}