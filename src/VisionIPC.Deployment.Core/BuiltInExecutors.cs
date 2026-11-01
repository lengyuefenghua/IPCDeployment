namespace VisionIPC.Deployment.Core
{
    public static class BuiltInExecutors
    {
        public static OperationRegistry CreateRegistry()
        {
            var registry = new OperationRegistry();
            registry.Register(new InstallOperationExecutor());
            registry.Register(new CopyOperationExecutor());
            registry.Register(new RegistryOperationExecutor());
            registry.Register(new CommandOperationExecutor());
            registry.Register(new ServiceOperationExecutor());
            registry.Register(new NetworkOperationExecutor());
            registry.Register(new PowerOperationExecutor());
            registry.Register(new AutoStartOperationExecutor());
            registry.Register(new DelayOperationExecutor());
            return registry;
        }
    }
}
