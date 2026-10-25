namespace App.Shared;

// One stage of the flow, as the host composes it. Each module's contract extends this.
public interface IModule
{
    string Name { get; }
}
