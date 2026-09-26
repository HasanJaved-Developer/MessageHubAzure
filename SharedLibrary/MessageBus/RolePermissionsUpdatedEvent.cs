namespace SharedLibrary.MessageBus
{
    public sealed class RolePermissionsUpdatedEvent
    {
        public string Role { get; set; }
        public DateTime OccurredAtUtc { get; set; }
        public string Source { get; set; }
    }
}
