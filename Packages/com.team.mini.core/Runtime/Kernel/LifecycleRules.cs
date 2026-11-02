namespace MiniFramework.Kernel
{
    public enum ModuleState { Created, Initializing, Ready, Failed, Stopping, Disposing, CleanupFailed, Disposed }
    // Context owns the state, not the module. This is a policy, not a Context implementation.
    public static class LifecycleRules
    {
        public static bool CanTransition(ModuleState from, ModuleState to)
        {
            switch (from)
            {
                case ModuleState.Created: return to == ModuleState.Initializing || to == ModuleState.Stopping;
                case ModuleState.Initializing: return to == ModuleState.Ready || to == ModuleState.Failed || to == ModuleState.Stopping;
                case ModuleState.Ready: return to == ModuleState.Stopping;
                case ModuleState.Failed: return to == ModuleState.Stopping;
                case ModuleState.Stopping: return to == ModuleState.Disposing;
                case ModuleState.Disposing: return to == ModuleState.Disposed || to == ModuleState.CleanupFailed;
                case ModuleState.CleanupFailed: return to == ModuleState.Disposing;
                default: return false;
            }
        }
    }
}
