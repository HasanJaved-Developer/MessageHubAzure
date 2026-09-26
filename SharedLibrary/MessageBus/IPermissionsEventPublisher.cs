namespace SharedLibrary.MessageBus

{
    public interface IPermissionsEventPublisher
    {
        Task PublishRolePermissionsUpdatedAsync(RolePermissionsUpdatedEvent evt, CancellationToken ct = default);
    }
}
