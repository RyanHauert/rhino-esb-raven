namespace Rhino.ServiceBus.Raven
{
    public interface IPersistedSagaIdentifier
    {
        string Id { get; set; }
    }
}