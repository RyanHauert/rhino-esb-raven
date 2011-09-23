using Raven.Client;

namespace Rhino.ServiceBus.Raven
{
    public interface IDocumentStoreProvider
    {
        IDocumentSession Current { get; }
    }
}