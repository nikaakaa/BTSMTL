using System;
using System.Globalization;

namespace ThirdPersonSimulation.ServerAuthoritative
{
    public sealed class ServerAuthoritativeAuthorityHostProductDescriptor
    {
        public ServerAuthoritativeAuthorityHostProductDescriptor(
            HostProductId productId,
            ServerAuthoritativeAuthorityHostRouteKind routeKind,
            string launchKind,
            int manifestSchemaVersion,
            SolverImplementationId authoritySolverId,
            string authoritySolverVersion,
            WorldCapability authoritySolverCapabilities,
            WorldFeature authoritySolverFeatures)
        {
            if (!productId.IsValid || !ServerAuthoritativeAuthorityHostIdentity.IsValidRouteKind(routeKind) ||
                string.IsNullOrWhiteSpace(launchKind) || manifestSchemaVersion <= 0 ||
                string.IsNullOrEmpty(authoritySolverId.Value) || string.IsNullOrWhiteSpace(authoritySolverVersion) ||
                authoritySolverCapabilities == WorldCapability.None)
            {
                throw new ArgumentException("ServerAuthoritative Authority Host product descriptor is incomplete.");
            }
            ProductId = productId;
            RouteKind = routeKind;
            LaunchKind = launchKind.Trim();
            ManifestSchemaVersion = manifestSchemaVersion;
            AuthoritySolverId = authoritySolverId;
            AuthoritySolverVersion = authoritySolverVersion.Trim();
            AuthoritySolverCapabilities = authoritySolverCapabilities;
            AuthoritySolverFeatures = authoritySolverFeatures;
            DescriptorHash = StableHash.Compute(
                "server-authoritative-authority-host-product/1",
                ProductId.Value,
                RouteKind.ToString(),
                LaunchKind,
                ManifestSchemaVersion.ToString(CultureInfo.InvariantCulture),
                AuthoritySolverId.Value,
                AuthoritySolverVersion,
                checked((ulong)AuthoritySolverCapabilities).ToString(CultureInfo.InvariantCulture),
                checked((ulong)AuthoritySolverFeatures).ToString(CultureInfo.InvariantCulture));
        }

        public HostProductId ProductId { get; }
        public ServerAuthoritativeAuthorityHostRouteKind RouteKind { get; }
        public string LaunchKind { get; }
        public int ManifestSchemaVersion { get; }
        public SolverImplementationId AuthoritySolverId { get; }
        public string AuthoritySolverVersion { get; }
        public WorldCapability AuthoritySolverCapabilities { get; }
        public WorldFeature AuthoritySolverFeatures { get; }
        public StableHash DescriptorHash { get; }

        public ServerAuthoritativeAuthorityHostIdentity CreateHostIdentity(
            string hostId,
            ServerAuthoritativeRoomId roomId) =>
            new ServerAuthoritativeAuthorityHostIdentity(ProductId, hostId, RouteKind, roomId);

        public void RequireAuthoritySolver(SimulationWorldSolverDefinitionDescriptor solver)
        {
            if (solver == null || !solver.ImplementationId.Equals(AuthoritySolverId) ||
                !string.Equals(solver.ImplementationVersion, AuthoritySolverVersion, StringComparison.Ordinal) ||
                solver.Capabilities != AuthoritySolverCapabilities || solver.Features != AuthoritySolverFeatures)
            {
                throw new InvalidOperationException(
                    $"Authority Host product '{ProductId}' rejected Solver '{solver?.Identity.ToString() ?? "absent"}'.");
            }
        }
    }
}
