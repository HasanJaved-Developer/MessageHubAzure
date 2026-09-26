namespace SharedLibrary.MessageBus

{
    public sealed class RabbitMqOptions
    {
        public string HostName { get; set; }
        public int Port { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }

        public string Exchange { get; set; }
        public string ExchangeType { get; set; }
        public bool Durable { get; set; }
    }
}
