using System;
using System.Globalization;
using Rhino.ServiceBus.Internal;

namespace Rhino.ServiceBus.Raven
{
    public class RavenSagaPersister<TSaga> : ISagaPersister<TSaga> where TSaga : class, IAccessibleSaga
    {
        private readonly IServiceLocator serviceLocator;
        private readonly IDocumentStoreProvider store;

        public RavenSagaPersister(IDocumentStoreProvider store, IServiceLocator serviceLocator)
        {
            this.store = store;
            this.serviceLocator = serviceLocator;
        }

        public void Complete(TSaga saga)
        {
            object persistedState = LoadPersistedState(saga.Id);
            if (persistedState != null)
            {
                store.Current.Delete(persistedState);
            }
        }

        public TSaga Get(Guid id)
        {
            dynamic state = LoadPersistedState(id);
            if (state == null)
            {
                return null;
            }
            dynamic saga = serviceLocator.Resolve<TSaga>();
            saga.Id = id;
            saga.State = state;
            return saga;
        }

        public void Save(TSaga saga)
        {
            dynamic dynamicSaga = saga;
            dynamic state = dynamicSaga.State;
            string documentId = CreateDocumentId(saga.Id);
            store.Current.Store(state, documentId);
        }

        private static string CreateDocumentId(Guid id)
        {
            return String.Format(CultureInfo.InvariantCulture, "{0}/{1}", typeof(TSaga).Name, id);
        }

        private dynamic LoadPersistedState(Guid id)
        {
            string documentId = CreateDocumentId(id);
            return store.Current.Load<dynamic>(documentId);
        }
    }
}