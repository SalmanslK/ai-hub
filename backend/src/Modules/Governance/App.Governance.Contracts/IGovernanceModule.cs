using App.Shared;

namespace App.Governance.Contracts;

// Governance's public contract: the only part of the module other modules may depend on.
public interface IGovernanceModule : IModule;
