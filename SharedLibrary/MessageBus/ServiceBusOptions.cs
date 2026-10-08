namespace SharedLibrary.MessageBus;

public sealed class ServiceBusOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string RolePermissionsUpdatedQueue { get; set; } = string.Empty;
}
